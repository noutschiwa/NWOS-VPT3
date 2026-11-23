using System.Diagnostics;
using System.IO.Pipes;
using NWOS.Contracts;

namespace NWOS.Ipc;

/// <summary>
/// Client-Gegenstück zu <see cref="IpcServer"/>. Implementiert
/// <see cref="IModuleMessenger"/> - ein externer Prozess (z. B. eine
/// künftige Version von NWOS.DesktopShell.Ui) kann diese Klasse genauso
/// verwenden wie ein In-Process-Modul den von Cardinal injizierten
/// Messenger: <c>await client.SendAsync(message, ct)</c>, ohne wissen zu
/// müssen, dass darunter eine Named Pipe steckt.
///
/// Verbindet sich pro Aufruf neu (siehe Begründung in <see cref="IpcServer"/>) -
/// einfach, robust gegenüber zwischenzeitlich neu gestartetem Cardinal-
/// Prozess, aber nicht die effizienteste Variante für sehr viele Anfragen
/// kurz hintereinander (TODO, siehe IpcServer).
/// </summary>
public sealed class IpcClient : IModuleMessenger
{
    private readonly string _serverName;
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _requestTimeout;

    private static readonly TimeSpan ConnectAttemptInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromMilliseconds(100);

    public IpcClient(
        string? serverName = null,
        string? pipeName = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? requestTimeout = null)
    {
        _serverName = serverName ?? ".";
        _pipeName = pipeName ?? IpcDefaults.CardinalPipeName;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(30);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(60);
        if (_connectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        }

        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public Task<ModuleMessageResult> SendAsync(ModuleMessage message, CancellationToken cancellationToken) =>
        SendAsync(message, cancellationToken, _requestTimeout);

    public async Task<ModuleMessageResult> SendAsync(
        ModuleMessage message,
        CancellationToken cancellationToken,
        TimeSpan requestTimeout)
    {
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(requestTimeout);
        NamedPipeClientStream? pipeClient;
        try
        {
            pipeClient = await ConnectAsync(requestCts.Token);
        }
        catch (OperationCanceledException) when (
            requestCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return RequestTimedOut(message, requestTimeout);
        }

        if (pipeClient is null)
        {
            return new ModuleMessageResult(
                ModuleMessageOutcome.ModuleFailure, null,
                $"Konnte innerhalb von {_connectTimeout.TotalSeconds}s keine Verbindung zu Cardinal (Pipe '{_pipeName}') herstellen. Prüfen Sie, ob NWOS/Cardinal läuft.");
        }

        await using (pipeClient)
        {
            try
            {
                await PipeFraming.WriteJsonAsync(pipeClient, message, requestCts.Token);
                return await PipeFraming.ReadJsonAsync<ModuleMessageResult>(pipeClient, requestCts.Token);
            }
            catch (OperationCanceledException) when (
                requestCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return RequestTimedOut(message, requestTimeout);
            }
        }
    }

    private async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < _connectTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = _connectTimeout - stopwatch.Elapsed;
            var attemptTimeout = remaining < ConnectAttemptInterval ? remaining : ConnectAttemptInterval;
            var pipeClient = new NamedPipeClientStream(
                _serverName, _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipeClient.ConnectAsync(
                    Math.Max(1, (int)Math.Ceiling(attemptTimeout.TotalMilliseconds)),
                    cancellationToken);
                return pipeClient;
            }
            catch (TimeoutException)
            {
                await pipeClient.DisposeAsync();
            }
            catch
            {
                await pipeClient.DisposeAsync();
                throw;
            }

            remaining = _connectTimeout - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(
                    remaining < ConnectRetryDelay ? remaining : ConnectRetryDelay,
                    cancellationToken);
            }
        }

        return null;
    }

    private static ModuleMessageResult RequestTimedOut(ModuleMessage message, TimeSpan requestTimeout) =>
        new(
            ModuleMessageOutcome.ModuleFailure, null,
            $"Die IPC-Anfrage an '{message.TargetModuleId}' wurde nach {requestTimeout.TotalSeconds}s wegen Zeitüberschreitung abgebrochen.");
}
