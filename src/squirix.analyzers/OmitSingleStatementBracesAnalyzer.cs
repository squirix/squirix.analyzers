using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags braces around a single-line single embedded statement that should be omitted (SQR0010).
/// Complements IDE0011, which does not reliably enforce <c language="csharp">csharp_prefer_braces = false</c> on build.
/// Keeps braces when the statement spans multiple lines, or when an if/else chain has a multi-statement branch (SA1520).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OmitSingleStatementBracesAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0010";

    private static readonly LocalizableString Description =
        "Omit braces when a control-flow body is a single single-line statement. Keep braces for multiline bodies and for if/else chains with a multi-statement branch.";

    private static readonly LocalizableString MessageFormat = "Omit braces from '{0}' when the body is a single single-line statement";
    private static readonly LocalizableString Title = "Omit braces from single-line single-statement body";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Style", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeIf, SyntaxKind.IfStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLoop, SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement, SyntaxKind.WhileStatement,
            SyntaxKind.DoStatement);
        context.RegisterSyntaxNodeAction(AnalyzeUsing, SyntaxKind.UsingStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLock, SyntaxKind.LockStatement);
        context.RegisterSyntaxNodeAction(AnalyzeFixed, SyntaxKind.FixedStatement);
    }

    private static void AnalyzeFixed(SyntaxNodeAnalysisContext context)
    {
        var statement = (FixedStatementSyntax)context.Node;
        ReportSimpleEmbedded(context, statement.Statement, "fixed");
    }

    private static void AnalyzeIf(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;

        // Analyze each if/else-if chain once from its root.
        if (ifStatement.Parent is ElseClauseSyntax)
            return;

        if (!ChainAllowsOmittingBraces(ifStatement, IsFollowedByElse(ifStatement)))
            return;

        var followedByElse = IsFollowedByElse(ifStatement);
        var current = ifStatement;
        while (true)
        {
            ReportIfBlock(context, current.Statement, "if", current.Else != null || followedByElse);

            if (current.Else == null)
                return;

            if (current.Else.Statement is not IfStatementSyntax elseIf)
            {
                ReportIfBlock(context, current.Else.Statement, "else", followedByElse);
                return;
            }

            current = elseIf;
        }
    }

    private static void AnalyzeLock(SyntaxNodeAnalysisContext context)
    {
        var statement = (LockStatementSyntax)context.Node;
        ReportSimpleEmbedded(context, statement.Statement, "lock");
    }

    private static void AnalyzeLoop(SyntaxNodeAnalysisContext context)
    {
        var body = LoopStatementSyntaxHelpers.GetLoopBody(context.Node);
        if (body is not BlockSyntax block || block.Statements.Count != 1)
            return;

        var only = block.Statements[0];

        // Nested-loop outer braces are SQR0001. Multiline non-loop bodies must keep braces (SQR0008).
        if (LoopStatementSyntaxHelpers.IsLoopStatement(only))
            return;

        if (!CanRemoveBraces(block, IsFollowedByElse(context.Node)))
            return;

        if (LoopStatementSyntaxHelpers.SpansMultipleLines(only))
            return;

        var kind = LoopStatementSyntaxHelpers.GetLoopKindName(context.Node);
        context.ReportDiagnostic(Diagnostic.Create(Rule, block.OpenBraceToken.GetLocation(), kind));
    }

    private static void AnalyzeUsing(SyntaxNodeAnalysisContext context)
    {
        var statement = (UsingStatementSyntax)context.Node;
        ReportSimpleEmbedded(context, statement.Statement, "using");
    }

    private static bool ChainAllowsOmittingBraces(IfStatementSyntax ifStatement, bool followedByElse)
    {
        // All branches must be single-line single statements so stripping braces cannot
        // leave a multiline braced sibling next to an unbraced branch (SA1520).
        var current = ifStatement;
        while (true)
        {
            if (!IsOmittableSingleLineBody(current.Statement, current.Else != null || followedByElse))
                return false;

            if (current.Else == null)
                return true;

            if (current.Else.Statement is not IfStatementSyntax elseIf)
                return IsOmittableSingleLineBody(current.Else.Statement, followedByElse);

            current = elseIf;
        }
    }

    private static bool IsOmittableSingleLineBody(StatementSyntax statement, bool followedByElse)
    {
        if (statement is not BlockSyntax block)
            return !LoopStatementSyntaxHelpers.SpansMultipleLines(statement);
        if (block.Statements.Count != 1)
            return false;

        return CanRemoveBraces(block, followedByElse) && !LoopStatementSyntaxHelpers.SpansMultipleLines(block.Statements[0]);
    }

    private static void ReportIfBlock(SyntaxNodeAnalysisContext context, StatementSyntax body, string kind, bool followedByElse)
    {
        if (body is not BlockSyntax block || block.Statements.Count != 1)
            return;

        if (!CanRemoveBraces(block, followedByElse))
            return;

        if (LoopStatementSyntaxHelpers.SpansMultipleLines(block.Statements[0]))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, block.OpenBraceToken.GetLocation(), kind));
    }

    private static void ReportSimpleEmbedded(SyntaxNodeAnalysisContext context, StatementSyntax body, string kind)
    {
        if (body is not BlockSyntax block || block.Statements.Count != 1)
            return;

        // using/lock/fixed chains may nest; only flag a block wrapping a non-using/lock/fixed single statement
        // when that statement is not itself another resource statement that prefers shared braces.
        var only = block.Statements[0];
        if (only.IsKind(SyntaxKind.UsingStatement) || only.IsKind(SyntaxKind.LockStatement) || only.IsKind(SyntaxKind.FixedStatement))
            return;

        if (!CanRemoveBraces(block, IsFollowedByElse(context.Node)))
            return;

        if (LoopStatementSyntaxHelpers.SpansMultipleLines(only))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, block.OpenBraceToken.GetLocation(), kind));
    }

    private static bool CanRemoveBraces(BlockSyntax block, bool followedByElse)
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

    private static bool IsFollowedByElse(SyntaxNode node)
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
}
