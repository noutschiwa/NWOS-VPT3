using System.Net.Http.Headers;
using System.Text.Json;

namespace NWOS.AI;

public sealed record OpenAiChatMessage(string Role, string Content);

public sealed class OpenAiChatClient
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90),
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiChatClient()
        : this(
            SharedHttpClient,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? string.Empty,
            Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() is { Length: > 0 } model
                ? model
                : "gpt-4o-mini")
    {
    }

    public OpenAiChatClient(HttpClient httpClient, string apiKey, string model)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Ein OpenAI-Modell muss angegeben sein.", nameof(model))
            : model;
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<OpenAiChatMessage> messages,
        CancellationToken cancellationToken)
        => await CompleteAsync(
            systemPrompt,
            messages,
            maxMessageLength: 4000,
            maxConversationLength: 16000,
            cancellationToken);

    public async Task<string> CompleteWithContextAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken)
        => await CompleteAsync(
            systemPrompt,
            new[] { new OpenAiChatMessage("user", userMessage) },
            maxMessageLength: 90000,
            maxConversationLength: 90000,
            cancellationToken);

    private async Task<string> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<OpenAiChatMessage> messages,
        int maxMessageLength,
        int maxConversationLength,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI ist nicht konfiguriert. Setze OPENAI_API_KEY als Benutzer- oder Systemumgebungsvariable und starte NWOS neu.");
        }

        if (messages.Count == 0 || messages.Count > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(messages), "Der Chat muss 1 bis 20 Nachrichten enthalten.");
        }

        if (messages.Any(message => message.Content.Length > maxMessageLength)
            || messages.Sum(message => (long)message.Content.Length) > maxConversationLength)
        {
            throw new ArgumentException("Der Chat-Verlauf überschreitet das erlaubte Größenlimit.", nameof(messages));
        }

        using var requestBody = new MemoryStream();
        using (var writer = new Utf8JsonWriter(requestBody))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _model);
            writer.WriteStartArray("messages");
            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", systemPrompt);
            writer.WriteEndObject();

            foreach (var message in messages)
            {
                if (message.Role is not ("user" or "assistant"))
                {
                    throw new ArgumentException("Ungültige oder zu lange Chat-Nachricht.", nameof(messages));
                }

                writer.WriteStartObject();
                writer.WriteString("role", message.Role);
                writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new ByteArrayContent(requestBody.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI antwortete mit HTTP {(int)response.StatusCode}: {LimitErrorText(responseBody)}");
        }

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var assistantMessage)
            || !assistantMessage.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(content.GetString()))
        {
            throw new InvalidDataException("OpenAI-Antwort enthält keine Chat-Nachricht.");
        }

        return content.GetString()!;
    }

    private static string LimitErrorText(string responseBody)
    {
        var text = responseBody.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 500 ? text : text[..500] + "...";
    }
}
