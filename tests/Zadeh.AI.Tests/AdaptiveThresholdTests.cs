using Xunit;
using Zadeh;
using Zadeh.AI;

namespace Zadeh.AI.Tests;

public class AdaptiveThresholdTests
{
    [Fact]
    public void VolatileStaleData_ProducesStrictThreshold()
    {
        var threshold = AdaptiveThreshold.CreateDefault(0.85, 0.97);
        var strict = threshold.Compute(volatility: 0.9, freshness: 0.1);

        Assert.True(strict > 0.93, $"volatile+stale should be near the strict end, got {strict:F3}");
    }

    [Fact]
    public void StableFreshData_ProducesLenientThreshold()
    {
        var threshold = AdaptiveThreshold.CreateDefault(0.85, 0.97);
        var lenient = threshold.Compute(volatility: 0.05, freshness: 0.95, confidence: 0.9);

        Assert.True(lenient < 0.89, $"stable+fresh should be near the lenient end, got {lenient:F3}");
    }

    [Fact]
    public void Threshold_AlwaysWithinBounds()
    {
        var threshold = AdaptiveThreshold.CreateDefault(0.85, 0.97);

        for (var v = 0.0; v <= 1.0; v += 0.25)
        for (var f = 0.0; f <= 1.0; f += 0.25)
        {
            var value = threshold.Compute(v, f);
            Assert.InRange(value, 0.85, 0.97);
        }
    }

    [Fact]
    public void VolatilityIncreases_ThresholdNeverDecreases()
    {
        var threshold = AdaptiveThreshold.CreateDefault();
        double? previous = null;

        for (var v = 0.0; v <= 1.0; v += 0.1)
        {
            var value = threshold.Compute(v, freshness: 0.5);
            if (previous.HasValue)
                Assert.True(value >= previous.Value - 0.005,
                    $"threshold dropped from {previous:F3} to {value:F3} as volatility rose to {v:F1}");
            previous = value;
        }
    }

    [Fact]
    public void CustomRange_IsRespected()
    {
        var threshold = AdaptiveThreshold.CreateDefault(0.70, 0.99);
        var value = threshold.Compute(0.5, 0.5);

        Assert.InRange(value, 0.70, 0.99);
        Assert.Equal(0.70, threshold.MinThreshold);
        Assert.Equal(0.99, threshold.MaxThreshold);
    }

    [Fact]
    public void InvalidRange_Throws()
    {
        var engine = AdaptiveThreshold.CreateDefault().Engine;
        Assert.Throws<ArgumentException>(() => AdaptiveThreshold.FromEngine(engine, 0.97, 0.85));
    }

    [Fact]
    public void DetailedResult_CarriesTraceAndStrictness()
    {
        var threshold = AdaptiveThreshold.CreateDefault();
        var decision = threshold.ComputeDetailed(volatility: 0.9, freshness: 0.1);

        Assert.InRange(decision.Strictness, 0, 100);
        Assert.Contains("RULES", decision.Explanation);
        Assert.Contains("Volatility", decision.Explanation);
    }
}
