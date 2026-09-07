using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags methods prefixed with "Try" that do not return bool (SQR0025).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TryPrefixMustReturnBoolAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0025";

    private static readonly LocalizableString Description = "Methods with 'Try' prefix must return bool.";

    private static readonly LocalizableString MessageFormat = "Method '{0}' has 'Try' prefix but returns '{1}', expected bool";

    private static readonly LocalizableString Title = "Try-prefixed method must return bool";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Naming", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (AnalyzerHelpers.IsCompilerOrGenerated(method))
            return;

        var name = method.Name;
        if (!name.StartsWith("Try", StringComparison.Ordinal))
            return;

        if (method.ReturnType.SpecialType == SpecialType.System_Boolean)
            return;

        var location = AnalyzerHelpers.GetBestLocation(method);
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, method.ReturnType.Name));
    }
}