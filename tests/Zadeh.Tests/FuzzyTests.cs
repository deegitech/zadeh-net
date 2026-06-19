using Xunit;

namespace Zadeh.Tests;

public class FuzzySetTests
{
    [Theory]
    [InlineData(0, 0.0)]    // before left foot
    [InlineData(20, 0.0)]   // at left foot
    [InlineData(35, 0.5)]   // halfway up
    [InlineData(50, 1.0)]   // at peak
    [InlineData(65, 0.5)]   // halfway down
    [InlineData(80, 0.0)]   // at right foot
    [InlineData(100, 0.0)]  // beyond right foot
    public void Triangle_ReturnCorrectMembership(double input, double expected)
    {
        var set = FuzzySet.Triangle("Medium", 20, 50, 80);
        Assert.Equal(expected, set.Membership(input), precision: 2);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(20, 0.0)]
    [InlineData(30, 0.5)]
    [InlineData(40, 1.0)]
    [InlineData(60, 1.0)]
    [InlineData(70, 0.5)]
    [InlineData(80, 0.0)]
    public void Trapezoid_PlateauRegion_FullMembership(double input, double expected)
    {
        var set = FuzzySet.Trapezoid("Comfortable", 20, 40, 60, 80);
        Assert.Equal(expected, set.Membership(input), precision: 2);
    }

    [Theory]
    [InlineData(0, 1.0)]    // well below midpoint
    [InlineData(20, 1.0)]   // at midpoint
    [InlineData(30, 0.5)]   // halfway
    [InlineData(40, 0.0)]   // at edge
    [InlineData(60, 0.0)]   // beyond edge
    public void LeftShoulder_OpenLeftEnd(double input, double expected)
    {
        var set = FuzzySet.LeftShoulder("Low", 20, 40);
        Assert.Equal(expected, set.Membership(input), precision: 2);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(40, 0.0)]   // at edge
    [InlineData(50, 0.5)]   // halfway
    [InlineData(60, 1.0)]   // at midpoint
    [InlineData(100, 1.0)]  // well above
    public void RightShoulder_OpenRightEnd(double input, double expected)
    {
        var set = FuzzySet.RightShoulder("High", 40, 60);
        Assert.Equal(expected, set.Membership(input), precision: 2);
    }

    [Fact]
    public void Gaussian_PeakAtMean()
    {
        var set = FuzzySet.Gaussian("Normal", 50, 10);
        Assert.Equal(1.0, set.Membership(50), precision: 5);
        Assert.True(set.Membership(40) > 0.5);
        Assert.True(set.Membership(30) < 0.5);
    }

    [Fact]
    public void Triangle_InvalidParameters_Throws()
    {
        Assert.Throws<ArgumentException>(() => FuzzySet.Triangle("Bad", 80, 50, 20));
    }

    [Fact]
    public void Gaussian_ZeroSigma_Throws()
    {
        Assert.Throws<ArgumentException>(() => FuzzySet.Gaussian("Bad", 50, 0));
    }
}

public class FuzzyVariableTests
{
    [Fact]
    public void Fuzzify_ReturnsAllSetMemberships()
    {
        var temp = new FuzzyVariable("Temp", 0, 100);
        temp.Set(FuzzySet.LeftShoulder("Cold", 20, 35));
        temp.Set(FuzzySet.Triangle("Warm", 25, 40, 55));
        temp.Set(FuzzySet.RightShoulder("Hot", 45, 60));

        var result = temp.Fuzzify(30);

        Assert.Equal(3, result.Count);
        Assert.True(result["Cold"] > 0);    // partially cold
        Assert.True(result["Warm"] > 0);    // partially warm
        Assert.Equal(0.0, result["Hot"]);   // not hot
    }

    [Fact]
    public void Is_ReturnsFuzzyTerm()
    {
        var temp = new FuzzyVariable("Temp", 0, 100);
        temp.Set(FuzzySet.Triangle("Warm", 25, 40, 55));

        var term = temp.Is("Warm");
        Assert.Equal("Temp", term.Variable.Name);
        Assert.Equal("Warm", term.SetName);
    }

    [Fact]
    public void GetSet_UnknownName_Throws()
    {
        var temp = new FuzzyVariable("Temp", 0, 100);
        Assert.Throws<KeyNotFoundException>(() => temp.GetSet("NonExistent"));
    }

    [Fact]
    public void Constructor_MinGreaterThanMax_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FuzzyVariable("Bad", 100, 0));
    }
}

public class FuzzyRuleTests
{
    [Fact]
    public void SingleCondition_Evaluates()
    {
        var temp = new FuzzyVariable("Temp", 0, 100);
        temp.Set(FuzzySet.RightShoulder("Hot", 45, 60));

        var fan = new FuzzyVariable("Fan", 0, 100);
        fan.Set(FuzzySet.Triangle("Fast", 50, 75, 100));

        var rule = FuzzyRule.If(temp.Is("Hot")).Then(fan.Is("Fast"));

        var fuzzified = new Dictionary<string, Dictionary<string, double>>
        {
            ["Temp"] = temp.Fuzzify(55)
        };

        var strength = rule.Evaluate(fuzzified);
        Assert.True(strength > 0.5);
    }

    [Fact]
    public void MultiCondition_UsesMinAnd()
    {
        var temp = new FuzzyVariable("Temp", 0, 100);
        temp.Set(FuzzySet.RightShoulder("Hot", 45, 60));

        var humidity = new FuzzyVariable("Humidity", 0, 100);
        humidity.Set(FuzzySet.RightShoulder("Humid", 60, 80));

        var fan = new FuzzyVariable("Fan", 0, 100);
        fan.Set(FuzzySet.Triangle("Max", 80, 100, 100));

        var rule = FuzzyRule.If(temp.Is("Hot"))
                            .And(humidity.Is("Humid"))
                            .Then(fan.Is("Max"));

        var fuzzified = new Dictionary<string, Dictionary<string, double>>
        {
            ["Temp"] = temp.Fuzzify(55),       // Hot membership ~0.67
            ["Humidity"] = humidity.Fuzzify(70)  // Humid membership = 0.5
        };

        var strength = rule.Evaluate(fuzzified);
        // MIN(0.67, 0.5) = 0.5
        Assert.Equal(0.5, strength, precision: 1);
    }

    [Fact]
    public void WeightedRule_ScalesStrength()
    {
        var temp = new FuzzyVariable("Temp", 0, 100);
        temp.Set(FuzzySet.RightShoulder("Hot", 0, 100));

        var fan = new FuzzyVariable("Fan", 0, 100);
        fan.Set(FuzzySet.Triangle("Fast", 0, 100, 100));

        var rule = FuzzyRule.If(temp.Is("Hot"))
                            .WithWeight(0.5)
                            .Then(fan.Is("Fast"));

        var fuzzified = new Dictionary<string, Dictionary<string, double>>
        {
            ["Temp"] = temp.Fuzzify(100) // membership = 1.0
        };

        var strength = rule.Evaluate(fuzzified);
        Assert.Equal(0.5, strength, precision: 2); // 1.0 * 0.5 weight
    }
}

public class MamdaniEngineTests
{
    private MamdaniEngine BuildSimpleEngine()
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
    public void ColdInput_ProducesLowFanSpeed()
    {
        var engine = BuildSimpleEngine();
        var result = engine.Evaluate(new Dictionary<string, double> { ["Temperature"] = 10 });
        Assert.True(result["FanSpeed"] < 35, $"Expected low fan speed, got {result["FanSpeed"]:F1}");
    }

    [Fact]
    public void HotInput_ProducesHighFanSpeed()
    {
        var engine = BuildSimpleEngine();
        var result = engine.Evaluate(new Dictionary<string, double> { ["Temperature"] = 45 });
        Assert.True(result["FanSpeed"] > 65, $"Expected high fan speed, got {result["FanSpeed"]:F1}");
    }

    [Fact]
    public void WarmInput_ProducesMediumFanSpeed()
    {
        var engine = BuildSimpleEngine();
        var result = engine.Evaluate(new Dictionary<string, double> { ["Temperature"] = 30 });
        var speed = result["FanSpeed"];
        Assert.True(speed > 35 && speed < 65, $"Expected medium fan speed, got {speed:F1}");
    }

    [Fact]
    public void Deterministic_SameInputSameOutput()
    {
        var engine = BuildSimpleEngine();
        var input = new Dictionary<string, double> { ["Temperature"] = 33 };

        var result1 = engine.Evaluate(input);
        var result2 = engine.Evaluate(input);

        Assert.Equal(result1["FanSpeed"], result2["FanSpeed"], precision: 10);
    }

    [Fact]
    public void EvaluateSingle_ReturnsScalar()
    {
        var engine = BuildSimpleEngine();
        var speed = engine.EvaluateSingle(new Dictionary<string, double> { ["Temperature"] = 30 });
        Assert.True(speed > 0 && speed < 100);
    }

    [Fact]
    public void MissingInput_Throws()
    {
        var engine = BuildSimpleEngine();
        Assert.Throws<ArgumentException>(() =>
            engine.Evaluate(new Dictionary<string, double> { ["WrongName"] = 30 }));
    }

    [Theory]
    [InlineData(DefuzzificationMethod.Centroid)]
    [InlineData(DefuzzificationMethod.Bisector)]
    [InlineData(DefuzzificationMethod.MeanOfMaximum)]
    public void AllDefuzzMethods_ProduceReasonableOutput(DefuzzificationMethod method)
    {
        var temp = new FuzzyVariable("Temp", 0, 50);
        temp.Set(FuzzySet.RightShoulder("Hot", 35, 45));

        var fan = new FuzzyVariable("Fan", 0, 100);
        fan.Set(FuzzySet.Triangle("Fast", 50, 75, 100));

        var engine = new MamdaniEngine(method)
            .Input(temp)
            .Output(fan)
            .Rule(FuzzyRule.If(temp.Is("Hot")).Then(fan.Is("Fast")));

        var result = engine.Evaluate(new Dictionary<string, double> { ["Temp"] = 45 });
        Assert.True(result["Fan"] > 50 && result["Fan"] <= 100, $"{method}: got {result["Fan"]:F1}");
    }
}
