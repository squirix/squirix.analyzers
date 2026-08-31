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
/// capturing delegate (a non-static lambda or an anonymous method, which allocates a new delegate and a display class
/// on every call). A <c language="csharp">static</c> lambda has no closure and is cached as a single static delegate,
/// so it does not allocate and is not flagged.
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
        "Exception asserts that capture the operation in a non-static delegate allocate a new delegate and a display " +
        "class on every call. A static lambda has no closure, is cached as a single static delegate, and does not " +
        "allocate. Supply an already-started operation to an allocation-free assert instead of a capturing lambda or " +
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

        if (!CapturesDelegate(node))
            return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), $"`{name}`"));
    }

    private static bool CapturesDelegate(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList is null)
            return false;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (ContainsCapturingDelegate(argument.Expression))
                return true;
        }

        return false;
    }

    private static bool ContainsCapturingDelegate(ExpressionSyntax expression)
    {
        foreach (var node in EnumerateDelegateNodes(expression))
        {
            switch (node)
            {
                // Static lambdas have no closure: the compiler caches a single static delegate, so repeated calls do
                // not allocate. Capturing (non-static) lambdas and anonymous methods allocate a new delegate, plus a
                // display class when they capture state, on every call.
                case AnonymousMethodExpressionSyntax:
                    return true;
                case SimpleLambdaExpressionSyntax { Modifiers: var simpleModifiers } when !simpleModifiers.Any(SyntaxKind.StaticKeyword):
                    return true;
                case ParenthesizedLambdaExpressionSyntax { Modifiers: var parenthesizedModifiers } when !parenthesizedModifiers.Any(SyntaxKind.StaticKeyword):
                    return true;
            }
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
