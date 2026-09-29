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

    private static readonly LocalizableString Description = "Null-coalescing expressions that throw ArgumentNullException read more clearly as " +
                                                            "ArgumentNullException.ThrowIfNull. The helper keeps the throwing path out of the caller, which keeps the " +
                                                            "caller small and inlineable. Assign first, then validate: 'ThrowIfNull(value); field = value;'.";

    private static readonly LocalizableString MessageFormat = "Use 'ArgumentNullException.ThrowIfNull' instead of '?? throw'";

    private static readonly LocalizableString Title = "Prefer ArgumentNullException.ThrowIfNull over null-coalescing throw";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Info, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

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

        if (!IsPlainParameterGuard(context, coalesce, creation))
            return;

        if (!IsHoistableValue(coalesce))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, coalesce.OperatorToken.GetLocation()));
    }

    /// <summary>
    /// True when the left side is a parameter and the exception carries only the matching parameter name, so the
    /// rewrite to <c language="csharp">ThrowIfNull</c> preserves the exception exactly.
    /// </summary>
    private static bool IsPlainParameterGuard(SyntaxNodeAnalysisContext context, BinaryExpressionSyntax coalesce, ObjectCreationExpressionSyntax creation)
    {
        if (coalesce.Left is not IdentifierNameSyntax identifier)
            return false;

        if (context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not IParameterSymbol)
            return false;

        var arguments = creation.ArgumentList?.Arguments;
        if (arguments is not { Count: 1 } || creation.Initializer != null)
            return false;

        var argument = arguments.Value[0];
        if (argument.NameColon != null)
            return false;

        var name = context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken);
        return name.HasValue && name.Value is string text && text == identifier.Identifier.ValueText;
    }

    /// <summary>
    /// True when the coalesce is the whole value of a simple assignment statement or local declaration inside a
    /// block, so the guard can move to a preceding statement.
    /// </summary>
    private static bool IsHoistableValue(BinaryExpressionSyntax coalesce)
    {
        SyntaxNode value = coalesce;
        while (value.Parent is ParenthesizedExpressionSyntax parenthesized)
            value = parenthesized;

        return value.Parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right == value => assignment.Parent is ExpressionStatementSyntax { Parent: BlockSyntax },
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax { Parent: BlockSyntax } } } } => true,
            _ => false,
        };
    }
}
