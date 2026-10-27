using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace Squirix.Analyzers;

/// <summary>
/// Rewrites 'x is null' to 'x == null' and 'x is not null' to 'x != null' (SQR0012).
/// A null arm inside an 'or' pattern gets no fix: splitting the pattern would evaluate the operand twice.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PreferEqualityOperatorCodeFixProvider))]
[Shared]
public sealed class PreferEqualityOperatorCodeFixProvider : CodeFixProvider
{
    private const string NullCheckRuleId = "SQR0012";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [NullCheckRuleId];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan) is not ConstantPatternSyntax nullPattern)
                continue;

            if (!TryGetNullCheck(nullPattern, out var isPattern, out var negated))
                continue;

            var title = negated ? "Use '!='" : "Use '=='";
            context.RegisterCodeFix(
                CodeAction.Create(title, cancellationToken => ReplaceAsync(context.Document, isPattern, negated, cancellationToken), title),
                diagnostic);
        }
    }

    private static async Task<Document> ReplaceAsync(Document document, IsPatternExpressionSyntax isPattern, bool negated, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var pattern = (ConstantPatternSyntax)(negated ? ((UnaryPatternSyntax)isPattern.Pattern).Pattern : isPattern.Pattern);

        // 'x is not null' becomes 'x != null': the operator takes the leading trivia of 'is' and the trailing trivia of 'not'.
        var lastOperatorToken = negated ? ((UnaryPatternSyntax)isPattern.Pattern).OperatorToken : isPattern.IsKeyword;
        var operatorToken = SyntaxFactory.Token(negated ? SyntaxKind.ExclamationEqualsToken : SyntaxKind.EqualsEqualsToken)
            .WithLeadingTrivia(isPattern.IsKeyword.LeadingTrivia)
            .WithTrailingTrivia(lastOperatorToken.TrailingTrivia);

        var comparison = SyntaxFactory.BinaryExpression(negated ? SyntaxKind.NotEqualsExpression : SyntaxKind.EqualsExpression, isPattern.Expression, operatorToken,
            pattern.Expression);

        // '==' binds looser than 'is', so 'a == b is null' needs '(b == null)'; the simplifier drops the parentheses everywhere else.
        var replacement = SyntaxFactory.ParenthesizedExpression(comparison.WithoutTrivia())
            .WithTriviaFrom(isPattern)
            .WithAdditionalAnnotations(Simplifier.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(isPattern, replacement));
    }

    private static bool TryGetNullCheck(ConstantPatternSyntax nullPattern, out IsPatternExpressionSyntax isPattern, out bool negated)
    {
        switch (nullPattern.Parent)
        {
            case IsPatternExpressionSyntax parent:
                isPattern = parent;
                negated = false;
                return true;
            case UnaryPatternSyntax { Parent: IsPatternExpressionSyntax parent } notPattern when notPattern.IsKind(SyntaxKind.NotPattern):
                isPattern = parent;
                negated = true;
                return true;
            default:
                isPattern = null!;
                negated = false;
                return false;
        }
    }
}
