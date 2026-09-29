using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags methods with names that are too long (SQR0005).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodNameTooLongAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0005";

    private static readonly LocalizableString Description =
        "Method, property, and event simple names must be at most 40 characters " +
        "(excluding overrides and interface implementations). Applies to production and test code.";

    private static readonly LocalizableString MessageFormat = "Member name '{0}' length is {1} (limit {2})";
    private static readonly LocalizableString Title = "Avoid methods with name too long";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Naming", DiagnosticSeverity.Info, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Method, SymbolKind.Property, SymbolKind.Event);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;
        if (symbol is IMethodSymbol { AssociatedSymbol: not null })
            return;

        if (AnalyzerHelpers.IsCompilerOrGenerated(symbol) || IsNameDictatedByBase(symbol))
            return;

        var name = symbol.Name;
        if (name.Length <= AnalyzerLimits.MaxMethodNameLength)
            return;

        var location = AnalyzerHelpers.GetBestLocation(symbol);
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, name.Length, AnalyzerLimits.MaxMethodNameLength));
    }

    private static bool IsNameDictatedByBase(ISymbol symbol)
    {
        if (symbol.IsOverride)
            return true;

        var explicitImplementations = symbol switch
        {
            IMethodSymbol method => method.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
            IPropertySymbol property => property.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
            IEventSymbol @event => @event.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
            _ => default,
        };

        if (!explicitImplementations.IsDefaultOrEmpty)
            return true;

        var type = symbol.ContainingType;
        if (type is null)
            return false;

        foreach (var contract in type.AllInterfaces)
        {
            foreach (var member in contract.GetMembers(symbol.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), symbol))
                    return true;
            }
        }

        return false;
    }
}
