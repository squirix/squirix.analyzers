using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Forbids exception-assert invocations that allocate a new delegate on every call, regardless of the assert library
/// (xUnit <c language="csharp">Assert.Throws</c>, FluentAssertions <c language="csharp">Should().Throw</c>, NUnit
/// <c language="csharp">Assert.Throws</c>, and similar helpers). An invocation is flagged when the method is a
/// <c language="csharp">Throws</c>/<c language="csharp">Throw</c> family member and at least one argument is a
/// capturing delegate (a lambda or anonymous method that captures outer state or 'this', which allocates a new
/// delegate and a display class on every call). A non-capturing lambda has no closure and is cached as a single
/// static delegate (whether or not it is marked 'static'), so it does not allocate per call and is not flagged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoAllocatingThrowsAssertAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0019";

    private static readonly HashSet<string> ThrowMethodNames =
    [
        "Throws", "ThrowsAny", "ThrowsAsync", "ThrowsAnyAsync",
        "Throw", "ThrowAny", "ThrowExactly", "ThrowAsync",
    ];

    private static readonly LocalizableString Description =
        "Exception asserts that capture the operation in a capturing delegate allocate a new delegate and a display " +
        "class on every call. A non-capturing lambda has no closure, is cached as a single static delegate, and does " +
        "not allocate. Supply an already-started operation to an allocation-free assert instead of a capturing lambda or " +
        "anonymous method.";

    private static readonly LocalizableString MessageFormat =
        "Do not use {0} with a delegate; use an allocation-free exception assert that takes an already-started operation instead";

    private static readonly LocalizableString Title = "Avoid allocating exception assert invocations";

    private static readonly DiagnosticDescriptor Rule =
        new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var node = (InvocationExpressionSyntax)context.Node;

        if (node.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var name = memberAccess.Name.Identifier.Text;
        if (!ThrowMethodNames.Contains(name))
            return;

        if (!CapturesDelegate(node, context.SemanticModel, context.CancellationToken))
            return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), $"`{name}`"));
    }

    private static bool CapturesDelegate(InvocationExpressionSyntax invocation, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (ContainsCapturingDelegate(argument.Expression, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool ContainsCapturingDelegate(ExpressionSyntax expression, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        foreach (var node in EnumerateDelegateNodes(expression))
        {
            if (node is AnonymousFunctionExpressionSyntax function && IsCapturingDelegate(function, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool IsCapturingDelegate(AnonymousFunctionExpressionSyntax function, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        // Explicitly static lambdas never capture; non-capturing lambdas without the modifier
        // are likewise cached by the compiler, so only true outer-state capture allocates per call.
        if (function.Modifiers.Any(SyntaxKind.StaticKeyword))
            return false;

        foreach (var descendant in function.DescendantNodes())
        {
            // Skip nested anonymous functions: their captures are checked separately when
            // enumerated, and identifiers declared inside the outer function are not outer captures.
            // Skip nameof(...) arguments: they resolve to symbols but never capture at runtime.
            var skip = false;
            for (var ancestor = descendant.Parent; ancestor is not null && ancestor != function; ancestor = ancestor.Parent)
            {
                if (ancestor is AnonymousFunctionExpressionSyntax)
                {
                    skip = true;
                    break;
                }

                if (ancestor is InvocationExpressionSyntax nameofInvocation
                    && nameofInvocation.Expression is IdentifierNameSyntax nameofName
                    && nameofName.Identifier.ValueText == "nameof"
                    && IsWithin(nameofInvocation.ArgumentList, descendant))
                {
                    skip = true;
                    break;
                }
            }

            if (skip)
                continue;

            if (descendant is ThisExpressionSyntax or BaseExpressionSyntax)
                return true;

            if (descendant is not IdentifierNameSyntax identifier)
                continue;

            var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (symbol is null)
                continue;

            switch (symbol)
            {
                case ILocalSymbol or IParameterSymbol or IRangeVariableSymbol:
                {
                    var declaredInside = false;
                    foreach (var reference in symbol.DeclaringSyntaxReferences)
                    {
                        var syntax = reference.GetSyntax(cancellationToken);
                        if (function.Span.Contains(syntax.Span))
                        {
                            declaredInside = true;
                            break;
                        }
                    }

                    if (!declaredInside)
                        return true;
                    break;
                }

                case IFieldSymbol field:
                    if (!field.IsStatic)
                        return true;
                    break;

                case IPropertySymbol property:
                    if (!property.IsStatic)
                        return true;
                    break;

                case IMethodSymbol method:
                    if (!method.IsStatic && method.MethodKind is not (MethodKind.Constructor or MethodKind.StaticConstructor))
                        return true;
                    break;

                case IEventSymbol evt:
                    if (!evt.IsStatic)
                        return true;
                    break;
            }
        }

        return false;
    }

    private static bool IsWithin(SyntaxNode ancestor, SyntaxNode descendant)
    {
        for (var current = descendant; current is not null; current = current.Parent)
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    private static IEnumerable<SyntaxNode> EnumerateDelegateNodes(ExpressionSyntax expression)
    {
        if (IsDelegateNode(expression))
            yield return expression;

        foreach (var descendant in expression.DescendantNodes())
        {
            if (IsDelegateNode(descendant))
                yield return descendant;
        }
    }

    private static bool IsDelegateNode(SyntaxNode node) =>
        node.IsKind(SyntaxKind.SimpleLambdaExpression)
        || node.IsKind(SyntaxKind.ParenthesizedLambdaExpression)
        || node.IsKind(SyntaxKind.AnonymousMethodExpression);
}
