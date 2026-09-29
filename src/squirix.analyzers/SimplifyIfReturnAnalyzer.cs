using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags an <c language="csharp">if</c> statement without <c language="csharp">else</c> whose body is a single
/// <c language="csharp">return</c> or <c language="csharp">throw</c> immediately followed by another
/// <c language="csharp">return</c> or <c language="csharp">throw</c>.
/// Such pairs read more clearly as one conditional return using a throw expression where needed, for example
/// <c language="csharp">return condition ? first : second;</c> or
/// <c language="csharp">return condition ? throw new ArgumentException(...) : second;</c> (SQR0026).
/// Only value-returning <c language="csharp">return</c> statements and <c language="csharp">throw</c> statements
/// with an explicit expression are considered, at least one side must be a <c language="csharp">return</c>,
/// <c language="csharp">ref</c> returns are never flagged, and two returns are flagged only when both expressions have
/// the same natural type (or one is <c language="csharp">null</c> and the other a reference or nullable type),
/// so the rewrite never changes behavior.
/// This rule backports the newer <c>IDE0046</c> detections for SDK 10-era compilers, which stay silent on
/// several of these shapes (for example an <c language="csharp">if</c> return followed by a ternary return or
/// a trailing <c language="csharp">throw</c>).
/// On SDK 11 and later the two rules overlap on the same spans; consumers should keep only one of them enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SimplifyIfReturnAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0026";

    private static readonly LocalizableString Description = "An 'if' statement without 'else' whose body is a single 'return' or 'throw' " +
                                                            "immediately followed by another 'return' or 'throw' should be simplified to one " +
                                                            "conditional return, for example 'return condition ? first : second;'.";

    private static readonly LocalizableString MessageFormat = "Simplify this 'if' and the following statement into one conditional return";
    private static readonly LocalizableString Title = "Simplify if-return to conditional return";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Style", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

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
        if (ifStatement.Else != null)
            return;

        if (!IsReturnOrThrow(ifStatement.Statement, out var ifIsReturn, out var ifExpression))
            return;

        if (ifStatement.Parent is not BlockSyntax parent)
            return;

        var statements = parent.Statements;
        var index = statements.IndexOf(ifStatement);
        if (index < 0 || index + 1 >= statements.Count)
            return;

        if (!IsReturnOrThrow(statements[index + 1], out var nextIsReturn, out var nextExpression))
            return;

        if (!ifIsReturn && !nextIsReturn)
            return;

        if (ifIsReturn && nextIsReturn && !HaveCompatibleTypes(ifExpression, nextExpression, context.SemanticModel, context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.IfKeyword.GetLocation()));
    }

    /// <summary>
    /// Checks that a conditional over the two returned expressions keeps the type each branch returned today.
    /// Both must have the same natural type, or one must be a <c language="csharp">null</c> literal and the other a
    /// reference or nullable type. Expressions without a natural type (target-typed, <c language="csharp">default</c>,
    /// lambdas) or with differing types are rejected.
    /// </summary>
    /// <param name="first">Expression returned by the <c language="csharp">if</c> body.</param>
    /// <param name="second">Expression returned by the following statement.</param>
    /// <param name="model">Semantic model of the analyzed tree.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static bool HaveCompatibleTypes(ExpressionSyntax first, ExpressionSyntax second, SemanticModel model, CancellationToken cancellationToken)
    {
        if (IsTargetTyped(first) || IsTargetTyped(second))
            return false;

        var firstType = model.GetTypeInfo(first, cancellationToken).Type;
        var secondType = model.GetTypeInfo(second, cancellationToken).Type;

        if (firstType == null && first.IsKind(SyntaxKind.NullLiteralExpression))
            return secondType != null && IsNullable(secondType);

        if (secondType == null && second.IsKind(SyntaxKind.NullLiteralExpression))
            return firstType != null && IsNullable(firstType);

        if (firstType == null || secondType == null || firstType.TypeKind == TypeKind.Error || secondType.TypeKind == TypeKind.Error)
            return false;

        return SymbolEqualityComparer.Default.Equals(firstType, secondType);
    }

    private static bool IsTargetTyped(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression.Kind() is SyntaxKind.ImplicitObjectCreationExpression
            or SyntaxKind.DefaultLiteralExpression
            or SyntaxKind.CollectionExpression
            or SyntaxKind.SimpleLambdaExpression
            or SyntaxKind.ParenthesizedLambdaExpression
            or SyntaxKind.AnonymousMethodExpression;
    }

    private static bool IsNullable(ITypeSymbol type) =>
        type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>
    /// Checks for a single value-returning <c language="csharp">return</c> or an explicit-expression
    /// <c language="csharp">throw</c>, either directly or as the only statement of a block.
    /// </summary>
    /// <param name="statement">Statement to inspect.</param>
    /// <param name="isReturn">True for <c language="csharp">return</c>, false for <c language="csharp">throw</c>.</param>
    /// <param name="expression">Returned or thrown expression when the statement matches.</param>
    private static bool IsReturnOrThrow(StatementSyntax statement, out bool isReturn, out ExpressionSyntax expression)
    {
        expression = null!;
        var inner = statement;
        if (inner is BlockSyntax block)
        {
            if (block.Statements.Count != 1)
            {
                isReturn = false;
                return false;
            }

            inner = block.Statements[0];
        }

        if (inner is ReturnStatementSyntax { Expression: not null and not RefExpressionSyntax } returnStatement)
        {
            expression = returnStatement.Expression;
            isReturn = true;
            return true;
        }

        if (inner is ThrowStatementSyntax { Expression: not null } throwStatement)
        {
            expression = throwStatement.Expression;
            isReturn = false;
            return true;
        }

        isReturn = false;
        return false;
    }
}
