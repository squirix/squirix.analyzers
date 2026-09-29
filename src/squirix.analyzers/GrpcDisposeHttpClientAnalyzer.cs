using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a <c>Grpc.Net.Client.GrpcChannelOptions</c> object initializer or property assignment that sets
/// <c language="csharp">HttpHandler</c> or <c language="csharp">HttpClient</c> to a newly created or locally owned
/// disposable while <c language="csharp">DisposeHttpClient</c> is not set for the same options object (SQR0027).
/// The channel disposes the handler or client only when <c language="csharp">DisposeHttpClient</c> is
/// <c language="csharp">true</c>, so an unset flag leaks the connection pools of a handler created for one channel.
/// A handler or client held in a parameter or field is owned by the caller or the type and is never flagged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrpcDisposeHttpClientAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0027";
    private const string DisposeFlagName = "DisposeHttpClient";
    private const string OptionsTypeName = "GrpcChannelOptions";

    private static readonly LocalizableString Description = "GrpcChannel disposes the handler or client passed through GrpcChannelOptions only when " +
                                                            "DisposeHttpClient is true, and the flag defaults to false. Set DisposeHttpClient explicitly " +
                                                            "when the handler or client is created for the channel.";

    private static readonly LocalizableString MessageFormat = "GrpcChannelOptions.{0} is set to a disposable owned here but DisposeHttpClient is not set";
    private static readonly LocalizableString Title = "Set DisposeHttpClient when GrpcChannelOptions receives an owned handler or client";
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
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        var propertyName = GetAssignedName(assignment.Left);
        if (propertyName is not ("HttpHandler" or "HttpClient"))
            return;

        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;
        if (!TryGetOptionsOwner(assignment, semanticModel, cancellationToken, out var owner))
            return;

        if (!IsOwnedDisposable(assignment.Right, semanticModel, cancellationToken))
            return;

        if (IsDisposeFlagSet(assignment, owner, semanticModel, cancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.GetLocation(), propertyName));
    }

    private static string? GetAssignedName(ExpressionSyntax left) => left switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null,
    };

    private static ISymbol? GetOwnerSymbolOfCreation(BaseObjectCreationExpressionSyntax creation, SemanticModel semanticModel, CancellationToken cancellationToken) =>
        creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
            AssignmentExpressionSyntax assignment when assignment.Right == creation => semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol,
            _ => null,
        };

    private static bool HasDisposeFlagAssignment(SyntaxNode scope, ISymbol owner, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var node in scope.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax member }
                    when member.Name.Identifier.ValueText == DisposeFlagName
                         && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(member.Expression, cancellationToken).Symbol, owner):
                    return true;
                case AssignmentExpressionSyntax { Right: var right } assignment
                    when CreationInitializerSetsFlag(right)
                         && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, owner):
                    return true;
                case VariableDeclaratorSyntax { Initializer: { } initializer } declarator
                    when CreationInitializerSetsFlag(initializer.Value)
                         && SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(declarator, cancellationToken), owner):
                    return true;
            }
        }

        return false;
    }

    private static bool CreationInitializerSetsFlag(ExpressionSyntax value) =>
        Unwrap(value) is BaseObjectCreationExpressionSyntax { Initializer: { } initializer } && InitializerSetsDisposeFlag(initializer);

    private static bool IsUsingDeclared(VariableDeclaratorSyntax declarator) =>
        declarator.Parent is VariableDeclarationSyntax
        {
            Parent: LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } or UsingStatementSyntax,
        };

    private static bool InitializerSetsDisposeFlag(InitializerExpressionSyntax initializer)
    {
        foreach (var expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment && GetAssignedName(assignment.Left) == DisposeFlagName)
                return true;
        }

        return false;
    }

    private static bool IsDisposable(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (IsSystemDisposable(type))
            return true;

        foreach (var contract in type.AllInterfaces)
        {
            if (IsSystemDisposable(contract))
                return true;
        }

        return false;
    }

    private static bool IsDisposeFlagSet(AssignmentExpressionSyntax assignment, OptionsOwner owner, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (owner.Creation is { Initializer: { } initializer } && InitializerSetsDisposeFlag(initializer))
            return true;

        if (owner.Symbol == null)
            return false;

        SyntaxNode? scope = assignment.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (scope is GlobalStatementSyntax)
            scope = scope.Parent;

        return scope != null && HasDisposeFlagAssignment(scope, owner.Symbol, semanticModel, cancellationToken);
    }

    private static bool IsOptionsType(ITypeSymbol? type) =>
        type is { Name: OptionsTypeName, ContainingNamespace: { Name: "Client", ContainingNamespace: { Name: "Net", ContainingNamespace: { Name: "Grpc", ContainingNamespace.IsGlobalNamespace: true } } } };

    private static bool IsOwnedDisposable(ExpressionSyntax value, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var expression = Unwrap(value);
        if (IsOwningExpression(expression))
            return IsDisposable(semanticModel.GetTypeInfo(expression, cancellationToken).Type);

        if (expression is not IdentifierNameSyntax)
            return false;

        if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not ILocalSymbol { DeclaringSyntaxReferences.Length: > 0 } local)
            return false;

        if (local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax { Initializer: { } initializer } declarator)
            return false;

        if (IsUsingDeclared(declarator))
            return false;

        return IsOwningExpression(Unwrap(initializer.Value)) && IsDisposable(local.Type);
    }

    private static bool IsOwningExpression(ExpressionSyntax expression) => expression is BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax;

    private static bool IsSystemDisposable(ITypeSymbol type) => type is { Name: "IDisposable", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    private static bool TryGetOptionsOwner(AssignmentExpressionSyntax assignment, SemanticModel semanticModel, CancellationToken cancellationToken, out OptionsOwner owner)
    {
        owner = default;
        if (assignment is { Left: IdentifierNameSyntax, Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax creation } })
        {
            if (!IsOptionsType(semanticModel.GetTypeInfo(creation, cancellationToken).Type))
                return false;

            owner = new OptionsOwner(creation, GetOwnerSymbolOfCreation(creation, semanticModel, cancellationToken));
            return true;
        }

        if (assignment.Left is not MemberAccessExpressionSyntax member)
            return false;

        if (!IsOptionsType(semanticModel.GetTypeInfo(member.Expression, cancellationToken).Type))
            return false;

        owner = new OptionsOwner(null, semanticModel.GetSymbolInfo(member.Expression, cancellationToken).Symbol);
        return true;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    private readonly struct OptionsOwner
    {
        public OptionsOwner(BaseObjectCreationExpressionSyntax? creation, ISymbol? symbol)
        {
            Creation = creation;
            Symbol = symbol;
        }

        public BaseObjectCreationExpressionSyntax? Creation { get; }

        public ISymbol? Symbol { get; }
    }
}
