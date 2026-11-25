using System.Net;
using System.Text;
using System.Text.Json;
using NWOS.AI;
using NWOS.Cardinal;
using NWOS.Contracts;
using Xunit;

namespace NWOS.Cardinal.Tests;

public sealed class CardinalCodingWorkspaceTests
{
    [Fact]
    public async Task Code_change_requires_explicit_approval_and_is_persistently_reviewable()
    {
        var root = CreateWorkspace();
        var state = Path.Combine(Path.GetTempPath(), "nwos-cardinal-state-" + Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "Sample.cs");
        const string original = "public class Sample\n{\n    public string Name => \"Old\";\n}\n";
        await File.WriteAllTextAsync(sourcePath, original);

        var llmResponse = """
            {"summary":"Aktualisiert den Namen.","edits":[{"path":"Sample.cs","before":"Old","after":"New"}],"tests":["dotnet test"],"risks":[]}
            """;
        const string apiKey = "sk-proj-AAAAAAAAAAAAAAAAAAAAAAAA";
        string? submittedRequest = null;
        var module = CreateModule(state, llmResponse, body => submittedRequest = body);

        var workspaceReply = await SendAsync(module, $"/workspace \"{root}\"");
        Assert.Contains("Arbeitsbereich gespeichert", workspaceReply);

        var proposalReply = await SendAsync(module, $"Ändere den Sample-Namen von Old auf New. {apiKey}");
        Assert.Contains("noch nicht angewendet", proposalReply);
        Assert.Contains("ANWENDEN", proposalReply);
        Assert.Equal(original, await File.ReadAllTextAsync(sourcePath));
        Assert.DoesNotContain(apiKey, submittedRequest ?? string.Empty);

        var id = ExtractReviewId(proposalReply);
        var restartedModule = CreateModule(state, llmResponse);
        var reviewReply = await SendAsync(restartedModule, $"/review {id}");
        Assert.Contains("--- a/Sample.cs", reviewReply);
        Assert.Contains("Old", reviewReply);
        Assert.Contains("New", reviewReply);
        Assert.True(File.Exists(Path.Combine(state, "cardinal-code-review", id + ".json")));
        Assert.DoesNotContain(apiKey, await File.ReadAllTextAsync(
            Path.Combine(state, "cardinal-code-review", id + ".json")));

        var applyReply = await SendAsync(restartedModule, $"ANWENDEN {id}");
        Assert.Contains("wurde nach deiner Bestätigung angewendet", applyReply);
        Assert.Equal(original.Replace("Old", "New", StringComparison.Ordinal), await File.ReadAllTextAsync(sourcePath));
        Assert.Contains("\"status\":\"applied\"", await File.ReadAllTextAsync(
            Path.Combine(state, "cardinal-code-review", id + ".json")));
    }

    [Fact]
    public async Task Code_change_rejects_paths_outside_workspace()
    {
        var root = CreateWorkspace();
        var state = Path.Combine(Path.GetTempPath(), "nwos-cardinal-state-" + Guid.NewGuid().ToString("N"));
        var outsidePath = Path.Combine(Path.GetDirectoryName(root)!, "outside.cs");
        await File.WriteAllTextAsync(outsidePath, "keep");
        var module = CreateModule(
            state,
            """{"summary":"Bad path.","edits":[{"path":"../outside.cs","before":"keep","after":"changed"}],"tests":[],"risks":[]}""");

        await SendAsync(module, $"/workspace \"{root}\"");
        var reply = await SendAsync(module, "Update the adjacent file.");

        Assert.Contains("außerhalb des erlaubten NWOS-Bereichs", reply);
        Assert.Equal("keep", await File.ReadAllTextAsync(outsidePath));
    }

    [Fact]
    public async Task Workspace_auto_finds_and_persists_nested_nwos_checkout()
    {
        var searchRoot = Path.Combine(Path.GetTempPath(), "nwos-search-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(searchRoot, "checkout", "NWOS");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "NWOS.sln"), "Test workspace");
        var cardinalProject = Path.Combine(root, "NWOS-KISysteme", "NWOS.Cardinal");
        Directory.CreateDirectory(cardinalProject);
        File.WriteAllText(Path.Combine(cardinalProject, "NWOS.Cardinal.csproj"), "<Project />");
        var state = Path.Combine(Path.GetTempPath(), "nwos-cardinal-state-" + Guid.NewGuid().ToString("N"));
        var module = CreateModule(state, "{}", workspaceSearchRoots: new[] { searchRoot });

        var reply = await SendAsync(module, "/workspace auto");

        Assert.Contains($"Arbeitsbereich gespeichert: `{root}`", reply);
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(state, "cardinal-workspace.json")));
        Assert.Equal(root, settings.RootElement.GetProperty("root").GetString());
    }

    [Fact]
    public async Task Applying_review_refuses_files_changed_after_proposal()
    {
        var root = CreateWorkspace();
        var state = Path.Combine(Path.GetTempPath(), "nwos-cardinal-state-" + Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "Sample.cs");
        await File.WriteAllTextAsync(sourcePath, "public string Name => \"Old\";");
        var module = CreateModule(
            state,
            """{"summary":"Update name.","edits":[{"path":"Sample.cs","before":"Old","after":"New"}],"tests":[],"risks":[]}""");

        await SendAsync(module, $"/workspace \"{root}\"");
        var proposal = await SendAsync(module, "Update Sample name.");
        var id = ExtractReviewId(proposal);
        await File.WriteAllTextAsync(sourcePath, "public string Name => \"Changed elsewhere\";");

        var response = await SendAsync(module, $"ANWENDEN {id}");

        Assert.Contains("wurde seit Erstellung des Vorschlags verändert", response);
        Assert.Contains("Changed elsewhere", await File.ReadAllTextAsync(sourcePath));
    }

    private static CardinalCodingAssistantModule CreateModule(
        string stateDirectory,
        string completion,
        Action<string>? requestObserver = null,
        IEnumerable<string>? workspaceSearchRoots = null)
    {
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.Content is not null)
            {
                requestObserver?.Invoke(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = completion } } },
                }),
                Encoding.UTF8,
                "application/json"),
            };
        });
        return new CardinalCodingAssistantModule(
            new OpenAiChatClient(new HttpClient(handler), "test-key", "test-model"),
            stateDirectory,
            workspaceSearchRoots);
    }

    private static async Task<string> SendAsync(CardinalCodingAssistantModule module, string userMessage)
    {
        var payload = JsonSerializer.Serialize(new
        {
            messages = new[] { new { role = "user", content = userMessage } },
        });
        var request = ModuleMessage.Create(
            "external.ipc",
            module.ModuleId,
            "cardinal.chat",
            payload,
            PermissionScope.None);
        var response = await module.HandleMessageAsync(request, CancellationToken.None);
        using var document = JsonDocument.Parse(response!.PayloadJson);
        return document.RootElement.GetProperty("reply").GetString()!;
    }

    private static string CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "nwos-cardinal-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "NWOS.sln"), "Test workspace");
        var cardinalProject = Path.Combine(root, "NWOS-KISysteme", "NWOS.Cardinal");
        Directory.CreateDirectory(cardinalProject);
        File.WriteAllText(Path.Combine(cardinalProject, "NWOS.Cardinal.csproj"), "<Project />");
        return root;
    }

    private static string ExtractReviewId(string response)
    {
        const string marker = "Vorschlag `";
        var start = response.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += marker.Length;
        var end = response.IndexOf('`', start);
        Assert.True(end > start);
        return response[start..end];
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
