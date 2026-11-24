using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NWOS.Cardinal;
using NWOS.Contracts;
using Xunit;

namespace NWOS.Ipc.Tests;

[SupportedOSPlatform("windows")]
internal sealed class RecordingMessenger : IModuleMessenger
{
    public ModuleMessage? LastReceived { get; private set; }
    public ModuleMessageResult ResultToReturn { get; set; } =
        new(ModuleMessageOutcome.Delivered, null, null);

    public Task<ModuleMessageResult> SendAsync(ModuleMessage message, CancellationToken cancellationToken)
    {
        LastReceived = message;
        return Task.FromResult(ResultToReturn);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class ControlledMessenger : IModuleMessenger
{
    public TaskCompletionSource<bool> SlowRequestStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> ReleaseSlowRequest { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ModuleMessageResult> SendAsync(ModuleMessage message, CancellationToken cancellationToken)
    {
        if (message.MessageType == "slow")
        {
            SlowRequestStarted.TrySetResult(true);
            await ReleaseSlowRequest.Task;
        }

        return new ModuleMessageResult(ModuleMessageOutcome.Delivered, null, null);
    }
}

[SupportedOSPlatform("windows")]
public class IpcServerClientTests
{
    // Jeder Test nutzt einen eigenen, zufälligen Pipe-Namen, damit parallel
    // laufende Tests sich nicht gegenseitig stören.
    private static string UniquePipeName() => "nwos-ipc-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Client_request_reaches_server_side_messenger_unchanged()
    {
        var pipeName = UniquePipeName();
        var messenger = new RecordingMessenger
        {
            ResultToReturn = new ModuleMessageResult(ModuleMessageOutcome.Delivered,
                ModuleMessage.Create("nwos.alice", "test", "reply", "{}", PermissionScope.None), null),
        };

        var server = new IpcServer(messenger, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);

        var client = new IpcClient(pipeName: pipeName);
        var request = ModuleMessage.Create("test-caller", "nwos.alice", "alice.getRecentEvents", "{\"count\":5}", PermissionScope.None);

        var result = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(ModuleMessageOutcome.Delivered, result.Outcome);
        Assert.NotNull(messenger.LastReceived);
        Assert.Equal(request.MessageId, messenger.LastReceived!.MessageId);
        Assert.Equal(request.TargetModuleId, messenger.LastReceived.TargetModuleId);
        Assert.Equal(request.MessageType, messenger.LastReceived.MessageType);

        serverCts.Cancel();
        await SafeAwaitAsync(serverTask);
    }

    [Fact]
    public async Task Denied_outcome_and_reason_survive_the_round_trip()
    {
        var pipeName = UniquePipeName();
        var messenger = new RecordingMessenger
        {
            ResultToReturn = new ModuleMessageResult(ModuleMessageOutcome.Denied, null, "Scope nicht gewährt."),
        };

        var server = new IpcServer(messenger, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);

        var client = new IpcClient(pipeName: pipeName);
        var request = ModuleMessage.Create("test-caller", "nwos.marc", "marc.launchApp", "{}", PermissionScope.ProcessControl);

        var result = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(ModuleMessageOutcome.Denied, result.Outcome);
        Assert.Equal("Scope nicht gewährt.", result.Reason);

        serverCts.Cancel();
        await SafeAwaitAsync(serverTask);
    }

    [Fact]
    public async Task Server_routes_through_task_router_and_returns_a_response()
    {
        var pipeName = UniquePipeName();
        var registry = new ModuleRegistry();
        registry.GrantExternalScope(IpcDefaults.ExternalCallerIdentity, PermissionScope.FileSystemRead);
        var permissions = new PermissionManager(registry, NullLogger<PermissionManager>.Instance);
        var router = new TaskRouter(registry, permissions, NullLogger<TaskRouter>.Instance);
        var server = new IpcServer(router, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);

        try
        {
            var request = ModuleMessage.Create(
                "test-caller",
                "nwos.missing",
                "test.action",
                "{}",
                PermissionScope.FileSystemRead);
            var result = await new IpcClient(pipeName: pipeName)
                .SendAsync(request, CancellationToken.None);

            Assert.Equal(ModuleMessageOutcome.Denied, result.Outcome);
            Assert.Contains("nwos.missing", result.Reason);
        }
        finally
        {
            serverCts.Cancel();
            await SafeAwaitAsync(serverTask);
        }
    }

    [Fact]
    public async Task Multiple_sequential_requests_are_each_handled_correctly()
    {
        var pipeName = UniquePipeName();
        var messenger = new RecordingMessenger();

        var server = new IpcServer(messenger, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);

        var client = new IpcClient(pipeName: pipeName);

        for (var i = 0; i < 3; i++)
        {
            var request = ModuleMessage.Create("test-caller", "nwos.marc", $"marc.action{i}", "{}", PermissionScope.None);
            var result = await client.SendAsync(request, CancellationToken.None);

            Assert.Equal(ModuleMessageOutcome.Delivered, result.Outcome);
            Assert.Equal($"marc.action{i}", messenger.LastReceived!.MessageType);
        }

        serverCts.Cancel();
        await SafeAwaitAsync(serverTask);
    }

    [Fact]
    public async Task Client_returns_ModuleFailure_when_no_server_is_listening()
    {
        var client = new IpcClient(pipeName: UniquePipeName(), connectTimeout: TimeSpan.FromMilliseconds(300));
        var request = ModuleMessage.Create("test-caller", "nwos.marc", "marc.createFolder", "{}", PermissionScope.None);

        var result = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(ModuleMessageOutcome.ModuleFailure, result.Outcome);
        Assert.Contains("Cardinal", result.Reason);
    }

    [Fact]
    public async Task Client_connects_when_server_starts_during_connection_retry_window()
    {
        var pipeName = UniquePipeName();
        var server = new IpcServer(new RecordingMessenger(), pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = Task.Run(async () =>
        {
            await Task.Delay(700);
            await server.RunAsync(serverCts.Token);
        });
        var client = new IpcClient(
            pipeName: pipeName,
            connectTimeout: TimeSpan.FromSeconds(3));

        try
        {
            var result = await client.SendAsync(
                ModuleMessage.Create("test-caller", "nwos.alice", "alice.manualScan", "{}", PermissionScope.None),
                CancellationToken.None);

            Assert.Equal(ModuleMessageOutcome.Delivered, result.Outcome);
        }
        finally
        {
            serverCts.Cancel();
            await SafeAwaitAsync(serverTask);
        }
    }

    [Fact]
    public async Task Slow_request_does_not_block_another_system_shell_request()
    {
        var pipeName = UniquePipeName();
        var messenger = new ControlledMessenger();
        var server = new IpcServer(messenger, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);
        var client = new IpcClient(pipeName: pipeName);

        try
        {
            var slowRequest = client.SendAsync(
                ModuleMessage.Create("test-caller", "nwos.alice", "slow", "{}", PermissionScope.None),
                CancellationToken.None);
            await messenger.SlowRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var fastResult = await client.SendAsync(
                    ModuleMessage.Create("test-caller", "nwos.desktopshell", "fast", "{}", PermissionScope.None),
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(ModuleMessageOutcome.Delivered, fastResult.Outcome);
            messenger.ReleaseSlowRequest.TrySetResult(true);
            Assert.Equal(ModuleMessageOutcome.Delivered, (await slowRequest).Outcome);
        }
        finally
        {
            messenger.ReleaseSlowRequest.TrySetResult(true);
            serverCts.Cancel();
            await SafeAwaitAsync(serverTask);
        }
    }

    [Fact]
    public async Task Client_returns_ModuleFailure_when_response_exceeds_request_timeout()
    {
        var pipeName = UniquePipeName();
        var messenger = new ControlledMessenger();
        var server = new IpcServer(messenger, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);
        var client = new IpcClient(pipeName: pipeName);

        try
        {
            var result = await client.SendAsync(
                ModuleMessage.Create("test-caller", "nwos.alice", "slow", "{}", PermissionScope.None),
                CancellationToken.None,
                TimeSpan.FromMilliseconds(200));

            Assert.Equal(ModuleMessageOutcome.ModuleFailure, result.Outcome);
            Assert.Contains("Zeitüberschreitung", result.Reason);
        }
        finally
        {
            messenger.ReleaseSlowRequest.TrySetResult(true);
            serverCts.Cancel();
            await SafeAwaitAsync(serverTask);
        }
    }

    private static async Task SafeAwaitAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Erwarteter Abschluss nach Cancel().
        }
    }
}