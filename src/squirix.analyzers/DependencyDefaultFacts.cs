using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Squirix.Analyzers;

/// <summary>
/// Answers questions about the places where a well-known default, such as TimeProvider.System, stands in for a missing dependency.
/// </summary>
internal static class DependencyDefaultFacts
{
    /// <summary>Returns whether the type is the type of that name in the System namespace.</summary>
    internal static bool IsSystemType(ITypeSymbol? type, string name) =>
        type is INamedTypeSymbol { ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } && type.Name == name;

    /// <summary>Returns whether the member access reads the System property of TimeProvider.</summary>
    internal static bool IsTimeProviderSystem(SemanticModel semanticModel, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        return memberAccess.Name.Identifier.ValueText == "System"
            && semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol property
            && IsSystemType(property.ContainingType, "TimeProvider");
    }

    /// <summary>
    /// Returns whether the expression supplies a dependency that the caller did not choose: the right side of a
    /// null-coalescing operator, the initializer of an instance field or property, an argument of a constructor initializer,
    /// or the value assigned to an instance field or property of the containing type.
    /// </summary>
    internal static bool IsHiddenDefault(SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken)
    {
        var current = expression;
        while (current.Parent is ParenthesizedExpressionSyntax or PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
            current = (ExpressionSyntax)current.Parent;

        return current.Parent switch
        {
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce => coalesce.Right == current,
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceAssignmentExpression } coalesceAssignment => coalesceAssignment.Right == current,
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment =>
                assignment.Right == current && AssignsOwnInstanceMember(semanticModel, assignment, cancellationToken),
            EqualsValueClauseSyntax initializer => IsInstanceMemberInitializer(initializer),
            ArgumentSyntax { Parent: ArgumentListSyntax { Parent: ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax } } => true,
            _ => false,
        };
    }

    private static bool AssignsOwnInstanceMember(SemanticModel semanticModel, AssignmentExpressionSyntax assignment, CancellationToken cancellationToken)
    {
        // A member named in an object initializer is set by the code that creates the object, which is an explicit choice.
        return assignment.Parent is not InitializerExpressionSyntax
            && assignment.Left is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
            && semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false };
    }

    private static bool IsInstanceMemberInitializer(EqualsValueClauseSyntax initializer)
    {
        return initializer.Parent switch
        {
            PropertyDeclarationSyntax property => !property.Modifiers.Any(SyntaxKind.StaticKeyword),
            VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } => !field.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };
    }
}
