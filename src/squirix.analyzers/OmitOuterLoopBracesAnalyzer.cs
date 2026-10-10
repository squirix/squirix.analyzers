using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags outer loops whose block contains only a nested loop — braces must be omitted.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OmitOuterLoopBracesAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0001";

    private static readonly LocalizableString Description = "When an outer loop's body is only a nested loop (no other statements), omit the outer braces.";

    private static readonly LocalizableString MessageFormat = "Omit braces from outer {0} when it only contains a nested loop";
    private static readonly LocalizableString Title = "Omit braces from outer loop that only contains a nested loop";
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

        context.RegisterSyntaxNodeAction(AnalyzeLoop, SyntaxKind.ForStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLoop, SyntaxKind.ForEachStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLoop, SyntaxKind.ForEachVariableStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLoop, SyntaxKind.WhileStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLoop, SyntaxKind.DoStatement);
    }

    private static void AnalyzeLoop(SyntaxNodeAnalysisContext context)
    {
        var body = LoopStatementSyntaxHelpers.GetLoopBody(context.Node);
        if (body is not BlockSyntax block)
            return;

        if (block.Statements.Count != 1)
            return;

        var only = block.Statements[0];
        if (!LoopStatementSyntaxHelpers.IsLoopStatement(only))
            return;

        if (HasConditionalDirective(block))
            return;

        // The 'while (...)' that closes a do loop keeps a following else away from the body.
        if (context.Node is not DoStatementSyntax && BraceRemovalGuards.IsFollowedByElse(context.Node) && BraceRemovalGuards.EndsInUnmatchedIf(only))
            return;

        var loopKind = LoopStatementSyntaxHelpers.GetLoopKindName(context.Node);
        context.ReportDiagnostic(Diagnostic.Create(Rule, block.OpenBraceToken.GetLocation(), loopKind));
    }

    /// <summary>
    /// Returns whether the block holds a conditional compilation directive. Its statements then differ between
    /// configurations, and the braces may be what keeps them together.
    /// </summary>
    private static bool HasConditionalDirective(BlockSyntax block)
    {
        if (!block.ContainsDirectives)
            return false;

        for (var directive = block.GetFirstDirective(); directive != null && directive.SpanStart < block.FullSpan.End; directive = directive.GetNextDirective())
        {
            if (directive.Kind() is SyntaxKind.IfDirectiveTrivia or SyntaxKind.ElifDirectiveTrivia or SyntaxKind.ElseDirectiveTrivia or SyntaxKind.EndIfDirectiveTrivia)
                return true;
        }

        return false;
    }
}
