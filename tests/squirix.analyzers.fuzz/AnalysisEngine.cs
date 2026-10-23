using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.Fuzz;

/// <summary>Runs the analyzers on a C# file, with the options of its <c>//!</c> comments, and collects the defects.</summary>
internal sealed class AnalysisEngine
{
    private const string CompilerComponent = "Microsoft.CodeAnalysis.CSharp";

    private const string HarnessComponent = "Squirix.Analyzers.Fuzz";

    private readonly ComponentFilter _filter;

    public AnalysisEngine(ComponentFilter filter)
    {
        _filter = filter;
    }

    public async Task<List<Finding>> RunAsync(string source, CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        try
        {
            var tree = CSharpSyntaxTree.ParseText(source, Components.ParseOptions, "Fuzz.cs", cancellationToken: cancellationToken);
            var options = Components.CompilationOptions.WithOutputKind(HasTopLevelStatements(tree, cancellationToken) ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary);
            var compilation = CSharpCompilation.Create("Fuzz", [tree], Components.MetadataReferences, options);

            // The compiler can throw on invalid code, such as "[assembly:(async()=>", and then hangs when the compilation is used again,
            // as the symbols it was completing are never completed. The analyzers cannot be blamed for it, so the input is not analyzed.
            try
            {
                _ = compilation.GetDiagnostics(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                findings.Add(Finding.FromException(FindingKind.RoslynException, CompilerComponent, [], exception));
                return findings;
            }

            await RunAnalyzersAsync(compilation, InputOptions.ParseOptions(source), findings, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            findings.Add(Finding.FromException(FindingKind.HarnessException, HarnessComponent, [], exception));
        }

        return Distinct(findings);
    }

    private static List<Finding> Distinct(List<Finding> findings)
    {
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<Finding>();
        foreach (var finding in findings)
        {
            if (signatures.Add(finding.Signature))
                distinct.Add(finding);
        }

        return distinct;
    }

    private static bool HasTopLevelStatements(SyntaxTree tree, CancellationToken cancellationToken)
    {
        foreach (var member in ((CompilationUnitSyntax)tree.GetRoot(cancellationToken)).Members)
        {
            if (member is GlobalStatementSyntax)
                return true;
        }

        return false;
    }

    private async Task RunAnalyzersAsync(Compilation compilation, ImmutableDictionary<string, string> analyzerOptions, List<Finding> findings, CancellationToken cancellationToken)
    {
        if (_filter.Analyzers.IsEmpty)
            return;

        var exceptions = new ConcurrentQueue<(DiagnosticAnalyzer Analyzer, Exception Exception)>();
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([], new InputOptionsProvider(analyzerOptions)),
            (exception, analyzer, _) => exceptions.Enqueue((analyzer, exception)),
            false, // Keep the coverage deterministic, so libFuzzer can tell which mutations are interesting
            false,
            false);

        _ = await compilation.WithAnalyzers(_filter.Analyzers, options).GetAnalyzerDiagnosticsAsync(cancellationToken);
        foreach (var (analyzer, exception) in exceptions)
        {
            if (exception is not OperationCanceledException)
                findings.Add(Finding.FromException(FindingKind.AnalyzerException, Components.GetName(analyzer), Components.GetDiagnosticIds(analyzer), exception));
        }
    }
}
