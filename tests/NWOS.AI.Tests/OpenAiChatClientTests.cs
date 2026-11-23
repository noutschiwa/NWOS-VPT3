using System.Net;
using System.Text;
using System.Text.Json;
using NWOS.AI;
using Xunit;

namespace NWOS.AI.Tests;

public sealed class OpenAiChatClientTests
{
    [Fact]
    public async Task CompleteAsync_SendsConfiguredConversationAndReturnsReply()
    {
        string? requestBody = null;
        string? authorization = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse("""{"choices":[{"message":{"content":"Hallo!"}}]}""");
        }));
        var client = new OpenAiChatClient(httpClient, "test-key", "test-model");

        var reply = await client.CompleteAsync(
            "System prompt",
            new[] { new OpenAiChatMessage("user", "Hallo") },
            CancellationToken.None);

        Assert.Equal("Hallo!", reply);
        Assert.Equal("Bearer test-key", authorization);
        using var requestDocument = JsonDocument.Parse(requestBody!);
        Assert.Equal("test-model", requestDocument.RootElement.GetProperty("model").GetString());
        var messages = requestDocument.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task CompleteAsync_RejectsMissingApiKeyWithoutSendingRequest()
    {
        var requestSent = false;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            requestSent = true;
            return Task.FromResult(JsonResponse("{}"));
        }));
        var client = new OpenAiChatClient(httpClient, string.Empty, "test-model");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(
                "System prompt",
                new[] { new OpenAiChatMessage("user", "Hallo") },
                CancellationToken.None));

        Assert.Contains("OPENAI_API_KEY", error.Message);
        Assert.False(requestSent);
    }

    [Fact]
    public async Task CompleteAsync_RejectsUnsupportedMessageRoles()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(JsonResponse("{}"))));
        var client = new OpenAiChatClient(httpClient, "test-key", "test-model");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CompleteAsync(
                "System prompt",
                new[] { new OpenAiChatMessage("system", "Injected prompt") },
                CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_RejectsOversizedHistory()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("Request should not be sent.")));
        var client = new OpenAiChatClient(httpClient, "test-key", "test-model");
        var oversizedMessage = new[] { new OpenAiChatMessage("user", new string('x', 4001)) };
        var oversizedHistory = Enumerable.Range(0, 4)
            .Select(_ => new OpenAiChatMessage("user", new string('x', 4000)))
            .Append(new OpenAiChatMessage("assistant", "x"))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CompleteAsync("System prompt", oversizedMessage, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CompleteAsync("System prompt", oversizedHistory, CancellationToken.None));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
