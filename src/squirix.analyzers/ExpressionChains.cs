using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Squirix.Analyzers;

/// <summary>
/// Syntax helpers for expression chains and calls: unwrapping parentheses, finding the root of a member access chain and
/// collecting the expressions a value can come from.
/// </summary>
internal static class ExpressionChains
{
    internal static void CollectCandidates(ExpressionSyntax expression, List<ExpressionSyntax> candidates)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                CollectCandidates(parenthesized.Expression, candidates);
                break;
            case CastExpressionSyntax cast:
                CollectCandidates(cast.Expression, candidates);
                break;
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppress:
                CollectCandidates(suppress.Operand, candidates);
                break;
            case ConditionalExpressionSyntax conditional:
                CollectCandidates(conditional.WhenTrue, candidates);
                CollectCandidates(conditional.WhenFalse, candidates);
                break;
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce:
                CollectCandidates(coalesce.Left, candidates);
                CollectCandidates(coalesce.Right, candidates);
                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                    CollectCandidates(arm.Expression, candidates);

                break;
            case ConditionalAccessExpressionSyntax conditionalAccess:
                CollectCandidates(conditionalAccess.WhenNotNull, candidates);
                break;
            default:
                candidates.Add(expression);
                break;
        }
    }

    internal static SyntaxNode GetChainRoot(SyntaxNode node)
    {
        while (true)
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax member:
                    node = member.Expression;
                    break;
                case InvocationExpressionSyntax invocation:
                    node = invocation.Expression;
                    break;
                case ElementAccessExpressionSyntax element:
                    node = element.Expression;
                    break;
                case ConditionalAccessExpressionSyntax conditional:
                    node = conditional.Expression;
                    break;
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppress:
                    node = suppress.Operand;
                    break;
                default:
                    return node;
            }
        }
    }

    internal static SyntaxNode? GetConditionalReceiver(SyntaxNode call)
    {
        for (var node = call.Parent; node != null && node is not StatementSyntax; node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull.Span.Contains(call.Span))
                return conditional.Expression;
        }

        return null;
    }

    internal static bool IsCall(SyntaxNode node) => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax;

    internal static bool IsChain(SyntaxNode node) => node is MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax;

    internal static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }
}
