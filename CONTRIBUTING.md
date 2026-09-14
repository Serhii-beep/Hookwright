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

Most conventions are enforced by analyzers. These are not:

- **One type per file.** One exception: a closed hierarchy - an abstract base with a private constructor plus its nested cases live in a single file.
- **No `DateTime.UtcNow` in code with behaviour.** Inject `TimeProvider`, so time-dependent logic is testable without sleeping.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat(scope): summary`.
One logical change per commit - the history is intended to be read.
