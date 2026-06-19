// ─────────────────────────────────────────────────────────────
// Zadeh.NET — Sample: Air Conditioning Controller
// ─────────────────────────────────────────────────────────────
// A classic fuzzy logic example: given room temperature and
// humidity, determine the optimal AC fan speed.
// ─────────────────────────────────────────────────────────────

using Zadeh;

Console.WriteLine("🔮 Zadeh.NET — Air Conditioning Controller Demo\n");

// ── Define Input Variables ───────────────────────────────────

var temperature = new FuzzyVariable("Temperature", 0, 50);
temperature.Set(FuzzySet.LeftShoulder("Cold", 15, 22));
temperature.Set(FuzzySet.Triangle("Comfortable", 18, 24, 30));
temperature.Set(FuzzySet.Triangle("Warm", 26, 32, 38));
temperature.Set(FuzzySet.RightShoulder("Hot", 34, 42));

var humidity = new FuzzyVariable("Humidity", 0, 100);
humidity.Set(FuzzySet.LeftShoulder("Dry", 30, 45));
humidity.Set(FuzzySet.Triangle("Normal", 35, 50, 65));
humidity.Set(FuzzySet.RightShoulder("Humid", 55, 75));

// ── Define Output Variable ───────────────────────────────────

var fanSpeed = new FuzzyVariable("FanSpeed", 0, 100);
fanSpeed.Set(FuzzySet.Triangle("Off", 0, 0, 20));
fanSpeed.Set(FuzzySet.Triangle("Low", 10, 30, 50));
fanSpeed.Set(FuzzySet.Triangle("Medium", 35, 55, 75));
fanSpeed.Set(FuzzySet.Triangle("High", 60, 80, 100));
fanSpeed.Set(FuzzySet.Triangle("Max", 80, 100, 100));

// ── Define Rules ─────────────────────────────────────────────

var engine = new MamdaniEngine()
    .Input(temperature)
    .Input(humidity)
    .Output(fanSpeed)
    // Cold → Off or Low
    .Rule(FuzzyRule.If(temperature.Is("Cold")).And(humidity.Is("Dry")).Then(fanSpeed.Is("Off")))
    .Rule(FuzzyRule.If(temperature.Is("Cold")).And(humidity.Is("Normal")).Then(fanSpeed.Is("Off")))
    .Rule(FuzzyRule.If(temperature.Is("Cold")).And(humidity.Is("Humid")).Then(fanSpeed.Is("Low")))
    // Comfortable → Low or Medium
    .Rule(FuzzyRule.If(temperature.Is("Comfortable")).And(humidity.Is("Dry")).Then(fanSpeed.Is("Low")))
    .Rule(FuzzyRule.If(temperature.Is("Comfortable")).And(humidity.Is("Normal")).Then(fanSpeed.Is("Low")))
    .Rule(FuzzyRule.If(temperature.Is("Comfortable")).And(humidity.Is("Humid")).Then(fanSpeed.Is("Medium")))
    // Warm → Medium or High
    .Rule(FuzzyRule.If(temperature.Is("Warm")).And(humidity.Is("Dry")).Then(fanSpeed.Is("Medium")))
    .Rule(FuzzyRule.If(temperature.Is("Warm")).And(humidity.Is("Normal")).Then(fanSpeed.Is("High")))
    .Rule(FuzzyRule.If(temperature.Is("Warm")).And(humidity.Is("Humid")).Then(fanSpeed.Is("High")))
    // Hot → High or Max
    .Rule(FuzzyRule.If(temperature.Is("Hot")).And(humidity.Is("Dry")).Then(fanSpeed.Is("High")))
    .Rule(FuzzyRule.If(temperature.Is("Hot")).And(humidity.Is("Normal")).Then(fanSpeed.Is("Max")))
    .Rule(FuzzyRule.If(temperature.Is("Hot")).And(humidity.Is("Humid")).Then(fanSpeed.Is("Max")));

// ── Evaluate Scenarios ───────────────────────────────────────

var scenarios = new (double Temp, double Hum, string Label)[]
{
    (18, 40, "Cool morning, normal humidity"),
    (24, 50, "Comfortable room"),
    (28, 70, "Warm and humid afternoon"),
    (35, 80, "Hot summer day, very humid"),
    (42, 30, "Extreme heat, dry"),
    (22, 60, "Mild but humid"),
};

Console.WriteLine($"{"Scenario",-40} {"Temp",6} {"Humid",6}  {"Fan Speed",10}");
Console.WriteLine(new string('─', 70));

foreach (var (temp, hum, label) in scenarios)
{
    var result = engine.Evaluate(new Dictionary<string, double>
    {
        ["Temperature"] = temp,
        ["Humidity"] = hum
    });

    var speed = result["FanSpeed"];
    var bar = new string('█', (int)(speed / 5));

    Console.WriteLine($"{label,-40} {temp,5}°C {hum,5}%  {speed,6:F1}% {bar}");
}

Console.WriteLine($"\n✅ Engine: {engine.Inputs.Count} inputs, {engine.Outputs.Count} output, {engine.Rules.Count} rules");
Console.WriteLine("🔮 All evaluations are deterministic — same inputs always produce the same output.");
