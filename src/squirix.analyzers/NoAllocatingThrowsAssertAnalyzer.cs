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
/// <c language="csharp">Throws</c>/<c language="csharp">Throw</c> family member and it, or an earlier call of the same
/// chain (<c language="csharp">Assert.That(...)</c>, <c language="csharp">Invoking(...)</c>), is given a
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
        "Throws", "ThrowsAny", "ThrowsAsync", "ThrowsAnyAsync", "ThrowsExactly", "ThrowsExactlyAsync", "ThrowsException", "ThrowsExceptionAsync",
        "Throw", "ThrowAny", "ThrowExactly", "ThrowAsync", "ThrowAnyAsync", "ThrowExactlyAsync", "ThrowWithinAsync",
    ];

    // Mocking libraries name their "make this call throw" methods like asserts; a chain that ends in one is a setup.
    private static readonly HashSet<string> MockingNamespaces = ["Moq", "NSubstitute", "FakeItEasy"];

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
        var name = GetInvokedName(node.Expression);
        if (name == null || !ThrowMethodNames.Contains(name))
            return;

        // A bare name is an assert only when it comes from another type through 'using static'; a method of the
        // enclosing type with such a name is the code's own helper.
        if (node.Expression is SimpleNameSyntax && !IsImportedStaticMethod(context))
            return;

        if (!CapturesDelegate(node, context.SemanticModel, context.CancellationToken) || IsMockSetup(context))
            return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), $"`{name}`"));
    }

    /// <summary>
    /// Returns whether the assert is given a capturing delegate. In its own arguments any delegate counts. Along the
    /// chain before it, only a delegate that is itself the argument of a call counts, as in
    /// <c language="csharp">Assert.That(() => ...).Throws&lt;T&gt;()</c> and
    /// <c language="csharp">Invoking(() => ...).Should().Throw&lt;T&gt;()</c>, or the delegate the chain starts from
    /// (<c language="csharp">new Action(() => ...).Should().Throw&lt;T&gt;()</c>): a lambda deeper inside an argument
    /// belongs to the operation under test, not to the assert.
    /// </summary>
    private static bool CapturesDelegate(InvocationExpressionSyntax assert, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var argument in assert.ArgumentList.Arguments)
        {
            if (ContainsCapturingDelegate(argument.Expression, semanticModel, cancellationToken))
                return true;
        }

        for (var link = GetReceiver(assert); link != null; link = GetReceiver(link))
        {
            if (GetDirectDelegate(link, semanticModel, cancellationToken) is { } subject && IsCapturingDelegate(subject, semanticModel, cancellationToken))
                return true;

            if (link is not InvocationExpressionSyntax call)
                continue;

            foreach (var argument in call.ArgumentList.Arguments)
            {
                if (GetDirectDelegate(argument.Expression, semanticModel, cancellationToken) is { } function && IsCapturingDelegate(function, semanticModel, cancellationToken))
                    return true;
            }
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

    /// <summary>
    /// Returns the lambda or anonymous method an expression consists of, looking through parentheses, a cast and a
    /// delegate creation such as <c language="csharp">new Action(() => ...)</c>.
    /// </summary>
    private static AnonymousFunctionExpressionSyntax? GetDirectDelegate(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (expression)
            {
                case AnonymousFunctionExpressionSyntax function:
                    return function;
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    break;
                case ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } creation
                    when semanticModel.GetTypeInfo(creation, cancellationToken).Type is { TypeKind: TypeKind.Delegate }:
                    expression = arguments[0].Expression;
                    break;
                default:
                    return null;
            }
        }
    }

    private static string? GetInvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// Returns the previous link of a call chain: what an invocation or member access is applied to, the inside of
    /// parentheses, of a cast, of a null-forgiving operator and of an await, and for a link that follows
    /// <c language="csharp">?.</c> the expression before it. A whole <c language="csharp">a?.b</c> expression continues
    /// with its last link.
    /// </summary>
    private static ExpressionSyntax? GetReceiver(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax call => call.Expression,
        MemberAccessExpressionSyntax access => access.Expression,
        MemberBindingExpressionSyntax binding => GetConditionalReceiver(binding),
        ConditionalAccessExpressionSyntax conditional => conditional.WhenNotNull,
        CastExpressionSyntax cast => cast.Expression,
        ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
        PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppression => suppression.Operand,
        AwaitExpressionSyntax awaited => awaited.Expression,
        _ => null,
    };

    private static ExpressionSyntax? GetConditionalReceiver(MemberBindingExpressionSyntax binding)
    {
        for (var node = binding.Parent; node != null; node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull.Span.Contains(binding.Span))
                return conditional.Expression;
        }

        return null;
    }

    private static bool IsCapturingDelegate(AnonymousFunctionExpressionSyntax function, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        // Explicitly static lambdas never capture; non-capturing lambdas without the modifier
        // are likewise cached by the compiler, so only true outer-state capture allocates per call.
        if (function.Modifiers.Any(SyntaxKind.StaticKeyword))
            return false;

        // A lambda that becomes an expression tree, as in a mock setup, is data for the library and not a delegate to call.
        if (IsExpressionTree(semanticModel.GetTypeInfo(function, cancellationToken).ConvertedType))
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

    private static bool IsExpressionTree(ITypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current is { Name: "Expression", ContainingNamespace: { Name: "Expressions", ContainingNamespace: { Name: "Linq", ContainingNamespace.Name: "System" } } })
                return true;
        }

        return false;
    }

    /// <summary>Returns whether the matched method belongs to a mocking library, where it sets a call up to throw and asserts nothing.</summary>
    private static bool IsMockSetup(SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not IMethodSymbol method)
            return false;

        var root = method.ContainingNamespace;
        while (root is { IsGlobalNamespace: false, ContainingNamespace.IsGlobalNamespace: false })
            root = root.ContainingNamespace;

        return root != null && MockingNamespaces.Contains(root.Name);
    }

    /// <summary>Returns whether the invoked method is a static method of a type other than the enclosing ones and their bases.</summary>
    private static bool IsImportedStaticMethod(SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary } method)
            return false;

        for (var enclosing = context.ContainingSymbol?.ContainingType; enclosing != null; enclosing = enclosing.ContainingType)
        {
            for (var type = enclosing; type != null; type = type.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, method.ContainingType.OriginalDefinition))
                    return false;
            }
        }

        return true;
    }

    private static bool IsDelegateNode(SyntaxNode node) => node.IsKind(SyntaxKind.SimpleLambdaExpression) || node.IsKind(SyntaxKind.ParenthesizedLambdaExpression) ||
                                                           node.IsKind(SyntaxKind.AnonymousMethodExpression);
}
