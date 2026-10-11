using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    internal static int GetIntOption(in SymbolAnalysisContext context, ISymbol symbol, string optionName, int defaultValue)
    {
        var tree = GetBestLocation(symbol)?.SourceTree;
        return tree == null ? defaultValue : GetIntOption(context.Options, tree, optionName, defaultValue);
    }

    /// <summary>
    /// Reads an integer .editorconfig option for a syntax tree, returning <paramref name="defaultValue" />
    /// when the option is absent or not a valid positive int.
    /// </summary>
    internal static int GetIntOption(AnalyzerOptions analyzerOptions, SyntaxTree tree, string optionName, int defaultValue)
    {
        var options = analyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree);
        return !options.TryGetValue(optionName, out var raw) ? defaultValue : int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
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
            if (IsGeneratedMarker(attribute))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the member is generated, for a rule that asks to run on generated code as well. A partial member
    /// whose definition a person wrote stays hand-written when a generator adds the implementation: the generator marks
    /// its own part, the attributes of both parts merge, and the driver then takes the whole member for generated code.
    /// </summary>
    internal static bool IsGeneratedMember(in SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;
        if (!HasPartialImplementation(symbol))
            return context.IsGeneratedCode || IsCompilerOrGenerated(symbol);

        // A type marked as generated covers its members, whoever wrote their parts.
        for (var type = symbol.ContainingType; type != null; type = type.ContainingType)
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.Name == "GeneratedCodeAttribute")
                    return true;
            }

        // Of the member's own markers only one written on the definition part counts. A definition in a generated file needs no check: the driver drops
        // what is reported there.
        foreach (var attribute in symbol.GetAttributes())
        {
            if (IsGeneratedMarker(attribute) && IsWrittenOn(attribute, symbol, context.CancellationToken))
                return true;
        }

        return false;
    }

    private static bool HasPartialImplementation(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.PartialImplementationPart != null,
            IPropertySymbol property => property.PartialImplementationPart != null,
            IEventSymbol @event => @event.PartialImplementationPart != null,
            _ => false,
        };
    }

    private static bool IsGeneratedMarker(AttributeData attribute) => attribute.AttributeClass?.Name is "CompilerGeneratedAttribute" or "GeneratedCodeAttribute";

    private static bool IsWrittenOn(AttributeData attribute, ISymbol symbol, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference is not { } application)
            return false;

        foreach (var declaration in symbol.DeclaringSyntaxReferences)
        {
            if (declaration.SyntaxTree != application.SyntaxTree)
                continue;

            // A field-like event is declared by its name alone; its attributes sit on the member around it.
            var member = declaration.GetSyntax(cancellationToken).FirstAncestorOrSelf<MemberDeclarationSyntax>();
            if ((member?.Span ?? declaration.Span).Contains(application.Span))
                return true;
        }

        return false;
    }
}
