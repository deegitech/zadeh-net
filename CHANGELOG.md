# Changelog

All notable changes to Zadeh.NET will be documented in this file.

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
