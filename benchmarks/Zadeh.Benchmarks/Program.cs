using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Zadeh;

BenchmarkRunner.Run<InferenceBenchmarks>();

/// <summary>
/// Measures the cost of a single inference across realistic engine shapes.
/// Run with: dotnet run -c Release
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class InferenceBenchmarks
{
    private MamdaniEngine _small = null!;   // 1 input,  3 rules  — game difficulty
    private MamdaniEngine _medium = null!;  // 2 inputs, 5 rules  — dynamic pricing
    private MamdaniEngine _large = null!;   // 3 inputs, 12 rules — AI intent scoring
    private Dictionary<string, double> _smallInput = null!;
    private Dictionary<string, double> _mediumInput = null!;
    private Dictionary<string, double> _largeInput = null!;
    private string _mediumJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = BuildSmall();
        _medium = BuildMedium();
        _large = BuildLarge();
        _smallInput = new() { ["Skill"] = 45 };
        _mediumInput = new() { ["Demand"] = 72, ["Stock"] = 18 };
        _largeInput = new() { ["AIConfidence"] = 0.68, ["UserHistory"] = 0.72, ["ContextRelevance"] = 0.5 };
        _mediumJson = _medium.ToJson();
    }

    [Benchmark]
    public double Small_1Input_3Rules() => _small.EvaluateSingle(_smallInput);

    [Benchmark]
    public double Medium_2Inputs_5Rules() => _medium.EvaluateSingle(_mediumInput);

    [Benchmark]
    public double Large_3Inputs_12Rules() => _large.EvaluateSingle(_largeInput);

    [Benchmark]
    public FuzzyResult Detailed_2Inputs_5Rules() => _medium.EvaluateDetailed(_mediumInput)["Price"];

    [Benchmark]
    public InferenceTrace Trace_2Inputs_5Rules() => _medium.EvaluateWithTrace(_mediumInput);

    [Benchmark]
    public MamdaniEngine FromJson_2Inputs_5Rules() => MamdaniEngine.FromJson(_mediumJson);

    private static MamdaniEngine BuildSmall()
    {
        var skill = new FuzzyVariable("Skill", 0, 100);
        skill.Set(FuzzySet.LeftShoulder("Beginner", 20, 40));
        skill.Set(FuzzySet.Triangle("Intermediate", 30, 50, 70));
        skill.Set(FuzzySet.RightShoulder("Expert", 60, 80));

        var difficulty = new FuzzyVariable("Difficulty", 0, 100);
        difficulty.Set(FuzzySet.Triangle("Easy", 0, 20, 45));
        difficulty.Set(FuzzySet.Triangle("Normal", 30, 50, 70));
        difficulty.Set(FuzzySet.Triangle("Hard", 55, 80, 100));

        return new MamdaniEngine()
            .Input(skill).Output(difficulty)
            .Rule(FuzzyRule.If(skill.Is("Beginner")).Then(difficulty.Is("Easy")))
            .Rule(FuzzyRule.If(skill.Is("Intermediate")).Then(difficulty.Is("Normal")))
            .Rule(FuzzyRule.If(skill.Is("Expert")).Then(difficulty.Is("Hard")));
    }

    private static MamdaniEngine BuildMedium()
    {
        var demand = new FuzzyVariable("Demand", 0, 100);
        demand.Set(FuzzySet.LeftShoulder("Low", 15, 35));
        demand.Set(FuzzySet.Triangle("Medium", 25, 50, 75));
        demand.Set(FuzzySet.RightShoulder("High", 65, 85));

        var stock = new FuzzyVariable("Stock", 0, 100);
        stock.Set(FuzzySet.LeftShoulder("Scarce", 10, 25));
        stock.Set(FuzzySet.Triangle("Normal", 20, 50, 80));
        stock.Set(FuzzySet.RightShoulder("Surplus", 70, 90));

        var price = new FuzzyVariable("Price", 50, 150);
        price.Set(FuzzySet.LeftShoulder("Discount", 60, 80));
        price.Set(FuzzySet.Triangle("Normal", 85, 100, 115));
        price.Set(FuzzySet.RightShoulder("Premium", 120, 145));

        return new MamdaniEngine()
            .Input(demand).Input(stock).Output(price)
            .Rule(FuzzyRule.If(demand.Is("High")).And(stock.Is("Scarce")).Then(price.Is("Premium")))
            .Rule(FuzzyRule.If(demand.Is("High")).And(stock.Is("Normal")).Then(price.Is("Premium")))
            .Rule(FuzzyRule.If(demand.Is("Medium")).And(stock.Is("Normal")).Then(price.Is("Normal")))
            .Rule(FuzzyRule.If(demand.Is("Low")).And(stock.Is("Surplus")).Then(price.Is("Discount")))
            .Rule(FuzzyRule.If(demand.Is("Low")).And(stock.Is("Normal")).Then(price.Is("Discount")));
    }

    private static MamdaniEngine BuildLarge()
    {
        var conf = new FuzzyVariable("AIConfidence", 0, 1);
        conf.Set(FuzzySet.LeftShoulder("Low", 0.15, 0.40));
        conf.Set(FuzzySet.Triangle("Medium", 0.30, 0.55, 0.75));
        conf.Set(FuzzySet.RightShoulder("High", 0.65, 0.90));

        var history = new FuzzyVariable("UserHistory", 0, 1);
        history.Set(FuzzySet.LeftShoulder("Rare", 0.1, 0.3));
        history.Set(FuzzySet.Triangle("Occasional", 0.2, 0.5, 0.7));
        history.Set(FuzzySet.RightShoulder("Frequent", 0.5, 0.8));

        var context = new FuzzyVariable("ContextRelevance", 0, 1);
        context.Set(FuzzySet.LeftShoulder("Weak", 0.2, 0.4));
        context.Set(FuzzySet.Triangle("Moderate", 0.3, 0.5, 0.7));
        context.Set(FuzzySet.RightShoulder("Strong", 0.6, 0.8));

        var score = new FuzzyVariable("FinalScore", 0, 1);
        score.Set(FuzzySet.LeftShoulder("VeryLow", 0.1, 0.25));
        score.Set(FuzzySet.Triangle("Low", 0.15, 0.3, 0.45));
        score.Set(FuzzySet.Triangle("Medium", 0.35, 0.5, 0.65));
        score.Set(FuzzySet.Triangle("High", 0.55, 0.7, 0.85));
        score.Set(FuzzySet.RightShoulder("VeryHigh", 0.75, 0.9));

        return new MamdaniEngine()
            .Input(conf).Input(history).Input(context).Output(score)
            .Rule(FuzzyRule.If(conf.Is("High")).And(history.Is("Frequent")).Then(score.Is("VeryHigh")))
            .Rule(FuzzyRule.If(conf.Is("High")).And(history.Is("Occasional")).Then(score.Is("High")))
            .Rule(FuzzyRule.If(conf.Is("High")).And(history.Is("Rare")).Then(score.Is("High")))
            .Rule(FuzzyRule.If(conf.Is("Medium")).And(history.Is("Frequent")).Then(score.Is("High")))
            .Rule(FuzzyRule.If(conf.Is("Medium")).And(history.Is("Occasional")).And(context.Is("Strong")).Then(score.Is("High")))
            .Rule(FuzzyRule.If(conf.Is("Medium")).And(history.Is("Occasional")).Then(score.Is("Medium")))
            .Rule(FuzzyRule.If(conf.Is("Medium")).And(history.Is("Rare")).And(context.Is("Weak")).Then(score.Is("Low")))
            .Rule(FuzzyRule.If(conf.Is("Medium")).And(history.Is("Rare")).Then(score.Is("Medium")))
            .Rule(FuzzyRule.If(conf.Is("Low")).And(history.Is("Frequent")).Then(score.Is("Medium")))
            .Rule(FuzzyRule.If(conf.Is("Low")).And(history.Is("Occasional")).Then(score.Is("Low")))
            .Rule(FuzzyRule.If(conf.Is("Low")).And(history.Is("Rare")).Then(score.Is("VeryLow")))
            .Rule(FuzzyRule.If(conf.Is("Low")).And(context.Is("Weak")).Then(score.Is("VeryLow")));
    }
}
