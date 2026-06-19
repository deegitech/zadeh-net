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
public sealed class MamdaniEngine
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
        // For each output variable, collect (set name, firing strength) pairs
        var ruleOutputs = new Dictionary<string, List<(string SetName, double Strength)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var outputVar in _outputs)
            ruleOutputs[outputVar.Name] = new List<(string, double)>();

        foreach (var rule in _rules)
        {
            var strength = rule.Evaluate(fuzzified);
            if (strength > 0)
            {
                var outputVarName = rule.Consequent.Variable.Name;
                if (ruleOutputs.ContainsKey(outputVarName))
                {
                    ruleOutputs[outputVarName].Add((rule.Consequent.SetName, strength));
                }
            }
        }

        // ── Step 3 + 4: Aggregation + Defuzzification ────────────────────
        var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var outputVar in _outputs)
        {
            var activations = ruleOutputs[outputVar.Name];
            if (activations.Count == 0)
            {
                results[outputVar.Name] = (outputVar.Min + outputVar.Max) / 2.0; // fallback to midpoint
                continue;
            }

            results[outputVar.Name] = Defuzzify(outputVar, activations);
        }

        return results;
    }

    /// <summary>
    /// Convenience overload: evaluate with a single output variable.
    /// </summary>
    public double EvaluateSingle(Dictionary<string, double> inputs)
    {
        var results = Evaluate(inputs);
        return results.Values.First();
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
