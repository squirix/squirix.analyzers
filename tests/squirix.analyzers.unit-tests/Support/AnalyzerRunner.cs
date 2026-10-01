using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.UnitTests.Support;

/// <summary>Compiles C# source and returns the findings of a single analyzer.</summary>
internal static class AnalyzerRunner
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    public static Task<ImmutableArray<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source, CancellationToken cancellationToken = default) =>
        RunAsync(analyzer, source, null, cancellationToken);

    public static Task<ImmutableArray<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source, ImmutableDictionary<string, string>? analyzerOptions = null,
        CancellationToken cancellationToken = default) => RunAsync(analyzer, source, analyzerOptions, References.Value, cancellationToken);

    /// <summary>Compiles <paramref name="library" /> into an assembly first, so <paramref name="source" /> sees its types as metadata.</summary>
    public static Task<ImmutableArray<Diagnostic>> RunWithLibraryAsync(DiagnosticAnalyzer analyzer, string library, string source, CancellationToken cancellationToken = default)
    {
        var tree = CSharpSyntaxTree.ParseText(library, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create("Squirix.Analyzers.UnitTests.Library", new[] { tree }, References.Value, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        ThrowIfSourceDoesNotCompile(compilation, cancellationToken);

        using var image = new MemoryStream();
        _ = compilation.Emit(image, cancellationToken: cancellationToken);
        return RunAsync(analyzer, source, null, References.Value.Add(MetadataReference.CreateFromImage(image.ToArray())), cancellationToken);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source, ImmutableDictionary<string, string>? analyzerOptions,
        ImmutableArray<MetadataReference> references, CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create("Squirix.Analyzers.UnitTests", new[] { tree }, references, new CSharpCompilationOptions(HasTopLevelStatements(tree, cancellationToken) ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        ThrowIfSourceDoesNotCompile(compilation, cancellationToken);

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

    private static string GetTrustedPlatformAssemblies() => AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? ThrowTrustedAssembliesMissing();

    private static bool HasTopLevelStatements(SyntaxTree tree, CancellationToken cancellationToken)
    {
        foreach (var member in ((CompilationUnitSyntax)tree.GetRoot(cancellationToken)).Members)
        {
            if (member is GlobalStatementSyntax)
                return true;
        }

        return false;
    }

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in GetTrustedPlatformAssemblies().Split(Path.PathSeparator))
        {
            if (path.Length > 0)
                builder.Add(MetadataReference.CreateFromFile(path));
        }

        return builder.ToImmutable();
    }

    private static string ThrowTrustedAssembliesMissing() => throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");

    private static void ThrowIfSourceDoesNotCompile(Compilation compilation, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                errors.Add(diagnostic.ToString());
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Test source has compiler errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}
