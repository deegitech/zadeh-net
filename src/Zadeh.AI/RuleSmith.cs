using System.Text.Json;

namespace Zadeh.AI;

/// <summary>
/// Minimal LLM chat abstraction: one system prompt + one user prompt in, text out.
/// Implement this over any provider — the built-in <see cref="Providers.AnthropicChatClient"/>,
/// <see cref="Providers.OpenAIChatClient"/>, and <see cref="Providers.GeminiChatClient"/>
/// use only the .NET base library, or wrap an official SDK client you already have.
/// </summary>
public interface IChatClient
{
    /// <summary>Sends one completion request and returns the model's text output.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of a <see cref="RuleSmith"/> generation.
/// </summary>
public sealed class RuleSmithResult
{
    /// <summary>The validated, ready-to-use engine.</summary>
    public MamdaniEngine Engine { get; }

    /// <summary>The engine's JSON definition — persist it, review it, version it.</summary>
    public string Json { get; }

    /// <summary>How many LLM attempts were needed (1 = first try was valid).</summary>
    public int Attempts { get; }

    internal RuleSmithResult(MamdaniEngine engine, string json, int attempts)
    {
        Engine = engine;
        Json = json;
        Attempts = attempts;
    }
}

/// <summary>
/// Generates a complete fuzzy decision engine from a plain-language policy description,
/// using an LLM as the rule author.
///
/// The architectural point: the LLM runs <b>once, at design time</b>. It translates
/// "if a very active customer places a large order, give high priority" into fuzzy
/// variables, sets, and rules — which a human reviews and persists as JSON. Every
/// runtime decision then goes through the deterministic Mamdani engine: no tokens,
/// no latency, no hallucination in the decision path.
///
/// "AI designs the rules; fuzzy makes the decisions."
///
/// Generated output is validated by actually building the engine
/// (<see cref="MamdaniEngine.FromJson"/>); on validation failure the error is fed
/// back to the LLM for a bounded number of repair attempts.
///
/// <code>
/// var smith = new RuleSmith(new AnthropicChatClient(apiKey));
/// var result = await smith.GenerateAsync(
///     "Decide order priority from customer activity (0-1) and order size (0-10000 TL). " +
///     "Very active customers with large orders get top priority.");
/// File.WriteAllText("priority-rules.json", result.Json);  // human review + versioning
/// var priority = result.Engine.EvaluateSingle(new() { ["CustomerActivity"] = 0.9, ["OrderSize"] = 7500 });
/// </code>
/// </summary>
public sealed class RuleSmith
{
    private const string SystemPrompt = """
        You are an expert in fuzzy logic control design (Mamdani inference). Your job is to
        translate a plain-language decision policy into a fuzzy engine definition.

        Output ONLY a single JSON object, no prose, no markdown fences, in exactly this shape:

        {
          "defuzzification": "centroid",
          "inputs": [
            { "name": "VariableName", "min": 0, "max": 1,
              "sets": [
                { "name": "Low",  "type": "leftShoulder",  "params": [0.15, 0.40] },
                { "name": "Medium", "type": "triangle",    "params": [0.30, 0.55, 0.75] },
                { "name": "High", "type": "rightShoulder", "params": [0.65, 0.90] } ] }
          ],
          "outputs": [ { "name": "Decision", "min": 0, "max": 100, "sets": [ ... ] } ],
          "rules": [
            { "if": { "VariableName": "High" }, "then": { "Decision": "SetName" } },
            { "if": { "A": "Low", "B": "High" }, "then": { "Decision": "SetName" }, "weight": 0.8 }
          ]
        }

        Set types and their params (in this exact order):
          triangle [left, peak, right] — left <= peak <= right
          trapezoid [a, b, c, d] — a <= b <= c <= d
          leftShoulder [midpoint, edge] — midpoint < edge; full membership below midpoint
          rightShoulder [edge, midpoint] — edge < midpoint; full membership above midpoint
          gaussian [mean, sigma] — sigma > 0

        Design guidelines:
        - Choose variable ranges that match the policy's units; [0, 1] for normalized signals.
        - 3 sets per input is usually right; overlap adjacent sets so transitions are smooth.
        - Cover the input space with rules: every plausible combination should fire something.
        - Conditions within a rule are AND (all must hold). Use multiple rules for OR cases.
        - Use "weight" (0-1) only when the policy marks a rule as a weaker hint.
        - Exactly one output variable unless the policy explicitly needs more.
        """;

    private readonly IChatClient _chat;
    private readonly int _maxAttempts;

    /// <summary>
    /// Creates a rule generator.
    /// </summary>
    /// <param name="chat">The LLM client to author rules with.</param>
    /// <param name="maxAttempts">
    /// Total LLM attempts including repair rounds after validation errors (default 2).
    /// </param>
    public RuleSmith(IChatClient chat, int maxAttempts = 2)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _maxAttempts = Math.Max(1, maxAttempts);
    }

    /// <summary>
    /// Generates and validates a fuzzy engine from a plain-language policy.
    /// </summary>
    /// <param name="policy">The decision policy in plain language — inputs, output, and how they relate.</param>
    /// <param name="cancellationToken">Cancels the underlying LLM calls.</param>
    /// <exception cref="InvalidOperationException">
    /// If the LLM fails to produce a valid engine definition within the attempt budget.
    /// </exception>
    public async Task<RuleSmithResult> GenerateAsync(string policy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(policy))
            throw new ArgumentException("Policy description is empty.", nameof(policy));

        var userPrompt = $"Design a fuzzy engine for this policy:\n\n{policy}";
        string? lastError = null;
        string? lastOutput = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            var prompt = lastError is null
                ? userPrompt
                : $"{userPrompt}\n\nYour previous output failed validation with this error:\n{lastError}\n\n" +
                  $"Previous output:\n{lastOutput}\n\nFix the problem and output the corrected JSON only.";

            var response = await _chat.CompleteAsync(SystemPrompt, prompt, cancellationToken).ConfigureAwait(false);
            var json = ExtractJson(response);
            lastOutput = json;

            try
            {
                var engine = MamdaniEngine.FromJson(json);
                return new RuleSmithResult(engine, engine.ToJson(), attempt);
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or JsonException)
            {
                lastError = ex.Message;
            }
        }

        throw new InvalidOperationException(
            $"RuleSmith could not produce a valid engine in {_maxAttempts} attempt(s). " +
            $"Last validation error: {lastError}\nLast output:\n{lastOutput}");
    }

    /// <summary>
    /// Pulls the JSON object out of an LLM response: strips markdown fences and
    /// surrounding prose by locating the outermost braces.
    /// </summary>
    internal static string ExtractJson(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        return start >= 0 && end > start
            ? response[start..(end + 1)]
            : response.Trim();
    }
}
