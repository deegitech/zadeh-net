# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest minor of the current major (Zadeh.NET, Zadeh.AI) | ✅ |
| Older versions | ❌ — please upgrade |

## Reporting a Vulnerability

Please report security vulnerabilities privately to **fareed@deegital.org**.
Do **not** open a public GitHub issue for security reports.

Include where possible:
- A description of the issue and its impact
- A minimal reproduction
- The affected package (`Zadeh.NET` or `Zadeh.AI`) and version

We aim to acknowledge reports within **3 business days**. Confirmed
vulnerabilities are fixed in a patch release and disclosed in the CHANGELOG
after a fix is available.

## Notes for integrators

- The core `Zadeh.NET` package has zero runtime dependencies.
- `Zadeh.AI` LLM providers send data only to the endpoint you configure;
  API keys are supplied by the host application and are never persisted by the library.
- The MCP server component executes no arbitrary code; it evaluates only
  engines you explicitly register.
