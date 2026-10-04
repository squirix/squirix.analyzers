using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a <c language="csharp">Task</c> or <c language="csharp">ValueTask</c> that uses a resource declared by
/// <c language="csharp">using</c> or <c language="csharp">await using</c> and leaves the using scope without being awaited
/// (SQR0030). The task is fire-and-forget or is returned to the caller, so the resource is disposed while the operation
/// may still be running against it. Code inside lambdas, anonymous methods and local functions runs on its own schedule
/// and is not analyzed as part of the enclosing scope.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TaskOutlivesUsingScopeAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0030";

    private static readonly LocalizableString Description = "A task that uses a resource declared by using or await using must complete before the resource is disposed. " +
                                                            "When the task is not awaited, or is returned to the caller, the resource is disposed at the end of the using scope " +
                                                            "while the operation may still be running. Await the task inside the scope.";

    private static readonly LocalizableString MessageFormat = "'{0}' is not awaited before '{1}' is disposed at the end of its using scope";
    private static readonly LocalizableString Title = "Await tasks that use a resource before its using scope ends";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Reliability", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
        context.RegisterSyntaxNodeAction(AnalyzeUsingDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AddFollowing(SyntaxList<StatementSyntax> statements, SyntaxNode declaration, List<SyntaxNode> result)
    {
        var found = false;
        foreach (var statement in statements)
        {
            if (found)
                result.Add(statement);
            else if (statement == declaration)
                found = true;
        }
    }

    private static void AnalyzeUsingDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;
        if (declaration.UsingKeyword.RawKind == 0 || IsInsideOuterScope(declaration, context))
            return;

        var resources = new List<ILocalSymbol>();
        AddDeclaredLocals(declaration.Declaration, context, resources);
        if (resources.Count == 0)
            return;

        var walker = new ScopeWalker(context, resources);
        foreach (var following in GetFollowingStatements(declaration))
            walker.Walk(following);
    }

    private static void AnalyzeUsingStatement(SyntaxNodeAnalysisContext context)
    {
        var statement = (UsingStatementSyntax)context.Node;
        var resources = new List<ILocalSymbol>();
        if (!TryGetResources(statement, context, resources) || IsInsideOuterScope(statement, context))
            return;

        new ScopeWalker(context, resources).Walk(statement.Statement);
    }

    private static bool TryGetResources(UsingStatementSyntax statement, SyntaxNodeAnalysisContext context, List<ILocalSymbol> resources)
    {
        if (statement.Declaration != null)
            AddDeclaredLocals(statement.Declaration, context, resources);
        else if (statement.Expression is AssignmentExpressionSyntax { Left: var left }
                 && context.SemanticModel.GetSymbolInfo(left, context.CancellationToken).Symbol is ILocalSymbol assigned)
            resources.Add(assigned);

        return resources.Count > 0;
    }

    private static bool IsInsideOuterScope(SyntaxNode node, SyntaxNodeAnalysisContext context)
    {
        var child = node;
        for (var parent = node.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            if (IsFunctionBoundary(parent) || parent is MemberDeclarationSyntax and not GlobalStatementSyntax)
                return false;

            switch (parent)
            {
                case UsingStatementSyntax outer when child == outer.Statement && TryGetResources(outer, context, []):
                    return true;
                case BlockSyntax block when HasPrecedingUsingDeclaration(block.Statements, child):
                    return true;
                case CompilationUnitSyntax unit when HasPrecedingGlobalUsingDeclaration(unit, child):
                    return true;
            }
        }

        return false;
    }

    private static bool IsUsingDeclaration(StatementSyntax statement)
    {
        while (statement is LabeledStatementSyntax labeled)
            statement = labeled.Statement;

        return statement is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 };
    }

    private static bool HasPrecedingUsingDeclaration(SyntaxList<StatementSyntax> statements, SyntaxNode child)
    {
        foreach (var statement in statements)
        {
            if (statement == child)
                return false;

            if (IsUsingDeclaration(statement))
                return true;
        }

        return false;
    }

    private static bool HasPrecedingGlobalUsingDeclaration(CompilationUnitSyntax unit, SyntaxNode child)
    {
        foreach (var member in unit.Members)
        {
            if (member == child)
                return false;

            if (member is GlobalStatementSyntax global && IsUsingDeclaration(global.Statement))
                return true;
        }

        return false;
    }

    private static void AddDeclaredLocals(VariableDeclarationSyntax declaration, SyntaxNodeAnalysisContext context, List<ILocalSymbol> resources)
    {
        foreach (var declarator in declaration.Variables)
        {
            if (context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken) is ILocalSymbol local)
                resources.Add(local);
        }
    }

    private static List<SyntaxNode> GetFollowingStatements(LocalDeclarationStatementSyntax declaration)
    {
        var result = new List<SyntaxNode>();
        SyntaxNode current = declaration;
        while (current.Parent is LabeledStatementSyntax)
            current = current.Parent;

        switch (current.Parent)
        {
            case BlockSyntax block:
                AddFollowing(block.Statements, current, result);
                break;
            case GlobalStatementSyntax { Parent: CompilationUnitSyntax unit } global:
                var found = false;
                foreach (var member in unit.Members)
                {
                    if (found && member is GlobalStatementSyntax later)
                        result.Add(later.Statement);
                    else if (member == global)
                        found = true;
                }

                break;
        }

        return result;
    }

    private static bool IsFunctionBoundary(SyntaxNode node) => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    private static bool IsTasksType(ITypeSymbol type) =>
        type.Name is "Task" or "ValueTask"
        && type.ContainingNamespace is { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };

    private static bool IsTaskLike(ITypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (IsTasksType(current))
                return true;
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    private static void CollectCandidates(ExpressionSyntax expression, List<ExpressionSyntax> candidates)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                CollectCandidates(parenthesized.Expression, candidates);
                break;
            case CastExpressionSyntax cast:
                CollectCandidates(cast.Expression, candidates);
                break;
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppress:
                CollectCandidates(suppress.Operand, candidates);
                break;
            case ConditionalExpressionSyntax conditional:
                CollectCandidates(conditional.WhenTrue, candidates);
                CollectCandidates(conditional.WhenFalse, candidates);
                break;
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce:
                CollectCandidates(coalesce.Left, candidates);
                CollectCandidates(coalesce.Right, candidates);
                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                    CollectCandidates(arm.Expression, candidates);

                break;
            case ConditionalAccessExpressionSyntax conditionalAccess:
                CollectCandidates(conditionalAccess.WhenNotNull, candidates);
                break;
            default:
                candidates.Add(expression);
                break;
        }
    }

    private static SyntaxNode GetChainRoot(SyntaxNode node)
    {
        while (true)
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax member:
                    node = member.Expression;
                    break;
                case InvocationExpressionSyntax invocation:
                    node = invocation.Expression;
                    break;
                case ElementAccessExpressionSyntax element:
                    node = element.Expression;
                    break;
                case ConditionalAccessExpressionSyntax conditional:
                    node = conditional.Expression;
                    break;
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppress:
                    node = suppress.Operand;
                    break;
                default:
                    return node;
            }
        }
    }

    private static SyntaxNode? GetConditionalReceiver(SyntaxNode call)
    {
        for (var node = call.Parent; node != null && node is not StatementSyntax; node = node.Parent)
        {
            if (node is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull.Span.Contains(call.Span))
                return conditional.Expression;
        }

        return null;
    }

    private static bool IsChain(SyntaxNode node) => node is MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax;

    private static bool IsCall(SyntaxNode node) => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax;

    private static bool CanCarryResource(ITypeSymbol? type)
    {
        if (type == null)
            return true;

        if (type.TypeKind is TypeKind.Enum or TypeKind.Array)
            return false;

        return type.SpecialType is not (SpecialType.System_Void or SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte
            or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
            or SpecialType.System_UInt64 or SpecialType.System_Decimal or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_String
            or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_DateTime or SpecialType.System_Enum);
    }

    private sealed class ScopeWalker
    {
        private readonly SyntaxNodeAnalysisContext _context;
        private readonly Dictionary<ISymbol, ILocalSymbol> _references = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, ILocalSymbol> _tainted = new(SymbolEqualityComparer.Default);

        public ScopeWalker(SyntaxNodeAnalysisContext context, List<ILocalSymbol> resources)
        {
            _context = context;
            foreach (var resource in resources)
                _references[resource] = resource;
        }

        private CancellationToken CancellationToken => _context.CancellationToken;

        private SemanticModel SemanticModel => _context.SemanticModel;

        public void Walk(SyntaxNode root)
        {
            foreach (var node in root.DescendantNodesAndSelf(static node => !IsFunctionBoundary(node)))
                Visit(node);
        }

        private static void Consider(ILocalSymbol candidate, ref ILocalSymbol? best)
        {
            if (best == null || GetDeclarationStart(candidate) > GetDeclarationStart(best))
                best = candidate;
        }

        private static int GetDeclarationStart(ILocalSymbol local) => local.Locations.IsEmpty ? 0 : local.Locations[0].SourceSpan.Start;

        private void AddNestedResources(UsingStatementSyntax statement)
        {
            var nested = new List<ILocalSymbol>();
            if (!TryGetResources(statement, _context, nested))
                return;

            foreach (var resource in nested)
                _references[resource] = resource;
        }

        private void AddNestedResources(LocalDeclarationStatementSyntax declaration)
        {
            var nested = new List<ILocalSymbol>();
            AddDeclaredLocals(declaration.Declaration, _context, nested);
            foreach (var resource in nested)
                _references[resource] = resource;
        }

        private void ClearAwaited(ExpressionSyntax awaited)
        {
            if (_tainted.Count == 0)
                return;

            foreach (var node in awaited.DescendantNodesAndSelf(static node => !IsFunctionBoundary(node)))
            {
                if (node is IdentifierNameSyntax identifier && SemanticModel.GetSymbolInfo(identifier, CancellationToken).Symbol is { } symbol)
                    _ = _tainted.Remove(symbol);
            }
        }

        private string GetCallName(ExpressionSyntax expression)
        {
            if (expression is not InvocationExpressionSyntax invocation)
                return SemanticModel.GetTypeInfo(expression, CancellationToken).Type?.Name ?? expression.ToString();

            if (SemanticModel.GetSymbolInfo(invocation, CancellationToken).Symbol is { } method)
                return method.Name;

            return invocation.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : invocation.Expression.ToString();
        }

        private bool IsFinishedFactory(SyntaxNode node)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    return SemanticModel.GetSymbolInfo(invocation, CancellationToken).Symbol is IMethodSymbol { Name: "FromResult" or "FromException" or "FromCanceled" } method
                           && IsTasksType(method.ContainingType);
                case BaseObjectCreationExpressionSyntax creation:
                    return SemanticModel.GetTypeInfo(creation, CancellationToken).Type is { Name: "ValueTask" } type && IsTasksType(type);
                default:
                    return false;
            }
        }

        private bool IsTaskCall(ExpressionSyntax expression) =>
            IsCall(expression) && IsTaskLike(SemanticModel.GetTypeInfo(expression, CancellationToken).Type) && !IsFinishedFactory(expression);

        private void Report(ExpressionSyntax expression, string name, ILocalSymbol resource) =>
            _context.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation(), name, resource.Name));

        private void ReportIfBound(ExpressionSyntax expression)
        {
            var candidates = new List<ExpressionSyntax>();
            CollectCandidates(expression, candidates);
            foreach (var candidate in candidates)
            {
                if (candidate is IdentifierNameSyntax identifier
                    && SemanticModel.GetSymbolInfo(identifier, CancellationToken).Symbol is { } symbol
                    && _tainted.TryGetValue(symbol, out var taintedResource))
                {
                    Report(candidate, identifier.Identifier.ValueText, taintedResource);
                }
                else if (IsTaskCall(candidate) && TryFindBound(candidate, out var resource))
                {
                    Report(candidate, GetCallName(candidate), resource);
                }
            }
        }

        private void Track(ILocalSymbol? local, ExpressionSyntax value)
        {
            if (local == null || (_references.TryGetValue(local, out var self) && SymbolEqualityComparer.Default.Equals(self, local)))
                return;

            var expression = Unwrap(value);
            if (TryGetChainRoot(expression, out var root) && (expression is IdentifierNameSyntax || CanCarryResource(SemanticModel.GetTypeInfo(expression, CancellationToken).Type)))
            {
                _references[local] = root;
                _ = _tainted.Remove(local);
                return;
            }

            _ = _references.Remove(local);
            _ = _tainted.Remove(local);
            var candidates = new List<ExpressionSyntax>();
            CollectCandidates(expression, candidates);
            foreach (var candidate in candidates)
            {
                if (candidate is IdentifierNameSyntax identifier
                    && SemanticModel.GetSymbolInfo(identifier, CancellationToken).Symbol is { } symbol
                    && _tainted.TryGetValue(symbol, out var copied))
                {
                    _tainted[local] = copied;
                    return;
                }

                if (!IsCall(candidate) || IsFinishedFactory(candidate) || !TryFindBound(candidate, out var resource))
                    continue;

                if (IsTaskCall(candidate))
                {
                    _tainted[local] = resource;
                    return;
                }

                if (CanCarryResource(local.Type))
                {
                    _references[local] = resource;
                    return;
                }
            }
        }

        private bool TryFindBound(ExpressionSyntax call, out ILocalSymbol resource)
        {
            ILocalSymbol? best = null;
            if (call is InvocationExpressionSyntax invocation)
            {
                var root = GetChainRoot(invocation.Expression);
                if (root is MemberBindingExpressionSyntax && GetConditionalReceiver(call) is { } receiver)
                    root = GetChainRoot(receiver);

                if (TryResolve(root, out var receiverResource))
                    Consider(receiverResource, ref best);
                else if (invocation.Expression is MemberAccessExpressionSyntax member)
                    Search(member.Expression, ref best);
            }

            var arguments = call is InvocationExpressionSyntax invoked ? invoked.ArgumentList : ((BaseObjectCreationExpressionSyntax)call).ArgumentList;
            if (arguments != null)
            {
                foreach (var argument in arguments.Arguments)
                    Search(argument.Expression, ref best);
            }

            resource = best!;
            return best != null;
        }

        private void Search(SyntaxNode node, ref ILocalSymbol? best)
        {
            if (node is LocalFunctionStatementSyntax)
                return;

            if (node is AnonymousFunctionExpressionSyntax)
            {
                foreach (var inner in node.DescendantNodes())
                {
                    if (TryResolve(inner, out var captured))
                        Consider(captured, ref best);
                }

                return;
            }

            if (node is IdentifierNameSyntax)
            {
                if (TryResolve(node, out var identified))
                    Consider(identified, ref best);

                return;
            }

            if ((IsChain(node) || IsCall(node)) && node is ExpressionSyntax expression)
            {
                if (IsFinishedFactory(node) || !CanCarryResource(SemanticModel.GetTypeInfo(expression, CancellationToken).Type))
                    return;

                if (IsChain(node) && TryResolve(GetChainRoot(node), out var rooted))
                {
                    Consider(rooted, ref best);
                    return;
                }
            }

            foreach (var child in node.ChildNodes())
                Search(child, ref best);
        }

        private bool TryResolve(SyntaxNode node, out ILocalSymbol resource)
        {
            resource = null!;
            if (node is not IdentifierNameSyntax)
                return false;

            var symbol = SemanticModel.GetSymbolInfo(node, CancellationToken).Symbol;
            return symbol != null && (_references.TryGetValue(symbol, out resource!) || _tainted.TryGetValue(symbol, out resource!));
        }

        private bool TryGetChainRoot(ExpressionSyntax expression, out ILocalSymbol resource)
        {
            resource = null!;
            while (expression is MemberAccessExpressionSyntax member)
                expression = Unwrap(member.Expression);

            if (expression is not IdentifierNameSyntax identifier)
                return false;

            return SemanticModel.GetSymbolInfo(identifier, CancellationToken).Symbol is { } symbol && _references.TryGetValue(symbol, out resource!);
        }

        private void Visit(SyntaxNode node)
        {
            switch (node)
            {
                case UsingStatementSyntax usingStatement:
                    AddNestedResources(usingStatement);
                    break;
                case LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } usingDeclaration:
                    AddNestedResources(usingDeclaration);
                    break;
                case VariableDeclaratorSyntax { Initializer: { } initializer } declarator:
                    Track(SemanticModel.GetDeclaredSymbol(declarator, CancellationToken) as ILocalSymbol, initializer.Value);
                    break;
                case AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment:
                    Track(SemanticModel.GetSymbolInfo(assignment.Left, CancellationToken).Symbol as ILocalSymbol, assignment.Right);
                    break;
                case AwaitExpressionSyntax awaitExpression:
                    ClearAwaited(awaitExpression.Expression);
                    break;
                case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" or "Wait" or "GetAwaiter" } blocking:
                    ClearAwaited(blocking.Expression);
                    break;
                case ExpressionStatementSyntax statement:
                    VisitExpressionStatement(statement);
                    break;
                case ReturnStatementSyntax { Expression: { } returned }:
                    ReportIfBound(returned);
                    break;
            }
        }

        private void VisitExpressionStatement(ExpressionStatementSyntax statement)
        {
            var expression = Unwrap(statement.Expression);
            if (expression is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment
                && SemanticModel.GetSymbolInfo(assignment.Left, CancellationToken).Symbol is IDiscardSymbol)
            {
                expression = Unwrap(assignment.Right);
            }

            ReportIfBound(expression);
        }
    }
}
