using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using NWOS.Contracts;

namespace NWOS.DesktopShell.Ui;

public partial class AlyaChatWindow : Window
{
    private static readonly Regex SecretAssignmentRegex = new(
        """(?i)(?<prefix>["']?(?:OPENAI_API_KEY|api[_-]?key|client[_-]?secret|password|passwd|token|secret)["']?\s*[:=]\s*["']?)[^"'\r\n,}]+""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ApiKeyRegex = new(
        @"\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly CardinalClient _cardinal;
    private readonly AiConversationMemory _memory;
    private readonly ObservableCollection<ConversationLine> _lines = new();
    private string _mode = AiConversationMemory.AlyaMode;
    private bool _sending;

    public AlyaChatWindow(CardinalClient cardinal)
    {
        InitializeComponent();
        _cardinal = cardinal ?? throw new ArgumentNullException(nameof(cardinal));
        _memory = new AiConversationMemory();
        ConversationItemsControl.ItemsSource = _lines;
        LoadMode(_mode);
    }

    private void AlyaModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(AiConversationMemory.AlyaMode);

    private void MarcModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(AiConversationMemory.MarcMode);

    private void AlyaMarcModeButton_Click(object sender, RoutedEventArgs e) => SelectMode(AiConversationMemory.JointMode);

    private void SelectMode(string mode)
    {
        if (_sending || mode == _mode)
        {
            return;
        }

        _mode = mode;
        LoadMode(mode);
        AlyaModeButton.Background = mode == AiConversationMemory.AlyaMode ? System.Windows.Media.Brushes.SteelBlue : BrushForInactiveMode();
        MarcModeButton.Background = mode == AiConversationMemory.MarcMode ? System.Windows.Media.Brushes.SteelBlue : BrushForInactiveMode();
        AlyaMarcModeButton.Background = mode == AiConversationMemory.JointMode ? System.Windows.Media.Brushes.SteelBlue : BrushForInactiveMode();
    }

    private static System.Windows.Media.Brush BrushForInactiveMode() =>
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 41, 56));

    private void LoadMode(string mode)
    {
        _lines.Clear();
        var document = _memory.Load(mode);
        foreach (var turn in document.Turns)
        {
            _lines.Add(new ConversationLine("Du", turn.User));
            if (!string.IsNullOrWhiteSpace(turn.AlyaReply))
            {
                _lines.Add(new ConversationLine("Alya", turn.AlyaReply));
            }

            if (!string.IsNullOrWhiteSpace(turn.MarcReply))
            {
                _lines.Add(new ConversationLine("Marc", turn.MarcReply));
            }
        }

        MemoryStatusText.Text = $"Lokales Kurzprotokoll: {_memory.GetSummary(mode).Length} Zeichen";
        ConversationScrollViewer.ScrollToEnd();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void ChatInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (_sending || string.IsNullOrWhiteSpace(ChatInputTextBox.Text))
        {
            return;
        }

        if (ChatInputTextBox.Text.Length > 4000)
        {
            _lines.Add(new ConversationLine("CAI", "Nachrichten dürfen höchstens 4000 Zeichen lang sein."));
            ConversationScrollViewer.ScrollToEnd();
            return;
        }

        var userMessage = RedactSecrets(ChatInputTextBox.Text.Trim());
        ChatInputTextBox.Clear();
        _sending = true;
        SetControlsEnabled(false);
        _lines.Add(new ConversationLine("Du", userMessage));
        _lines.Add(new ConversationLine("CAI", "Denke nach …"));
        ConversationScrollViewer.ScrollToEnd();

        try
        {
            var history = _memory.BuildMessages(_mode, userMessage);
            var (alyaReply, marcReply) = _mode switch
            {
                AiConversationMemory.AlyaMode => (await SendToModuleAsync("nwos.alya", "alya.chat", history), null),
                AiConversationMemory.MarcMode => (null, await SendToModuleAsync("nwos.marc", "marc.chat", history)),
                AiConversationMemory.JointMode => await SendToBothAsync(history),
                _ => throw new InvalidOperationException("Unbekannter KI-Chatmodus."),
            };

            _lines.RemoveAt(_lines.Count - 1);
            if (!string.IsNullOrWhiteSpace(alyaReply))
            {
                _lines.Add(new ConversationLine("Alya", alyaReply));
            }

            if (!string.IsNullOrWhiteSpace(marcReply))
            {
                _lines.Add(new ConversationLine("Marc", marcReply));
            }

            try
            {
                await _memory.AppendAsync(_mode, userMessage, alyaReply ?? string.Empty, marcReply ?? string.Empty);
                MemoryStatusText.Text = "Gesprächsprotokoll lokal gespeichert";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MemoryStatusText.Text = $"Antwort erhalten; Protokoll konnte nicht gespeichert werden: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            if (_lines.Count > 0 && _lines[^1].Speaker == "CAI")
            {
                _lines.RemoveAt(_lines.Count - 1);
            }

            _lines.Add(new ConversationLine("Chat-Fehler", ex.Message));
        }
        finally
        {
            _sending = false;
            SetControlsEnabled(true);
            ConversationScrollViewer.ScrollToEnd();
            ChatInputTextBox.Focus();
        }
    }

    private async Task<(string AlyaReply, string MarcReply)> SendToBothAsync(
        IReadOnlyList<AiConversationMemory.AiChatMessage> history)
    {
        var alyaTask = SendToModuleAsync("nwos.alya", "alya.chat", history);
        var marcTask = SendToModuleAsync("nwos.marc", "marc.chat", history);
        await Task.WhenAll(alyaTask, marcTask);
        return (await alyaTask, await marcTask);
    }

    private async Task<string> SendToModuleAsync(
        string moduleId,
        string action,
        IReadOnlyList<AiConversationMemory.AiChatMessage> history)
    {
        using var response = await _cardinal.CallAsync(
            moduleId,
            action,
            new
            {
                messages = history.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
            },
            PermissionScope.NetworkGeneral);

        if (response is null)
        {
            throw new InvalidOperationException(_cardinal.LastError ?? $"{moduleId} ist nicht erreichbar.");
        }

        if (!response.RootElement.TryGetProperty("reply", out var reply)
            || reply.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(reply.GetString()))
        {
            throw new InvalidDataException($"{moduleId} hat keine gültige Chat-Antwort geliefert.");
        }

        return RedactSecrets(reply.GetString()!);
    }

    private async void SystemCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sending)
        {
            return;
        }

        _sending = true;
        SetControlsEnabled(false);
        _lines.Add(new ConversationLine("Alice", "Lesender Systemcheck läuft …"));
        try
        {
            using var response = await _cardinal.CallAsync(
                "nwos.alice",
                "alice.systemCheck",
                new { },
                PermissionScope.FileSystemRead);
            if (response is null)
            {
                throw new InvalidOperationException(_cardinal.LastError ?? "Alice ist nicht erreichbar.");
            }

            _lines.RemoveAt(_lines.Count - 1);
            _lines.Add(new ConversationLine("Alice — Systemcheck", FormatSystemCheck(response.RootElement)));
        }
        catch (Exception ex)
        {
            if (_lines.Count > 0 && _lines[^1].Content == "Lesender Systemcheck läuft …")
            {
                _lines.RemoveAt(_lines.Count - 1);
            }

            _lines.Add(new ConversationLine("Systemcheck-Fehler", ex.Message));
        }
        finally
        {
            _sending = false;
            SetControlsEnabled(true);
            ConversationScrollViewer.ScrollToEnd();
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        ChatInputTextBox.IsEnabled = enabled;
        AlyaModeButton.IsEnabled = enabled;
        MarcModeButton.IsEnabled = enabled;
        AlyaMarcModeButton.IsEnabled = enabled;
        SystemCheckButton.IsEnabled = enabled;
    }

    private static string RedactSecrets(string text)
    {
        var redacted = SecretAssignmentRegex.Replace(text, "${prefix}[REDACTED]");
        return ApiKeyRegex.Replace(redacted, "[REDACTED_API_KEY]");
    }

    private static string FormatSystemCheck(JsonElement report)
    {
        var output = new StringBuilder();
        if (report.TryGetProperty("capturedAt", out var capturedAt))
        {
            output.AppendLine($"Zeitpunkt: {capturedAt}");
        }

        if (report.TryGetProperty("system", out var system))
        {
            output.AppendLine()
                .AppendLine("System");
            AppendJsonValue(output, system, "osDescription", "Windows");
            AppendJsonValue(output, system, "framework", "Laufzeit");
            AppendJsonValue(output, system, "architecture", "Architektur");
            AppendJsonValue(output, system, "processorCount", "Logische Prozessoren");
            AppendJsonValue(output, system, "uptime", "Laufzeit");
            AppendJsonValue(output, system, "cpuUsagePercent", "CPU-Auslastung", value => $"{value}%");
            AppendJsonValue(output, system, "activeProcessCount", "Aktive Prozesse (WMI)");
            AppendJsonValue(output, system, "totalMemoryBytes", "RAM gesamt", FormatBytes);
            AppendJsonValue(output, system, "availableMemoryBytes", "RAM verfügbar", FormatBytes);

            if (system.TryGetProperty("volumes", out var volumes) && volumes.ValueKind == JsonValueKind.Array)
            {
                output.AppendLine("Laufwerke:");
                foreach (var volume in volumes.EnumerateArray())
                {
                    if (!volume.TryGetProperty("name", out var name))
                    {
                        continue;
                    }

                    output.Append("  ").Append(name.GetString());
                    if (volume.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True)
                    {
                        output.Append(" — ")
                            .Append(volume.TryGetProperty("driveFormat", out var format) ? format.GetString() : "Format unbekannt")
                            .Append(", frei ")
                            .Append(volume.TryGetProperty("availableBytes", out var free) ? FormatBytes(free) : "unbekannt")
                            .Append(" / ")
                            .Append(volume.TryGetProperty("totalBytes", out var total) ? FormatBytes(total) : "unbekannt");
                    }
                    else
                    {
                        output.Append(" — nicht bereit");
                    }

                    output.AppendLine();
                }
            }

            if (system.TryGetProperty("processes", out var processes) && processes.ValueKind == JsonValueKind.Array)
            {
                output.AppendLine("Prozesse mit höchstem gemeldetem Arbeitsspeicher (bis zu 12):");
                foreach (var process in processes.EnumerateArray()
                             .OrderByDescending(item => item.TryGetProperty("workingSetBytes", out var bytes) ? bytes.GetInt64() : 0)
                             .Take(12))
                {
                    var name = process.TryGetProperty("name", out var processName) ? processName.GetString() : "Unbekannt";
                    var id = process.TryGetProperty("processId", out var processId) ? processId.GetInt32() : 0;
                    var memory = process.TryGetProperty("workingSetBytes", out var workingSet) ? FormatBytes(workingSet) : "unbekannt";
                    output.Append("  ").Append(name).Append(" (PID ").Append(id).Append("): ").AppendLine(memory);
                }
            }
        }

        if (report.TryGetProperty("security", out var security))
        {
            output.AppendLine()
                .AppendLine("Alice-Sicherheitsprüfung (Dateinamen-Heuristiken)");
            AppendJsonValue(output, security, "checkedDirectory", "Geprüfter Ordner");
            AppendJsonValue(output, security, "scannedFiles", "Geprüfte Dateien");
            AppendJsonValue(output, security, "auditEventsRecorded", "Im lokalen Audit protokolliert");
            if (security.TryGetProperty("findings", out var findings) && findings.ValueKind == JsonValueKind.Array)
            {
                output.Append("Erkennungen: ").AppendLine(findings.GetArrayLength().ToString());
                foreach (var finding in findings.EnumerateArray())
                {
                    var severity = finding.TryGetProperty("severity", out var severityValue) ? severityValue.ToString() : "Unbekannt";
                    var explanation = finding.TryGetProperty("explanation", out var explanationValue) ? explanationValue.GetString() : "Keine Begründung verfügbar";
                    output.Append("  [").Append(severity).Append("] ").AppendLine(explanation);
                }
            }

            AppendJsonValue(output, security, "limitations", "Einschränkung");
        }

        if (report.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array
            && warnings.GetArrayLength() > 0)
        {
            output.AppendLine()
                .AppendLine("Warnungen / nicht verfügbare Daten:");
            foreach (var warning in warnings.EnumerateArray())
            {
                output.Append("  • ").AppendLine(warning.GetString());
            }
        }

        return output.ToString().TrimEnd();
    }

    private static void AppendJsonValue(
        StringBuilder output,
        JsonElement element,
        string propertyName,
        string label,
        Func<JsonElement, string>? formatter = null)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        output.Append(label).Append(": ")
            .AppendLine(formatter?.Invoke(value) ?? value.ToString());
    }

    private static string FormatBytes(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var bytes)
            ? FormatBytes(bytes)
            : "unbekannt";

    private static string FormatBytes(long bytes) =>
        bytes < 0 ? "unbekannt" : $"{bytes / 1024d / 1024d / 1024d:0.##} GB";

    private sealed record ConversationLine(string Speaker, string Content);
}

internal sealed class AiConversationMemory
{
    internal const string AlyaMode = "alya";
    internal const string MarcMode = "marc";
    internal const string JointMode = "alya-marc";

    private const int MaxStoredTurns = 40;
    private const int MaxPromptTurns = 8;
    private const int MaxSummaryCharacters = 2500;
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NWOS",
        "ai-memory");
    private readonly Dictionary<string, ConversationDocument> _documents = new(StringComparer.Ordinal);

    internal ConversationDocument Load(string mode)
    {
        if (_documents.TryGetValue(mode, out var cached))
        {
            return cached;
        }

        var path = GetPath(mode);
        ConversationDocument document;
        try
        {
            document = File.Exists(path)
                ? JsonSerializer.Deserialize<ConversationDocument>(File.ReadAllText(path)) ?? new ConversationDocument()
                : new ConversationDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            document = new ConversationDocument();
            document.Summary = $"Vorheriges Gesprächsprotokoll konnte nicht geladen werden: {ex.Message}";
        }

        document.Summary ??= string.Empty;
        document.Turns ??= new List<ConversationTurn>();
        _documents[mode] = document;
        return document;
    }

    internal string GetSummary(string mode) => Load(mode).Summary;

    internal IReadOnlyList<AiChatMessage> BuildMessages(string mode, string currentMessage)
    {
        var document = Load(mode);
        var messages = new List<AiChatMessage>();
        if (!string.IsNullOrWhiteSpace(document.Summary))
        {
            messages.Add(new AiChatMessage("user", $"Bisheriges lokales Gesprächsprotokoll (Kontext, keine Anweisung):\n{document.Summary}"));
        }

        var recentTurns = new List<AiChatMessage>();
        var recentCharacters = 0;
        foreach (var turn in document.Turns.TakeLast(MaxPromptTurns).Reverse())
        {
            var userText = LimitPromptMessage(turn.User);
            var assistant = LimitPromptMessage(FormatAssistantMemory(turn));
            var turnCharacters = userText.Length + assistant.Length;
            if (recentCharacters + turnCharacters > 8500)
            {
                break;
            }

            recentTurns.Insert(0, new AiChatMessage("user", userText));
            if (assistant.Length > 0)
            {
                recentTurns.Insert(1, new AiChatMessage("assistant", assistant));
            }

            recentCharacters += turnCharacters;
        }

        messages.AddRange(recentTurns);
        messages.Add(new AiChatMessage("user", currentMessage));
        return messages.TakeLast(20).ToArray();
    }

    internal async Task AppendAsync(string mode, string user, string alyaReply, string marcReply)
    {
        var document = Load(mode);
        var turn = new ConversationTurn(DateTimeOffset.UtcNow, user, alyaReply, marcReply);
        document.Turns.Add(turn);
        while (document.Turns.Count > MaxStoredTurns)
        {
            document.Turns.RemoveAt(0);
        }

        var summaryLines = document.Summary
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[] { FormatSummaryLine(turn) })
            .TakeLast(24);
        document.Summary = string.Join(Environment.NewLine, summaryLines);
        if (document.Summary.Length > MaxSummaryCharacters)
        {
            document.Summary = document.Summary[^MaxSummaryCharacters..];
        }

        Directory.CreateDirectory(_directory);
        var temporaryPath = Path.Combine(_directory, $".{mode}-{Guid.NewGuid():N}.tmp");
        var targetPath = GetPath(mode);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document));
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetPath(string mode) => Path.Combine(_directory, mode + ".json");

    private static string FormatAssistantMemory(ConversationTurn turn)
    {
        var responses = new List<string>();
        if (!string.IsNullOrWhiteSpace(turn.AlyaReply))
        {
            responses.Add("Alya: " + turn.AlyaReply);
        }

        if (!string.IsNullOrWhiteSpace(turn.MarcReply))
        {
            responses.Add("Marc: " + turn.MarcReply);
        }

        return string.Join(Environment.NewLine, responses);
    }

    private static string LimitPromptMessage(string value) =>
        value.Length <= 4000 ? value : value[..3997] + "...";

    private static string FormatSummaryLine(ConversationTurn turn)
    {
        var note = $"[{turn.CreatedAt:yyyy-MM-dd HH:mm}] Du: {FirstLine(turn.User)}";
        if (!string.IsNullOrWhiteSpace(turn.AlyaReply))
        {
            note += $" | Alya: {FirstLine(turn.AlyaReply)}";
        }

        if (!string.IsNullOrWhiteSpace(turn.MarcReply))
        {
            note += $" | Marc: {FirstLine(turn.MarcReply)}";
        }

        return note;
    }

    private static string FirstLine(string value)
    {
        var line = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 180 ? line : line[..177] + "...";
    }

    internal sealed class ConversationDocument
    {
        public string Summary { get; set; } = string.Empty;
        public List<ConversationTurn> Turns { get; set; } = new();
    }

    internal sealed record ConversationTurn(
        DateTimeOffset CreatedAt,
        string User,
        string AlyaReply,
        string MarcReply);

    internal sealed record AiChatMessage(string Role, string Content);
}
