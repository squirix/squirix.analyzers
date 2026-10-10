using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Squirix.Analyzers;

/// <summary>
/// Checks shared by the rules that ask to drop the braces around a single embedded statement.
/// </summary>
internal static class BraceRemovalGuards
{
    /// <summary>Returns whether the braces of a single-statement block can go without changing what the code means.</summary>
    internal static bool CanRemoveBraces(BlockSyntax block, bool followedByElse)
    {
        // Preprocessor directives inside the block cannot be kept once the braces are gone.
        if (block.ContainsDirectives)
            return false;

        var only = block.Statements[0];

        // Declarations, local functions and labeled statements are not valid embedded statements.
        if (only is LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax or LabeledStatementSyntax)
            return false;

        // Removing the braces would re-bind a following else to an inner if.
        return !(followedByElse && EndsInUnmatchedIf(only));
    }

    /// <summary>Returns whether an else follows the statement, so that an unbraced inner if would capture it.</summary>
    internal static bool IsFollowedByElse(SyntaxNode node)
    {
        // True when the statement sits in the tail of an if branch whose if has an else.
        var child = node;
        while (true)
        {
            var parent = child.Parent;
            switch (parent)
            {
                case IfStatementSyntax ifStatement when ifStatement.Statement == child:
                    if (ifStatement.Else != null)
                        return true;

                    child = ifStatement;
                    break;
                case ElseClauseSyntax elseClause:
                    child = elseClause.Parent!;
                    break;
                case WhileStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax or UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax
                    or LabeledStatementSyntax:
                    child = parent;
                    break;
                default:
                    return false;
            }
        }
    }

    private static bool EndsInUnmatchedIf(StatementSyntax statement)
    {
        while (true)
        {
            switch (statement)
            {
                case IfStatementSyntax ifStatement:
                    if (ifStatement.Else == null)
                        return true;

                    statement = ifStatement.Else.Statement;
                    break;
                case WhileStatementSyntax whileStatement:
                    statement = whileStatement.Statement;
                    break;
                case ForStatementSyntax forStatement:
                    statement = forStatement.Statement;
                    break;
                case CommonForEachStatementSyntax forEachStatement:
                    statement = forEachStatement.Statement;
                    break;
                case UsingStatementSyntax usingStatement:
                    statement = usingStatement.Statement;
                    break;
                case LockStatementSyntax lockStatement:
                    statement = lockStatement.Statement;
                    break;
                case FixedStatementSyntax fixedStatement:
                    statement = fixedStatement.Statement;
                    break;
                case LabeledStatementSyntax labeledStatement:
                    statement = labeledStatement.Statement;
                    break;
                default:
                    return false;
            }
        }
    }
}
