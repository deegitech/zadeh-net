using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zadeh.AI.Providers;

/// <summary>
/// <see cref="IChatClient"/> over the Google Gemini API (generateContent), using
/// only the .NET base library.
/// </summary>
public sealed class GeminiChatClient : IChatClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;

    /// <summary>
    /// Creates a Gemini-backed chat client.
    /// </summary>
    /// <param name="apiKey">Google AI API key.</param>
    /// <param name="model">Model ID (default: gemini-2.5-flash).</param>
    /// <param name="httpClient">Optional shared HttpClient; one is created (and owned) if omitted.</param>
    public GeminiChatClient(string apiKey, string model = "gemini-2.5-flash", HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        _apiKey = apiKey;
        _endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } }
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = userPrompt } }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API returned {(int)response.StatusCode}: {payload}");

        var root = JsonNode.Parse(payload)?.AsObject()
            ?? throw new JsonException("Empty response from Gemini API.");

        var text = new StringBuilder();
        if (root["candidates"]?[0]?["content"]?["parts"] is JsonArray parts)
        {
            foreach (var part in parts)
                text.Append(part?["text"]?.GetValue<string>());
        }

        if (text.Length == 0)
            throw new InvalidOperationException("Gemini API returned no text content.");

        return text.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
