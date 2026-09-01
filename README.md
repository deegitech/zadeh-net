# 🔮 Zadeh.NET

**Kill your if/else chains. Embrace fuzzy decisions.**

[![NuGet](https://img.shields.io/nuget/v/Zadeh.NET.svg)](https://www.nuget.org/packages/Zadeh.NET)
[![Downloads](https://img.shields.io/nuget/dt/Zadeh.NET.svg)](https://www.nuget.org/packages/Zadeh.NET)
[![CI](https://github.com/deegitech/zadeh-net/actions/workflows/ci.yml/badge.svg)](https://github.com/deegitech/zadeh-net/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-AGPL--3.0%20%2F%20Commercial-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0+-purple.svg)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-108%20passed-brightgreen.svg)]()

Lightweight, zero-dependency Mamdani fuzzy logic inference engine for .NET.  
Deterministic, explainable decisions in **2–4 µs** (measured with BenchmarkDotNet — see [`benchmarks/`](benchmarks/)).

Named after **[Lütfi Zadeh](https://en.wikipedia.org/wiki/Lotfi_A._Zadeh)** (1921–2017), the founder of fuzzy set theory.

---

## The Problem

Traditional software makes **binary** decisions. But the real world isn't binary.

```
❌ Traditional:   29.9°C → Fan OFF   |   30.1°C → Fan ON      ← Abrupt jump
✅ Zadeh.NET:     29.9°C → Fan 48%   |   30.1°C → Fan 52%     ← Smooth transition
```

Every time you write `if (value > threshold)`, you create a **cliff edge** in your logic. Zadeh.NET replaces cliffs with slopes.

---

## Install

```bash
dotnet add package Zadeh.NET
```

---

## Real-World Examples: Before & After

### 🤖 Example 1: AI Confidence Scoring

You have an AI that returns a confidence score. Is 0.68 good enough? Depends on context.

<details>
<summary>❌ Before — fragile if/else chain</summary>

```csharp
// This grows into unmaintainable spaghetti
double GetFinalScore(double aiConfidence, double userHistory, double context)
{
    if (aiConfidence > 0.8)
    {
        if (userHistory > 0.5) return 0.95;
        if (context > 0.6) return 0.90;
        return 0.85;
    }
    else if (aiConfidence > 0.5)
    {
        if (userHistory > 0.7) return 0.75;
        if (context > 0.7) return 0.70;
        if (userHistory < 0.2 && context < 0.3) return 0.30;
        return 0.55;
    }
    else
    {
        if (userHistory > 0.8) return 0.50; // frequent user, trust more
        if (context > 0.8) return 0.45;
        return 0.20;
    }
    // 🤯 What happens at 0.799 vs 0.801? Completely different paths.
    // 🤯 Adding a 4th factor? Rewrite everything.
}
```
</details>

✅ **After — with Zadeh.NET**

```csharp
var aiConf = new FuzzyVariable("AIConfidence", 0, 1);
aiConf.Set(FuzzySet.LeftShoulder("Low", 0.2, 0.4));
aiConf.Set(FuzzySet.Triangle("Medium", 0.3, 0.55, 0.75));
aiConf.Set(FuzzySet.RightShoulder("High", 0.65, 0.85));

var history = new FuzzyVariable("UserHistory", 0, 1);
history.Set(FuzzySet.LeftShoulder("Rare", 0.1, 0.3));
history.Set(FuzzySet.Triangle("Occasional", 0.2, 0.5, 0.7));
history.Set(FuzzySet.RightShoulder("Frequent", 0.5, 0.8));

var score = new FuzzyVariable("FinalScore", 0, 1);
score.Set(FuzzySet.LeftShoulder("Low", 0.1, 0.3));
score.Set(FuzzySet.Triangle("Medium", 0.3, 0.5, 0.7));
score.Set(FuzzySet.RightShoulder("High", 0.6, 0.85));

var engine = new MamdaniEngine()
    .Input(aiConf).Input(history).Output(score)
    .Rule(FuzzyRule.If(aiConf.Is("High")).And(history.Is("Frequent")).Then(score.Is("High")))
    .Rule(FuzzyRule.If(aiConf.Is("Medium")).And(history.Is("Frequent")).Then(score.Is("High")))
    .Rule(FuzzyRule.If(aiConf.Is("Medium")).And(history.Is("Rare")).Then(score.Is("Low")))
    .Rule(FuzzyRule.If(aiConf.Is("Low")).And(history.Is("Frequent")).Then(score.Is("Medium")))
    .Rule(FuzzyRule.If(aiConf.Is("Low")).And(history.Is("Rare")).Then(score.Is("Low")));

// Smooth decisions — no cliff edges
var result = engine.EvaluateSingle(new() { ["AIConfidence"] = 0.68, ["UserHistory"] = 0.72 });
// → 0.73 — naturally high because both inputs lean positive
```

**What changed?**
- No magic numbers. Rules read like English.
- Adding a 3rd input? Add 1 variable + a few rules. No rewrite.
- 0.68 confidence isn't binary "medium" or "high" — it's **partially both**.

---

### 💰 Example 2: Dynamic Pricing

Your e-commerce needs to adjust prices based on demand and stock levels.

<details>
<summary>❌ Before — rigid tiers</summary>

```csharp
double GetPriceMultiplier(int demandScore, int stockLevel)
{
    if (demandScore > 80 && stockLevel < 10) return 1.50;  // surge price
    if (demandScore > 80 && stockLevel < 30) return 1.30;
    if (demandScore > 50 && stockLevel < 10) return 1.20;
    if (demandScore > 50 && stockLevel < 30) return 1.10;
    if (demandScore < 20 && stockLevel > 80) return 0.70;  // clearance
    if (demandScore < 20 && stockLevel > 50) return 0.85;
    return 1.00;
    // 😤 Stock=31 vs Stock=29: price jumps 20%. Customers notice.
}
```
</details>

✅ **After — with Zadeh.NET**

```csharp
var demand = new FuzzyVariable("Demand", 0, 100);
demand.Set(FuzzySet.LeftShoulder("Low", 15, 35));
demand.Set(FuzzySet.Triangle("Medium", 25, 50, 75));
demand.Set(FuzzySet.RightShoulder("High", 65, 85));

var stock = new FuzzyVariable("Stock", 0, 100);
stock.Set(FuzzySet.LeftShoulder("Scarce", 10, 25));
stock.Set(FuzzySet.Triangle("Normal", 20, 50, 80));
stock.Set(FuzzySet.RightShoulder("Surplus", 70, 90));

var price = new FuzzyVariable("PriceMultiplier", 50, 150);
price.Set(FuzzySet.LeftShoulder("Discount", 60, 80));
price.Set(FuzzySet.Triangle("Normal", 85, 100, 115));
price.Set(FuzzySet.RightShoulder("Premium", 120, 145));

var engine = new MamdaniEngine()
    .Input(demand).Input(stock).Output(price)
    .Rule(FuzzyRule.If(demand.Is("High")).And(stock.Is("Scarce")).Then(price.Is("Premium")))
    .Rule(FuzzyRule.If(demand.Is("High")).And(stock.Is("Normal")).Then(price.Is("Premium")))
    .Rule(FuzzyRule.If(demand.Is("Medium")).And(stock.Is("Normal")).Then(price.Is("Normal")))
    .Rule(FuzzyRule.If(demand.Is("Low")).And(stock.Is("Surplus")).Then(price.Is("Discount")))
    .Rule(FuzzyRule.If(demand.Is("Low")).And(stock.Is("Normal")).Then(price.Is("Discount")));

var multiplier = engine.EvaluateSingle(new() { ["Demand"] = 72, ["Stock"] = 18 });
// → 128.4% — smooth premium, not a sudden 50% jump
```

**What changed?**
- Stock=29 and Stock=31 produce **nearly identical** prices. No customer-shocking jumps.
- Business team can tweak rules without touching code logic.

---

### 🎮 Example 3: Game Difficulty (5 lines of config)

```csharp
var skill = new FuzzyVariable("PlayerSkill", 0, 100);
skill.Set(FuzzySet.LeftShoulder("Beginner", 20, 40));
skill.Set(FuzzySet.Triangle("Intermediate", 30, 50, 70));
skill.Set(FuzzySet.RightShoulder("Expert", 60, 80));

var difficulty = new FuzzyVariable("Difficulty", 0, 100);
difficulty.Set(FuzzySet.Triangle("Easy", 0, 20, 45));
difficulty.Set(FuzzySet.Triangle("Normal", 30, 50, 70));
difficulty.Set(FuzzySet.Triangle("Hard", 55, 80, 100));

var engine = new MamdaniEngine()
    .Input(skill).Output(difficulty)
    .Rule(FuzzyRule.If(skill.Is("Beginner")).Then(difficulty.Is("Easy")))
    .Rule(FuzzyRule.If(skill.Is("Intermediate")).Then(difficulty.Is("Normal")))
    .Rule(FuzzyRule.If(skill.Is("Expert")).Then(difficulty.Is("Hard")));

// Player with skill 45 → Difficulty: 47.2 (between easy and normal — smooth)
// Player with skill 65 → Difficulty: 68.8 (between normal and hard — gradual)
```

---

## Features

### 5 Membership Functions

| Type | Shape | Best For |
|------|-------|----------|
| `Triangle(name, left, peak, right)` | /\\ | General purpose |
| `Trapezoid(name, a, b, c, d)` | /‾\\ | "Definitely X" plateau |
| `LeftShoulder(name, mid, edge)` | ‾\\ | "Low", "Cold", "Cheap" |
| `RightShoulder(name, edge, mid)` | /‾ | "High", "Hot", "Expensive" |
| `Gaussian(name, mean, σ)` | 🔔 | Natural distributions |

### 3 Defuzzification Methods

```csharp
new MamdaniEngine(DefuzzificationMethod.Centroid)       // Default — smoothest
new MamdaniEngine(DefuzzificationMethod.Bisector)       // Equal area split
new MamdaniEngine(DefuzzificationMethod.MeanOfMaximum)  // Fastest
```

### Multi-Input Rules with AND

```csharp
FuzzyRule.If(temperature.Is("Hot"))
         .And(humidity.Is("High"))
         .Then(fanSpeed.Is("Max"))
```

### Weighted Rules

```csharp
FuzzyRule.If(temperature.Is("Warm"))
         .WithWeight(0.8)  // This rule has less influence
         .Then(fanSpeed.Is("Medium"))
```

### Fluent Builder API

```csharp
var engine = new MamdaniEngine()
    .Input("Temperature", 0, 100, t => {
        t.Set(FuzzySet.LeftShoulder("Cold", 20, 35));
        t.Set(FuzzySet.RightShoulder("Hot", 45, 60));
    })
    .Output("FanSpeed", 0, 100, f => {
        f.Set(FuzzySet.Triangle("Slow", 0, 25, 50));
        f.Set(FuzzySet.Triangle("Fast", 50, 75, 100));
    })
    .Rule(FuzzyRule.If(temperature.Is("Cold")).Then(fanSpeed.Is("Slow")))
    .Rule(FuzzyRule.If(temperature.Is("Hot")).Then(fanSpeed.Is("Fast")));
```

### 🔍 Explainability — "Why did the engine decide this?" *(new in 1.5)*

Every decision can produce a full audit trail. Unlike a neural network, a fuzzy engine
can always show its work — critical for enterprise, compliance, and AI-agent feedback loops.

```csharp
var trace = engine.EvaluateWithTrace(new() { ["Demand"] = 72, ["Stock"] = 18 });
Console.WriteLine(trace.Explain());
```
```
INPUTS
  Demand = 72  →  High: 0.35, Medium: 0.12, Low: 0.00
  Stock = 18   →  Scarce: 0.47, Normal: 0.00, Surplus: 0.00
RULES
  ✓ [0.35] IF Demand=High AND Stock=Scarce THEN Price=Premium (w=1.00)
  ✗ [0.00] IF Demand=Low AND Stock=Surplus THEN Price=Discount (w=1.00)
OUTPUTS
  Price = 128.4 (dominant: Premium; activations: Premium: 0.35)
```

### 📊 Detailed Results — linguistic answer + number *(new in 1.5)*

```csharp
var result = engine.EvaluateDetailed(inputs)["Price"];
result.CrispValue;         // 128.4
result.DominantSet;        // "Premium" — the linguistic answer
result.OutputMemberships;  // { Premium: 0.35, Normal: 0.12 }
result.AnyRuleFired;       // false → midpoint fallback was used
```

### 📄 JSON Rule Loading — rules as configuration *(new in 1.5)*

Define the whole engine in JSON: business teams tune rules without recompiling,
and LLMs can *generate* rule sets that stay fully deterministic at runtime.
Uses only `System.Text.Json` from the .NET base library — still zero external dependencies.

```csharp
var engine = MamdaniEngine.FromJson(File.ReadAllText("pricing-rules.json"));
var json = engine.ToJson(); // round-trip safe
```
```json
{
  "defuzzification": "centroid",
  "inputs": [
    { "name": "Demand", "min": 0, "max": 100,
      "sets": [
        { "name": "Low",  "type": "leftShoulder",  "params": [15, 35] },
        { "name": "High", "type": "rightShoulder", "params": [65, 85] } ] }
  ],
  "outputs": [
    { "name": "Price", "min": 50, "max": 150,
      "sets": [ { "name": "Premium", "type": "rightShoulder", "params": [120, 145] } ] }
  ],
  "rules": [
    { "if": { "Demand": "High" }, "then": { "Price": "Premium" } }
  ]
}
```

---

## Why not Accord.NET / AForge.NET?

The only established fuzzy options in .NET are the `Fuzzy` modules of AForge.NET (last release 2013)
and Accord.NET (last release 2017, repository archived). Zadeh.NET is built for today's .NET:

| | AForge / Accord Fuzzy | Zadeh.NET |
|---|---|---|
| Maintenance | Archived / abandoned | ✅ Active |
| Platform | Legacy .NET Framework | ✅ .NET 8+ |
| Dependencies | Part of a large ML/vision suite | ✅ Zero — single small package |
| Membership functions | Trapezoid-derived only | ✅ Triangle, Trapezoid, Shoulders, **Gaussian** |
| Defuzzification | Centroid only | ✅ Centroid, Bisector, MeanOfMaximum |
| Rule definition | String parsing | ✅ Type-safe fluent API **+ JSON config** |
| Explainability | — | ✅ Full rule trace with `Explain()` |
| Linguistic result | — | ✅ `DominantSet` + activations per output |
| Measured performance | — | ✅ ~2–4 µs per inference (BenchmarkDotNet) |

---

## Benchmarks

Measured with BenchmarkDotNet (`benchmarks/Zadeh.Benchmarks`, ShortRun, Apple Silicon, .NET 8):

| Scenario | Mean | Allocated |
|---|---:|---:|
| 1 input, 3 rules (game difficulty) | 2.1 µs | 1.45 KB |
| 2 inputs, 5 rules (dynamic pricing) | 2.3 µs | 1.66 KB |
| 3 inputs, 12 rules (AI confidence scoring) | 4.0 µs | 1.88 KB |
| `EvaluateDetailed` (2 in, 5 rules) | 2.2 µs | 1.38 KB |
| `EvaluateWithTrace` (2 in, 5 rules) | 2.3 µs | 2.10 KB |
| `FromJson` (build engine from config) | 13.6 µs | 30.6 KB |

**~250,000+ decisions per second** on a single core. Tracing is effectively free —
explainability costs nothing at runtime.

---

## Use Cases

| Domain | Input → Output |
|--------|---------------|
| 🤖 AI Post-Processing | Confidence × History × Context → Reliability Score |
| 💰 Dynamic Pricing | Demand × Stock × Competition → Price Multiplier |
| 🏭 Industrial Control | Temperature × Pressure → Valve Opening |
| 🎮 Game AI | Player Skill × Game Time → Enemy Difficulty |
| 🏥 Medical DSS | Symptom Severity × Age → Risk Level |
| 📊 Credit Scoring | Income × Credit History → Loan Limit |
| 🌡️ IoT / Smart Home | Room Temp × Humidity × Time → AC Level |
| 🔍 Search Ranking | Relevance × Freshness × Popularity → Final Rank |

---

## Technical Specs

| Spec | Value |
|------|-------|
| Dependencies | **Zero** (JSON support uses only the .NET base library) |
| Code size | ~1,200 lines |
| Inference time | **~2–4 µs** (measured, see Benchmarks) |
| Thread safety | ✅ Immutable after construction |
| Deterministic | ✅ Same inputs → always same output |
| Target | .NET 8.0+ |
| Tests | 65 passing |

---

## 🤖 Zadeh.AI — the AI companion package

**The duo: AI decides what the user wants; Zadeh decides whether to trust it.**
LLMs are powerful but non-deterministic and expensive per call. `Zadeh.AI` pairs them
with deterministic, explainable, microsecond fuzzy judgment — and like the core, it
uses only the .NET base library.

```bash
dotnet add package Zadeh.AI
```

### ConfidenceGate — LLM confidence → action decision

```csharp
using Zadeh.AI;

var gate = ConfidenceGate.CreateDefault();
var decision = gate.Decide(confidence: 0.68, userHistory: 0.72, contextRelevance: 0.5);

decision.Action;       // GateAction.Execute | Confirm | Clarify | Reject
decision.Score;        // 0-100
decision.Explanation;  // full rule-by-rule trace — auditable, agent-feedable
```

Same 0.68 confidence executes for a frequent user in a matching context and asks for
clarification for a stranger — deterministically, with a trace, at zero token cost.

### RuleSmith — plain language → fuzzy rules (AI designs, fuzzy decides)

```csharp
using Zadeh.AI;
using Zadeh.AI.Providers;

var smith = new RuleSmith(new AnthropicChatClient(apiKey)); // or OpenAIChatClient, GeminiChatClient, or your own IChatClient
var result = await smith.GenerateAsync(
    "Decide order priority from customer activity (0-1) and order size (0-10000). " +
    "Very active customers with large orders get top priority.");

File.WriteAllText("priority-rules.json", result.Json);  // review, version, deploy
var engine = result.Engine;                              // runtime stays 100% deterministic
```

The LLM runs **once, at design time**; generated JSON is validated by actually building
the engine (with bounded auto-repair on validation errors). No hallucination can reach
the runtime decision path.

### AdaptiveThreshold — dynamic cutoffs for semantic caching / RAG

```csharp
var threshold = AdaptiveThreshold.CreateDefault(minThreshold: 0.85, maxThreshold: 0.97);
double cutoff = threshold.Compute(volatility: 0.8, freshness: 0.3);
if (cosineSimilarity >= cutoff) { /* cache hit */ }
```

Volatile or stale data tightens the cutoff; stable, fresh data relaxes it — per lookup,
explainably, instead of one hardcoded constant that is always wrong somewhere.

### McpEngineServer — fuzzy judgment as an agent tool

```csharp
using Zadeh.AI.Mcp;

await new McpEngineServer("pricing-judge")
    .AddEngine("decide_price", "Computes the price multiplier from demand and stock.", pricingEngine)
    .RunOnStdioAsync();
```

Any MCP-capable AI agent (Claude, etc.) can now call your fuzzy engines as tools —
deterministic numbers with explanations, instead of the model improvising.

---

## Roadmap

### v1.5 — Developer Experience ✅ (shipped)
- ✅ JSON rule loading (runtime configuration, round-trip `ToJson`)
- ✅ Rule explainability: `EvaluateWithTrace()` / `Explain()`
- ✅ Detailed results: `EvaluateDetailed()` with dominant set + activations
- ✅ BenchmarkDotNet suite with published numbers
- ASP.NET Core DI integration package (next)

### Zadeh.AI v1.0 — the AI companion package ✅ (shipped)
- ✅ **ConfidenceGate** — LLM confidence scores → Execute / Confirm / Clarify / Reject decisions
- ✅ **RuleSmith** — LLM generates rule sets from plain-language policy; runtime stays 100% deterministic
- ✅ **McpEngineServer** — expose any engine as a Model Context Protocol tool for AI agents
- ✅ **AdaptiveThreshold** — fuzzy-driven dynamic similarity thresholds for RAG / semantic caching

### v3.0 — Advanced Inference (R&D)
- Takagi-Sugeno inference engine
- Type-2 Fuzzy Sets (uncertainty of uncertainty)
- Data-driven membership function tuning (ANFIS-lite)
- Visualization package

---

## Academic Foundation

- L.A. Zadeh (1965) — *Fuzzy Sets*, Information and Control, 8(3), 338–353
- E.H. Mamdani & S. Assilian (1975) — *An Experiment in Linguistic Synthesis with a Fuzzy Logic Controller*

---

## License

Dual licensed under:
- **[GNU Affero General Public License v3.0 (AGPL-3.0)](LICENSE)** — free for open-source projects, academic use, and evaluation.
- **[Commercial License](COMMERCIAL_LICENSE.md)** — required for closed-source commercial applications, SaaS products, and enterprise environments.

For commercial licensing plans, custom integrations, or advanced soft-computing capabilities, contact us at **info@deegitech.com**.

---

**Made with 🔮 by [DeegiTech](https://deegitech.com)** · [Website](https://zadehnet.com) · [NuGet](https://www.nuget.org/packages/Zadeh.NET)
