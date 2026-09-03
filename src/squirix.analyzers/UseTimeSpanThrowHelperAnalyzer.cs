using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags <c language="csharp">if</c> guards that compare a value against <c language="csharp">TimeSpan.Zero</c> and
/// throw <c language="csharp">ArgumentOutOfRangeException</c>, which read more clearly as one of the
/// <c language="csharp">ArgumentOutOfRangeException.ThrowIf*</c> helpers (SQR0022). Numeric-constant comparisons
/// are CA1512 territory and are intentionally not flagged here; only <c language="csharp">TimeSpan.Zero</c>
/// comparisons, which CA1512 does not recognize, are considered. <c language="csharp">switch</c> arms cannot use a
/// throw helper and are never flagged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseTimeSpanThrowHelperAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0022";

    private static readonly LocalizableString Description =
        "Guards that compare a value against TimeSpan.Zero and throw ArgumentOutOfRangeException read more clearly " +
        "as ArgumentOutOfRangeException.ThrowIfNegative, ThrowIfNegativeOrZero, ThrowIfZero, ThrowIfGreaterThan, " +
        "ThrowIfGreaterThanOrEqual, or ThrowIfNotEqual. The helper keeps the throwing path out of the caller, which " +
        "keeps the caller small and inlineable.";

    private static readonly LocalizableString MessageFormat = "Use '{0}' instead of an 'if' check with 'throw'";

    private static readonly LocalizableString Title = "Prefer ArgumentOutOfRangeException throw helpers for TimeSpan guards";

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
        context.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);
    }

    private static void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;

        // No else clause: a guard must be a lone 'if'.
        if (ifStatement.Else != null)
            return;

        var helperName = GetThrowHelperName(context, ifStatement.Condition);
        if (helperName == null)
            return;

        if (!ThrowsArgumentOutOfRange(context, ifStatement.Statement))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.IfKeyword.GetLocation(), helperName));
    }

    private static string? GetThrowHelperName(SyntaxNodeAnalysisContext context, ExpressionSyntax condition)
    {
        if (condition is not BinaryExpressionSyntax binary)
            return null;

        // Normalize so the TimeSpan.Zero operand is always on the right.
        var left = binary.Left;
        var kind = binary.Kind();
        if (IsTimeSpanZero(context, left))
        {
            left = binary.Right;
            kind = Flip(kind);
        }
        else if (!IsTimeSpanZero(context, binary.Right))
        {
            return null;
        }

        if (kind == SyntaxKind.None)
            return null;

        // 'ThrowIf*' has no 'TimeSpan?' overload: a lifted 'TimeSpan?' comparison is valid
        // but the suggested replacement would not compile, so only flag plain TimeSpan.
        var operandType = context.SemanticModel.GetTypeInfo(left, context.CancellationToken).Type;
        if (operandType == null)
            return null;

        if (operandType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return null;

        if (operandType.ToDisplayString() != "System.TimeSpan")
            return null;

        var valueText = left.ToString();
        return kind switch
        {
            SyntaxKind.LessThanExpression => "ArgumentOutOfRangeException.ThrowIfNegative(" + valueText + ")",
            SyntaxKind.LessThanOrEqualExpression => "ArgumentOutOfRangeException.ThrowIfNegativeOrZero(" + valueText + ")",
            SyntaxKind.EqualsExpression => "ArgumentOutOfRangeException.ThrowIfZero(" + valueText + ")",
            SyntaxKind.GreaterThanExpression => "ArgumentOutOfRangeException.ThrowIfGreaterThan(" + valueText + ", System.TimeSpan.Zero)",
            SyntaxKind.GreaterThanOrEqualExpression => "ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(" + valueText + ", System.TimeSpan.Zero)",
            SyntaxKind.NotEqualsExpression => "ArgumentOutOfRangeException.ThrowIfNotEqual(" + valueText + ", System.TimeSpan.Zero)",
            _ => null,
        };
    }

    private static SyntaxKind Flip(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
            SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.EqualsExpression => kind,
            SyntaxKind.NotEqualsExpression => kind,
            _ => SyntaxKind.None,
        };
    }

    private static bool IsTimeSpanZero(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (memberAccess.Name.Identifier.ValueText != "Zero")
            return false;

        // Resolve semantically so 'global::System.TimeSpan.Zero' and aliases are recognized
        // while a user-defined 'MyNs.TimeSpan.Zero' is not.
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (symbol is not IPropertySymbol and not IFieldSymbol)
            return false;

        var containingType = symbol.ContainingType;
        return containingType?.Name == "TimeSpan" && containingType.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static bool ThrowsArgumentOutOfRange(SyntaxNodeAnalysisContext context, StatementSyntax statement)
    {
        ThrowStatementSyntax? throwStatement;
        if (statement is BlockSyntax block)
        {
            if (block.Statements.Count != 1 || block.Statements[0] is not ThrowStatementSyntax single)
                return false;

            throwStatement = single;
        }
        else if (statement is ThrowStatementSyntax direct)
        {
            throwStatement = direct;
        }
        else
        {
            return false;
        }

        if (throwStatement.Expression is not ObjectCreationExpressionSyntax creation)
            return false;

        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol constructor)
            return false;

        var containingType = constructor.ContainingType;
        return containingType?.Name == "ArgumentOutOfRangeException" && containingType.ContainingNamespace?.ToDisplayString() == "System";
    }
}

