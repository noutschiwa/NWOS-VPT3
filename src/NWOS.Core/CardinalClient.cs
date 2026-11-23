using System.Text.Json;
using NWOS.Contracts;
using NWOS.Ipc;

namespace NWOS.DesktopShell.Ui
{
    public sealed class CardinalClient
    {
        private readonly IpcClient _ipcClient = new();
        public string? LastError { get; private set; }

        public async Task<JsonDocument?> CallAsync(string targetService, string action, object payload, PermissionScope scope)
        {
            try
            {
                var message = ModuleMessage.Create(
                    IpcDefaults.ExternalCallerIdentity,
                    targetService,
                    action,
                    JsonSerializer.Serialize(payload),
                    scope);

                ModuleMessageResult result = await _ipcClient.SendAsync(message, CancellationToken.None);
                if (result.Outcome != ModuleMessageOutcome.Delivered || result.Response is null)
                {
                    LastError = result.Reason ?? $"Cardinal meldete {result.Outcome}.";
                    return null;
                }

                LastError = null;
                return JsonDocument.Parse(result.Response.PayloadJson);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
    }
}
