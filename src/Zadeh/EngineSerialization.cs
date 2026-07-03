using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zadeh;

/// <summary>
/// JSON (de)serialization for <see cref="MamdaniEngine"/>: define variables, sets, and rules
/// in configuration instead of code, and load them at runtime with <see cref="FromJson"/>.
///
/// Uses only System.Text.Json from the .NET base library — no external dependencies.
///
/// Document shape:
/// <code>
/// {
///   "defuzzification": "centroid",          // optional: centroid | bisector | meanOfMaximum
///   "resolution": 200,                       // optional
///   "inputs":  [ { "name": "Demand", "min": 0, "max": 100,
///                  "sets": [ { "name": "Low",  "type": "leftShoulder", "params": [15, 35] },
///                            { "name": "High", "type": "rightShoulder", "params": [65, 85] } ] } ],
///   "outputs": [ { "name": "Price", "min": 50, "max": 150,
///                  "sets": [ { "name": "Premium", "type": "rightShoulder", "params": [120, 145] } ] } ],
///   "rules":   [ { "if": { "Demand": "High" }, "then": { "Price": "Premium" }, "weight": 1.0 } ]
/// }
/// </code>
/// Set types and their params (factory-argument order):
/// triangle [left, peak, right] · trapezoid [a, b, c, d] · leftShoulder [midpoint, edge]
/// · rightShoulder [edge, midpoint] · gaussian [mean, sigma]
/// </summary>
public sealed partial class MamdaniEngine
{
    /// <summary>
    /// Builds a fully configured engine from a JSON document (see class-level docs for the shape).
    /// All references are validated during parsing: unknown variables, sets, or shape types
    /// fail immediately with a descriptive message.
    /// </summary>
    /// <exception cref="ArgumentException">On structural or semantic errors in the document.</exception>
    /// <exception cref="JsonException">On malformed JSON.</exception>
    public static MamdaniEngine FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON document is empty.");

        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new ArgumentException("JSON root must be an object.");

        // ── Engine options ───────────────────────────────────────────────
        var defuzz = DefuzzificationMethod.Centroid;
        if (root["defuzzification"] is JsonNode defuzzNode)
        {
            var text = defuzzNode.GetValue<string>();
            if (!Enum.TryParse(text, ignoreCase: true, out defuzz))
                throw new ArgumentException(
                    $"Unknown defuzzification method '{text}'. Valid: centroid, bisector, meanOfMaximum.");
        }

        var resolution = root["resolution"]?.GetValue<int>() ?? 200;
        var engine = new MamdaniEngine(defuzz, resolution);

        // ── Variables ────────────────────────────────────────────────────
        var variablesByName = new Dictionary<string, FuzzyVariable>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in ParseVariables(root, "inputs"))
        {
            engine.Input(variable);
            variablesByName[variable.Name] = variable;
        }

        foreach (var variable in ParseVariables(root, "outputs"))
        {
            engine.Output(variable);
            variablesByName[variable.Name] = variable;
        }

        if (engine.Inputs.Count == 0)
            throw new ArgumentException("Document must define at least one input variable.");
        if (engine.Outputs.Count == 0)
            throw new ArgumentException("Document must define at least one output variable.");

        // ── Rules ────────────────────────────────────────────────────────
        var rulesNode = root["rules"]?.AsArray()
            ?? throw new ArgumentException("Document must contain a 'rules' array.");

        foreach (var ruleNode in rulesNode)
        {
            var ruleObj = ruleNode?.AsObject()
                ?? throw new ArgumentException("Each rule must be an object.");
            engine.Rule(ParseRule(ruleObj, variablesByName));
        }

        if (engine.Rules.Count == 0)
            throw new ArgumentException("Document must define at least one rule.");

        return engine;
    }

    /// <summary>
    /// Serializes this engine's full configuration (variables, sets, rules, options) to JSON
    /// in the same shape <see cref="FromJson"/> reads. Round-trip safe.
    /// </summary>
    public string ToJson(bool indented = true)
    {
        var root = new JsonObject
        {
            ["defuzzification"] = ToCamelCase(_defuzzMethod.ToString()),
            ["resolution"] = _resolution,
            ["inputs"] = SerializeVariables(_inputs),
            ["outputs"] = SerializeVariables(_outputs),
            ["rules"] = SerializeRules(_rules)
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
            root.WriteTo(writer);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    // ─── Parsing helpers ─────────────────────────────────────────────────

    private static IEnumerable<FuzzyVariable> ParseVariables(JsonObject root, string sectionName)
    {
        if (root[sectionName] is not JsonNode section)
            yield break;

        foreach (var node in section.AsArray())
        {
            var obj = node?.AsObject()
                ?? throw new ArgumentException($"Each entry in '{sectionName}' must be an object.");

            var name = GetRequiredString(obj, "name", $"variable in '{sectionName}'");
            var min = GetRequiredDouble(obj, "min", $"variable '{name}'");
            var max = GetRequiredDouble(obj, "max", $"variable '{name}'");

            var variable = new FuzzyVariable(name, min, max);

            var setsNode = obj["sets"]?.AsArray()
                ?? throw new ArgumentException($"Variable '{name}' must contain a 'sets' array.");

            foreach (var setNode in setsNode)
            {
                var setObj = setNode?.AsObject()
                    ?? throw new ArgumentException($"Each set of variable '{name}' must be an object.");
                variable.Set(ParseSet(setObj, name));
            }

            if (variable.Sets.Count == 0)
                throw new ArgumentException($"Variable '{name}' must define at least one set.");

            yield return variable;
        }
    }

    private static FuzzySet ParseSet(JsonObject obj, string variableName)
    {
        var name = GetRequiredString(obj, "name", $"set of variable '{variableName}'");
        var typeText = GetRequiredString(obj, "type", $"set '{name}' of variable '{variableName}'");

        if (!Enum.TryParse<MembershipFunctionKind>(typeText, ignoreCase: true, out var kind))
            throw new ArgumentException(
                $"Unknown set type '{typeText}' on set '{name}' of variable '{variableName}'. " +
                "Valid: triangle, trapezoid, leftShoulder, rightShoulder, gaussian.");

        var paramsNode = obj["params"]?.AsArray()
            ?? throw new ArgumentException($"Set '{name}' of variable '{variableName}' must contain a 'params' array.");
        var p = paramsNode.Select(n => n!.GetValue<double>()).ToArray();

        var expected = kind switch
        {
            MembershipFunctionKind.Triangle => 3,
            MembershipFunctionKind.Trapezoid => 4,
            _ => 2
        };
        if (p.Length != expected)
            throw new ArgumentException(
                $"Set '{name}' ({ToCamelCase(kind.ToString())}) of variable '{variableName}' requires {expected} params, got {p.Length}.");

        return kind switch
        {
            MembershipFunctionKind.Triangle => FuzzySet.Triangle(name, p[0], p[1], p[2]),
            MembershipFunctionKind.Trapezoid => FuzzySet.Trapezoid(name, p[0], p[1], p[2], p[3]),
            MembershipFunctionKind.LeftShoulder => FuzzySet.LeftShoulder(name, p[0], p[1]),
            MembershipFunctionKind.RightShoulder => FuzzySet.RightShoulder(name, p[0], p[1]),
            MembershipFunctionKind.Gaussian => FuzzySet.Gaussian(name, p[0], p[1]),
            _ => throw new ArgumentException($"Unhandled set type '{kind}'.")
        };
    }

    private static FuzzyRule ParseRule(JsonObject obj, Dictionary<string, FuzzyVariable> variablesByName)
    {
        var ifObj = obj["if"]?.AsObject()
            ?? throw new ArgumentException("Each rule must contain an 'if' object ({ variable: set, ... }).");
        var thenObj = obj["then"]?.AsObject()
            ?? throw new ArgumentException("Each rule must contain a 'then' object ({ variable: set }).");

        if (ifObj.Count == 0)
            throw new ArgumentException("A rule's 'if' object must contain at least one condition.");
        if (thenObj.Count != 1)
            throw new ArgumentException($"A rule's 'then' object must contain exactly one assignment, got {thenObj.Count}.");

        FuzzyRule.RuleBuilder? builder = null;
        foreach (var (variableName, setNode) in ifObj)
        {
            var term = ResolveTerm(variableName, setNode, variablesByName, "if");
            builder = builder is null ? FuzzyRule.If(term) : builder.And(term);
        }

        if (obj["weight"] is JsonNode weightNode)
            builder!.WithWeight(weightNode.GetValue<double>());

        var (outVariable, outSetNode) = thenObj.First();
        return builder!.Then(ResolveTerm(outVariable, outSetNode, variablesByName, "then"));
    }

    private static FuzzyTerm ResolveTerm(
        string variableName,
        JsonNode? setNode,
        Dictionary<string, FuzzyVariable> variablesByName,
        string clause)
    {
        if (!variablesByName.TryGetValue(variableName, out var variable))
            throw new ArgumentException(
                $"Rule '{clause}' references unknown variable '{variableName}'. Known: {string.Join(", ", variablesByName.Keys)}.");

        var setName = setNode?.GetValue<string>()
            ?? throw new ArgumentException($"Rule '{clause}' condition for '{variableName}' must be a set name string.");

        variable.GetSet(setName); // validates existence, throws with available names
        return variable.Is(setName);
    }

    // ─── Serialization helpers ───────────────────────────────────────────

    private static JsonArray SerializeVariables(IEnumerable<FuzzyVariable> variables)
    {
        var array = new JsonArray();
        foreach (var variable in variables)
        {
            var sets = new JsonArray();
            foreach (var set in variable.Sets.Values)
            {
                var setParams = new JsonArray();
                foreach (var p in set.Parameters)
                    setParams.Add(p);

                sets.Add(new JsonObject
                {
                    ["name"] = set.Name,
                    ["type"] = ToCamelCase(set.Kind.ToString()),
                    ["params"] = setParams
                });
            }

            array.Add(new JsonObject
            {
                ["name"] = variable.Name,
                ["min"] = variable.Min,
                ["max"] = variable.Max,
                ["sets"] = sets
            });
        }
        return array;
    }

    private static JsonArray SerializeRules(IEnumerable<FuzzyRule> rules)
    {
        var array = new JsonArray();
        foreach (var rule in rules)
        {
            var ifObj = new JsonObject();
            foreach (var term in rule.Antecedents)
            {
                if (ifObj.ContainsKey(term.Variable.Name))
                    throw new NotSupportedException(
                        $"Rule '{rule}' has multiple conditions on variable '{term.Variable.Name}'; " +
                        "this cannot be expressed in the JSON rule format.");
                ifObj[term.Variable.Name] = term.SetName;
            }

            var ruleObj = new JsonObject
            {
                ["if"] = ifObj,
                ["then"] = new JsonObject { [rule.Consequent.Variable.Name] = rule.Consequent.SetName }
            };

            if (rule.Weight != 1.0)
                ruleObj["weight"] = rule.Weight;

            array.Add(ruleObj);
        }
        return array;
    }

    private static string ToCamelCase(string pascalCase) =>
        char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];

    private static string GetRequiredString(JsonObject obj, string property, string context)
    {
        var value = obj[property]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing or empty '{property}' on {context}.");
        return value;
    }

    private static double GetRequiredDouble(JsonObject obj, string property, string context)
    {
        return obj[property]?.GetValue<double>()
            ?? throw new ArgumentException($"Missing '{property}' on {context}.");
    }
}
