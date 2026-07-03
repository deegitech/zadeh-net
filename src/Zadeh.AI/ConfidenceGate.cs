namespace Zadeh.AI;

/// <summary>
/// The action a <see cref="ConfidenceGate"/> recommends for an AI-proposed operation.
/// Ordered from most to least cautious.
/// </summary>
public enum GateAction
{
    /// <summary>Do not act on the AI output at all.</summary>
    Reject,

    /// <summary>Ask the user a clarifying question before acting.</summary>
    Clarify,

    /// <summary>Show the user what will happen and ask for confirmation first.</summary>
    Confirm,

    /// <summary>Act on the AI output directly.</summary>
    Execute
}

/// <summary>
/// The outcome of a <see cref="ConfidenceGate"/> evaluation: the recommended action,
/// the underlying score, and a full explainable trace of how the decision was made.
/// </summary>
public sealed class GateDecision
{
    /// <summary>The recommended action.</summary>
    public GateAction Action { get; }

    /// <summary>The defuzzified decision score [0, 100]. Higher = more trust.</summary>
    public double Score { get; }

    /// <summary>The dominant linguistic answer (e.g. "Execute"). Empty if no rule fired.</summary>
    public string DominantSet { get; }

    /// <summary>The full inference trace behind this decision.</summary>
    public InferenceTrace Trace { get; }

    /// <summary>
    /// Human-readable answer to "why this action?" — inputs, fired rules, and output.
    /// Suitable for audit logs or for feeding back into an AI agent's context.
    /// </summary>
    public string Explanation => Trace.Explain();

    internal GateDecision(GateAction action, double score, string dominantSet, InferenceTrace trace)
    {
        Action = action;
        Score = score;
        DominantSet = dominantSet;
        Trace = trace;
    }

    /// <summary>Returns a compact representation, e.g. "Execute (score 81.8)".</summary>
    public override string ToString() => $"{Action} (score {Score:0.#})";
}

/// <summary>
/// Turns an LLM's confidence score into a deterministic, explainable action decision:
/// <see cref="GateAction.Execute"/>, <see cref="GateAction.Confirm"/>,
/// <see cref="GateAction.Clarify"/>, or <see cref="GateAction.Reject"/>.
///
/// The problem it solves: an LLM says "this message is an order query, confidence 0.68".
/// Is 0.68 enough to act on? The right answer depends on context — how often this user
/// triggers this action, how well it fits the conversation. ConfidenceGate combines those
/// signals through Mamdani fuzzy inference, so the gating logic is:
///   - deterministic (same inputs → same decision, unlike asking the LLM to self-judge),
///   - explainable (every decision carries a rule-by-rule trace),
///   - free (microseconds, no tokens),
///   - and tunable by non-programmers (rules read like English; JSON-loadable).
///
/// "AI decides what the user wants; the gate decides whether to trust it."
///
/// <code>
/// var gate = ConfidenceGate.CreateDefault();
/// var decision = gate.Decide(confidence: 0.68, userHistory: 0.72);
/// if (decision.Action == GateAction.Execute) { ... }
/// logger.LogDebug(decision.Explanation);
/// </code>
/// </summary>
public sealed class ConfidenceGate
{
    /// <summary>Input variable name for the AI confidence signal.</summary>
    public const string ConfidenceInput = "Confidence";

    /// <summary>Input variable name for the user-history signal.</summary>
    public const string UserHistoryInput = "UserHistory";

    /// <summary>Input variable name for the context-relevance signal.</summary>
    public const string ContextRelevanceInput = "ContextRelevance";

    private readonly MamdaniEngine _engine;

    /// <summary>The underlying inference engine (inspect, serialize with ToJson, etc.).</summary>
    public MamdaniEngine Engine => _engine;

    private ConfidenceGate(MamdaniEngine engine)
    {
        _engine = engine;
    }

    // ─── Factories ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a gate with the default profile: 3 inputs (Confidence, UserHistory,
    /// ContextRelevance, each [0,1]) and 12 rules, production-proven in conversational
    /// AI intent gating. Neutral defaults (0.5) make the extra signals optional.
    /// </summary>
    public static ConfidenceGate CreateDefault()
    {
        var confidence = new FuzzyVariable(ConfidenceInput, 0, 1);
        confidence.Set(FuzzySet.LeftShoulder("Low", 0.15, 0.40));
        confidence.Set(FuzzySet.Triangle("Medium", 0.30, 0.55, 0.75));
        confidence.Set(FuzzySet.RightShoulder("High", 0.65, 0.90));

        var history = new FuzzyVariable(UserHistoryInput, 0, 1);
        history.Set(FuzzySet.LeftShoulder("Rare", 0.10, 0.30));
        history.Set(FuzzySet.Triangle("Occasional", 0.20, 0.50, 0.70));
        history.Set(FuzzySet.RightShoulder("Frequent", 0.50, 0.80));

        var context = new FuzzyVariable(ContextRelevanceInput, 0, 1);
        context.Set(FuzzySet.LeftShoulder("Weak", 0.10, 0.35));
        context.Set(FuzzySet.Triangle("Moderate", 0.25, 0.50, 0.75));
        context.Set(FuzzySet.RightShoulder("Strong", 0.60, 0.85));

        var action = new FuzzyVariable("Action", 0, 100);
        action.Set(FuzzySet.LeftShoulder(nameof(GateAction.Reject), 10, 25));
        action.Set(FuzzySet.Triangle(nameof(GateAction.Clarify), 20, 40, 60));
        action.Set(FuzzySet.Triangle(nameof(GateAction.Confirm), 45, 65, 82));
        action.Set(FuzzySet.RightShoulder(nameof(GateAction.Execute), 75, 92));

        var engine = new MamdaniEngine()
            .Input(confidence).Input(history).Input(context)
            .Output(action)
            // High confidence — act, unless the user is a stranger to this action
            .Rule(FuzzyRule.If(confidence.Is("High")).And(history.Is("Frequent")).Then(action.Is("Execute")))
            .Rule(FuzzyRule.If(confidence.Is("High")).And(context.Is("Strong")).Then(action.Is("Execute")))
            .Rule(FuzzyRule.If(confidence.Is("High")).And(history.Is("Occasional")).Then(action.Is("Confirm")))
            .Rule(FuzzyRule.If(confidence.Is("High")).And(history.Is("Rare")).And(context.Is("Weak")).Then(action.Is("Confirm")))
            // Medium confidence — context and history decide
            .Rule(FuzzyRule.If(confidence.Is("Medium")).And(history.Is("Frequent")).Then(action.Is("Execute")))
            .Rule(FuzzyRule.If(confidence.Is("Medium")).And(context.Is("Strong")).Then(action.Is("Confirm")))
            .Rule(FuzzyRule.If(confidence.Is("Medium")).And(history.Is("Occasional")).And(context.Is("Moderate")).Then(action.Is("Confirm")))
            .Rule(FuzzyRule.If(confidence.Is("Medium")).And(history.Is("Rare")).And(context.Is("Weak")).Then(action.Is("Clarify")))
            // Low confidence — cautious by default
            .Rule(FuzzyRule.If(confidence.Is("Low")).And(history.Is("Frequent")).Then(action.Is("Clarify")))
            .Rule(FuzzyRule.If(confidence.Is("Low")).And(context.Is("Strong")).Then(action.Is("Clarify")))
            .Rule(FuzzyRule.If(confidence.Is("Low")).And(history.Is("Rare")).Then(action.Is("Reject")))
            .Rule(FuzzyRule.If(confidence.Is("Low")).And(context.Is("Weak")).Then(action.Is("Reject")))
            // Coverage floor: with neutral secondary signals no pair-rule fires at low
            // confidence, which would fall back to the midpoint and create a cliff edge.
            .Rule(FuzzyRule.If(confidence.Is("Low")).WithWeight(0.6).Then(action.Is("Clarify")));

        return new ConfidenceGate(engine);
    }

    /// <summary>
    /// Wraps a custom engine as a gate. The engine must have exactly one output variable
    /// whose set names are all <see cref="GateAction"/> names (Reject, Clarify, Confirm, Execute).
    /// </summary>
    /// <exception cref="ArgumentException">If the engine's output shape doesn't fit.</exception>
    public static ConfidenceGate FromEngine(MamdaniEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (engine.Outputs.Count != 1)
            throw new ArgumentException($"A ConfidenceGate engine must have exactly one output variable, got {engine.Outputs.Count}.");

        foreach (var setName in engine.Outputs[0].Sets.Keys)
        {
            if (!Enum.TryParse<GateAction>(setName, ignoreCase: true, out _))
                throw new ArgumentException(
                    $"Output set '{setName}' is not a GateAction. " +
                    $"Valid names: {string.Join(", ", Enum.GetNames<GateAction>())}.");
        }

        return new ConfidenceGate(engine);
    }

    /// <summary>
    /// Builds a gate from a JSON engine definition (see <see cref="MamdaniEngine.FromJson"/>),
    /// with the same output-shape validation as <see cref="FromEngine"/>.
    /// </summary>
    public static ConfidenceGate FromJson(string json) => FromEngine(MamdaniEngine.FromJson(json));

    // ─── Decision ────────────────────────────────────────────────────────

    /// <summary>
    /// Decides what to do with an AI output.
    /// </summary>
    /// <param name="confidence">The AI's confidence score [0, 1].</param>
    /// <param name="userHistory">
    /// How established this action is for this user [0, 1] (e.g. usage frequency).
    /// Defaults to a neutral 0.5 when no history signal is available.
    /// </param>
    /// <param name="contextRelevance">
    /// How well the action fits the current conversation/context [0, 1].
    /// Defaults to a neutral 0.5.
    /// </param>
    public GateDecision Decide(double confidence, double userHistory = 0.5, double contextRelevance = 0.5)
    {
        var inputs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Feed every declared engine input; the three well-known names get the
        // supplied signals, any extra custom inputs get a neutral midpoint.
        foreach (var variable in _engine.Inputs)
        {
            inputs[variable.Name] = variable.Name.ToUpperInvariant() switch
            {
                "CONFIDENCE" => Math.Clamp(confidence, variable.Min, variable.Max),
                "USERHISTORY" => Math.Clamp(userHistory, variable.Min, variable.Max),
                "CONTEXTRELEVANCE" => Math.Clamp(contextRelevance, variable.Min, variable.Max),
                _ => (variable.Min + variable.Max) / 2.0
            };
        }

        return Decide(inputs);
    }

    /// <summary>
    /// Decides using explicit named inputs — for gates built from custom engines
    /// whose input variables differ from the default three.
    /// </summary>
    public GateDecision Decide(Dictionary<string, double> inputs)
    {
        var trace = _engine.EvaluateWithTrace(inputs);
        var result = trace.Results.Values.First();

        var action = Enum.TryParse<GateAction>(result.DominantSet, ignoreCase: true, out var parsed)
            ? parsed
            : ActionFromScore(result.CrispValue, _engine.Outputs[0]);

        return new GateDecision(action, result.CrispValue, result.DominantSet, trace);
    }

    /// <summary>
    /// Fallback mapping when no rule fired: split the output range into four
    /// equal bands, most cautious first.
    /// </summary>
    private static GateAction ActionFromScore(double score, FuzzyVariable output)
    {
        var normalized = (score - output.Min) / (output.Max - output.Min);
        return normalized switch
        {
            < 0.25 => GateAction.Reject,
            < 0.50 => GateAction.Clarify,
            < 0.75 => GateAction.Confirm,
            _ => GateAction.Execute
        };
    }
}
