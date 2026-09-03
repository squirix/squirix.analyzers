using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags null-coalescing expressions whose right side throws <c language="csharp">ArgumentNullException</c>, which
/// read more clearly as <c language="csharp">ArgumentNullException.ThrowIfNull</c> (SQR0023). The helper keeps the
/// throwing path out of the caller, which keeps the caller small and inlineable, while <c language="csharp">?? throw</c>
/// embeds the throw in the caller body. Only <c language="csharp">ArgumentNullException</c> is considered; other
/// exception types carry different semantics and have no equivalent helper.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CoalesceThrowIfNullAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0023";

    private static readonly LocalizableString Description =
        "Null-coalescing expressions that throw ArgumentNullException read more clearly as " +
        "ArgumentNullException.ThrowIfNull. The helper keeps the throwing path out of the caller, which keeps the " +
        "caller small and inlineable. Assign first, then validate: 'ThrowIfNull(value); field = value;'.";

    private static readonly LocalizableString MessageFormat =
        "Use 'ArgumentNullException.ThrowIfNull' instead of '?? throw'";

    private static readonly LocalizableString Title = "Prefer ArgumentNullException.ThrowIfNull over null-coalescing throw";

    private static readonly DiagnosticDescriptor Rule =
        new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Info, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCoalesce, SyntaxKind.CoalesceExpression);
    }

    private static void AnalyzeCoalesce(SyntaxNodeAnalysisContext context)
    {
        var coalesce = (BinaryExpressionSyntax)context.Node;

        if (coalesce.Right is not ThrowExpressionSyntax throwExpression)
            return;

        if (throwExpression.Expression is not ObjectCreationExpressionSyntax creation)
            return;

        // Resolve semantically so 'global::System.ArgumentNullException' and aliases are
        // recognized while a user-defined 'ArgumentNullException' elsewhere is not.
        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol constructor)
            return;

        var containingType = constructor.ContainingType;
        if (containingType?.Name != "ArgumentNullException" || containingType.ContainingNamespace?.ToDisplayString() != "System")
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, coalesce.OperatorToken.GetLocation()));
    }
}

