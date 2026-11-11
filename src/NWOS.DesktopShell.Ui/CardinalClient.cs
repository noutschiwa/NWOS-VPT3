using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using NWOS.Contracts;

namespace NWOS.DesktopShell.Ui
{
    public sealed class CardinalClient
    {
        private const string PipeName = "NWOS_Cardinal_Pipe";
        public string? LastError { get; private set; }

        public async Task<JsonDocument?> CallAsync(string targetService, string action, object payload, PermissionScope scope)
        {
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using var clientStream = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await clientStream.ConnectAsync(2000); // 2 Sekunden Timeout pro Versuch

                    using var writer = new StreamWriter(clientStream);
                    using var reader = new StreamReader(clientStream);

                    var request = new
                    {
                        Service = targetService,
                        Action = action,
                        Scope = scope.ToString(),
                        Payload = JsonSerializer.Serialize(payload)
                    };

                    string jsonRequest = JsonSerializer.Serialize(request);
                    await writer.WriteLineAsync(jsonRequest);
                    await writer.FlushAsync();

                    string? jsonResponse = await reader.ReadLineAsync();
                    LastError = null;

                    if (string.IsNullOrEmpty(jsonResponse)) return null;

                    return JsonDocument.Parse(jsonResponse);
                }
                catch (Exception ex)
                {
                    retries--;
                    if (retries == 0)
                    {
                        LastError = ex.Message;
                        return null;
                    }
                    await Task.Delay(400); // Kurz warten vor dem nächsten Verbindungsversuch
                }
            }
            return null;
        }
    }
}