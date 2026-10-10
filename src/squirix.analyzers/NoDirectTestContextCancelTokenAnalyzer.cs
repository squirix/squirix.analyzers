using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Squirix.Analyzers;

/// <summary>
/// Forbids reading the test cancellation token from <c language="csharp">TestContext.Current</c>.
/// For xUnit, <c language="csharp">TestContext.Current.CancellationToken</c> is reported inside non-static classes unless the class or one of its
/// base classes exposes a shared <c language="csharp">CancellationToken</c> member. For TUnit,
/// <c language="csharp">TestContext.Current.Execution.CancellationToken</c> is always reported, because TUnit passes the token to the test method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoDirectTestContextCancelTokenAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0017";
    private const string TUnitAccess = "TestContext.Current.Execution.CancellationToken";
    private const string TUnitAdvice = "take the CancellationToken parameter of the test method and pass it down instead";
    private const string XunitAccess = "TestContext.Current.CancellationToken";
    private const string XunitAdvice = "consume the shared CancellationToken exposed by a base class instead";

    private static readonly LocalizableString Description = "The test cancellation token must not be read from TestContext.Current. With xUnit, consume a shared " +
                                                            "CancellationToken member exposed by the class or one of its base classes. With TUnit, take the " +
                                                            "CancellationToken parameter of the test method.";

    private static readonly LocalizableString MessageFormat = "Do not use {0} directly; {1}";

    private static readonly LocalizableString Title = "Avoid reading the cancellation token from TestContext.Current";
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
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var token = (IPropertyReferenceOperation)context.Operation;
        if (token.Property.Name != "CancellationToken" || !IsCancellationTokenType(token.Property.Type))
            return;

        var start = token.Syntax.SpanStart;
        var receiver = GetReceiver(token, ref start);
        if (IsCurrentOf(receiver, "Xunit", null))
        {
            if (GetContainingType(context.ContainingSymbol) is { IsStatic: false, TypeKind: TypeKind.Class } type && !ExposesSharedCancellationToken(type))
                Report(context, token, start, XunitAccess, XunitAdvice);

            return;
        }

        if (receiver is IConditionalAccessOperation { WhenNotNull: IPropertyReferenceOperation inner })
            receiver = inner;

        if (receiver is IPropertyReferenceOperation { Property.Name: "Execution" } execution
            && IsTestContext(execution.Property.ContainingType, "Core", "TUnit")
            && IsCurrentOf(GetReceiver(execution, ref start), "Core", "TUnit"))
            Report(context, token, start, TUnitAccess, TUnitAdvice);
    }

    private static bool DeclaresCancellationTokenMember(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol property:
                {
                    if (IsCancellationTokenType(property.Type))
                        return true;

                    continue;
                }
                case IFieldSymbol field when IsCancellationTokenType(field.Type):
                    return true;
            }
        }

        return false;
    }

    private static bool ExposesSharedCancellationToken(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            if (current.TypeKind == TypeKind.Class && DeclaresCancellationTokenMember(current))
                return true;
        }

        return false;
    }

    /// <summary>Returns the type that declares the code, or null for top-level statements, which have no type to derive from a test base.</summary>
    private static INamedTypeSymbol? GetContainingType(ISymbol? symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            switch (current)
            {
                case IMethodSymbol { Name: WellKnownMemberNames.TopLevelStatementsEntryPointMethodName }:
                    return null;
                case INamedTypeSymbol type:
                    return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the operation a property is read from. For <c language="csharp">a?.B</c> that is <c language="csharp">a</c>, and
    /// <paramref name="start" /> moves back to where the whole conditional access begins.
    /// </summary>
    private static IOperation? GetReceiver(IPropertyReferenceOperation reference, ref int start)
    {
        var instance = reference.Instance;
        if (instance is not IConditionalAccessInstanceOperation)
            return instance;

        var child = instance;
        for (var parent = instance.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent is not IConditionalAccessOperation conditional || conditional.WhenNotNull != child)
                continue;

            start = Math.Min(start, conditional.Syntax.SpanStart);
            return conditional.Operation;
        }

        return null;
    }

    private static bool IsCancellationTokenType(ITypeSymbol? type) =>
        type is { Name: "CancellationToken", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };

    private static bool IsCurrentOf(IOperation? operation, string ns, string? outerNs) =>
        operation is IPropertyReferenceOperation { Property: { Name: "Current", IsStatic: true } current } && IsTestContext(current.ContainingType, ns, outerNs);

    private static bool IsTestContext(INamedTypeSymbol? type, string ns, string? outerNs)
    {
        if (type is not { Name: "TestContext", Arity: 0, ContainingType: null, ContainingNamespace: { IsGlobalNamespace: false } inner } || inner.Name != ns)
            return false;

        var outer = inner.ContainingNamespace;
        return outerNs is null ? outer.IsGlobalNamespace : outer.Name == outerNs && outer.ContainingNamespace is { IsGlobalNamespace: true };
    }

    private static void Report(OperationAnalysisContext context, IPropertyReferenceOperation token, int start, string access, string advice)
    {
        var location = Location.Create(token.Syntax.SyntaxTree, TextSpan.FromBounds(start, token.Syntax.Span.End));
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, access, advice));
    }
}
