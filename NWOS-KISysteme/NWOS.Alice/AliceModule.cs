using System.Text.Json;
using System.Diagnostics;
using NWOS.AI;
using NWOS.Contracts;
using NWOS.SystemCenter;

namespace NWOS.Alice;

/// <summary>
/// Registriert sich bei Cardinal als "nwos.alice" (Architekturdokument
/// Abschnitt 8). Überwacht ein konfiguriertes Verzeichnis passiv per
/// <see cref="FileSystemWatcher"/> (kein Polling, Abschnitt 35), wertet neue/
/// geänderte Dateien über <see cref="HeuristicEngine"/> und
/// <see cref="BurstDetector"/> aus, protokolliert jede Entscheidung
/// (<see cref="SecurityAuditLog"/>) und isoliert nur bei der einen, eng
/// gefassten Regel automatisch, die als eindeutig sicher automatisierbar
/// eingestuft ist (Abschnitt 8) - alles andere bleibt informativ, bis der
/// Nutzer (über eine höhere Schicht) eine Aktion bestätigt.
///
/// Unterstützte MessageType-Werte:
///   - "alice.getRecentEvents"  { "count": int }
///   - "alice.quarantine"       { "filePath": string }
///   - "alice.restore"          { "quarantineId": string }
///   - "alice.listQuarantined"  (kein Payload nötig)
///   - "alice.manualScan"       (manueller Scan des überwachten Ordners)
///   - "alice.systemCheck"      (lesende Windows-, Laufwerks-, Prozess- und Verzeichnisdiagnose)
/// </summary>
public sealed class AliceModule : IModule, IDisposable
{
    public string ModuleId => "nwos.alice";
    public string DisplayName => "Alice";

    private readonly string _watchedDirectory;
    private readonly HeuristicEngine _heuristics = new();
    private readonly BurstDetector _burstDetector = new();
    private readonly SecurityAuditLog _auditLog;
    private readonly QuarantineService _quarantine;
    private readonly OpenAiChatClient _chatClient;
    private readonly ISystemStatusProvider? _systemStatusProvider;

    private FileSystemWatcher? _watcher;
    private bool _watcherActive;

    // Einzige Regel, die ohne Nutzerbestätigung automatisch isoliert wird -
    // bewusst als explizite, überschaubare Liste statt verstreuter Bedingungen,
    // damit sie bei Bedarf (Abschnitt 8) leicht überprüft/erweitert werden kann.
    private static readonly IReadOnlySet<string> AutoQuarantineRuleIds = new HashSet<string> { "double-extension-executable" };

    public AliceModule(
        string watchedDirectory,
        string stateDirectory,
        OpenAiChatClient? chatClient = null,
        ISystemStatusProvider? systemStatusProvider = null)
    {
        _watchedDirectory = watchedDirectory;
        _auditLog = new SecurityAuditLog(stateDirectory);
        _quarantine = new QuarantineService(stateDirectory);
        _chatClient = chatClient ?? new OpenAiChatClient();
        _systemStatusProvider = systemStatusProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _auditLog.LoadAsync(cancellationToken);
        await _quarantine.LoadAsync(cancellationToken);

        if (!Directory.Exists(_watchedDirectory))
        {
            // Abschnitt 36: ein fehlendes Verzeichnis darf Alice nicht zum
            // Absturz bringen - Modul bleibt "degraded" statt unresponsive,
            // damit HealthMonitor keine Crash-Loop-Recovery auslöst.
            _watcherActive = false;
            return;
        }

        _watcher = new FileSystemWatcher(_watchedDirectory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
        _watcherActive = true;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileEvent;
            _watcher.Changed -= OnFileEvent;
            _watcher.Dispose();
            _watcher = null;
        }

        _watcherActive = false;
        return Task.CompletedTask;
    }

    public void Dispose() => _watcher?.Dispose();

    public Task<ModuleHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ModuleHealth(
            ModuleId,
            _watcherActive ? ModuleStatus.Healthy : ModuleStatus.Degraded,
            _watcherActive
                ? $"Überwacht: {_watchedDirectory}"
                : $"Überwachung inaktiv (Verzeichnis fehlt oder Modul gestoppt): {_watchedDirectory}"));

    private async void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher-Callbacks laufen auf einem eigenen Thread-Pool-
        // Thread; Exceptions hier dürfen den Prozess nicht mitreißen
        // (Abschnitt 36) - deshalb vollständig try/catch-umschlossen statt
        // sie an den Aufrufer durchzureichen (es gibt hier keinen sinnvollen
        // Aufrufer, der sie behandeln könnte).
        try
        {
            var now = DateTimeOffset.UtcNow;

            if (_burstDetector.RecordEventAndCheckBurst(now))
            {
                var burstEvent = ThreatEvent.Create(
                    _watchedDirectory,
                    ruleId: "mass-file-write-burst",
                    explanation: "Ungewöhnlich viele Dateiänderungen in kurzer Zeit im überwachten Verzeichnis.",
                    severity: ThreatSeverity.Suspicious,
                    autoActionTaken: false);
                await _auditLog.AppendAsync(burstEvent, CancellationToken.None);
            }

            foreach (var threatEvent in _heuristics.EvaluateFileCreatedOrChanged(e.FullPath))
            {
                var autoAction = AutoQuarantineRuleIds.Contains(threatEvent.RuleId);
                var finalEvent = threatEvent with { AutoActionTaken = autoAction };
                await _auditLog.AppendAsync(finalEvent, CancellationToken.None);

                if (autoAction)
                {
                    try
                    {
                        await _quarantine.QuarantineAsync(e.FullPath, CancellationToken.None);
                    }
                    catch (IOException)
                    {
                        // Datei evtl. bereits verschoben/gelöscht/gesperrt -
                        // Ereignis bleibt trotzdem protokolliert.
                    }
                }
            }
        }
        catch
        {
            // Bewusst breit gefangen: ein Fehler in der Auswertung eines
            // einzelnen Dateiereignisses darf die Überwachung als Ganzes nicht
            // stoppen (Abschnitt 36). TODO(Phase 4): strukturiertes Logging
            // statt stillem Schlucken, sobald ein zentraler Log-Sink existiert.
        }
    }

    public async Task<ModuleMessage?> HandleMessageAsync(ModuleMessage message, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.PayloadJson);
        var root = payload.RootElement;

        object result = message.MessageType switch
        {
            "alice.getRecentEvents" => _auditLog.GetRecent(
                root.TryGetProperty("count", out var c) ? c.GetInt32() : 20),

            "alice.quarantine" => new
            {
                quarantineId = await _quarantine.QuarantineAsync(root.GetProperty("filePath").GetString()!, cancellationToken),
            },

            "alice.restore" => await HandleRestoreAsync(root, cancellationToken),

            "alice.listQuarantined" => _quarantine.AllQuarantined,

            "alice.manualScan" => await RunManualScanAsync(cancellationToken),

            "alice.systemCheck" => await RunSystemCheckAsync(cancellationToken),

            "alice.chat" => new
            {
                reply = await _chatClient.CompleteAsync(
                    "Du bist Alice, die Sicherheitsassistentin von NWOS. Antworte auf Deutsch, sachlich und umsichtig. " +
                    "Du kannst nur allgemeine Sicherheitsfragen beantworten. Du hast keinen Zugriff auf Dateien, " +
                    "Systemeinstellungen oder Scan-Aktionen und darfst nicht behaupten, einen Scan oder eine " +
                    "Systemaktion ausgeführt zu haben. Für echte Scans nutze der Benutzer die separate Scan-Funktion.",
                    ReadChatMessages(root),
                    cancellationToken),
            },

            _ => throw new NotSupportedException($"Unbekannter MessageType '{message.MessageType}' für {ModuleId}."),
        };

        return message with
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SourceModuleId = ModuleId,
            TargetModuleId = message.SourceModuleId,
            MessageType = $"{message.MessageType}.result",
            PayloadJson = JsonSerializer.Serialize(result, NwosJson.ResponseOptions),
        };
    }

    private static IReadOnlyList<OpenAiChatMessage> ReadChatMessages(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Chat-Payload muss ein messages-Array enthalten.");
        }

        return messages.EnumerateArray()
            .Select(item => new OpenAiChatMessage(
                item.GetProperty("role").GetString() ?? string.Empty,
                item.GetProperty("content").GetString() ?? string.Empty))
            .ToArray();
    }

    private async Task<object> HandleRestoreAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var quarantineId = root.GetProperty("quarantineId").GetString()!;
        await _quarantine.RestoreAsync(quarantineId, cancellationToken);
        return new { restored = true, quarantineId };
    }

    private async Task<object> RunManualScanAsync(CancellationToken cancellationToken)
    {
        var scannedFiles = 0;
        var detectedThreats = 0;
        var quarantinedFiles = 0;

        foreach (var filePath in EnumerateFilesSafe(_watchedDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedFiles++;

            foreach (var threatEvent in _heuristics.EvaluateFileCreatedOrChanged(filePath))
            {
                detectedThreats++;
                var autoAction = AutoQuarantineRuleIds.Contains(threatEvent.RuleId);
                await _auditLog.AppendAsync(threatEvent with { AutoActionTaken = autoAction }, cancellationToken);

                if (!autoAction)
                {
                    continue;
                }

                try
                {
                    await _quarantine.QuarantineAsync(filePath, cancellationToken);
                    quarantinedFiles++;
                }
                catch (IOException)
                {
                    // Gesperrte oder bereits verschobene Datei bleibt als Fund protokolliert.
                }
            }
        }

        return new
        {
            scannedFiles,
            detectedThreats,
            quarantinedFiles,
            watchedDirectory = _watchedDirectory,
        };
    }

    private async Task<object> RunSystemCheckAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        SystemStatusSnapshot? status = null;
        if (_systemStatusProvider is null)
        {
            warnings.Add("Windows-Systemmetriken sind nicht verfügbar: kein Windows-Statusanbieter konfiguriert.");
        }
        else
        {
            try
            {
                status = await _systemStatusProvider.CaptureAsync(cancellationToken);
                if (status.CpuUsagePercent is null)
                {
                    warnings.Add("CPU-Auslastung konnte nicht über die konfigurierte Windows-Datenquelle ermittelt werden.");
                }

                if (status.TotalMemoryBytes is null || status.AvailableMemoryBytes is null)
                {
                    warnings.Add("RAM-Werte konnten nicht über die konfigurierte Windows-Datenquelle ermittelt werden.");
                }

                if (status.ActiveProcessCount is null)
                {
                    warnings.Add("Prozessanzahl konnte nicht ermittelt werden.");
                }

                if (status.Disks is null)
                {
                    warnings.Add("WMI-Laufwerksdaten nicht verfügbar; Laufwerke werden separat über DriveInfo geprüft.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Windows-Systemmetriken konnten nicht gelesen werden: {ex.Message}");
            }
        }

        var volumes = new List<object>();
        IEnumerable<DriveInfo> drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            drives = Array.Empty<DriveInfo>();
            warnings.Add($"Laufwerksliste konnte nicht gelesen werden: {ex.Message}");
        }

        foreach (var drive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady)
                {
                    volumes.Add(new { name = drive.Name, ready = false });
                    continue;
                }

                volumes.Add(new
                {
                    name = drive.Name,
                    ready = true,
                    drive.DriveType,
                    drive.DriveFormat,
                    totalBytes = drive.TotalSize,
                    availableBytes = drive.AvailableFreeSpace,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                warnings.Add($"Laufwerk {drive.Name} konnte nicht vollständig gelesen werden: {ex.Message}");
            }
        }

        var scan = await RunReadOnlyHeuristicScanAsync(cancellationToken);
        warnings.AddRange(scan.Warnings);
        ProcessReport? processReport = null;
        try
        {
            processReport = CaptureProcesses(cancellationToken);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            warnings.Add($"Prozessliste konnte nicht gelesen werden: {ex.Message}");
        }

        if (processReport is { InaccessibleProcesses: > 0 } processDetails)
        {
            warnings.Add($"{processDetails.InaccessibleProcesses} Prozess(e) konnten wegen fehlender Rechte oder zwischenzeitlichem Ende nicht vollständig gelesen werden.");
        }

        return new
        {
            capturedAt = DateTimeOffset.UtcNow,
            system = new
            {
                osDescription = Environment.OSVersion.ToString(),
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount,
                uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString("c"),
                activeProcessCount = status?.ActiveProcessCount,
                enumeratedProcessCount = processReport?.TotalProcesses,
                processes = processReport?.Processes,
                omittedProcesses = processReport?.OmittedProcesses,
                cpuUsagePercent = status?.CpuUsagePercent,
                totalMemoryBytes = status?.TotalMemoryBytes,
                availableMemoryBytes = status?.AvailableMemoryBytes,
                metricsCapturedAt = status?.CapturedAt,
                windowsProviderDisks = status?.Disks,
                volumes,
            },
            security = new
            {
                checkedDirectory = _watchedDirectory,
                directoryAvailable = Directory.Exists(_watchedDirectory),
                scanScope = "Nur Alices überwachtes Verzeichnis; transparente Dateinamens-Heuristiken, kein Antivirus- oder vollständiger Malware-Scan.",
                scannedFiles = scan.ScannedFiles,
                findings = scan.Findings,
                auditEventsRecorded = scan.Findings.Count,
                auditLogUpdated = true,
                automaticChangesMade = false,
                limitations = "Keine Windows-Defender-/Antivirus-Abfrage, kein vollständiger Datenträger- oder Malware-Scan.",
            },
            warnings,
        };
    }

    private async Task<ReadOnlyScanResult> RunReadOnlyHeuristicScanAsync(CancellationToken cancellationToken)
    {
        const int maxFiles = 20000;
        var findings = new List<ThreatEvent>();
        var warnings = new List<string>();
        var scannedFiles = 0;

        foreach (var filePath in EnumerateFilesSafe(_watchedDirectory, warnings.Add))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scannedFiles >= maxFiles)
            {
                warnings.Add($"Scan-Limit von {maxFiles} Dateien erreicht; weitere Dateien wurden nicht geprüft.");
                break;
            }

            scannedFiles++;
            foreach (var threatEvent in _heuristics.EvaluateFileCreatedOrChanged(filePath))
            {
                findings.Add(threatEvent);
                await _auditLog.AppendAsync(threatEvent, cancellationToken);
            }
        }

        return new ReadOnlyScanResult(scannedFiles, findings, warnings);
    }

    private sealed record ReadOnlyScanResult(
        int ScannedFiles,
        IReadOnlyList<ThreatEvent> Findings,
        IReadOnlyList<string> Warnings);

    private static ProcessReport CaptureProcesses(CancellationToken cancellationToken)
    {
        const int maxReportedProcesses = 500;
        var processes = Process.GetProcesses();
        var result = new List<ProcessSummary>(Math.Min(processes.Length, maxReportedProcesses));
        var inaccessibleProcesses = 0;
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (result.Count < maxReportedProcesses)
                    {
                        result.Add(new ProcessSummary(process.Id, process.ProcessName, process.WorkingSet64));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    inaccessibleProcesses++;
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return new ProcessReport(
            processes.Length,
            result,
            Math.Max(0, processes.Length - result.Count),
            inaccessibleProcesses);
    }

    private sealed record ProcessSummary(int ProcessId, string Name, long WorkingSetBytes);

    private sealed record ProcessReport(
        int TotalProcesses,
        IReadOnlyList<ProcessSummary> Processes,
        int OmittedProcesses,
        int InaccessibleProcesses);

    private static IEnumerable<string> EnumerateFilesSafe(
        string rootDirectory,
        Action<string>? warning = null)
    {
        if (!Directory.Exists(rootDirectory))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] subdirectories;

            try
            {
                files = Directory.GetFiles(directory);
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                warning?.Invoke($"Zugriff auf Verzeichnis nicht möglich: {directory} ({ex.Message})");
                continue;
            }
            catch (IOException ex)
            {
                warning?.Invoke($"Verzeichnis konnte nicht gelesen werden: {directory} ({ex.Message})");
                continue;
            }

            foreach (var file in files)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    warning?.Invoke($"Datei konnte nicht geprüft werden: {file} ({ex.Message})");
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    yield return file;
                }
            }

            foreach (var subdirectory in subdirectories)
            {
                try
                {
                    if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(subdirectory);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    warning?.Invoke($"Unterverzeichnis konnte nicht geprüft werden: {subdirectory} ({ex.Message})");
                    continue;
                }
            }
        }
    }
}
