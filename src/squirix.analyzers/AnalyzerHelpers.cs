using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    /// <summary>
    /// Reads an integer .editorconfig option for the symbol's syntax tree, returning
    /// <paramref name="defaultValue" /> when the option is absent or not a valid int.
    /// </summary>
    internal static int GetIntOption(SymbolAnalysisContext context, ISymbol symbol, string optionName, int defaultValue)
    {
        var tree = GetBestLocation(symbol)?.SourceTree;
        if (tree is null)
            return defaultValue;

        return GetIntOption(context.Options, tree, optionName, defaultValue);
    }

    /// <summary>
    /// Reads an integer .editorconfig option for a syntax tree, returning <paramref name="defaultValue" />
    /// when the option is absent or not a valid positive int.
    /// </summary>
    internal static int GetIntOption(AnalyzerOptions analyzerOptions, SyntaxTree tree, string optionName, int defaultValue)
    {
        var options = analyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree);
        if (!options.TryGetValue(optionName, out var raw))
            return defaultValue;

        return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }

    /// <summary>
    /// Returns the definition part of a partial member, or the member itself. A rule that looks at the member as a whole
    /// reports it on this part, and lookups such as the implementation of an interface member return it as well.
    /// </summary>
    internal static ISymbol GetPartialDefinition(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { PartialDefinitionPart: { } part } => part,
            IPropertySymbol { PartialDefinitionPart: { } part } => part,
            IEventSymbol { PartialDefinitionPart: { } part } => part,
            _ => symbol,
        };
    }

    /// <summary>
    /// Returns whether a conditional compilation directive starts inside the node at or after <paramref name="from" />.
    /// The code around such a directive is not the same in every configuration.
    /// </summary>
    internal static bool HasConditionalDirective(SyntaxNode node, int from)
    {
        if (!node.ContainsDirectives)
            return false;

        for (var directive = node.GetFirstDirective(); directive != null && directive.SpanStart < node.FullSpan.End; directive = directive.GetNextDirective())
        {
            if (directive.SpanStart < from)
                continue;

            if (directive.Kind() is SyntaxKind.IfDirectiveTrivia or SyntaxKind.ElifDirectiveTrivia or SyntaxKind.ElseDirectiveTrivia or SyntaxKind.EndIfDirectiveTrivia)
                return true;
        }

        return false;
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
}
