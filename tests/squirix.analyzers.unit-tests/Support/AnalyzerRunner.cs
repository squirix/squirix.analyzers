using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        var references = new List<MetadataReference>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                continue;

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
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
        var supportedIds = new HashSet<string>();
        foreach (var supported in analyzer.SupportedDiagnostics)
            _ = supportedIds.Add(supported.Id);

        var filtered = new List<Diagnostic>();
        foreach (var diagnostic in allDiagnostics)
        {
            if (supportedIds.Contains(diagnostic.Id))
                filtered.Add(diagnostic);
        }

        return [.. filtered];
    }
}
