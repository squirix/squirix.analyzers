using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Shared helpers for Squirix Roslyn analyzers.
/// </summary>
internal static class AnalyzerHelpers
{
    internal static Location? GetBestLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
                return location;
        }

        return null;
    }

    internal static bool IsCompilerOrGenerated(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared)
            return true;

        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is "CompilerGeneratedAttribute" or "GeneratedCodeAttribute")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads an integer .editorconfig option for the symbol's syntax tree, returning
    /// <paramref name="defaultValue"/> when the option is absent or not a valid int.
    /// </summary>
    internal static int GetIntOption(SymbolAnalysisContext context, ISymbol symbol, string optionName, int defaultValue)
    {
        var tree = GetBestLocation(symbol)?.SourceTree;
        if (tree is null)
            return defaultValue;

        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree);
        if (!options.TryGetValue(optionName, out var raw))
            return defaultValue;

        return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }
}
