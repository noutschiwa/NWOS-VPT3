using System.Text.Json;
using System.Net;
using System.Text;
using NWOS.AI;
using NWOS.Contracts;
using Xunit;

namespace NWOS.Marc.Tests;

public class MarcModuleTests
{
    [Fact]
    public async Task CreateFolder_message_creates_directory_and_returns_result()
    {
        var module = new MarcModule();
        await module.StartAsync(CancellationToken.None);

        var target = Path.Combine(CreateTempDir(), "neuer-ordner");
        var message = ModuleMessage.Create(
            "nwos.cardinal", module.ModuleId, "marc.createFolder",
            JsonSerializer.Serialize(new { path = target }), PermissionScope.FileSystemWrite);

        var response = await module.HandleMessageAsync(message, CancellationToken.None);

        Assert.True(Directory.Exists(target));
        Assert.Equal("marc.createFolder.result", response!.MessageType);
    }

    [Fact]
    public async Task OrganizeFolder_message_returns_move_count()
    {
        var module = new MarcModule();
        await module.StartAsync(CancellationToken.None);

        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "bild.png"), "x");

        var message = ModuleMessage.Create(
            "nwos.cardinal", module.ModuleId, "marc.organizeFolder",
            JsonSerializer.Serialize(new { folderPath = dir }), PermissionScope.FileSystemWrite);

        var response = await module.HandleMessageAsync(message, CancellationToken.None);

        using var doc = JsonDocument.Parse(response!.PayloadJson);
        Assert.Equal(1, doc.RootElement.GetProperty("filesMoved").GetInt32());
    }

    [Fact]
    public async Task Unknown_message_type_throws()
    {
        var module = new MarcModule();
        await module.StartAsync(CancellationToken.None);

        var message = ModuleMessage.Create("nwos.cardinal", module.ModuleId, "does.not.exist", "{}", PermissionScope.None);

        await Assert.ThrowsAsync<NotSupportedException>(() => module.HandleMessageAsync(message, CancellationToken.None));
    }

    [Fact]
    public async Task Health_is_unresponsive_before_start()
    {
        var module = new MarcModule();
        var health = await module.CheckHealthAsync(CancellationToken.None);
        Assert.Equal(ModuleStatus.Unresponsive, health.Status);
    }

    [Fact]
    public async Task Chat_uses_configured_ai_client_without_running_local_actions()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"content":"Hallo vom Marc."}}]}""",
                Encoding.UTF8,
                "application/json"),
        }));
        var module = new MarcModule(new OpenAiChatClient(new HttpClient(handler), "test-key", "test-model"));
        await module.StartAsync(CancellationToken.None);
        var message = ModuleMessage.Create(
            "nwos.cardinal",
            module.ModuleId,
            "marc.chat",
            """{"messages":[{"role":"user","content":"Hallo"}]}""",
            PermissionScope.None);

        var response = await module.HandleMessageAsync(message, CancellationToken.None);

        using var payload = JsonDocument.Parse(response!.PayloadJson);
        Assert.Equal("Hallo vom Marc.", payload.RootElement.GetProperty("reply").GetString());
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nwos-marc-module-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
