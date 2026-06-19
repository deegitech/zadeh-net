namespace Zadeh;

/// <summary>
/// An IF-THEN fuzzy rule: IF (antecedent) THEN (consequent).
/// 
/// Supports single and multi-condition antecedents with AND semantics (MIN operator).
/// 
/// Example:
///   FuzzyRule.If(temperature.Is("Hot")).And(humidity.Is("High")).Then(fanSpeed.Is("Fast"))
/// </summary>
public sealed class FuzzyRule
{
    /// <summary>Input conditions (all must be satisfied — AND/MIN semantics).</summary>
    public IReadOnlyList<FuzzyTerm> Antecedents => _antecedents;

    /// <summary>Output assignment.</summary>
    public FuzzyTerm Consequent { get; }

    /// <summary>Optional weight for this rule [0, 1]. Default = 1.0.</summary>
    public double Weight { get; }

    private readonly List<FuzzyTerm> _antecedents;

    private FuzzyRule(List<FuzzyTerm> antecedents, FuzzyTerm consequent, double weight)
    {
        _antecedents = antecedents;
        Consequent = consequent;
        Weight = Math.Clamp(weight, 0.0, 1.0);
    }

    /// <summary>
    /// Evaluates the rule's firing strength given fuzzified input values.
    /// Uses MIN (AND) to combine multiple antecedent memberships.
    /// </summary>
    /// <param name="fuzzifiedInputs">Variable name → (set name → membership degree).</param>
    /// <returns>Firing strength [0, 1], weighted by rule weight.</returns>
    public double Evaluate(Dictionary<string, Dictionary<string, double>> fuzzifiedInputs)
    {
        var strength = double.MaxValue;

        foreach (var term in _antecedents)
        {
            if (!fuzzifiedInputs.TryGetValue(term.Variable.Name, out var setMemberships))
                return 0.0;

            if (!setMemberships.TryGetValue(term.SetName, out var membership))
                return 0.0;

            strength = Math.Min(strength, membership);
        }

        return strength == double.MaxValue ? 0.0 : strength * Weight;
    }

    /// <summary>Returns a human-readable representation of the rule.</summary>
    public override string ToString()
    {
        var conditions = string.Join(" AND ", _antecedents.Select(a => $"{a.Variable.Name}={a.SetName}"));
        return $"IF {conditions} THEN {Consequent.Variable.Name}={Consequent.SetName} (w={Weight:F2})";
    }

    // ─── Builder ─────────────────────────────────────────────────────────

    /// <summary>Start building a rule with the first condition.</summary>
    public static RuleBuilder If(FuzzyTerm condition) => new RuleBuilder().And(condition);

    /// <summary>Fluent rule builder: If(...).And(...).Then(...).</summary>
    public sealed class RuleBuilder
    {
        private readonly List<FuzzyTerm> _conditions = new();
        private double _weight = 1.0;

        /// <summary>Add another AND condition.</summary>
        public RuleBuilder And(FuzzyTerm condition)
        {
            _conditions.Add(condition);
            return this;
        }

        /// <summary>Set rule weight (default 1.0).</summary>
        public RuleBuilder WithWeight(double weight)
        {
            _weight = weight;
            return this;
        }

        /// <summary>Set the consequent (output) and build the rule.</summary>
        public FuzzyRule Then(FuzzyTerm consequent)
        {
            if (_conditions.Count == 0)
                throw new InvalidOperationException("A rule must have at least one antecedent (IF condition).");

            return new FuzzyRule(new List<FuzzyTerm>(_conditions), consequent, _weight);
        }
    }
}
