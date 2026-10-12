using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a read of the system clock in code that has a TimeProvider at hand: a field or property of the containing type, or
/// a parameter of the containing member (SQR0037).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClockBypassesTimeProviderAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0037";

    private static readonly LocalizableString Description =
        "Code that has a TimeProvider and still reads the system clock keeps running on real time when a test supplies its own provider. Read the time from " +
        "the available provider.";

    private static readonly LocalizableString MessageFormat = "'{0}' reads the system clock although TimeProvider '{1}' is available; read the time from '{1}'";

    private static readonly LocalizableString Title = "Do not read the system clock when a TimeProvider is available";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Warning, true, Description,
        HelpLinks.For(DiagnosticId));

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
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.ValueText;
        if (memberName is not ("System" or "TickCount" or "TickCount64" or "Now" or "UtcNow"))
            return;

        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (symbol is not IPropertySymbol || !IsClockMember(memberName, symbol.ContainingType))
            return;

        if (!ReadsClock(context.SemanticModel, memberAccess, context.CancellationToken))
            return;

        var provider = FindProvider(context.SemanticModel, memberAccess, context.CancellationToken);
        if (provider != null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation(), symbol.ContainingType.Name + "." + memberName, provider));
    }

    private static string? FindMember(SemanticModel semanticModel, TypeDeclarationSyntax declaration, SyntaxNode node, bool instanceAllowed, CancellationToken cancellationToken)
    {
        var type = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
        for (var current = type; current != null; current = current.BaseType)
            foreach (var member in current.GetMembers())
            {
                var memberType = member switch
                {
                    IFieldSymbol { IsImplicitlyDeclared: false } field => field.Type,
                    IPropertySymbol { IsIndexer: false, GetMethod: not null } property => property.Type,
                    _ => null,
                };

                if (!SystemClockFacts.IsTimeProvider(memberType))
                    continue;

                if (!member.IsStatic && !instanceAllowed)
                    continue;

                if (!SymbolEqualityComparer.Default.Equals(current, type) && member.DeclaredAccessibility == Accessibility.Private)
                    continue;

                // The member's own declaration is where its value comes from, not a place that can read it.
                if (!IsDeclaredAround(member, node))
                    return member.Name;
            }

        return null;
    }

    private static string? FindParameter(SemanticModel semanticModel, SyntaxNode? parameters, CancellationToken cancellationToken)
    {
        switch (parameters)
        {
            case ParameterSyntax parameter:
                return semanticModel.GetDeclaredSymbol(parameter, cancellationToken) is { } symbol && SystemClockFacts.IsTimeProvider(symbol.Type) ? symbol.Name : null;
            case BaseParameterListSyntax list:
                foreach (var parameter in list.Parameters)
                {
                    if (FindParameter(semanticModel, parameter, cancellationToken) is { } name)
                        return name;
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Walks outwards from the node and returns the name of the first TimeProvider the code at the node can use: a parameter
    /// of an enclosing function or member, a primary constructor parameter, or a field or property of the containing type.
    /// </summary>
    private static string? FindProvider(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        // A static function cannot capture the enclosing parameters or the instance, and a static member has no instance.
        var capturesAllowed = true;
        var instanceAllowed = true;
        var primaryAllowed = true;
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            SyntaxNode? parameters = null;
            var modifiers = default(SyntaxTokenList);
            switch (current)
            {
                case TypeDeclarationSyntax type:
                    if (capturesAllowed && primaryAllowed && FindParameter(semanticModel, type.ParameterList, cancellationToken) is { } primary)
                        return primary;

                    return FindMember(semanticModel, type, node, instanceAllowed, cancellationToken);
                case LocalFunctionStatementSyntax localFunction:
                    parameters = localFunction.ParameterList;
                    modifiers = localFunction.Modifiers;
                    break;
                case AnonymousFunctionExpressionSyntax anonymous:
                    parameters = GetParameters(anonymous);
                    modifiers = anonymous.Modifiers;
                    break;
                case ConstructorInitializerSyntax:
                case PrimaryConstructorBaseTypeSyntax:
                    instanceAllowed = false;
                    continue;
                case BaseMethodDeclarationSyntax method:
                    parameters = method.ParameterList;
                    modifiers = method.Modifiers;
                    break;
                case IndexerDeclarationSyntax indexer:
                    parameters = indexer.ParameterList;
                    modifiers = indexer.Modifiers;
                    break;
                case BasePropertyDeclarationSyntax property:
                    modifiers = property.Modifiers;
                    if (property is PropertyDeclarationSyntax { Initializer: { } initializer } && initializer.Span.Contains(node.Span))
                        instanceAllowed = false;

                    break;
                case BaseFieldDeclarationSyntax field:
                    modifiers = field.Modifiers;
                    instanceAllowed = false;
                    break;
                default:
                    continue;
            }

            if (capturesAllowed && FindParameter(semanticModel, parameters, cancellationToken) is { } name)
                return name;

            if (!modifiers.Any(SyntaxKind.StaticKeyword))
                continue;

            instanceAllowed = false;
            if (current is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                capturesAllowed = false;
            else
                primaryAllowed = false;
        }

        return null;
    }

    private static SyntaxNode? GetParameters(AnonymousFunctionExpressionSyntax anonymous)
    {
        return anonymous switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList,
            AnonymousMethodExpressionSyntax method => method.ParameterList,
            _ => null,
        };
    }

    private static bool IsClockMember(string memberName, ITypeSymbol type)
    {
        return memberName switch
        {
            "System" => SystemClockFacts.IsSystemType(type, "TimeProvider"),
            "TickCount" or "TickCount64" => SystemClockFacts.IsSystemType(type, "Environment"),
            _ => SystemClockFacts.IsSystemType(type, "DateTime") || SystemClockFacts.IsSystemType(type, "DateTimeOffset"),
        };
    }

    private static bool IsDeclaredAround(ISymbol member, SyntaxNode node)
    {
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == node.SyntaxTree && reference.Span.Contains(node.Span))
                return true;
        }

        return false;
    }

    private static bool ReadsClock(SemanticModel semanticModel, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        SyntaxNode current = memberAccess;
        while (current.Parent is ParenthesizedExpressionSyntax)
            current = current.Parent;

        // A comparison with TimeProvider.System asks which provider this is and reads no time.
        if (current.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression or (int)SyntaxKind.NotEqualsExpression })
            return false;

        // A hidden default is the other rule's finding.
        if (memberAccess.Name.Identifier.ValueText == "System" && SystemClockFacts.IsHiddenDefault(semanticModel, memberAccess, cancellationToken))
            return false;

        for (var ancestor = current.Parent; ancestor is ExpressionSyntax or ArgumentSyntax or ArgumentListSyntax; ancestor = ancestor.Parent)
        {
            if (ancestor is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
                return false;
        }

        return true;
    }
}
