using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Forbids exception-assert invocations that capture the operation in a delegate to allocate a display class on every
/// call, regardless of the assert library (xUnit <c language="csharp">Assert.Throws</c>, FluentAssertions
/// <c language="csharp">Should().Throw</c>, NUnit <c language="csharp">Assert.Throws</c>, and similar helpers). An
/// invocation is flagged when the method is a <c language="csharp">Throws</c>/<c language="csharp">Throw</c> family
/// member and at least one argument is a lambda or anonymous method (a delegate capture). Use an allocation-free assert
/// that takes an already-started operation instead, for example a closure-free testkit <c language="csharp">Throws</c>
/// helper.
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
        "Exception asserts that capture the operation in a delegate allocate a display class on every call. Supply an " +
        "already-started operation to an allocation-free assert instead of a lambda or anonymous method.";

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
            if (ContainsDelegateSyntax(argument.Expression))
                return true;
        }

        return false;
    }

    private static bool ContainsDelegateSyntax(ExpressionSyntax expression)
    {
        if (expression.IsKind(SyntaxKind.SimpleLambdaExpression)
            || expression.IsKind(SyntaxKind.ParenthesizedLambdaExpression)
            || expression.IsKind(SyntaxKind.AnonymousMethodExpression))
        {
            return true;
        }

        foreach (var descendant in expression.DescendantNodes())
        {
            if (descendant.IsKind(SyntaxKind.SimpleLambdaExpression)
                || descendant.IsKind(SyntaxKind.ParenthesizedLambdaExpression)
                || descendant.IsKind(SyntaxKind.AnonymousMethodExpression))
            {
                return true;
            }
        }

        return false;
    }
}
