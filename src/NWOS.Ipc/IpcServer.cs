using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NWOS.Contracts;
using NWOS.Cardinal;

namespace NWOS.Ipc;

public static class IpcDefaults
{
    public const string CardinalPipeName = "NWOS.Cardinal";
    public const string ExternalCallerIdentity = "NWOS.ExternalCaller";
}

[SupportedOSPlatform("windows")]
public class IpcServer
{
    private const int MaxConcurrentClients = 16;
    private readonly IModuleMessenger? _messenger;
    private readonly TaskRouter? _taskRouter;
    private readonly string _pipeName;
    private readonly ILogger<IpcServer>? _logger;
    private readonly SemaphoreSlim _clientSlots = new(MaxConcurrentClients, MaxConcurrentClients);

    private IModuleMessenger ActiveMessenger => _taskRouter != null ? (IModuleMessenger)(object)_taskRouter : (_messenger ?? throw new InvalidOperationException("Kein Messenger konfiguriert."));

    public IpcServer(TaskRouter taskRouter, string pipeName, ILogger<IpcServer>? logger = null)
    {
        _taskRouter = taskRouter;
        _pipeName = pipeName;
        _logger = logger;
    }

    public IpcServer(IModuleMessenger messenger, string pipeName, ILogger<IpcServer>? logger = null)
    {
        _messenger = messenger;
        _pipeName = pipeName;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clients = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            var slotAcquired = false;
            try
            {
                await _clientSlots.WaitAsync(cancellationToken);
                slotAcquired = true;
                serverStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    MaxConcurrentClients,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await serverStream.WaitForConnectionAsync(cancellationToken);

                var connectedStream = serverStream;
                serverStream = null;
                slotAcquired = false;
                clients.Add(HandleClientAndReleaseSlotAsync(connectedStream, cancellationToken));
                clients.RemoveAll(task => task.IsCompleted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler im IPC-Server-Loop.");
                try
                {
                    await Task.Delay(10, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                serverStream?.Dispose();
                if (slotAcquired)
                {
                    _clientSlots.Release();
                }
            }
        }

        await Task.WhenAll(clients);
    }

    private async Task HandleClientAndReleaseSlotAsync(
        NamedPipeServerStream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(stream, cancellationToken);
        }
        finally
        {
            _clientSlots.Release();
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        try
        {
            var request = await PipeFraming.ReadJsonAsync<ModuleMessage>(stream, cancellationToken);

            var externalRequest = request with { SourceModuleId = IpcDefaults.ExternalCallerIdentity };
            var result = await ActiveMessenger.SendAsync(externalRequest, cancellationToken);

            await PipeFraming.WriteJsonAsync(stream, result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fehler beim Verarbeiten einer IPC-Client-Verbindung.");
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }
}