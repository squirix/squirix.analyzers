using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
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
            case ILocalReferenceOperation { Local.DeclaringSyntaxReferences.Length: > 0 } reference:
                if (operation.SemanticModel == null)
                    return false;

                if (reference.Local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax { Initializer: { } initializer })
                    return false;

                var initialValue = operation.SemanticModel.GetOperation(initializer.Value, cancellationToken);
                return initialValue != null && IsWhenAnyOrigin(initialValue, types, cancellationToken, depth + 1);
            default:
                return false;
        }
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
