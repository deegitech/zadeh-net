# Contributing to Zadeh.NET

Thank you for your interest in contributing! 🔮

## Quick Start

```bash
git clone https://github.com/deegitech/zadeh-net.git
cd zadeh-net
dotnet build
dotnet test
```

## Development

- **Source code**: `src/Zadeh/`
- **Tests**: `tests/Zadeh.Tests/`
- **Samples**: `samples/Zadeh.Samples/`

## Guidelines

1. **All PRs must pass tests**: `dotnet test` must show 0 failures.
2. **Zero dependencies**: Do not add NuGet packages to the core library.
3. **XML documentation**: All public APIs must have `<summary>` docs.
4. **Naming**: Follow existing patterns — `FuzzySet`, `FuzzyVariable`, `FuzzyRule`, `MamdaniEngine`.
5. **Thread safety**: Evaluations must remain deterministic and thread-safe.

## What We're Looking For

- New membership function types
- Performance optimizations
- Additional defuzzification methods
- Real-world example projects
- Documentation improvements
- Bug reports with reproduction steps

## License

By contributing, you agree that your contributions will be dual-licensed under AGPL-3.0 and the DeegiTech Commercial License.
