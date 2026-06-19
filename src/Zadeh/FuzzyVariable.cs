namespace Zadeh;

/// <summary>
/// A linguistic variable with a name, numeric range, and a collection of fuzzy sets.
/// 
/// Example: Temperature [0, 100] with sets { Cold, Warm, Hot }
/// 
/// Fuzzification: given a crisp input value, computes the degree of membership
/// for every fuzzy set in this variable.
/// </summary>
public sealed class FuzzyVariable
{
    /// <summary>Variable name (e.g., "Temperature", "FanSpeed").</summary>
    public string Name { get; }

    /// <summary>Minimum value of the universe of discourse.</summary>
    public double Min { get; }

    /// <summary>Maximum value of the universe of discourse.</summary>
    public double Max { get; }

    private readonly Dictionary<string, FuzzySet> _sets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All fuzzy sets defined for this variable.</summary>
    public IReadOnlyDictionary<string, FuzzySet> Sets => _sets;

    /// <summary>
    /// Creates a new fuzzy variable.
    /// </summary>
    /// <param name="name">Linguistic name</param>
    /// <param name="min">Minimum of the universe of discourse</param>
    /// <param name="max">Maximum of the universe of discourse</param>
    public FuzzyVariable(string name, double min, double max)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        if (min >= max)
            throw new ArgumentException($"Min must be less than Max, got ({min}, {max})");
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Adds a fuzzy set to this variable.
    /// </summary>
    /// <returns>This variable (for chaining).</returns>
    public FuzzyVariable Set(FuzzySet set)
    {
        _sets[set.Name] = set ?? throw new ArgumentNullException(nameof(set));
        return this;
    }

    /// <summary>
    /// Retrieves a fuzzy set by name.
    /// </summary>
    /// <exception cref="KeyNotFoundException">If set name doesn't exist.</exception>
    public FuzzySet GetSet(string name)
    {
        if (_sets.TryGetValue(name, out var set))
            return set;
        throw new KeyNotFoundException($"Fuzzy set '{name}' not found in variable '{Name}'. Available: {string.Join(", ", _sets.Keys)}");
    }

    /// <summary>
    /// Shorthand for referencing a set by name (for rule building).
    /// Returns a (Variable, SetName) pair used in rule construction.
    /// </summary>
    public FuzzyTerm Is(string setName) => new(this, setName);

    /// <summary>
    /// Fuzzifies a crisp value: computes membership degree for every set.
    /// </summary>
    /// <returns>Dictionary of set name → membership degree [0, 1].</returns>
    public Dictionary<string, double> Fuzzify(double value)
    {
        var result = new Dictionary<string, double>(_sets.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, set) in _sets)
        {
            result[name] = set.Membership(value);
        }
        return result;
    }

    /// <summary>Returns a string representation of this variable.</summary>
    public override string ToString() => $"FuzzyVariable(\"{Name}\", [{Min}, {Max}], sets: {_sets.Count})";
}

/// <summary>
/// A reference to a specific fuzzy set within a variable.
/// Used in rule construction: temperature.Is("Hot") → FuzzyTerm.
/// </summary>
public readonly record struct FuzzyTerm(FuzzyVariable Variable, string SetName)
{
    /// <summary>Resolves the actual FuzzySet from the variable.</summary>
    public FuzzySet Resolve() => Variable.GetSet(SetName);
}
