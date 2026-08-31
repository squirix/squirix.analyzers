# Squirix.Analyzers

[![NuGet](https://img.shields.io/nuget/v/squirix.analyzers)](https://www.nuget.org/packages/squirix.analyzers)
[![License](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)

Design-time Roslyn analyzers for the squirix ecosystem.

## Purpose

`Squirix.Analyzers` hosts the custom Roslyn diagnostics that enforce squirix's internal coding conventions across its
repositories: brace style, member-size limits, naming rules, and code-smell guards. They are applied at build time and
their findings are surfaced as compiler diagnostics (fail the build when `TreatWarningsAsErrors` is active).

## Installation

Add the package reference from [nuget.org](https://www.nuget.org/packages/squirix.analyzers):

```xml
<ItemGroup>
    <PackageReference Include="squirix.analyzers" Version="0.1.0"
                      PrivateAssets="all" OutputItemType="Analyzer"/>
</ItemGroup>
```

The package ships the analyzer assembly under `analyzers/dotnet/cs`, so it is loaded as a Roslyn analyzer at build time.

The root `.editorconfig` carries the severity configuration for the `SQR` diagnostics. Copy it (or the relevant
`dotnet_diagnostic.SQR000x` entries) into the consuming repository's `.editorconfig`.

Some rules read their thresholds from per-rule `.editorconfig` options. `SQR0002` defaults `SQR0002.max_methods_per_type`
to 20 methods per type, and `SQR0003` defaults `SQR0003.max_fields_per_type` to 15 fields per type. Add the key under a
`[*.cs]` section in the consuming repository's `.editorconfig`, e.g.:

```editorconfig
[*.cs]
SQR0002.max_methods_per_type = 30
```

## Rules

Rules are prefixed with `SQR`. Detailed documentation, including non-compliant/compliant examples, lives in
[docs/rules](docs/rules). No rule ships an auto-fix (code fix provider) yet.

| Rule | Category | Analyzer | What it enforces |
| --- | --- | --- | --- |
| [`SQR0001`](docs/rules/SQR0001.md) | Style | `OmitOuterLoopBracesAnalyzer` | Outer loop body is only a nested loop: drop braces. |
| [`SQR0002`](docs/rules/SQR0002.md) | Design | `TooManyMethodsAnalyzer` | Cap instance/static methods per type (default 20). |
| [`SQR0003`](docs/rules/SQR0003.md) | Design | `TooManyFieldsAnalyzer` | Cap non-literal, non-static-readonly fields (default 15). |
| [`SQR0004`](docs/rules/SQR0004.md) | Naming | `TypeNameTooLongAnalyzer` | Type name at most 40 characters. |
| [`SQR0005`](docs/rules/SQR0005.md) | Naming | `MethodNameTooLongAnalyzer` | Method name at most 40 chars (accessors drop prefix). |
| [`SQR0006`](docs/rules/SQR0006.md) | Naming | `FieldNameTooLongAnalyzer` | Field name at most 40 characters. |
| [`SQR0007`](docs/rules/SQR0007.md) | Naming | `TypeNamespacePrefixAnalyzer` | Type name must not repeat its namespace segment. |
| [`SQR0008`](docs/rules/SQR0008.md) | Style | `RequireMultilineLoopBodyBracesAnalyzer` | Require braces on multi-line loop bodies. |
| [`SQR0009`](docs/rules/SQR0009.md) | Style | `RedundantNamedArgumentAnalyzer` | Prefer positional args; drop names in declaration order. |
| [`SQR0010`](docs/rules/SQR0010.md) | Style | `OmitSingleStatementBracesAnalyzer` | Omit braces for single-line bodies; keep multi-line. |
| [`SQR0011`](docs/rules/SQR0011.md) | Style | `RedundantDefaultArgumentAnalyzer` | Omit arguments equal to the parameter default. |
| [`SQR0012`](docs/rules/SQR0012.md) | Style | `PreferEqualityOperatorAnalyzer` | Prefer ==/!= over is/is-not for null checks. |
| [`SQR0013`](docs/rules/SQR0013.md) | Style | `PreferEqualityOperatorAnalyzer` | Prefer == over is when comparing to a constant. |
| [`SQR0014`](docs/rules/SQR0014.md) | Style | `PreferEqualityOperatorAnalyzer` | Prefer != over is-not when comparing to a constant. |
| [`SQR0015`](docs/rules/SQR0015.md) | Concurrency | `NoBoolDisposedFieldAnalyzer` | Dispose guard must be an int flag via Interlocked. |
| [`SQR0016`](docs/rules/SQR0016.md) | Concurrency | `NoBoolDisposedFieldAnalyzer` | int dispose flag only via Interlocked/Volatile. |
| [`SQR0017`](docs/rules/SQR0017.md) | Usage | `NoDirectTestContextCancelTokenAnalyzer` | Don't use TestContext.Current.CancellationToken directly. |
| [`SQR0018`](docs/rules/SQR0018.md) | Style | `RequireMultilineIfBodyBracesAnalyzer` | Require braces on multi-line if/else bodies. |
| [`SQR0019`](docs/rules/SQR0019.md) | Usage | `NoAllocatingThrowsAssertAnalyzer` | Avoid allocating exception assert invocations. |
| [`SQR0020`](docs/rules/SQR0020.md) | Usage | `MergeDuplicateCatchBlocksAnalyzer` | Merge consecutive catch blocks with identical bodies. |

## Building

Requires the .NET SDK. The analyzer target is `netstandard2.1`; the unit-test project targets `net10.0`.

```bash
dotnet build Squirix.Analyzers.slnx -c Release
dotnet test tests/squirix.analyzers.unit-tests/Squirix.Analyzers.UnitTests.csproj -c Release
dotnet pack src/squirix.analyzers/Squirix.Analyzers.csproj -c Release -o ./artifacts
```

The packed analyzer is emitted under `artifacts/squirix.analyzers.<version>.nupkg`.

## License

Licensed under the [Apache License, Version 2.0](LICENSE).
