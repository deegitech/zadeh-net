namespace Zadeh;

/// <summary>
/// Defuzzification strategy — converts a fuzzy output back to a crisp number.
/// </summary>
public enum DefuzzificationMethod
{
    /// <summary>
    /// Centroid (Center of Gravity): weighted average of the aggregated output area.
    /// Most commonly used, produces smooth results.
    /// Formula: x* = ∫(μ(x)·x dx) / ∫(μ(x) dx)
    /// </summary>
    Centroid,

    /// <summary>
    /// Bisector: the x-value that divides the aggregated area into two equal halves.
    /// </summary>
    Bisector,

    /// <summary>
    /// Mean of Maximum: average of the x-values where membership is maximum.
    /// Faster but less smooth than Centroid.
    /// </summary>
    MeanOfMaximum
}

/// <summary>
/// Mamdani fuzzy inference engine.
/// 
/// Implements the classic Mamdani inference pipeline:
///   1. Fuzzification: crisp inputs → membership degrees
///   2. Rule Evaluation: fire all rules, compute activation strengths (MIN-AND)
///   3. Aggregation: combine rule outputs (MAX)
///   4. Defuzzification: aggregated fuzzy output → crisp value
/// 
/// Thread-safe after construction. All evaluations are deterministic —
/// same inputs always produce the same output.
/// 
/// Reference: E.H. Mamdani &amp; S. Assilian (1975)
/// "An Experiment in Linguistic Synthesis with a Fuzzy Logic Controller"
/// </summary>
public sealed partial class MamdaniEngine
{
    private readonly List<FuzzyVariable> _inputs = new();
    private readonly List<FuzzyVariable> _outputs = new();
    private readonly List<FuzzyRule> _rules = new();
    private readonly DefuzzificationMethod _defuzzMethod;
    private readonly int _resolution;

    /// <summary>All input variables.</summary>
    public IReadOnlyList<FuzzyVariable> Inputs => _inputs;

    /// <summary>All output variables.</summary>
    public IReadOnlyList<FuzzyVariable> Outputs => _outputs;

    /// <summary>All rules.</summary>
    public IReadOnlyList<FuzzyRule> Rules => _rules;

    /// <summary>The defuzzification method this engine was configured with.</summary>
    public DefuzzificationMethod Defuzzification => _defuzzMethod;

    /// <summary>The number of sample points used for defuzzification.</summary>
    public int Resolution => _resolution;

    /// <summary>
    /// Creates a new Mamdani inference engine.
    /// </summary>
    /// <param name="defuzzification">Defuzzification method (default: Centroid).</param>
    /// <param name="resolution">Number of sample points for defuzzification (default: 200).</param>
    public MamdaniEngine(
        DefuzzificationMethod defuzzification = DefuzzificationMethod.Centroid,
        int resolution = 200)
    {
        _defuzzMethod = defuzzification;
        _resolution = Math.Max(50, resolution);
    }

    /// <summary>Register an input variable.</summary>
    public MamdaniEngine Input(FuzzyVariable variable)
    {
        _inputs.Add(variable ?? throw new ArgumentNullException(nameof(variable)));
        return this;
    }

    /// <summary>Register an input variable with inline set configuration.</summary>
    public MamdaniEngine Input(string name, double min, double max, Action<FuzzyVariable> configure)
    {
        var variable = new FuzzyVariable(name, min, max);
        configure(variable);
        _inputs.Add(variable);
        return this;
    }

    /// <summary>Register an output variable.</summary>
    public MamdaniEngine Output(FuzzyVariable variable)
    {
        _outputs.Add(variable ?? throw new ArgumentNullException(nameof(variable)));
        return this;
    }

    /// <summary>Register an output variable with inline set configuration.</summary>
    public MamdaniEngine Output(string name, double min, double max, Action<FuzzyVariable> configure)
    {
        var variable = new FuzzyVariable(name, min, max);
        configure(variable);
        _outputs.Add(variable);
        return this;
    }

    /// <summary>Add a fuzzy rule.</summary>
    public MamdaniEngine Rule(FuzzyRule rule)
    {
        _rules.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
        return this;
    }

    /// <summary>Add a fuzzy rule using builder syntax.</summary>
    public MamdaniEngine Rule(Func<FuzzyRule> ruleFactory)
    {
        _rules.Add(ruleFactory());
        return this;
    }

    /// <summary>
    /// Evaluate the inference engine with crisp input values.
    ///
    /// Pipeline: Fuzzification → Rule Evaluation → Aggregation → Defuzzification
    /// </summary>
    /// <param name="inputs">Variable name → crisp value.</param>
    /// <returns>Variable name → defuzzified crisp output value.</returns>
    /// <exception cref="ArgumentException">If required input variable is missing.</exception>
    public Dictionary<string, double> Evaluate(Dictionary<string, double> inputs)
    {
        var (_, _, results) = RunInference(inputs, collectActivations: false);

        var crisp = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, result) in results)
            crisp[name] = result.CrispValue;
        return crisp;
    }

    /// <summary>
    /// Like <see cref="Evaluate"/>, but returns a rich <see cref="FuzzyResult"/> per output:
    /// crisp value, dominant linguistic set, and per-set activation strengths.
    /// Use this when the caller needs the linguistic answer ("High") alongside the number.
    /// </summary>
    /// <param name="inputs">Variable name → crisp value.</param>
    /// <returns>Variable name → detailed result.</returns>
    /// <exception cref="ArgumentException">If required input variable is missing.</exception>
    public Dictionary<string, FuzzyResult> EvaluateDetailed(Dictionary<string, double> inputs)
    {
        var (_, _, results) = RunInference(inputs, collectActivations: false);
        return results;
    }

    /// <summary>
    /// Runs a full inference and records everything: fuzzified inputs, every rule's firing
    /// strength, and detailed outputs. The returned trace answers "why did the engine decide
    /// this?" via <see cref="InferenceTrace.Explain"/>.
    /// </summary>
    /// <param name="inputs">Variable name → crisp value.</param>
    /// <exception cref="ArgumentException">If required input variable is missing.</exception>
    public InferenceTrace EvaluateWithTrace(Dictionary<string, double> inputs)
    {
        var (fuzzified, activations, results) = RunInference(inputs, collectActivations: true);

        var inputsCopy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var inputVar in _inputs)
            inputsCopy[inputVar.Name] = inputs[inputVar.Name];

        var fuzzifiedView = new Dictionary<string, IReadOnlyDictionary<string, double>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (name, memberships) in fuzzified)
            fuzzifiedView[name] = memberships;

        return new InferenceTrace(inputsCopy, fuzzifiedView, activations!, results);
    }

    /// <summary>
    /// Convenience overload: evaluate with a single output variable.
    /// </summary>
    public double EvaluateSingle(Dictionary<string, double> inputs)
    {
        var results = Evaluate(inputs);
        return results.Values.First();
    }

    /// <summary>
    /// Shared inference core: fuzzify → fire rules → aggregate per-set strengths → defuzzify.
    /// Per-set aggregation keeps only the MAX strength per output set, which is equivalent to
    /// aggregating duplicates during defuzzification (MAX is idempotent) but cheaper.
    /// </summary>
    private (Dictionary<string, Dictionary<string, double>> Fuzzified,
             List<RuleActivation>? Activations,
             Dictionary<string, FuzzyResult> Results)
        RunInference(Dictionary<string, double> inputs, bool collectActivations)
    {
        // ── Step 1: Fuzzification ────────────────────────────────────────
        var fuzzified = new Dictionary<string, Dictionary<string, double>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var inputVar in _inputs)
        {
            if (!inputs.TryGetValue(inputVar.Name, out var crispValue))
                throw new ArgumentException($"Missing input value for variable '{inputVar.Name}'");

            fuzzified[inputVar.Name] = inputVar.Fuzzify(crispValue);
        }

        // ── Step 2: Rule Evaluation ──────────────────────────────────────
        // For each output variable, aggregate MAX firing strength per output set
        var ruleOutputs = new Dictionary<string, Dictionary<string, double>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var outputVar in _outputs)
            ruleOutputs[outputVar.Name] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var activations = collectActivations ? new List<RuleActivation>(_rules.Count) : null;

        foreach (var rule in _rules)
        {
            var strength = rule.Evaluate(fuzzified);
            activations?.Add(new RuleActivation(rule, strength));

            if (strength > 0)
            {
                var outputVarName = rule.Consequent.Variable.Name;
                if (ruleOutputs.TryGetValue(outputVarName, out var setStrengths))
                {
                    var setName = rule.Consequent.SetName;
                    if (!setStrengths.TryGetValue(setName, out var existing) || strength > existing)
                        setStrengths[setName] = strength;
                }
            }
        }

        // ── Step 3 + 4: Aggregation + Defuzzification ────────────────────
        var results = new Dictionary<string, FuzzyResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var outputVar in _outputs)
        {
            var setStrengths = ruleOutputs[outputVar.Name];

            if (setStrengths.Count == 0)
            {
                results[outputVar.Name] = new FuzzyResult(
                    outputVar.Name,
                    (outputVar.Min + outputVar.Max) / 2.0, // fallback to midpoint
                    dominantSet: string.Empty,
                    outputMemberships: setStrengths);
                continue;
            }

            var activationList = new List<(string SetName, double Strength)>(setStrengths.Count);
            var dominantSet = string.Empty;
            var dominantStrength = double.MinValue;

            foreach (var (setName, strength) in setStrengths)
            {
                activationList.Add((setName, strength));
                if (strength > dominantStrength)
                {
                    dominantStrength = strength;
                    dominantSet = setName;
                }
            }

            results[outputVar.Name] = new FuzzyResult(
                outputVar.Name,
                Defuzzify(outputVar, activationList),
                dominantSet,
                setStrengths);
        }

        return (fuzzified, activations, results);
    }

    // ─── Defuzzification ─────────────────────────────────────────────────

    private double Defuzzify(FuzzyVariable outputVar, List<(string SetName, double Strength)> activations)
    {
        return _defuzzMethod switch
        {
            DefuzzificationMethod.Centroid => DefuzzifyCentroid(outputVar, activations),
            DefuzzificationMethod.Bisector => DefuzzifyBisector(outputVar, activations),
            DefuzzificationMethod.MeanOfMaximum => DefuzzifyMOM(outputVar, activations),
            _ => DefuzzifyCentroid(outputVar, activations)
        };
    }

    /// <summary>
    /// Centroid defuzzification: x* = Σ(μ(x)·x) / Σ(μ(x))
    /// Discretized over the output range with configured resolution.
    /// </summary>
    private double DefuzzifyCentroid(FuzzyVariable outputVar, List<(string SetName, double Strength)> activations)
    {
        var step = (outputVar.Max - outputVar.Min) / _resolution;
        double numerator = 0, denominator = 0;

        for (int i = 0; i <= _resolution; i++)
        {
            var x = outputVar.Min + i * step;
            var aggregatedMembership = GetAggregatedMembership(outputVar, activations, x);

            numerator += aggregatedMembership * x;
            denominator += aggregatedMembership;
        }

        return denominator == 0 ? (outputVar.Min + outputVar.Max) / 2.0 : numerator / denominator;
    }

    /// <summary>
    /// Bisector defuzzification: find x where area is split into two equal halves.
    /// </summary>
    private double DefuzzifyBisector(FuzzyVariable outputVar, List<(string SetName, double Strength)> activations)
    {
        var step = (outputVar.Max - outputVar.Min) / _resolution;
        double totalArea = 0;

        // First pass: compute total area
        for (int i = 0; i <= _resolution; i++)
        {
            var x = outputVar.Min + i * step;
            totalArea += GetAggregatedMembership(outputVar, activations, x) * step;
        }

        // Second pass: find bisector
        double runningArea = 0;
        var halfArea = totalArea / 2.0;

        for (int i = 0; i <= _resolution; i++)
        {
            var x = outputVar.Min + i * step;
            runningArea += GetAggregatedMembership(outputVar, activations, x) * step;
            if (runningArea >= halfArea)
                return x;
        }

        return (outputVar.Min + outputVar.Max) / 2.0;
    }

    /// <summary>
    /// Mean of Maximum: average of x values where aggregated membership is maximum.
    /// </summary>
    private double DefuzzifyMOM(FuzzyVariable outputVar, List<(string SetName, double Strength)> activations)
    {
        var step = (outputVar.Max - outputVar.Min) / _resolution;
        double maxMembership = 0;
        double sumX = 0;
        int count = 0;

        for (int i = 0; i <= _resolution; i++)
        {
            var x = outputVar.Min + i * step;
            var membership = GetAggregatedMembership(outputVar, activations, x);

            if (membership > maxMembership + 1e-10)
            {
                maxMembership = membership;
                sumX = x;
                count = 1;
            }
            else if (Math.Abs(membership - maxMembership) < 1e-10 && membership > 0)
            {
                sumX += x;
                count++;
            }
        }

        return count == 0 ? (outputVar.Min + outputVar.Max) / 2.0 : sumX / count;
    }

    /// <summary>
    /// Aggregation: MAX of all activated rule outputs at point x.
    /// Each rule's output is MIN(firingStrength, setMembership(x)).
    /// </summary>
    private static double GetAggregatedMembership(
        FuzzyVariable outputVar,
        List<(string SetName, double Strength)> activations,
        double x)
    {
        double maxMembership = 0;

        foreach (var (setName, strength) in activations)
        {
            var set = outputVar.GetSet(setName);
            // Mamdani implication: MIN(firing strength, set membership)
            var clipped = Math.Min(strength, set.Membership(x));
            // MAX aggregation
            maxMembership = Math.Max(maxMembership, clipped);
        }

        return maxMembership;
    }
}
