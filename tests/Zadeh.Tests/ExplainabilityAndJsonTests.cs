// SPDX-FileCopyrightText: 2026 DeegiTech Teknoloji ve Yazılım Ltd. Şti.
//
// SPDX-License-Identifier: AGPL-3.0-only

using Xunit;

namespace Zadeh.Tests;

public class FuzzySetMetadataTests
{
    [Fact]
    public void Factories_RecordKindAndParameters()
    {
        var triangle = FuzzySet.Triangle("Medium", 20, 50, 80);
        Assert.Equal(MembershipFunctionKind.Triangle, triangle.Kind);
        Assert.Equal(new[] { 20.0, 50.0, 80.0 }, triangle.Parameters);

        var trapezoid = FuzzySet.Trapezoid("Comfort", 20, 40, 60, 80);
        Assert.Equal(MembershipFunctionKind.Trapezoid, trapezoid.Kind);
        Assert.Equal(4, trapezoid.Parameters.Count);

        var gaussian = FuzzySet.Gaussian("Normal", 50, 10);
        Assert.Equal(MembershipFunctionKind.Gaussian, gaussian.Kind);
        Assert.Equal(new[] { 50.0, 10.0 }, gaussian.Parameters);
    }
}

public class EvaluateDetailedTests
{
    private static MamdaniEngine BuildEngine()
    {
        var temp = new FuzzyVariable("Temperature", 0, 50);
        temp.Set(FuzzySet.LeftShoulder("Cold", 15, 25));
        temp.Set(FuzzySet.Triangle("Warm", 20, 30, 40));
        temp.Set(FuzzySet.RightShoulder("Hot", 35, 45));

        var fan = new FuzzyVariable("FanSpeed", 0, 100);
        fan.Set(FuzzySet.Triangle("Slow", 0, 25, 50));
        fan.Set(FuzzySet.Triangle("Medium", 25, 50, 75));
        fan.Set(FuzzySet.Triangle("Fast", 50, 75, 100));

        return new MamdaniEngine()
            .Input(temp)
            .Output(fan)
            .Rule(FuzzyRule.If(temp.Is("Cold")).Then(fan.Is("Slow")))
            .Rule(FuzzyRule.If(temp.Is("Warm")).Then(fan.Is("Medium")))
            .Rule(FuzzyRule.If(temp.Is("Hot")).Then(fan.Is("Fast")));
    }

    [Fact]
    public void ReturnsDominantSetAndActivations()
    {
        var engine = BuildEngine();
        var results = engine.EvaluateDetailed(new Dictionary<string, double> { ["Temperature"] = 42 });

        var fan = results["FanSpeed"];
        Assert.True(fan.AnyRuleFired);
        Assert.Equal("Fast", fan.DominantSet);
        Assert.True(fan.OutputMemberships["Fast"] > 0.5);
        Assert.True(fan.CrispValue > 65);
    }

    [Fact]
    public void MatchesEvaluateCrispValue()
    {
        var engine = BuildEngine();
        var input = new Dictionary<string, double> { ["Temperature"] = 28 };

        var crisp = engine.Evaluate(input)["FanSpeed"];
        var detailed = engine.EvaluateDetailed(input)["FanSpeed"];

        Assert.Equal(crisp, detailed.CrispValue, precision: 10);
    }

    [Fact]
    public void PartialMembership_ReportsBothSets()
    {
        var engine = BuildEngine();
        // 22 is partially Cold and partially Warm
        var fan = engine.EvaluateDetailed(new Dictionary<string, double> { ["Temperature"] = 22 })["FanSpeed"];

        Assert.True(fan.OutputMemberships.ContainsKey("Slow"));
        Assert.True(fan.OutputMemberships.ContainsKey("Medium"));
    }

    [Fact]
    public void NoRuleFired_FallsBackToMidpointWithEmptyDominantSet()
    {
        var temp = new FuzzyVariable("Temp", 0, 100);
        temp.Set(FuzzySet.Triangle("Narrow", 40, 50, 60));

        var outVar = new FuzzyVariable("Out", 0, 100);
        outVar.Set(FuzzySet.Triangle("Mid", 25, 50, 75));

        var engine = new MamdaniEngine()
            .Input(temp)
            .Output(outVar)
            .Rule(FuzzyRule.If(temp.Is("Narrow")).Then(outVar.Is("Mid")));

        var result = engine.EvaluateDetailed(new Dictionary<string, double> { ["Temp"] = 5 })["Out"];

        Assert.False(result.AnyRuleFired);
        Assert.Equal(string.Empty, result.DominantSet);
        Assert.Equal(50.0, result.CrispValue, precision: 5);
        Assert.Empty(result.OutputMemberships);
    }
}

public class EvaluateWithTraceTests
{
    private static MamdaniEngine BuildTwoInputEngine()
    {
        var demand = new FuzzyVariable("Demand", 0, 100);
        demand.Set(FuzzySet.LeftShoulder("Low", 15, 35));
        demand.Set(FuzzySet.RightShoulder("High", 65, 85));

        var stock = new FuzzyVariable("Stock", 0, 100);
        stock.Set(FuzzySet.LeftShoulder("Scarce", 10, 25));
        stock.Set(FuzzySet.RightShoulder("Surplus", 70, 90));

        var price = new FuzzyVariable("Price", 50, 150);
        price.Set(FuzzySet.LeftShoulder("Discount", 60, 80));
        price.Set(FuzzySet.RightShoulder("Premium", 120, 145));

        return new MamdaniEngine()
            .Input(demand).Input(stock).Output(price)
            .Rule(FuzzyRule.If(demand.Is("High")).And(stock.Is("Scarce")).Then(price.Is("Premium")))
            .Rule(FuzzyRule.If(demand.Is("Low")).And(stock.Is("Surplus")).Then(price.Is("Discount")));
    }

    [Fact]
    public void Trace_ContainsAllRulesWithStrengths()
    {
        var engine = BuildTwoInputEngine();
        var trace = engine.EvaluateWithTrace(new Dictionary<string, double> { ["Demand"] = 90, ["Stock"] = 5 });

        Assert.Equal(2, trace.RuleActivations.Count);
        Assert.True(trace.RuleActivations[0].Fired);      // High+Scarce fires
        Assert.False(trace.RuleActivations[1].Fired);     // Low+Surplus does not
        Assert.Equal(1.0, trace.RuleActivations[0].Strength, precision: 5);
    }

    [Fact]
    public void Trace_RecordsInputsAndFuzzification()
    {
        var engine = BuildTwoInputEngine();
        var trace = engine.EvaluateWithTrace(new Dictionary<string, double> { ["Demand"] = 90, ["Stock"] = 5 });

        Assert.Equal(90, trace.Inputs["Demand"]);
        Assert.Equal(1.0, trace.FuzzifiedInputs["Demand"]["High"], precision: 5);
        Assert.Equal(0.0, trace.FuzzifiedInputs["Demand"]["Low"], precision: 5);
    }

    [Fact]
    public void Trace_CrispValueMatchesEvaluate()
    {
        var engine = BuildTwoInputEngine();
        var input = new Dictionary<string, double> { ["Demand"] = 72, ["Stock"] = 18 };

        var crisp = engine.Evaluate(input)["Price"];
        var trace = engine.EvaluateWithTrace(input);

        Assert.Equal(crisp, trace.Results["Price"].CrispValue, precision: 10);
    }

    [Fact]
    public void Explain_ProducesReadableSections()
    {
        var engine = BuildTwoInputEngine();
        var trace = engine.EvaluateWithTrace(new Dictionary<string, double> { ["Demand"] = 90, ["Stock"] = 5 });

        var explanation = trace.Explain();

        Assert.Contains("INPUTS", explanation);
        Assert.Contains("RULES", explanation);
        Assert.Contains("OUTPUTS", explanation);
        Assert.Contains("Demand", explanation);
        Assert.Contains("Premium", explanation);
    }

    [Fact]
    public void Explain_CanHideSilentRules()
    {
        var engine = BuildTwoInputEngine();
        var trace = engine.EvaluateWithTrace(new Dictionary<string, double> { ["Demand"] = 90, ["Stock"] = 5 });

        var full = trace.Explain(includeSilentRules: true);
        var compact = trace.Explain(includeSilentRules: false);

        Assert.True(full.Length > compact.Length);
        Assert.DoesNotContain("✗", compact);
    }
}

public class JsonSerializationTests
{
    private const string ValidDocument = """
    {
      "defuzzification": "centroid",
      "resolution": 200,
      "inputs": [
        { "name": "Demand", "min": 0, "max": 100,
          "sets": [
            { "name": "Low",  "type": "leftShoulder",  "params": [15, 35] },
            { "name": "Medium", "type": "triangle",    "params": [25, 50, 75] },
            { "name": "High", "type": "rightShoulder", "params": [65, 85] } ] },
        { "name": "Stock", "min": 0, "max": 100,
          "sets": [
            { "name": "Scarce",  "type": "leftShoulder",  "params": [10, 25] },
            { "name": "Surplus", "type": "rightShoulder", "params": [70, 90] } ] }
      ],
      "outputs": [
        { "name": "Price", "min": 50, "max": 150,
          "sets": [
            { "name": "Discount", "type": "leftShoulder",  "params": [60, 80] },
            { "name": "Normal",   "type": "triangle",      "params": [85, 100, 115] },
            { "name": "Premium",  "type": "rightShoulder", "params": [120, 145] } ] }
      ],
      "rules": [
        { "if": { "Demand": "High", "Stock": "Scarce" }, "then": { "Price": "Premium" } },
        { "if": { "Demand": "Low",  "Stock": "Surplus" }, "then": { "Price": "Discount" }, "weight": 0.8 },
        { "if": { "Demand": "Medium" }, "then": { "Price": "Normal" } }
      ]
    }
    """;

    [Fact]
    public void FromJson_BuildsWorkingEngine()
    {
        var engine = MamdaniEngine.FromJson(ValidDocument);

        Assert.Equal(2, engine.Inputs.Count);
        Assert.Single(engine.Outputs);
        Assert.Equal(3, engine.Rules.Count);
        Assert.Equal(0.8, engine.Rules[1].Weight, precision: 5);

        var price = engine.EvaluateSingle(new Dictionary<string, double> { ["Demand"] = 90, ["Stock"] = 5 });
        Assert.True(price > 115, $"High demand + scarce stock should produce premium price, got {price:F1}");
    }

    [Fact]
    public void FromJson_MatchesFluentApiOutput()
    {
        var fromJson = MamdaniEngine.FromJson(ValidDocument);

        var demand = new FuzzyVariable("Demand", 0, 100);
        demand.Set(FuzzySet.LeftShoulder("Low", 15, 35));
        demand.Set(FuzzySet.Triangle("Medium", 25, 50, 75));
        demand.Set(FuzzySet.RightShoulder("High", 65, 85));
        var stock = new FuzzyVariable("Stock", 0, 100);
        stock.Set(FuzzySet.LeftShoulder("Scarce", 10, 25));
        stock.Set(FuzzySet.RightShoulder("Surplus", 70, 90));
        var price = new FuzzyVariable("Price", 50, 150);
        price.Set(FuzzySet.LeftShoulder("Discount", 60, 80));
        price.Set(FuzzySet.Triangle("Normal", 85, 100, 115));
        price.Set(FuzzySet.RightShoulder("Premium", 120, 145));

        var fluent = new MamdaniEngine()
            .Input(demand).Input(stock).Output(price)
            .Rule(FuzzyRule.If(demand.Is("High")).And(stock.Is("Scarce")).Then(price.Is("Premium")))
            .Rule(FuzzyRule.If(demand.Is("Low")).And(stock.Is("Surplus")).WithWeight(0.8).Then(price.Is("Discount")))
            .Rule(FuzzyRule.If(demand.Is("Medium")).Then(price.Is("Normal")));

        var input = new Dictionary<string, double> { ["Demand"] = 72, ["Stock"] = 18 };
        Assert.Equal(
            fluent.EvaluateSingle(input),
            fromJson.EvaluateSingle(input),
            precision: 10);
    }

    [Fact]
    public void ToJson_RoundTripsToIdenticalBehavior()
    {
        var original = MamdaniEngine.FromJson(ValidDocument);
        var roundTripped = MamdaniEngine.FromJson(original.ToJson());

        for (var demand = 0; demand <= 100; demand += 10)
        {
            var input = new Dictionary<string, double> { ["Demand"] = demand, ["Stock"] = 100 - demand };
            Assert.Equal(
                original.EvaluateSingle(input),
                roundTripped.EvaluateSingle(input),
                precision: 10);
        }
    }

    [Fact]
    public void ToJson_PreservesOptions()
    {
        var engine = MamdaniEngine.FromJson(ValidDocument);
        var json = engine.ToJson();

        Assert.Contains("\"defuzzification\": \"centroid\"", json);
        Assert.Contains("\"resolution\": 200", json);
        Assert.Contains("\"weight\": 0.8", json);
    }

    [Fact]
    public void ToJson_EmitsCurrentSchemaVersion()
    {
        var engine = MamdaniEngine.FromJson(ValidDocument);

        Assert.Contains($"\"schemaVersion\": {MamdaniEngine.CurrentSchemaVersion}", engine.ToJson());
    }

    [Fact]
    public void FromJson_MissingSchemaVersion_TreatedAsVersion1()
    {
        // ValidDocument has no schemaVersion property — legacy documents must keep loading.
        var engine = MamdaniEngine.FromJson(ValidDocument);

        Assert.Equal(2, engine.Inputs.Count);
    }

    [Fact]
    public void FromJson_ExplicitVersion1_Loads()
    {
        var json = string.Concat("{ \"schemaVersion\": 1,", ValidDocument.TrimStart().AsSpan(1));

        var engine = MamdaniEngine.FromJson(json);

        Assert.Equal(2, engine.Inputs.Count);
    }

    [Fact]
    public void FromJson_NewerSchemaVersion_ThrowsWithUpgradeHint()
    {
        var json = string.Concat("{ \"schemaVersion\": 2,", ValidDocument.TrimStart().AsSpan(1));

        var ex = Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));

        Assert.Contains("schema version 2", ex.Message);
        Assert.Contains("Upgrade", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"bir\"")]
    public void FromJson_InvalidSchemaVersion_Throws(string versionLiteral)
    {
        var json = string.Concat($"{{ \"schemaVersion\": {versionLiteral},", ValidDocument.TrimStart().AsSpan(1));

        Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJson_EmptyDocument_Throws(string json)
    {
        Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));
    }

    [Fact]
    public void FromJson_UnknownSetType_ThrowsWithValidOptions()
    {
        var json = ValidDocument.Replace("\"leftShoulder\"", "\"sigmoid\"");
        var ex = Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));
        Assert.Contains("sigmoid", ex.Message);
        Assert.Contains("triangle", ex.Message);
    }

    [Fact]
    public void FromJson_RuleWithUnknownVariable_Throws()
    {
        var json = ValidDocument.Replace("\"if\": { \"Demand\": \"Medium\" }", "\"if\": { \"Basket\": \"Medium\" }");
        var ex = Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));
        Assert.Contains("Basket", ex.Message);
    }

    [Fact]
    public void FromJson_RuleWithUnknownSet_Throws()
    {
        var json = ValidDocument.Replace("\"then\": { \"Price\": \"Normal\" }", "\"then\": { \"Price\": \"Gigantic\" }");
        Assert.Throws<KeyNotFoundException>(() => MamdaniEngine.FromJson(json));
    }

    [Fact]
    public void FromJson_WrongParamCount_Throws()
    {
        var json = ValidDocument.Replace("\"params\": [25, 50, 75]", "\"params\": [25, 50]");
        var ex = Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));
        Assert.Contains("requires 3 params", ex.Message);
    }

    [Fact]
    public void FromJson_MissingRules_Throws()
    {
        var json = ValidDocument[..ValidDocument.LastIndexOf("\"rules\"")] + "\"rules\": [] }";
        Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));
    }

    [Fact]
    public void FromJson_UnknownDefuzzification_Throws()
    {
        var json = ValidDocument.Replace("\"centroid\"", "\"average\"");
        var ex = Assert.Throws<ArgumentException>(() => MamdaniEngine.FromJson(json));
        Assert.Contains("average", ex.Message);
    }
}
