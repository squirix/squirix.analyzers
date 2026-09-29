using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Squirix.Analyzers;

/// <summary>
/// Forbids direct use of <c language="csharp">TestContext.Current.CancellationToken</c> inside non-static classes.
/// A class may use it directly only when itself or one of its base classes exposes a shared
/// <c language="csharp">CancellationToken</c> member, so derived tests do not access TestContext directly everywhere.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoDirectTestContextCancelTokenAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0017";

    private static readonly LocalizableString Description = "TestContext.Current.CancellationToken must not be used directly unless the class or one of its " +
                                                            "base classes exposes a shared CancellationToken member. Prefer consuming that shared token from derived tests.";

    private static readonly LocalizableString MessageFormat =
        "Do not use TestContext.Current.CancellationToken directly; consume the shared CancellationToken exposed by a base class instead";

    private static readonly LocalizableString Title = "Avoid direct use of TestContext.Current.CancellationToken";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.MemberBindingExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;

        Location? location;
        switch (node)
        {
            case MemberAccessExpressionSyntax access when IsTestContextCancellationToken(access):
                location = access.GetLocation();
                break;
            case MemberBindingExpressionSyntax binding:
                location = GetConditionalAccessLocation(binding);
                break;
            default:
                return;
        }

        if (location is null)
            return;

        var typeDeclaration = GetEnclosingType(node);
        if (typeDeclaration is null)
            return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken);
        if (symbol is null)
            return;

        if (symbol.IsStatic || symbol.TypeKind != TypeKind.Class)
            return;

        if (ExposesSharedCancellationToken(symbol))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location));
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

    private static TypeDeclarationSyntax? GetEnclosingType(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is TypeDeclarationSyntax type)
                return type;
        }

        return null;
    }

    private static bool IsCancellationTokenType(ITypeSymbol? type)
    {
        if (type is not { Name: "CancellationToken" })
            return false;

        var threading = type.ContainingNamespace;
        var system = threading?.ContainingNamespace;
        return threading is { Name: "Threading" } && system is { Name: "System", IsGlobalNamespace: false } && system.ContainingNamespace.IsGlobalNamespace;
    }

    private static Location? GetConditionalAccessLocation(MemberBindingExpressionSyntax binding)
    {
        if (binding.Name.Identifier.Text != "CancellationToken")
            return null;

        SyntaxNode child = binding;
        for (var parent = binding.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case ConditionalAccessExpressionSyntax conditional when conditional.WhenNotNull == child:
                    return IsTestContextCurrent(conditional.Expression)
                        ? Location.Create(conditional.SyntaxTree, TextSpan.FromBounds(conditional.Expression.SpanStart, binding.Span.End))
                        : null;
                case ConditionalAccessExpressionSyntax conditional when conditional.Expression == child:
                case MemberAccessExpressionSyntax access when access.Expression == child:
                case InvocationExpressionSyntax invocation when invocation.Expression == child:
                case ElementAccessExpressionSyntax element when element.Expression == child:
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    private static bool IsNamespaceQualifier(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax => true,
        AliasQualifiedNameSyntax => true,
        MemberAccessExpressionSyntax access => IsNamespaceQualifier(access.Expression),
        _ => false,
    };

    private static bool IsTestContextCancellationToken(MemberAccessExpressionSyntax node) =>
        node.Name.Identifier.Text == "CancellationToken" && IsTestContextCurrent(node.Expression);

    private static bool IsTestContextCurrent(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppression:
                    expression = suppression.Operand;
                    continue;
                case MemberAccessExpressionSyntax { Name.Identifier.Text: "Current" } current:
                    return IsTestContextType(current.Expression);
                default:
                    return false;
            }
        }
    }

    private static bool IsTestContextType(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax { Identifier.Text: "TestContext" } => true,
        AliasQualifiedNameSyntax { Name.Identifier.Text: "TestContext" } => true,
        MemberAccessExpressionSyntax { Name.Identifier.Text: "TestContext" } qualified => IsNamespaceQualifier(qualified.Expression),
        _ => false,
    };
}
