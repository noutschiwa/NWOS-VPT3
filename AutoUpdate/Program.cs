using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace NWOS.AutoUpdate;

internal static class Program
{
    private const string RepositoryApiUrl = "https://api.github.com/repos/noutschiwa/NWOS-VPT3/commits/main";
    private const string ArchiveUrlTemplate = "https://github.com/noutschiwa/NWOS-VPT3/archive/{0}.zip";
    private static readonly string InstalledCommitPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NWOS",
        "autoupdate.commit");

    public static async Task<int> Main()
    {
        try
        {
            string? remoteCommit = await GetRemoteCommitAsync();
            if (string.IsNullOrWhiteSpace(remoteCommit))
            {
                Console.WriteLine("[NWOS Update] Versionsprüfung nicht verfügbar; NWOS startet ohne Update.");
                return 0;
            }

            string? installedCommit = ReadInstalledCommit();
            if (string.Equals(installedCommit, remoteCommit, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[NWOS Update] NWOS ist aktuell.");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(installedCommit))
            {
                Console.WriteLine("[NWOS Update] Installierte Version ist nicht bekannt; die Commit-ID wird beim nächsten Installationsvorgang gespeichert.");
                return 0;
            }

            Console.WriteLine($"[NWOS Update] Neue Version gefunden ({remoteCommit[..12]}). Installation wird vorbereitet.");
            return await InstallUpdateAsync(remoteCommit) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NWOS Update] Prüfung fehlgeschlagen: {ex.Message}");
            return 0;
        }
    }

    private static async Task<string?> GetRemoteCommitAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NWOS-AutoUpdate/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using HttpResponseMessage response = await client.GetAsync(RepositoryApiUrl);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[NWOS Update] GitHub antwortete mit {(int)response.StatusCode}.");
            return null;
        }

        await using Stream content = await response.Content.ReadAsStreamAsync();
        using JsonDocument document = await JsonDocument.ParseAsync(content);
        return document.RootElement.TryGetProperty("sha", out JsonElement sha) ? sha.GetString() : null;
    }

    private static string? ReadInstalledCommit()
    {
        if (!File.Exists(InstalledCommitPath))
            return null;

        string commit = File.ReadAllText(InstalledCommitPath).Trim();
        return string.IsNullOrWhiteSpace(commit) ? null : commit;
    }

    private static async Task<bool> InstallUpdateAsync(string commit)
    {
        string workingDirectory = Path.Combine(Path.GetTempPath(), "NWOS-AutoUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            string archivePath = Path.Combine(workingDirectory, "update.zip");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NWOS-AutoUpdate/1.0");
            await using (Stream source = await client.GetStreamAsync(string.Format(ArchiveUrlTemplate, commit)))
            await using (FileStream destination = File.Create(archivePath))
            {
                await source.CopyToAsync(destination);
            }

            string sourceDirectory = Path.Combine(workingDirectory, "source");
            ZipFile.ExtractToDirectory(archivePath, sourceDirectory);
            string? installer = Directory.EnumerateFiles(sourceDirectory, "Install-NWOS.ps1", SearchOption.AllDirectories).SingleOrDefault();
            if (installer is null)
            {
                Console.WriteLine("[NWOS Update] Das Update-Archiv enthält keinen Installer.");
                return false;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{installer}\" -Quiet -NoLaunch -VersionCommit {commit}",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(installer)!
            };

            using System.Diagnostics.Process? installerProcess = System.Diagnostics.Process.Start(startInfo);
            if (installerProcess is null)
            {
                Console.WriteLine("[NWOS Update] Installer konnte nicht gestartet werden.");
                return false;
            }

            await installerProcess.WaitForExitAsync();
            if (installerProcess.ExitCode != 0)
            {
                Console.WriteLine($"[NWOS Update] Installer fehlgeschlagen (Exit-Code {installerProcess.ExitCode}).");
                return false;
            }

            Console.WriteLine("[NWOS Update] Update erfolgreich installiert.");
            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Der temporäre Ordner wird beim nächsten Systemstart bereinigt.
            }
        }
    }
}
