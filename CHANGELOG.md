# Changelog

All notable changes to Zadeh.NET will be documented in this file.

## [Unreleased]

### Added
- **JSON schema versioning**: engine documents now carry `"schemaVersion": 1`.
  `ToJson()` writes it; `FromJson()` accepts documents without a version (treated
  as version 1, so all existing documents keep loading) and rejects documents
  from a newer schema with a clear upgrade message (`MamdaniEngine.CurrentSchemaVersion`).
  RuleSmith's generation template includes the version field.
- **SECURITY.md**: vulnerability disclosure policy for both packages.

## Zadeh.AI [1.0.0] — 2026-07-04

New companion package: **Zadeh.AI** — the AI duo for Zadeh.NET. The core package
stays zero-dependency; Zadeh.AI itself also uses only the .NET base library.

### Added
- **ConfidenceGate**: turns an LLM confidence score (+ optional user-history and
  context-relevance signals) into a deterministic, explainable action decision —
  Execute / Confirm / Clarify / Reject. Default 13-rule production-proven profile
  (12 combination rules plus a weighted catch-all that guarantees low-confidence
  inputs always activate at least one rule),
  or bring your own engine (`FromEngine`, `FromJson`). Every decision carries a
  full rule trace (`decision.Explanation`).
- **RuleSmith**: generates a complete fuzzy engine from a plain-language policy via
  an LLM — "AI designs the rules, fuzzy makes the decisions". Output is validated by
  actually building the engine; validation errors are fed back for bounded repair
  attempts. Provider-agnostic `IChatClient` with built-in raw-HTTP adapters for
  Anthropic (Claude), OpenAI, and Google Gemini (BCL only, no SDK dependencies).
- **AdaptiveThreshold**: computes a dynamic similarity threshold for semantic
  caching / RAG / vector search from volatility, freshness, and confidence signals —
  strict for volatile data, lenient for stable+fresh data, mapped onto a configurable
  [min, max] range.
- **McpEngineServer**: exposes any set of Mamdani engines as Model Context Protocol
  tools over stdio (initialize / tools/list / tools/call), so AI agents can call
  deterministic fuzzy judgment mid-conversation. Input schemas are derived from the
  engines' variables; results include crisp values, dominant sets, and the explanation.
- 36 tests (101 total across the solution).

## [1.5.0] — 2026-07-03

### Added
- **Explainability**: `EvaluateWithTrace()` returns an `InferenceTrace` recording fuzzified
  inputs, every rule's firing strength, and detailed outputs; `Explain()` renders a
  human-readable "why did the engine decide this?" report
- **Detailed results**: `EvaluateDetailed()` returns `FuzzyResult` per output —
  crisp value + dominant linguistic set + per-set activation strengths + fallback flag
- **JSON configuration**: `MamdaniEngine.FromJson()` builds a full engine from a JSON
  document (variables, sets, rules, options) with descriptive validation errors;
  `ToJson()` serializes back, round-trip safe. Uses only System.Text.Json from the
  base library — still zero external dependencies
- `FuzzySet.Kind` and `FuzzySet.Parameters` metadata on all factory-built sets
  (enables serialization, tracing, and future visualization)
- `MamdaniEngine.Defuzzification` and `MamdaniEngine.Resolution` read-only properties
- BenchmarkDotNet suite (`benchmarks/Zadeh.Benchmarks`); measured ~2–4 µs per inference,
  tracing overhead effectively zero
- 22 new tests (65 total)

### Changed
- Internal inference core unified for all three evaluation APIs; per-set MAX aggregation
  happens during rule evaluation (identical results, fewer defuzzification passes)

## [1.0.0] — 2026-05-21

### Added
- Core Mamdani fuzzy inference engine with 4-stage pipeline:
  Fuzzification → Rule Evaluation → Aggregation → Defuzzification
- 5 membership function types: Triangle, Trapezoid, LeftShoulder, RightShoulder, Gaussian
- 3 defuzzification methods: Centroid, Bisector, MeanOfMaximum
- Fluent rule builder: `FuzzyRule.If(...).And(...).WithWeight(...).Then(...)`
- Weighted rules support with [0, 1] weight clamping
- Multi-input, multi-output inference support
- Thread-safe, deterministic evaluation
- Fluent engine builder with inline variable configuration
- Comprehensive XML documentation on all public APIs
- 43 xUnit tests covering all MF types, rules, engine, and edge cases
- Air conditioning controller sample application
- AGPL-3.0 and Commercial dual license model with enterprise support option
- Zero external dependencies — pure C#, ~400 lines of production code
