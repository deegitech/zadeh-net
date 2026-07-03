using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zadeh.AI.Providers;

/// <summary>
/// <see cref="IChatClient"/> over the Anthropic Messages API (Claude), using only
/// the .NET base library — no SDK dependency, keeping Zadeh.AI zero-dependency.
/// If your application already uses the official Anthropic SDK, prefer wrapping
/// that client in your own <see cref="IChatClient"/> implementation instead.
/// </summary>
public sealed class AnthropicChatClient : IChatClient, IDisposable
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;

    /// <summary>
    /// Creates a Claude-backed chat client.
    /// </summary>
    /// <param name="apiKey">Anthropic API key.</param>
    /// <param name="model">Model ID (default: claude-opus-4-8).</param>
    /// <param name="maxTokens">Response token cap (default 16000).</param>
    /// <param name="httpClient">Optional shared HttpClient; one is created (and owned) if omitted.</param>
    public AnthropicChatClient(string apiKey, string model = "claude-opus-4-8", int maxTokens = 16000, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        _apiKey = apiKey;
        _model = model;
        _maxTokens = maxTokens;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = _maxTokens,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {payload}");

        var root = JsonNode.Parse(payload)?.AsObject()
            ?? throw new JsonException("Empty response from Anthropic API.");

        // Check stop_reason before reading content — a refusal returns 200 with empty content.
        var stopReason = root["stop_reason"]?.GetValue<string>();
        var text = new StringBuilder();
        if (root["content"] is JsonArray blocks)
        {
            foreach (var block in blocks)
            {
                if (block?["type"]?.GetValue<string>() == "text")
                    text.Append(block["text"]?.GetValue<string>());
            }
        }

        if (text.Length == 0)
            throw new InvalidOperationException(
                $"Anthropic API returned no text (stop_reason: {stopReason ?? "unknown"}).");

        return text.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
