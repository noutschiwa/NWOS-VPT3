using System.Net;
using System.Text.Json;
using NWOS.AI;
using NWOS.Contracts;
using Xunit;

namespace NWOS.Cardinal.Tests;

public sealed class CardinalCodingAssistantModuleTests
{
    [Fact]
    public async Task Cardinal_requires_an_explicit_workspace_before_reading_project_files()
    {
        var requestSent = false;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            requestSent = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"unexpected"}}]}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        }));
        var chatClient = new OpenAiChatClient(httpClient, "test-key", "test-model");
        var statePath = Path.Combine(Path.GetTempPath(), "nwos-cardinal-state-" + Guid.NewGuid().ToString("N"));
        var module = new CardinalCodingAssistantModule(chatClient, statePath);
        await module.StartAsync(CancellationToken.None);
        var request = ModuleMessage.Create(
            "external.ipc",
            module.ModuleId,
            "cardinal.chat",
            """{"messages":[{"role":"user","content":"Erstelle einen kleinen Patch."}]}""",
            PermissionScope.NetworkGeneral);

        var response = await module.HandleMessageAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("cardinal.chat.result", response!.MessageType);
        Assert.Equal("nwos.cardinal", response.SourceModuleId);
        Assert.Equal("external.ipc", response.TargetModuleId);
        using var payload = JsonDocument.Parse(response.PayloadJson);
        Assert.Contains("/workspace", payload.RootElement.GetProperty("reply").GetString());
        Assert.False(requestSent);
    }

    [Fact]
    public async Task Cardinal_rejects_non_chat_message_types()
    {
        var module = new CardinalCodingAssistantModule(
            new OpenAiChatClient(new HttpClient(new StubHandler(_ =>
                throw new InvalidOperationException("No request expected."))), "test-key", "test-model"));
        var request = ModuleMessage.Create(
            "external.ipc",
            module.ModuleId,
            "cardinal.execute",
            "{}",
            PermissionScope.None);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            module.HandleMessageAsync(request, CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
