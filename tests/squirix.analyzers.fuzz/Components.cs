using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.Fuzz;

/// <summary>
/// The analyzers under test, and the options used to compile the fuzzed code.
/// They are created once per process, as creating them is much slower than analyzing an input.
/// </summary>
internal static class Components
{
    // The assemblies of the harness that the fuzzed code must not reference
    private static readonly HashSet<string> ExcludedReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "squirix.analyzers",
        "Squirix.Analyzers.Fuzz",
        "SharpFuzz",
        "SharpFuzz.Common",
    };

    public static ImmutableArray<DiagnosticAnalyzer> Analyzers { get; } = CreateAnalyzers();

    /// <summary>
    /// The rules disabled by default, if any, are enabled, so all the rules run. The compiler binds the methods one after the other, so an input
    /// always runs the same code, and the rare races of the nullable analysis of Roslyn under load are not reported for an analyzer.
    /// </summary>
    public static CSharpCompilationOptions CompilationOptions { get; } = CreateCompilationOptions();

    /// <summary>The assemblies of the .NET runtime that runs the harness, as the unit tests compile their snippets with.</summary>
    public static ImmutableArray<MetadataReference> MetadataReferences { get; } = CreateMetadataReferences();

    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Preview);

    public static DiagnosticAnalyzer? FindByName(string name)
    {
        foreach (var analyzer in Analyzers)
        {
            if (GetName(analyzer) == name)
                return analyzer;
        }

        return null;
    }

    /// <summary>
    /// Finds the analyzer that declares the first frame of a stack trace that belongs to one.
    /// The stack traces separate the nested types with a '.', whereas <see cref="Type.FullName" /> uses a '+'.
    /// </summary>
    public static DiagnosticAnalyzer? FindByStackFrames(ImmutableArray<string> frames)
    {
        foreach (var frame in frames)
            foreach (var analyzer in Analyzers)
            {
                if (frame.StartsWith(GetName(analyzer).Replace('+', '.') + ".", StringComparison.Ordinal))
                    return analyzer;
            }

        return null;
    }

    public static ImmutableArray<string> GetDiagnosticIds(DiagnosticAnalyzer analyzer)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in analyzer.SupportedDiagnostics)
            _ = ids.Add(descriptor.Id);

        return [.. ids];
    }

    public static string GetName(DiagnosticAnalyzer analyzer) => analyzer.GetType().FullName!;

    private static ImmutableArray<DiagnosticAnalyzer> CreateAnalyzers()
    {
        var types = new List<Type>();
        foreach (var type in typeof(FinalizerDisposeFieldAnalyzer).Assembly.GetExportedTypes())
        {
            if (!type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                types.Add(type);
        }

        types.Sort(static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>(types.Count);
        foreach (var type in types)
            analyzers.Add((DiagnosticAnalyzer)Activator.CreateInstance(type)!);

        return analyzers.MoveToImmutable();
    }

    private static CSharpCompilationOptions CreateCompilationOptions()
    {
        var severities = new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal);
        foreach (var analyzer in Analyzers)
            foreach (var descriptor in analyzer.SupportedDiagnostics)
                severities[descriptor.Id] = ReportDiagnostic.Warn;

        return new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, concurrentBuild: false)
            .WithSpecificDiagnosticOptions(severities);
    }

    private static ImmutableArray<MetadataReference> CreateMetadataReferences()
    {
        var paths = new List<string>();
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!ExcludedReferences.Contains(name) && !name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }

        paths.Sort(StringComparer.Ordinal);
        var references = ImmutableArray.CreateBuilder<MetadataReference>(paths.Count);
        foreach (var path in paths)
            references.Add(MetadataReference.CreateFromFile(path));

        return references.MoveToImmutable();
    }
}
