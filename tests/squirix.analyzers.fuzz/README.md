# Fuzz tests

The fuzz tests run all the analyzers on C# code that [libFuzzer](https://llvm.org/docs/LibFuzzer.html) mutates, starting from the code snippets of the
unit tests. [SharpFuzz](https://github.com/Metalnem/sharpfuzz) instruments `squirix.analyzers.dll`, so libFuzzer keeps the mutations that execute
new branches of the rules. The harness is adapted from the one of [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer)
(see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

The harness reports these defects:

| Kind | Description |
| ---- | ----------- |
| `AnalyzerException` | An analyzer throws (AD0001), or Roslyn rejects an argument of an analyzer with an `ArgumentException` |
| `Crash` | The process crashes, e.g. on a stack overflow |
| `Timeout` | An analyzer does not complete within 60 seconds (twice the libFuzzer timeout) |

The exceptions that Roslyn throws by itself (`RoslynException`), and the exceptions of the harness itself (`HarnessException`), are saved, but no
issue is opened for them. Roslyn throws by itself when no frame of the analyzers is in the stack trace, or when the first frame outside the runtime
(`System.*`) is in Roslyn, such as a `NullReferenceException` in `SemanticModel.GetTypeInfo` on invalid code, except for an `ArgumentException` that
Roslyn throws itself to reject an argument of the analyzer. An analyzer that indexes a list out of range throws from the runtime, and owns the exception.
Every analyzer that asks the semantic model about the code reaches such a defect, so its signature is computed from the frames of Roslyn, not of the
analyzer, and the IDE shows it as an AD0001 of each of these rules. The analyzers do not run when the compiler throws on the input, as the compilation
then hangs when it is used again.

All the rules are enabled, and the code is compiled with the .NET assemblies of the harness, as the unit tests compile their snippets.
The compiler binds the methods one after the other (`concurrentBuild: false`), so an input always runs the same code. Under the load of the workers,
Roslyn still rarely throws on an input that it analyzes the next time, so the harness keeps a finding only when the input reproduces it.

## Options of the rules

The `//! key = value` comments that start an input are the `.editorconfig` options of the rules for this input, for instance:

```cs
//! SQR0002.max_methods_per_type = 1
//! SQR0028.max_by_value_size = -1
class C { }
```

libFuzzer mutates them along with the code. Every other seed starts with all the options that the rules read, which the `corpus` command finds in the
string literals of the rules (`SQRnnnn.name`), with edge values: `0`, `-1`, `2147483648`, `x`, ...

## Findings

Each defect has a signature, computed from its kind, the analyzer, the exception, and the frames of the analyzer.
The findings are saved in `findings/<signature>/`: the smallest inputs that reproduce them (`input-*.cs`), the input minimized by libFuzzer
(`minimized.cs`), and the details of the finding produced by each input (`input-*.json`, `minimized.json`, and `finding.json` for the first one).

## Weekly run

The [fuzz workflow](../../.github/workflows/fuzz.yml) fuzzes for 2 hours every Sunday, starting from the snippets of the tests and the corpus of the
previous run. It then opens an issue labeled `fuzzing` for each new finding, at most 20 per run. The signature is in the body of the issue, so:

- a finding with an open issue, or with an issue closed as not planned, is not reported again
- a finding with an issue closed as completed is reported in a new issue, as a regression, when the fuzzed commit contains the commit that
  closed the issue (the merge commit of its pull request). A run of an older commit, such as a branch that does not have the fix yet, still finds
  the defect, so the finding is not reported. When the closing commit is unknown, for instance for an issue closed manually, the finding is reported.

The workflow can be started manually with a shorter duration, and without opening issues. The findings, the corpus, and the logs are attached to the run.

## Reproducing a finding

The `repro` command runs the analyzers on files, on any OS, and prints the findings with their details:

```shell
dotnet run --project tests/squirix.analyzers.fuzz --configuration Release -- repro input.cs
```

`--only <type>` runs only some analyzers (full type names, separated by commas). A folder runs all its files, for instance the corpus of a run
or the snippets of the tests:

```shell
dotnet run --project tests/squirix.analyzers.fuzz --configuration Release -- corpus --tests tests/squirix.analyzers.unit-tests --rules src/squirix.analyzers --output artifacts/fuzz-seeds --dictionary artifacts/fuzz-seeds/csharp.dict
dotnet run --project tests/squirix.analyzers.fuzz --configuration Release -- repro artifacts/fuzz-seeds
```

## Fuzzing locally

libFuzzer requires Linux, or WSL on Windows. Install PowerShell, SharpFuzz and [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet/releases),
then run:

```shell
dotnet tool install --global SharpFuzz.CommandLine
pwsh tests/squirix.analyzers.fuzz/eng/run.ps1 -LibFuzzer ./libfuzzer-dotnet-ubuntu -DurationMinutes 30
```

The script publishes the harness to `artifacts/fuzz/bin`, instruments the analyzers, and starts the campaign:

1. One libFuzzer worker per core, sharing the corpus. Each worker starts with a part of the snippets of the tests.
2. The harness saves the findings without stopping libFuzzer. When an input crashes the process or does not complete, libFuzzer stops,
   the campaign finds the responsible analyzer by replaying the input with groups of analyzers, disables it, and restarts the worker.
3. The corpus is merged in `artifacts/fuzz/corpus-merged`, to pass to the next campaign with `-PreviousCorpus`.
4. libFuzzer minimizes the input of each `AnalyzerException`. This only works on x64: libfuzzer-dotnet reports the findings with `__builtin_trap()`,
   which raises `SIGTRAP` on Arm64, and libFuzzer does not handle this signal.

`pwsh tests/squirix.analyzers.fuzz/eng/report.ps1 -FindingsDirectory artifacts/fuzz/findings -DryRun` prints the issues without creating them.

## Not covered yet

- Several files in one compilation, such as the parts of a partial type
- The analysis of code that references other assemblies than the .NET runtime (the snippets declare the types they need, such as `ILogger`)
- The Roslyn versions other than the one of the harness
