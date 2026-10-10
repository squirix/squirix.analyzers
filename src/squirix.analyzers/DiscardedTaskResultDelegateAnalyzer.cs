using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a <c>Task&lt;T&gt;</c> that silently becomes a non-generic <c>Task</c> on its way out of a delegate (SQR0032):
/// in a value returned by a non-async lambda or anonymous method, in a method group, and in a delegate that returns
/// <c>Task&lt;T&gt;</c> used as one that returns <c>Task</c>. The <c>T</c> result is lost, and the compiler rejects
/// this only for async lambdas.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiscardedTaskResultDelegateAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0032";

    private static readonly LocalizableString Description = "Task<T> converts implicitly to Task, so a delegate that returns Task drops the result of a Task<T> it returns. " +
                                                            "Use a delegate that returns Task<T>, or discard the result explicitly.";

    private static readonly LocalizableString MessageFormat = "Delegate '{0}' returns Task, so the {1} result of this Task<{1}> is discarded";
    private static readonly LocalizableString Title = "Do not convert a Task<T> to Task in a delegate return";
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
        context.RegisterOperationAction(AnalyzeDelegateCreation, OperationKind.DelegateCreation);
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
        context.RegisterOperationAction(AnalyzeCoalesce, OperationKind.Coalesce);
    }

    /// <summary>
    /// Reports the left operand of a null-coalescing expression when it is a delegate that returns Task&lt;T&gt; and the
    /// expression is one that returns Task. That conversion is part of the operation and has no node of its own.
    /// </summary>
    private static void AnalyzeCoalesce(OperationAnalysisContext context)
    {
        var coalesce = (ICoalesceOperation)context.Operation;
        if (ReturnsPlainTask(coalesce.Type, out var delegateType) && TryGetDelegateResultType(coalesce.Value.Type, out var result))
            Report(context, coalesce.Value, delegateType, result);
    }

    /// <summary>Reports a delegate that returns Task&lt;T&gt; and is implicitly used as one that returns Task.</summary>
    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (!conversion.IsImplicit || !conversion.Conversion.Exists || !ReturnsPlainTask(conversion.Type, out var delegateType))
            return;

        // Comparing two delegates converts one of them, but passes it nowhere.
        if (conversion.Parent is IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals })
            return;

        if (TryGetDelegateResultType(conversion.Operand.Type, out var result))
            Report(context, conversion.Operand, delegateType, result);
    }

    private static void AnalyzeDelegateCreation(OperationAnalysisContext context)
    {
        var creation = (IDelegateCreationOperation)context.Operation;
        if (!ReturnsPlainTask(creation.Type, out var delegateType))
            return;

        switch (creation.Target)
        {
            case IMethodReferenceOperation reference:
                if (TryGetResultType(reference.Method.ReturnType, out var methodResult))
                    Report(context, reference, delegateType, methodResult);

                break;
            case IAnonymousFunctionOperation { Symbol.IsAsync: false } function:
                ReportReturns(context, function.Body, delegateType);
                break;
            case { } target when TryGetDelegateResultType(target.Type, out var delegateResult):
                Report(context, target, delegateType, delegateResult);
                break;
        }
    }

    private static bool IsNamed(ITypeSymbol type, string name, bool generic) =>
        type is INamedTypeSymbol { ContainingNamespace: { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } } named
        && named.Name == name
        && named.IsGenericType == generic;

    private static bool IsPlainTask(ITypeSymbol? type) => type != null && IsNamed(type, "Task", false);

    private static bool IsTaskLike(ITypeSymbol type) => IsNamed(type, "Task", false) || IsNamed(type, "Task", true) || IsNamed(type, "ValueTask", false) || IsNamed(type, "ValueTask", true);

    private static void Report(OperationAnalysisContext context, IOperation lost, INamedTypeSymbol delegateType, string result) =>
        context.ReportDiagnostic(Diagnostic.Create(Rule, lost.Syntax.GetLocation(), delegateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), result));

    /// <summary>
    /// Reports where a returned value turns from Task&lt;T&gt; into Task. The compiler converts either the whole value
    /// or, when its branches have different types, each branch on its own, so the search follows the value into the
    /// branches of conditional, switch and null-coalescing expressions, and through a conversion that only gives such
    /// an expression its Task type. An explicit cast states the intent and ends it.
    /// </summary>
    private static void ReportLostResults(OperationAnalysisContext context, IOperation? value, INamedTypeSymbol delegateType)
    {
        switch (value)
        {
            case IConversionOperation { IsImplicit: true } conversion when IsPlainTask(conversion.Type):
                if (TryGetResultType(conversion.Operand.Type, out var result))
                    Report(context, conversion.Operand, delegateType, result);
                else
                    ReportLostResults(context, conversion.Operand, delegateType);

                break;
            case IConditionalOperation conditional:
                ReportLostResults(context, conditional.WhenTrue, delegateType);
                ReportLostResults(context, conditional.WhenFalse, delegateType);
                break;
            case ISwitchExpressionOperation switchExpression:
                foreach (var arm in switchExpression.Arms)
                    ReportLostResults(context, arm.Value, delegateType);

                break;
            case ICoalesceOperation coalesce:
                // The conversion of the left operand is part of the operation and has no node of its own.
                if (IsPlainTask(coalesce.Type) && TryGetResultType(coalesce.Value.Type, out var left))
                    Report(context, coalesce.Value, delegateType, left);

                ReportLostResults(context, coalesce.WhenNull, delegateType);
                break;
        }
    }

    /// <summary>
    /// Visits every return of the function itself; nested lambdas and local functions have their own returns. The walk
    /// keeps its own stack, because an operation tree can be deeper than the call stack allows.
    /// </summary>
    private static void ReportReturns(OperationAnalysisContext context, IOperation body, INamedTypeSymbol delegateType)
    {
        var pending = new Stack<IOperation>();
        pending.Push(body);
        while (pending.Count > 0)
        {
            foreach (var child in pending.Pop().ChildOperations)
            {
                switch (child)
                {
                    case IAnonymousFunctionOperation or ILocalFunctionOperation:
                        break;
                    case IReturnOperation returned:
                        ReportLostResults(context, returned.ReturnedValue, delegateType);
                        break;
                    default:
                        pending.Push(child);
                        break;
                }
            }
        }
    }

    private static bool ReturnsPlainTask(ITypeSymbol? type, out INamedTypeSymbol delegateType)
    {
        delegateType = null!;
        if (type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } named || !IsPlainTask(invoke.ReturnType))
            return false;

        delegateType = named;
        return true;
    }

    private static bool TryGetDelegateResultType(ITypeSymbol? type, out string result)
    {
        result = string.Empty;
        return type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke } && TryGetResultType(invoke.ReturnType, out result);
    }

    private static bool TryGetResultType(ITypeSymbol? type, out string result)
    {
        result = string.Empty;
        var current = type;
        while (current != null && !IsNamed(current, "Task", true))
            current = current.BaseType;

        if (current == null)
            return false;

        var argument = ((INamedTypeSymbol)current).TypeArguments[0];
        if (IsTaskLike(argument))
            return false;

        result = argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return true;
    }
}
