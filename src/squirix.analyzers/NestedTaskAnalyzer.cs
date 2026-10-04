using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a nested task, a <c>Task&lt;X&gt;</c> or <c>ValueTask&lt;X&gt;</c> whose result type <c>X</c> is itself a task, when the
/// inner task is dropped (SQR0031). Awaiting the outer task completes when the inner task has merely started, so an
/// awaited result that is discarded, or an implicit conversion of the nested task to <c>Task</c>, loses the inner
/// task and any failure it carries.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedTaskAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0031";
    private const int MaxOriginDepth = 8;

    private static readonly LocalizableString Description = "A task whose result is another task completes when the inner task has started, not when it has finished. " +
                                                            "Awaiting it and discarding the result, or converting it to Task, never waits for the inner task and hides its exceptions. " +
                                                            "Call Unwrap(), use Task.Run, or await the result again.";

    private static readonly LocalizableString MessageFormat = "'{0}' produces a task whose result is another task; the inner task is not awaited";
    private static readonly LocalizableString Title = "Unwrap nested tasks before awaiting or returning them";
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
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var compilation = context.Compilation;
        var task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        var taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        if (task == null || taskOfT == null)
            return;

        var types = new TaskTypes(task, taskOfT, compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask"),
            compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"));
        context.RegisterOperationAction(operationContext => AnalyzeAwait(operationContext, types), OperationKind.Await);
        context.RegisterOperationAction(operationContext => AnalyzeConversion(operationContext, types), OperationKind.Conversion);
        context.RegisterOperationAction(operationContext => AnalyzeWait(operationContext, types), OperationKind.Invocation);
        context.RegisterOperationAction(operationContext => AnalyzeMethodGroup(operationContext, types), OperationKind.DelegateCreation);
    }

    private static void AnalyzeAwait(OperationAnalysisContext context, TaskTypes types)
    {
        var awaitOperation = (IAwaitOperation)context.Operation;
        if (!IsResultDiscarded(awaitOperation))
            return;

        var operand = awaitOperation.Operation;
        if (operand is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } instance })
            operand = instance;

        if (types.IsNestedTask(operand.Type) && !IsWhenAnyOrigin(operand, types, context.CancellationToken, 0))
            Report(context, operand);
    }

    private static void AnalyzeConversion(OperationAnalysisContext context, TaskTypes types)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (!conversion.IsImplicit || !SymbolEqualityComparer.Default.Equals(conversion.Type, types.Task))
            return;

        var operand = conversion.Operand;
        if (operand.Type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, types.TaskOfT) && types.IsNestedTask(named) && !IsWhenAnyOrigin(operand, types, context.CancellationToken, 0))
            Report(context, operand);
    }

    private static bool IsWhenAnyOrigin(IOperation operation, TaskTypes types, CancellationToken cancellationToken, int depth)
    {
        if (depth > MaxOriginDepth)
            return false;

        switch (operation)
        {
            case IInvocationOperation { TargetMethod.Name: "WhenAny" } invocation:
                return SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, types.Task);
            case IInvocationOperation { TargetMethod.Name: "ConfigureAwait" or "WaitAsync", Instance: { } instance }:
                return IsWhenAnyOrigin(instance, types, cancellationToken, depth + 1);
            case IConversionOperation conversion:
                return IsWhenAnyOrigin(conversion.Operand, types, cancellationToken, depth + 1);
            case IConditionalOperation conditional:
                return IsWhenAnyOriginOrNull(conditional.WhenTrue, types, cancellationToken, depth + 1)
                       && conditional.WhenFalse != null
                       && IsWhenAnyOriginOrNull(conditional.WhenFalse, types, cancellationToken, depth + 1);
            case ICoalesceOperation coalesce:
                return IsWhenAnyOriginOrNull(coalesce.Value, types, cancellationToken, depth + 1)
                       && IsWhenAnyOriginOrNull(coalesce.WhenNull, types, cancellationToken, depth + 1);
            case ILocalReferenceOperation { Local.DeclaringSyntaxReferences.Length: > 0 } reference:
                return operation.SemanticModel != null && AreAllAssignmentsWhenAnyOrigin(reference.Local, operation.SemanticModel, types, cancellationToken, depth + 1);
            default:
                return false;
        }
    }

    private static bool IsWhenAnyOriginOrNull(IOperation operation, TaskTypes types, CancellationToken cancellationToken, int depth)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;

        if (operation is IDefaultValueOperation || operation is { ConstantValue: { HasValue: true, Value: null } })
            return true;

        return IsWhenAnyOrigin(operation, types, cancellationToken, depth);
    }

    private static bool AreAllAssignmentsWhenAnyOrigin(ILocalSymbol local, SemanticModel semanticModel, TaskTypes types, CancellationToken cancellationToken, int depth)
    {
        if (depth > MaxOriginDepth)
            return false;

        if (local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declarator)
            return false;

        if (declarator.Initializer is { } initializer && !IsWhenAnyValue(initializer.Value, semanticModel, types, cancellationToken, depth))
            return false;

        SyntaxNode? scope = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (scope is GlobalStatementSyntax)
            scope = scope.Parent;

        if (scope == null)
            return declarator.Initializer != null;

        var hasValue = declarator.Initializer != null;
        foreach (var node in scope.DescendantNodes())
        {
            if (node is ArgumentSyntax { RefKindKeyword.RawKind: not 0 } argument
                && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol, local))
                return false;

            if (node is AssignmentExpressionSyntax { Left: TupleExpressionSyntax tuple } && TupleTargetsLocal(tuple, local, semanticModel, cancellationToken))
                return false;

            if (node is not AssignmentExpressionSyntax assignment || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                continue;

            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, local))
                continue;

            if (!IsWhenAnyValue(assignment.Right, semanticModel, types, cancellationToken, depth))
                return false;

            hasValue = true;
        }

        return hasValue;
    }

    private static bool TupleTargetsLocal(TupleExpressionSyntax tuple, ILocalSymbol local, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var node in tuple.DescendantNodes())
        {
            if (node is IdentifierNameSyntax && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(node, cancellationToken).Symbol, local))
                return true;
        }

        return false;
    }

    private static bool IsWhenAnyValue(ExpressionSyntax value, SemanticModel semanticModel, TaskTypes types, CancellationToken cancellationToken, int depth)
    {
        var operation = semanticModel.GetOperation(value, cancellationToken);
        return operation != null && IsWhenAnyOriginOrNull(operation, types, cancellationToken, depth + 1);
    }

    private static void AnalyzeWait(OperationAnalysisContext context, TaskTypes types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation is not { TargetMethod.Name: "Wait", Instance: { } instance } || !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, types.Task))
            return;

        if (types.IsNestedTask(instance.Type) && !IsWhenAnyOrigin(instance, types, context.CancellationToken, 0))
            Report(context, instance);
    }

    private static void AnalyzeMethodGroup(OperationAnalysisContext context, TaskTypes types)
    {
        var creation = (IDelegateCreationOperation)context.Operation;
        if (creation.Target is not IMethodReferenceOperation reference)
            return;

        if (creation.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } || !SymbolEqualityComparer.Default.Equals(invoke.ReturnType, types.Task))
            return;

        var returnType = reference.Method.ReturnType;
        if (returnType is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, types.TaskOfT) && types.IsNestedTask(named))
            Report(context, reference);
    }

    private static bool IsResultDiscarded(IAwaitOperation awaitOperation) => awaitOperation.Parent switch
    {
        IExpressionStatementOperation => true,
        ISimpleAssignmentOperation { Target: IDiscardOperation } => true,
        _ => false,
    };

    private static void Report(OperationAnalysisContext context, IOperation nested)
    {
        var name = nested is IInvocationOperation invocation ? invocation.TargetMethod.Name : nested.Syntax.ToString();
        context.ReportDiagnostic(Diagnostic.Create(Rule, nested.Syntax.GetLocation(), name));
    }

    private sealed class TaskTypes
    {
        private readonly INamedTypeSymbol? _valueTask;
        private readonly INamedTypeSymbol? _valueTaskOfT;

        public TaskTypes(INamedTypeSymbol task, INamedTypeSymbol taskOfT, INamedTypeSymbol? valueTask, INamedTypeSymbol? valueTaskOfT)
        {
            Task = task;
            TaskOfT = taskOfT;
            _valueTask = valueTask;
            _valueTaskOfT = valueTaskOfT;
        }

        public INamedTypeSymbol Task { get; }

        public INamedTypeSymbol TaskOfT { get; }

        public bool IsNestedTask(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol { TypeArguments.Length: 1 } named)
                return false;

            var definition = named.OriginalDefinition;
            if (!SymbolEqualityComparer.Default.Equals(definition, TaskOfT) && !SymbolEqualityComparer.Default.Equals(definition, _valueTaskOfT))
                return false;

            return IsTask(named.TypeArguments[0]);
        }

        private bool IsTask(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named)
                return false;

            var definition = named.OriginalDefinition;
            return SymbolEqualityComparer.Default.Equals(definition, Task)
                   || SymbolEqualityComparer.Default.Equals(definition, TaskOfT)
                   || SymbolEqualityComparer.Default.Equals(definition, _valueTask)
                   || SymbolEqualityComparer.Default.Equals(definition, _valueTaskOfT);
        }
    }
}
