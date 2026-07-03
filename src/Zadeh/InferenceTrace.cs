namespace Zadeh;

/// <summary>
/// The detailed result of an inference for a single output variable:
/// the crisp value plus the fuzzy context it came from.
/// </summary>
public sealed class FuzzyResult
{
    /// <summary>The output variable this result belongs to.</summary>
    public string VariableName { get; }

    /// <summary>The defuzzified crisp output value.</summary>
    public double CrispValue { get; }

    /// <summary>
    /// The output set with the highest activation strength — the linguistic answer
    /// (e.g., "High"). Empty string when no rule fired for this output.
    /// </summary>
    public string DominantSet { get; }

    /// <summary>
    /// Activation strength per output set (set name → [0, 1]).
    /// Only sets targeted by at least one fired rule appear here.
    /// </summary>
    public IReadOnlyDictionary<string, double> OutputMemberships { get; }

    /// <summary>False when no rule fired and <see cref="CrispValue"/> is the midpoint fallback.</summary>
    public bool AnyRuleFired => OutputMemberships.Count > 0;

    internal FuzzyResult(
        string variableName,
        double crispValue,
        string dominantSet,
        IReadOnlyDictionary<string, double> outputMemberships)
    {
        VariableName = variableName;
        CrispValue = crispValue;
        DominantSet = dominantSet;
        OutputMemberships = outputMemberships;
    }

    /// <summary>Returns a compact human-readable representation.</summary>
    public override string ToString()
    {
        if (!AnyRuleFired)
            return $"{VariableName} = {CrispValue:0.###} (no rule fired — midpoint fallback)";

        var activations = string.Join(", ",
            OutputMemberships.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value:0.00}"));
        return $"{VariableName} = {CrispValue:0.###} (dominant: {DominantSet}; activations: {activations})";
    }
}

/// <summary>
/// One rule's evaluation during an inference: the rule and the strength it fired with.
/// </summary>
public sealed class RuleActivation
{
    /// <summary>The evaluated rule.</summary>
    public FuzzyRule Rule { get; }

    /// <summary>Firing strength [0, 1] after AND (MIN) combination and rule weight.</summary>
    public double Strength { get; }

    /// <summary>True when the rule contributed to the output (strength &gt; 0).</summary>
    public bool Fired => Strength > 0;

    internal RuleActivation(FuzzyRule rule, double strength)
    {
        Rule = rule;
        Strength = strength;
    }

    /// <summary>Returns a compact human-readable representation.</summary>
    public override string ToString() => $"{(Fired ? "✓" : "✗")} [{Strength:0.00}] {Rule}";
}

/// <summary>
/// A full, explainable record of one inference run: crisp inputs, fuzzified memberships,
/// every rule's firing strength, and the detailed results per output variable.
///
/// Produced by <see cref="MamdaniEngine.EvaluateWithTrace"/>. Use <see cref="Explain"/> for a
/// human-readable answer to "why did the engine decide this?" — suitable for logs, audits,
/// or feeding back into an AI agent's context.
/// </summary>
public sealed class InferenceTrace
{
    /// <summary>The crisp input values the inference ran with (variable name → value).</summary>
    public IReadOnlyDictionary<string, double> Inputs { get; }

    /// <summary>Fuzzified inputs: variable name → (set name → membership degree).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> FuzzifiedInputs { get; }

    /// <summary>Every rule in the engine with the strength it fired at (including 0).</summary>
    public IReadOnlyList<RuleActivation> RuleActivations { get; }

    /// <summary>Detailed result per output variable (variable name → result).</summary>
    public IReadOnlyDictionary<string, FuzzyResult> Results { get; }

    internal InferenceTrace(
        IReadOnlyDictionary<string, double> inputs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> fuzzifiedInputs,
        IReadOnlyList<RuleActivation> ruleActivations,
        IReadOnlyDictionary<string, FuzzyResult> results)
    {
        Inputs = inputs;
        FuzzifiedInputs = fuzzifiedInputs;
        RuleActivations = ruleActivations;
        Results = results;
    }

    /// <summary>
    /// Builds a multi-line, human-readable explanation of the inference:
    /// inputs and their memberships, which rules fired at what strength, and the outputs.
    /// </summary>
    /// <param name="includeSilentRules">Include rules that did not fire (default: true).</param>
    public string Explain(bool includeSilentRules = true)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("INPUTS");
        foreach (var (varName, crisp) in Inputs)
        {
            var memberships = FuzzifiedInputs.TryGetValue(varName, out var sets)
                ? string.Join(", ", sets.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value:0.00}"))
                : "-";
            sb.AppendLine($"  {varName} = {crisp:0.###}  →  {memberships}");
        }

        sb.AppendLine("RULES");
        foreach (var activation in RuleActivations)
        {
            if (!activation.Fired && !includeSilentRules) continue;
            sb.AppendLine($"  {activation}");
        }

        sb.AppendLine("OUTPUTS");
        foreach (var result in Results.Values)
            sb.AppendLine($"  {result}");

        return sb.ToString();
    }

    /// <summary>Same as <see cref="Explain"/> with default options.</summary>
    public override string ToString() => Explain();
}
