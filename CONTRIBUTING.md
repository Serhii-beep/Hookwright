# Contributing

## Prerequisites

- .NET SDK 10.0.300 or later
- PostgreSQL and SQL Server are required only for integration tests (via Testcontainers, which needs a working Docker installation)

## Build and test

```bash
dotnet build
dotnet test
```

The build treats warning as errors and enforces `.editorconfig` style rules, so a build that succeeds locally will succeed in CI.

## Conventions

Most conventions are enforced by analyzers. These are not, and are checked in review:

- **One type per file.** No enum, record or helper class sharing a file with an unrelated type. The file name matches the type it contains.
- **No primary constructors.** A captured parameter cannot be marked `readonly`; an explicit constructor assigning `private readonly` fields states the intent and enforces it.
- **Analyzer suppressions carry a written justification**, in `.editorconfig` or the relevant `Directory.Build.props`. Never `#pragma`, never an unexplained entry in `NoWarn`.
- **No `DateTime.UtcNow` in code with behaviour.** Inject `TimeProvider`, so time-dependent logic is testable without sleeping.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat(scope): summary`.
One logical change per commit - the history is intended to be read.