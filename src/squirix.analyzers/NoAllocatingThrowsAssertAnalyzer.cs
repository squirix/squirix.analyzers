using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

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

    private static readonly LocalizableString Description = "Exception asserts that capture the operation in a capturing delegate allocate a new delegate and a display " +
                                                            "class on every call. A non-capturing lambda has no closure, is cached as a single static delegate, and does " +
                                                            "not allocate. Supply an already-started operation to an allocation-free assert instead of a capturing lambda or " +
                                                            "anonymous method.";

    private static readonly LocalizableString MessageFormat =
        "Do not use {0} with a delegate; use an allocation-free exception assert that takes an already-started operation instead";

    private static readonly HashSet<string> ThrowMethodNames =
    [
        "Throws", "ThrowsAny", "ThrowsAsync", "ThrowsAnyAsync",
        "Throw", "ThrowAny", "ThrowExactly", "ThrowAsync",
    ];

    private static readonly LocalizableString Title = "Avoid allocating exception assert invocations";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Warning, true, Description);

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

    private static bool CapturesDelegate(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (ContainsCapturingDelegate(argument.Expression, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool ContainsCapturingDelegate(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var node in EnumerateDelegateNodes(expression))
        {
            if (node is AnonymousFunctionExpressionSyntax function && IsCapturingDelegate(function, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static IEnumerable<SyntaxNode> EnumerateDelegateNodes(ExpressionSyntax expression)
    {
        // Only outermost delegates are yielded: captures of nested delegates are part of the enclosing delegate's analysis,
        // and a nested delegate capturing the enclosing delegate's own locals is not outer state.
        var pending = new Stack<SyntaxNode>();
        pending.Push(expression);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (IsDelegateNode(node))
            {
                yield return node;
                continue;
            }

            foreach (var child in node.ChildNodes())
                pending.Push(child);
        }
    }

    private static bool IsCapturingDelegate(AnonymousFunctionExpressionSyntax function, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        // Explicitly static lambdas never capture; non-capturing lambdas without the modifier
        // are likewise cached by the compiler, so only true outer-state capture allocates per call.
        if (function.Modifiers.Any(SyntaxKind.StaticKeyword))
            return false;

        var operation = semanticModel.GetOperation(function, cancellationToken);
        if (operation is null)
            return false;

        return CapturesOuterState(operation, function.Span, semanticModel, [], cancellationToken);
    }

    private static bool CapturesOuterState(IOperation operation, TextSpan scope, SemanticModel semanticModel, HashSet<ISymbol> visitedFunctions, CancellationToken cancellationToken)
    {
        // nameof(...) arguments resolve to symbols but never capture at runtime.
        if (operation is INameOfOperation)
            return false;

        switch (operation)
        {
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }:
                return true;

            case ILocalReferenceOperation localReference when !IsDeclaredWithin(localReference.Local, scope, cancellationToken):
                return true;

            case IParameterReferenceOperation parameterReference when !IsDeclaredWithin(parameterReference.Parameter, scope, cancellationToken):
                return true;

            case IInvocationOperation { TargetMethod.MethodKind: MethodKind.LocalFunction } invocation
                when LocalFunctionCaptures(invocation.TargetMethod, scope, semanticModel, visitedFunctions, cancellationToken):
                return true;

            case IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } methodReference
                when LocalFunctionCaptures(methodReference.Method, scope, semanticModel, visitedFunctions, cancellationToken):
                return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (CapturesOuterState(child, scope, semanticModel, visitedFunctions, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool LocalFunctionCaptures(IMethodSymbol localFunction, TextSpan scope, SemanticModel semanticModel, HashSet<ISymbol> visitedFunctions, CancellationToken cancellationToken)
    {
        // A local function declared inside the delegate is walked as part of its body.
        if (IsDeclaredWithin(localFunction, scope, cancellationToken))
            return false;

        if (!visitedFunctions.Add(localFunction))
            return false;

        foreach (var reference in localFunction.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(cancellationToken);
            if (syntax.SyntaxTree != semanticModel.SyntaxTree)
                return true;

            var operation = semanticModel.GetOperation(syntax, cancellationToken);
            if (operation is not null && CapturesOuterState(operation, syntax.Span, semanticModel, visitedFunctions, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool IsDeclaredWithin(ISymbol symbol, TextSpan scope, CancellationToken cancellationToken)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (scope.Contains(reference.GetSyntax(cancellationToken).Span))
                return true;
        }

        return false;
    }

    private static bool IsDelegateNode(SyntaxNode node) => node.IsKind(SyntaxKind.SimpleLambdaExpression) || node.IsKind(SyntaxKind.ParenthesizedLambdaExpression) ||
                                                           node.IsKind(SyntaxKind.AnonymousMethodExpression);
}
