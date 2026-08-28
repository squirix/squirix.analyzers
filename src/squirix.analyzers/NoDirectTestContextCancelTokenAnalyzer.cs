using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Forbids direct use of <c language="csharp">TestContext.Current.CancellationToken</c> inside non-static classes.
/// A class may use it directly only when itself or one of its base classes exposes a shared
/// <c language="csharp">CancellationToken</c> member, so derived tests do not sprinkle the xUnit static everywhere.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoDirectTestContextCancelTokenAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0017";

    private static readonly LocalizableString Description = "TestContext.Current.CancellationToken must not be used directly unless the class or one of its " +
                                                            "base classes exposes a shared CancellationToken member. Prefer consuming that shared token from derived tests.";

    private static readonly LocalizableString MessageFormat = "Do not use TestContext.Current.CancellationToken directly; consume the shared CancellationToken exposed by a base class instead";

    private static readonly LocalizableString Title = "Avoid direct use of TestContext.Current.CancellationToken";
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
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var node = (MemberAccessExpressionSyntax)context.Node;

        if (!IsTestContextCancellationToken(node))
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

        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
    }

    private static bool ExposesSharedCancellationToken(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.BaseType)
        {
            if (current.TypeKind == TypeKind.Class && DeclaresCancellationTokenMember(current))
                return true;
        }

        return false;
    }

    private static bool DeclaresCancellationTokenMember(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is IPropertySymbol property)
            {
                if (IsCancellationTokenType(property.Type))
                    return true;

                continue;
            }

            if (member is IFieldSymbol field && IsCancellationTokenType(field.Type))
                return true;
        }

        return false;
    }

    private static bool IsCancellationTokenType(ITypeSymbol? type)
    {
        var threading = type?.ContainingNamespace;
        var system = threading?.ContainingNamespace;
        return threading is { Name: "Threading" }
            && system is { Name: "System", IsGlobalNamespace: false }
            && system.ContainingNamespace.IsGlobalNamespace;
    }

    private static TypeDeclarationSyntax? GetEnclosingType(MemberAccessExpressionSyntax node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is TypeDeclarationSyntax type)
                return type;
        }

        return null;
    }

    private static bool IsTestContextCancellationToken(MemberAccessExpressionSyntax node)
    {
        if (node.Name.Identifier.Text != "CancellationToken")
            return false;

        if (node.Expression is not MemberAccessExpressionSyntax currentAccess)
            return false;

        if (currentAccess.Name.Identifier.Text != "Current")
            return false;

        return currentAccess.Expression is IdentifierNameSyntax { Identifier.Text: "TestContext" };
    }
}
