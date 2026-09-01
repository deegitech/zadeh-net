// SPDX-FileCopyrightText: 2026 DeegiTech Teknoloji ve Yazılım Ltd. Şti.
//
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zadeh.AI.Providers;

/// <summary>
/// <see cref="IChatClient"/> over the OpenAI Chat Completions API, using only
/// the .NET base library. Also works with OpenAI-compatible endpoints (local
/// models, gateways) via the <c>baseUrl</c> parameter.
/// </summary>
public sealed class OpenAIChatClient : IChatClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    /// <summary>
    /// Creates an OpenAI-backed chat client.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model ID (default: gpt-4o).</param>
    /// <param name="baseUrl">API base URL — override for OpenAI-compatible endpoints (default: https://api.openai.com).</param>
    /// <param name="httpClient">Optional shared HttpClient; one is created (and owned) if omitted.</param>
    public OpenAIChatClient(string apiKey, string model = "gpt-4o", string baseUrl = "https://api.openai.com", HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        _apiKey = apiKey;
        _model = model;
        _endpoint = baseUrl.TrimEnd('/') + "/v1/chat/completions";
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI API returned {(int)response.StatusCode}: {payload}");

        var root = JsonNode.Parse(payload)?.AsObject()
            ?? throw new JsonException("Empty response from OpenAI API.");

        var text = root["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException("OpenAI API returned no text content.");

        return text;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
