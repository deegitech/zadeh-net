using Xunit;
using Zadeh;
using Zadeh.AI;

namespace Zadeh.AI.Tests;

public class ConfidenceGateTests
{
    [Fact]
    public void HighConfidenceFrequentUser_Executes()
    {
        var gate = ConfidenceGate.CreateDefault();
        var decision = gate.Decide(confidence: 0.92, userHistory: 0.85, contextRelevance: 0.7);

        Assert.Equal(GateAction.Execute, decision.Action);
        Assert.True(decision.Score > 70);
    }

    [Fact]
    public void LowConfidenceRareUser_Rejects()
    {
        var gate = ConfidenceGate.CreateDefault();
        var decision = gate.Decide(confidence: 0.10, userHistory: 0.05, contextRelevance: 0.10);

        Assert.Equal(GateAction.Reject, decision.Action);
        Assert.True(decision.Score < 30);
    }

    [Fact]
    public void MediumConfidence_HistoryDecides()
    {
        var gate = ConfidenceGate.CreateDefault();

        var frequent = gate.Decide(confidence: 0.55, userHistory: 0.85);
        var rare = gate.Decide(confidence: 0.55, userHistory: 0.05, contextRelevance: 0.1);

        Assert.True(frequent.Score > rare.Score,
            $"frequent user ({frequent.Score:F1}) should score above rare user ({rare.Score:F1})");
        Assert.True(frequent.Action >= rare.Action, "frequent user should get at least as much autonomy");
    }

    [Fact]
    public void NeutralDefaults_AreUsable()
    {
        var gate = ConfidenceGate.CreateDefault();
        // Only confidence supplied — history/context default to neutral 0.5
        var decision = gate.Decide(confidence: 0.68);

        Assert.InRange(decision.Score, 0, 100);
        Assert.NotEqual(GateAction.Reject, decision.Action);
    }

    [Fact]
    public void Decision_IsDeterministic()
    {
        var gate = ConfidenceGate.CreateDefault();
        var first = gate.Decide(0.68, 0.72, 0.5);
        var second = gate.Decide(0.68, 0.72, 0.5);

        Assert.Equal(first.Score, second.Score, precision: 10);
        Assert.Equal(first.Action, second.Action);
    }

    [Fact]
    public void SmoothTransition_NoCliffEdges()
    {
        var gate = ConfidenceGate.CreateDefault();
        double? previous = null;

        // Continuity check: at a fine step the score must change gradually — a true
        // cliff edge (e.g. midpoint fallback when no rule fires) shows up as a jump
        // that does not shrink with the step size.
        for (var confidence = 0.05; confidence <= 0.95; confidence += 0.01)
        {
            var score = gate.Decide(confidence, 0.5, 0.5).Score;
            if (previous.HasValue)
                Assert.True(Math.Abs(score - previous.Value) < 4,
                    $"score jumped {Math.Abs(score - previous.Value):F1} at confidence {confidence:F2}");
            previous = score;
        }
    }

    [Fact]
    public void Explanation_ContainsRulesAndOutputs()
    {
        var gate = ConfidenceGate.CreateDefault();
        var decision = gate.Decide(0.92, 0.85, 0.7);

        Assert.Contains("INPUTS", decision.Explanation);
        Assert.Contains("RULES", decision.Explanation);
        Assert.Contains("Execute", decision.Explanation);
    }

    [Fact]
    public void InputsAreClampedToRange()
    {
        var gate = ConfidenceGate.CreateDefault();
        // Out-of-range signals must not throw — clamp instead
        var decision = gate.Decide(confidence: 1.7, userHistory: -0.3);

        Assert.InRange(decision.Score, 0, 100);
    }

    [Fact]
    public void FromEngine_RejectsWrongOutputSetNames()
    {
        var input = new FuzzyVariable("Confidence", 0, 1);
        input.Set(FuzzySet.RightShoulder("High", 0.5, 0.8));
        var output = new FuzzyVariable("Action", 0, 100);
        output.Set(FuzzySet.Triangle("Maybe", 25, 50, 75)); // not a GateAction

        var engine = new MamdaniEngine()
            .Input(input).Output(output)
            .Rule(FuzzyRule.If(input.Is("High")).Then(output.Is("Maybe")));

        var ex = Assert.Throws<ArgumentException>(() => ConfidenceGate.FromEngine(engine));
        Assert.Contains("Maybe", ex.Message);
    }

    [Fact]
    public void FromJson_BuildsCustomGate()
    {
        const string json = """
        {
          "inputs": [
            { "name": "Confidence", "min": 0, "max": 1,
              "sets": [
                { "name": "Low",  "type": "leftShoulder",  "params": [0.2, 0.5] },
                { "name": "High", "type": "rightShoulder", "params": [0.5, 0.8] } ] }
          ],
          "outputs": [
            { "name": "Action", "min": 0, "max": 100,
              "sets": [
                { "name": "Reject",  "type": "leftShoulder",  "params": [15, 35] },
                { "name": "Execute", "type": "rightShoulder", "params": [65, 85] } ] }
          ],
          "rules": [
            { "if": { "Confidence": "High" }, "then": { "Action": "Execute" } },
            { "if": { "Confidence": "Low" },  "then": { "Action": "Reject" } }
          ]
        }
        """;

        var gate = ConfidenceGate.FromJson(json);
        Assert.Equal(GateAction.Execute, gate.Decide(confidence: 0.95).Action);
        Assert.Equal(GateAction.Reject, gate.Decide(confidence: 0.05).Action);
    }
}
