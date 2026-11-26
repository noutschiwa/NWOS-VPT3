using System.Text.Json;
using NWOS.Contracts;
using NWOS.SystemCenter;
using Xunit;

namespace NWOS.Alice.Tests;

public class AliceModuleTests
{
    [Fact]
    public async Task CheckHealthAsync_is_degraded_when_watched_directory_does_not_exist()
    {
        var module = new AliceModule(
            watchedDirectory: Path.Combine(Path.GetTempPath(), "nwos-does-not-exist-" + Guid.NewGuid().ToString("N")),
            stateDirectory: CreateTempDir());

        await module.StartAsync(CancellationToken.None);
        var health = await module.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ModuleStatus.Degraded, health.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_is_healthy_once_watching_a_real_directory()
    {
        var module = new AliceModule(watchedDirectory: CreateTempDir(), stateDirectory: CreateTempDir());

        await module.StartAsync(CancellationToken.None);
        var health = await module.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ModuleStatus.Healthy, health.Status);
        await module.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Creating_a_double_extension_file_in_watched_directory_results_in_an_auto_quarantine_audit_entry()
    {
        var watchedDir = CreateTempDir();
        var stateDir = CreateTempDir();
        var module = new AliceModule(watchedDir, stateDir);
        await module.StartAsync(CancellationToken.None);

        var suspiciousFile = Path.Combine(watchedDir, "rechnung.pdf.exe");
        File.WriteAllText(suspiciousFile, "dummy");

        // FileSystemWatcher-Callbacks sind asynchron auf einem Hintergrund-
        // Thread - kurzes Polling statt fixem Sleep, um Flakiness zu minimieren.
        var found = await WaitForConditionAsync(async () =>
        {
            var response = await module.HandleMessageAsync(
                ModuleMessage.Create("test", module.ModuleId, "alice.getRecentEvents", "{\"count\":10}", PermissionScope.None),
                CancellationToken.None);

            using var doc = JsonDocument.Parse(response!.PayloadJson);
            return doc.RootElement.EnumerateArray().Any(e =>
                e.GetProperty("ruleId").GetString() == "double-extension-executable");
        }, TimeSpan.FromSeconds(5));

        Assert.True(found, "Erwartetes Audit-Log-Ereignis für double-extension-executable wurde nicht innerhalb des Timeouts gefunden.");

        await module.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Unknown_message_type_throws()
    {
        var module = new AliceModule(CreateTempDir(), CreateTempDir());
        await module.StartAsync(CancellationToken.None);

        var message = ModuleMessage.Create("test", module.ModuleId, "does.not.exist", "{}", PermissionScope.None);

        await Assert.ThrowsAsync<NotSupportedException>(() => module.HandleMessageAsync(message, CancellationToken.None));
    }

    [Fact]
    public async Task System_check_reports_real_metrics_and_does_not_quarantine_findings()
    {
        var watchedDir = CreateTempDir();
        File.WriteAllText(Path.Combine(watchedDir, "invoice.pdf.exe"), "not opened by scan");
        var module = new AliceModule(
            watchedDir,
            CreateTempDir(),
            systemStatusProvider: new FakeSystemStatusProvider());

        var response = await module.HandleMessageAsync(
            ModuleMessage.Create("test", module.ModuleId, "alice.systemCheck", "{}", PermissionScope.FileSystemRead),
            CancellationToken.None);

        using var report = JsonDocument.Parse(response!.PayloadJson);
        Assert.Equal(42.5, report.RootElement.GetProperty("system").GetProperty("cpuUsagePercent").GetDouble());
        Assert.Equal(2, report.RootElement.GetProperty("system").GetProperty("activeProcessCount").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("security").GetProperty("scannedFiles").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("security").GetProperty("findings").GetArrayLength());
        Assert.Equal(1, report.RootElement.GetProperty("security").GetProperty("auditEventsRecorded").GetInt32());
        Assert.False(report.RootElement.GetProperty("security").GetProperty("automaticChangesMade").GetBoolean());
        Assert.True(File.Exists(Path.Combine(watchedDir, "invoice.pdf.exe")));
    }

    private static async Task<bool> WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nwos-alice-module-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class FakeSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SystemStatusSnapshot(
                42.5,
                16_000_000_000,
                8_000_000_000,
                Array.Empty<DiskStatus>(),
                2,
                DateTimeOffset.UtcNow));
    }
}
