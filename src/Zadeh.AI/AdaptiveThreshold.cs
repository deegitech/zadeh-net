namespace Zadeh.AI;

/// <summary>
/// The outcome of an <see cref="AdaptiveThreshold"/> computation.
/// </summary>
public sealed class ThresholdDecision
{
    /// <summary>The computed threshold, within [MinThreshold, MaxThreshold].</summary>
    public double Threshold { get; }

    /// <summary>The raw strictness score [0, 100] before mapping onto the threshold range.</summary>
    public double Strictness { get; }

    /// <summary>The full inference trace behind this threshold.</summary>
    public InferenceTrace Trace { get; }

    /// <summary>Human-readable answer to "why this threshold?".</summary>
    public string Explanation => Trace.Explain();

    internal ThresholdDecision(double threshold, double strictness, InferenceTrace trace)
    {
        Threshold = threshold;
        Strictness = strictness;
        Trace = trace;
    }

    /// <summary>Returns a compact representation, e.g. "0.943 (strictness 78.1)".</summary>
    public override string ToString() => $"{Threshold:0.###} (strictness {Strictness:0.#})";
}

/// <summary>
/// Computes a dynamic similarity threshold from context signals — the fix for the
/// fixed-threshold problem in semantic caching, RAG retrieval, and vector search.
///
/// A single hardcoded cosine-similarity cutoff is always wrong somewhere: too loose
/// for volatile data (stale cache hits, wrong documents) and too strict for stable
/// data (needless misses, wasted recomputation). AdaptiveThreshold derives the cutoff
/// per query from how volatile the underlying data is, how fresh the cached/candidate
/// entry is, and how confident the caller is — via deterministic, explainable fuzzy
/// inference at microsecond cost.
///
/// <code>
/// var threshold = AdaptiveThreshold.CreateDefault(minThreshold: 0.85, maxThreshold: 0.97);
/// double cutoff = threshold.Compute(volatility: 0.8, freshness: 0.3);
/// if (cosineSimilarity >= cutoff) { /* cache hit */ }
/// </code>
/// </summary>
public sealed class AdaptiveThreshold
{
    /// <summary>Input variable name for the data-volatility signal.</summary>
    public const string VolatilityInput = "Volatility";

    /// <summary>Input variable name for the entry-freshness signal.</summary>
    public const string FreshnessInput = "Freshness";

    /// <summary>Input variable name for the caller-confidence signal.</summary>
    public const string ConfidenceInput = "Confidence";

    private readonly MamdaniEngine _engine;

    /// <summary>The lower bound of the produced threshold (most lenient).</summary>
    public double MinThreshold { get; }

    /// <summary>The upper bound of the produced threshold (most strict).</summary>
    public double MaxThreshold { get; }

    /// <summary>The underlying inference engine.</summary>
    public MamdaniEngine Engine => _engine;

    private AdaptiveThreshold(MamdaniEngine engine, double minThreshold, double maxThreshold)
    {
        _engine = engine;
        MinThreshold = minThreshold;
        MaxThreshold = maxThreshold;
    }

    // ─── Factories ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a threshold engine with the default profile: Volatility, Freshness, and
    /// Confidence inputs (each [0,1]) driving a Strictness score that is mapped linearly
    /// onto [<paramref name="minThreshold"/>, <paramref name="maxThreshold"/>].
    ///
    /// Semantics: high volatility → strict (fewer, safer matches);
    /// low freshness (stale entry) → strict; high freshness + stable data → lenient.
    /// </summary>
    /// <param name="minThreshold">Threshold when fully lenient (default 0.85).</param>
    /// <param name="maxThreshold">Threshold when fully strict (default 0.97).</param>
    public static AdaptiveThreshold CreateDefault(double minThreshold = 0.85, double maxThreshold = 0.97)
    {
        var volatility = new FuzzyVariable(VolatilityInput, 0, 1);
        volatility.Set(FuzzySet.LeftShoulder("Stable", 0.15, 0.35));
        volatility.Set(FuzzySet.Triangle("Moderate", 0.25, 0.50, 0.75));
        volatility.Set(FuzzySet.RightShoulder("Volatile", 0.60, 0.85));

        var freshness = new FuzzyVariable(FreshnessInput, 0, 1);
        freshness.Set(FuzzySet.LeftShoulder("Stale", 0.15, 0.40));
        freshness.Set(FuzzySet.Triangle("Recent", 0.30, 0.55, 0.80));
        freshness.Set(FuzzySet.RightShoulder("Fresh", 0.65, 0.90));

        var confidence = new FuzzyVariable(ConfidenceInput, 0, 1);
        confidence.Set(FuzzySet.LeftShoulder("Low", 0.15, 0.40));
        confidence.Set(FuzzySet.Triangle("Medium", 0.30, 0.55, 0.75));
        confidence.Set(FuzzySet.RightShoulder("High", 0.65, 0.90));

        var strictness = new FuzzyVariable("Strictness", 0, 100);
        strictness.Set(FuzzySet.LeftShoulder("Lenient", 15, 35));
        strictness.Set(FuzzySet.Triangle("Moderate", 25, 50, 75));
        strictness.Set(FuzzySet.RightShoulder("Strict", 65, 85));

        var engine = new MamdaniEngine()
            .Input(volatility).Input(freshness).Input(confidence)
            .Output(strictness)
            // Volatile data always tightens the cutoff
            .Rule(FuzzyRule.If(volatility.Is("Volatile")).Then(strictness.Is("Strict")))
            .Rule(FuzzyRule.If(volatility.Is("Moderate")).And(freshness.Is("Stale")).Then(strictness.Is("Strict")))
            .Rule(FuzzyRule.If(volatility.Is("Moderate")).And(freshness.Is("Recent")).Then(strictness.Is("Moderate")))
            .Rule(FuzzyRule.If(volatility.Is("Moderate")).And(freshness.Is("Fresh")).Then(strictness.Is("Moderate")))
            // Stable data can afford leniency — if the entry is fresh
            .Rule(FuzzyRule.If(volatility.Is("Stable")).And(freshness.Is("Fresh")).Then(strictness.Is("Lenient")))
            .Rule(FuzzyRule.If(volatility.Is("Stable")).And(freshness.Is("Recent")).Then(strictness.Is("Lenient")))
            .Rule(FuzzyRule.If(volatility.Is("Stable")).And(freshness.Is("Stale")).Then(strictness.Is("Moderate")))
            // Caller confidence nudges the balance (weighted — a hint, not a veto)
            .Rule(FuzzyRule.If(confidence.Is("High")).And(volatility.Is("Stable")).Then(strictness.Is("Lenient")))
            .Rule(FuzzyRule.If(confidence.Is("Low")).WithWeight(0.6).Then(strictness.Is("Strict")));

        return new AdaptiveThreshold(engine, minThreshold, maxThreshold);
    }

    /// <summary>
    /// Wraps a custom engine. The engine must have exactly one output variable;
    /// its crisp value is normalized over the variable's range and mapped onto
    /// [<paramref name="minThreshold"/>, <paramref name="maxThreshold"/>].
    /// </summary>
    public static AdaptiveThreshold FromEngine(MamdaniEngine engine, double minThreshold, double maxThreshold)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.Outputs.Count != 1)
            throw new ArgumentException($"An AdaptiveThreshold engine must have exactly one output variable, got {engine.Outputs.Count}.");
        if (minThreshold >= maxThreshold)
            throw new ArgumentException($"minThreshold must be less than maxThreshold, got ({minThreshold}, {maxThreshold}).");

        return new AdaptiveThreshold(engine, minThreshold, maxThreshold);
    }

    // ─── Computation ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes the threshold for one lookup.
    /// </summary>
    /// <param name="volatility">How volatile the underlying data is [0, 1]. 1 = changes constantly.</param>
    /// <param name="freshness">How fresh the cached/candidate entry is [0, 1]. 1 = just written. Defaults to 1.</param>
    /// <param name="confidence">Caller confidence in the match context [0, 1]. Defaults to a neutral 0.5.</param>
    public double Compute(double volatility, double freshness = 1.0, double confidence = 0.5) =>
        ComputeDetailed(volatility, freshness, confidence).Threshold;

    /// <summary>
    /// Like <see cref="Compute"/>, but returns the full decision with strictness and trace.
    /// </summary>
    public ThresholdDecision ComputeDetailed(double volatility, double freshness = 1.0, double confidence = 0.5)
    {
        var inputs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in _engine.Inputs)
        {
            inputs[variable.Name] = variable.Name.ToUpperInvariant() switch
            {
                "VOLATILITY" => Math.Clamp(volatility, variable.Min, variable.Max),
                "FRESHNESS" => Math.Clamp(freshness, variable.Min, variable.Max),
                "CONFIDENCE" => Math.Clamp(confidence, variable.Min, variable.Max),
                _ => (variable.Min + variable.Max) / 2.0
            };
        }

        return ComputeDetailed(inputs);
    }

    /// <summary>
    /// Computes using explicit named inputs — for custom engines with different variables.
    /// </summary>
    public ThresholdDecision ComputeDetailed(Dictionary<string, double> inputs)
    {
        var trace = _engine.EvaluateWithTrace(inputs);
        var result = trace.Results.Values.First();
        var output = _engine.Outputs[0];

        var normalized = (result.CrispValue - output.Min) / (output.Max - output.Min);
        var threshold = MinThreshold + normalized * (MaxThreshold - MinThreshold);

        return new ThresholdDecision(Math.Clamp(threshold, MinThreshold, MaxThreshold), result.CrispValue, trace);
    }
}
