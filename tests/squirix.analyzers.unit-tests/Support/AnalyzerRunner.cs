using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.UnitTests.Support;

/// <summary>Compiles C# source and returns the findings of a single analyzer.</summary>
internal static class AnalyzerRunner
{
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        CancellationToken cancellationToken = default,
        ImmutableDictionary<string, string>? analyzerOptions = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(static a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create(
            "Squirix.Analyzers.UnitTests",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        AnalyzerOptions? options = null;
        if (analyzerOptions is { Count: > 0 })
            options = new AnalyzerOptions([], new TestAnalyzerConfigOptionsProvider(analyzerOptions));

        var withAnalyzers = compilation.WithAnalyzers([analyzer], options);
        var allDiagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        var supportedIds = analyzer.SupportedDiagnostics.Select(static d => d.Id).ToHashSet();
        return [.. allDiagnostics.Where(d => supportedIds.Contains(d.Id))];
    }
}
