using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags null-coalescing expressions whose right side throws an exception (SQR0024). Embedding the throw keeps the
/// throwing path inside the caller, which makes the caller larger and a worse inlining candidate; routing it through
/// a dedicated throw-helper method keeps the caller small and inlineable while the cold throwing path stays in a
/// non-inlined helper. <c language="csharp">ArgumentNullException</c> is excluded: it is owned by SQR0023, which
/// points at the exact <c language="csharp">ArgumentNullException.ThrowIfNull</c> replacement. This rule is
/// intentionally project-agnostic: it does not name any helper, since only the consuming codebase knows which
/// exception types deserve one.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CoalesceThrowHelperAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0024";

    private static readonly LocalizableString Description = "Null-coalescing expressions that throw embed the throwing path in the caller, which makes the caller " +
                                                            "larger and a worse inlining candidate. Route the throw through a dedicated throw-helper method (a small " +
                                                            "check plus a non-inlined method that throws) so the caller stays small and inlineable.";

    private static readonly LocalizableString MessageFormat = "Route '?? throw' through a throw-helper method instead of embedding the throw in the caller";

    private static readonly LocalizableString Title = "Prefer a throw-helper method over null-coalescing throw";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Info, true, Description);

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

        // ArgumentNullException is owned by SQR0023, which names the exact replacement; do not double-report.
        if (throwExpression.Expression is ObjectCreationExpressionSyntax creation &&
            context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is IMethodSymbol { ContainingType.Name: "ArgumentNullException" } constructor &&
            constructor.ContainingType.ContainingNamespace?.ToDisplayString() == "System")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, coalesce.OperatorToken.GetLocation()));
    }
}
