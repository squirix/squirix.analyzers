using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a catch of OperationCanceledException or TaskCanceledException that reads no cancellation token in its filter or
/// its body, in a function that takes a CancellationToken (SQR0038).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationCatchFilterAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0038";

    private static readonly LocalizableString Description =
        "A catch of a cancellation exception without a filter handles every cancellation the same way: the one the caller requested through its token and " +
        "any other, such as an inner timeout. Filter the catch by the token, for example 'when (token.IsCancellationRequested)'.";

    private static readonly LocalizableString MessageFormat = "Catch of '{0}' handles any cancellation; add a filter such as 'when ({1}.IsCancellationRequested)'";

    private static readonly LocalizableString Title = "Filter a cancellation catch by the cancellation token";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Reliability", DiagnosticSeverity.Info, true, Description,
        HelpLinks.For(DiagnosticId));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Declaration == null || EndsWithThrow(catchClause.Block))
            return;

        var caught = context.SemanticModel.GetTypeInfo(catchClause.Declaration.Type, context.CancellationToken).Type;
        if (caught?.ToDisplayString() is not ("System.OperationCanceledException" or "System.Threading.Tasks.TaskCanceledException"))
            return;

        // A body that reads a token tells the cancellations apart itself, as a filter would.
        if (ReadsCancellation(context.SemanticModel, catchClause.Filter?.FilterExpression, context.CancellationToken)
            || ReadsCancellation(context.SemanticModel, catchClause.Block, context.CancellationToken))
            return;

        var token = FindToken(context.SemanticModel, catchClause, context.CancellationToken);
        if (token != null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, catchClause.CatchKeyword.GetLocation(), caught.Name, token.Name));
    }

    /// <summary>Returns whether the code reads a cancellation token or a cancellation token source, whichever one it is.</summary>
    private static bool ReadsCancellation(SemanticModel semanticModel, SyntaxNode? code, CancellationToken cancellationToken)
    {
        if (code == null)
            return false;

        foreach (var node in code.DescendantNodesAndSelf())
        {
            if (node is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
                continue;

            var type = semanticModel.GetTypeInfo(node, cancellationToken).Type?.ToDisplayString();
            if (type is "System.Threading.CancellationToken" or "System.Threading.CancellationTokenSource")
                return true;
        }

        return false;
    }

    /// <summary>A catch that ends by throwing passes the cancellation on instead of handling it.</summary>
    private static bool EndsWithThrow(BlockSyntax block) => block.Statements.Count > 0 && block.Statements[block.Statements.Count - 1] is ThrowStatementSyntax;

    /// <summary>Returns the first CancellationToken parameter of the functions around the node, innermost first.</summary>
    private static IParameterSymbol? FindToken(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case BaseMethodDeclarationSyntax method:
                    return FindToken(semanticModel, method.ParameterList, cancellationToken);
                case TypeDeclarationSyntax:
                    return null;
                case LocalFunctionStatementSyntax localFunction when FindToken(semanticModel, localFunction.ParameterList, cancellationToken) is { } token:
                    return token;
                case ParenthesizedLambdaExpressionSyntax lambda when FindToken(semanticModel, lambda.ParameterList, cancellationToken) is { } token:
                    return token;
                case AnonymousMethodExpressionSyntax { ParameterList: { } list } when FindToken(semanticModel, list, cancellationToken) is { } token:
                    return token;
            }
        }

        return null;
    }

    private static IParameterSymbol? FindToken(SemanticModel semanticModel, BaseParameterListSyntax parameters, CancellationToken cancellationToken)
    {
        foreach (var parameter in parameters.Parameters)
        {
            if (semanticModel.GetDeclaredSymbol(parameter, cancellationToken) is { } symbol && symbol.Type.ToDisplayString() == "System.Threading.CancellationToken")
                return symbol;
        }

        return null;
    }
}
