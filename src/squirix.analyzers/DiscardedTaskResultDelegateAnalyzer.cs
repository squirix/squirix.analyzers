using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Reliability", DiagnosticSeverity.Warning, true, Description,
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
        context.RegisterOperationAction(AnalyzeDelegateCreation, OperationKind.DelegateCreation);
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
        context.RegisterOperationAction(AnalyzeCoalesce, OperationKind.Coalesce);
        context.RegisterOperationAction(AnalyzeDeconstruction, OperationKind.DeconstructionAssignment);
        context.RegisterOperationAction(AnalyzeLoop, OperationKind.Loop);
        context.RegisterOperationAction(AnalyzeSpread, OperationKind.Spread);
    }

    /// <summary>
    /// Reports the left operand of a null-coalescing expression when using it as the type of the whole expression
    /// loses a result. That conversion is part of the operation and has no node of its own.
    /// </summary>
    private static void AnalyzeCoalesce(OperationAnalysisContext context)
    {
        var coalesce = (ICoalesceOperation)context.Operation;
        if (FindLosses(null, coalesce.Value.Type, coalesce.Type) && !IsUnderExplicitCast(coalesce, coalesce.Type))
            _ = FindLosses(new LossSink(context, coalesce.Value.Syntax), coalesce.Value.Type, coalesce.Type);
    }

    /// <summary>Reports a delegate that returns Task&lt;T&gt; and is implicitly used as one that returns Task.</summary>
    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (!conversion.IsImplicit || !conversion.Conversion.Exists || !FindLosses(null, conversion.Operand.Type, conversion.Type))
            return;

        // Comparing two delegates, or two tuples of them, converts one side but passes it nowhere.
        if (conversion.Parent is ITupleBinaryOperation or IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals })
            return;

        if (!IsUnderExplicitCast(conversion, conversion.Type))
            _ = FindLosses(new LossSink(context, conversion.Syntax), conversion.Operand.Type, conversion.Type);
    }

    /// <summary>
    /// Reports a value taken apart into targets that lose a result, as in <c language="csharp">(Func&lt;Task&gt; work, int n) = pair</c>:
    /// a tuple, or a value with a Deconstruct method. The parts are converted one by one, and none of those conversions
    /// has a node. An element of a tuple literal that is converted where it stands already has the type of its target,
    /// so it is not reported a second time here.
    /// </summary>
    private static void AnalyzeDeconstruction(OperationAnalysisContext context)
    {
        var deconstruction = (IDeconstructionAssignmentOperation)context.Operation;
        if (!HoldsPlainTaskDelegate(deconstruction.Target.Type))
            return;

        if (deconstruction.Syntax is not AssignmentExpressionSyntax syntax || deconstruction.SemanticModel is not { } model)
            return;

        var sink = new LossSink(context, deconstruction.Value.Syntax);
        ReportDeconstruction(sink, model.GetDeconstructionInfo(syntax), deconstruction.Value.Type, deconstruction.Target);
    }

    private static void AnalyzeDelegateCreation(OperationAnalysisContext context)
    {
        var creation = (IDelegateCreationOperation)context.Operation;
        if (!TaskTypeFacts.ReturnsPlainTask(creation.Type, out var delegateType))
            return;

        switch (creation.Target)
        {
            case IMethodReferenceOperation reference:
                if (TaskTypeFacts.TryGetResultType(reference.Method.ReturnType, out var methodResult))
                    Report(context, reference.Syntax, delegateType, methodResult);

                break;
            case IAnonymousFunctionOperation { Symbol.IsAsync: false } function:
                ReportReturns(context, function.Body, delegateType);
                break;
            case { } target when TaskTypeFacts.TryGetDelegateResultType(target.Type, out var delegateResult):
                Report(context, target.Syntax, delegateType, delegateResult);
                break;
        }
    }

    /// <summary>
    /// Reports a foreach whose declared variable, or the targets it takes each element apart into, lose the result of the
    /// elements; that conversion has no node either.
    /// </summary>
    private static void AnalyzeLoop(OperationAnalysisContext context)
    {
        if (context.Operation is not IForEachLoopOperation { Syntax: CommonForEachStatementSyntax syntax } loop || loop.SemanticModel is not { } model)
            return;

        // A loop that takes each element apart follows the rules of a deconstruction, part by part.
        if (syntax is ForEachVariableStatementSyntax parts)
        {
            if (!HoldsPlainTaskDelegate(loop.LoopControlVariable.Type))
                return;

            var taken = model.GetForEachStatementInfo(syntax).ElementType;
            ReportDeconstruction(new LossSink(context, loop.Collection.Syntax), model.GetDeconstructionInfo(parts), taken, loop.LoopControlVariable);
            return;
        }

        // Only a variable declared as a Task-returning delegate, or as a tuple that may hold one, is worth binding the loop for.
        var written = loop.LoopControlVariable is IVariableDeclaratorOperation variable ? variable.Symbol.Type : loop.LoopControlVariable.Type;
        var declared = TaskTypeFacts.Unwrap(written);
        if (written == null || (declared is not INamedTypeSymbol { IsTupleType: true } && !TaskTypeFacts.ReturnsPlainTask(declared, out _)))
            return;

        var element = model.GetForEachStatementInfo(syntax).ElementType;
        if (element == null || !FindLosses(null, element, declared))
            return;

        // The loop records a tuple conversion as explicit even when an implicit one exists, so the pair is classified again.
        if (context.Compilation.ClassifyConversion(element, written) is { Exists: true, IsImplicit: true })
            _ = FindLosses(new LossSink(context, loop.Collection.Syntax), element, declared);
    }

    /// <summary>Reports a spread element whose items lose their result in the collection they are copied into.</summary>
    private static void AnalyzeSpread(OperationAnalysisContext context)
    {
        var spread = (ISpreadOperation)context.Operation;
        if (!spread.ElementConversion.Exists || !spread.ElementConversion.IsImplicit || spread.ElementConversion.IsIdentity)
            return;

        var item = TaskTypeFacts.GetItemType(spread.Parent?.Type);
        if (FindLosses(null, spread.ElementType, item) && !IsUnderExplicitCast(spread, item))
            _ = FindLosses(new LossSink(context, spread.Operand.Syntax), spread.ElementType, item);
    }

    /// <summary>Returns the type of one part of a value that is taken apart: an out parameter of its Deconstruct method, or a tuple element.</summary>
    private static ITypeSymbol? GetPartType(DeconstructionInfo info, ITypeSymbol? source, int index)
    {
        if (info.Method is not { } method)
            return source is INamedTypeSymbol { IsTupleType: true } tuple && index < tuple.TupleElements.Length ? tuple.TupleElements[index].Type : null;

        // An extension Deconstruct takes the value itself first; the parts are its out parameters, in order.
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind == RefKind.Out && index-- == 0)
                return parameter.Type;
        }

        return null;
    }

    /// <summary>
    /// Returns whether the type is a delegate that returns Task, or a tuple that holds one at any depth. Only such a
    /// target can lose a result, so anything else is not worth binding a deconstruction for.
    /// </summary>
    private static bool HoldsPlainTaskDelegate(ITypeSymbol? type)
    {
        type = TaskTypeFacts.Unwrap(type);
        if (TaskTypeFacts.ReturnsPlainTask(type, out _))
            return true;

        if (type is not INamedTypeSymbol { IsTupleType: true } tuple)
            return false;

        foreach (var element in tuple.TupleElements)
        {
            if (HoldsPlainTaskDelegate(element.Type))
                return true;
        }

        return false;
    }

    /// <summary>Returns whether two types are the same apart from tuple element names and nullable annotations.</summary>
    private static bool IsSameType(IOperation at, ITypeSymbol? left, ITypeSymbol? right)
    {
        left = TaskTypeFacts.Unwrap(left);
        right = TaskTypeFacts.Unwrap(right);
        return left != null && right != null && at.SemanticModel is { } model && model.Compilation.ClassifyConversion(left, right).IsIdentity;
    }

    private static string GetDisplayName(INamedTypeSymbol delegateType) =>
        delegateType.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    /// <summary>
    /// Returns whether an explicit cast to the type that loses the result covers the value: directly, or through the
    /// branches of conditional, switch and null-coalescing expressions. Such a cast states the intent for every branch
    /// under it. A cast to anything else, or a user-defined conversion, says nothing about the result. For an element
    /// of a tuple literal or of a collection expression, the type that loses the result is that of the whole literal.
    /// </summary>
    private static bool IsUnderExplicitCast(IOperation value, ITypeSymbol? lossy)
    {
        for (var current = value; current.Parent is { } parent; current = parent)
        {
            switch (parent)
            {
                case IConversionOperation conversion:
                    if (!conversion.IsImplicit)
                        return conversion.OperatorMethod == null && IsSameType(conversion, conversion.Type, lossy);

                    break;
                case ITupleOperation or ICollectionExpressionOperation:
                    // A cast over the literal covers the element only while the element still has the type that loses the result.
                    if (current is not ISpreadOperation && !IsSameType(current, current.Type, lossy))
                        return false;

                    lossy = parent.Type;
                    break;
                case IConditionalOperation conditional when conditional.Condition != current:
                case ISwitchExpressionArmOperation arm when arm.Value == current:
                case ISwitchExpressionOperation switchExpression when switchExpression.Value != current:
                case ICoalesceOperation or ISpreadOperation:
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports at the expression as written: with the checked and null-forgiving wrappers that have no operation of
    /// their own, and without the parentheses around the whole.
    /// </summary>
    private static void Report(in OperationAnalysisContext context, SyntaxNode lost, INamedTypeSymbol delegateType, string result)
    {
        while (lost.Parent is ParenthesizedExpressionSyntax or CheckedExpressionSyntax or PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
            lost = lost.Parent;

        while (lost is ParenthesizedExpressionSyntax parenthesized)
            lost = parenthesized.Expression;

        context.ReportDiagnostic(Diagnostic.Create(Rule, lost.GetLocation(), GetDisplayName(delegateType), result));
    }

    /// <summary>
    /// Reports the parts of a value that is taken apart into targets, where a part loses its result in its target. A part
    /// comes from an element of a tuple or from an out parameter of the Deconstruct method the compiler chose, and may be
    /// taken apart further. A part that goes to a discard is not converted for anyone, whatever type is written for it.
    /// </summary>
    private static void ReportDeconstruction(LossSink sink, DeconstructionInfo info, ITypeSymbol? source, IOperation target)
    {
        if (target is IDeclarationExpressionOperation declaration)
            target = declaration.Expression;

        // The property builds a new array on every read.
        var nested = info.Nested;
        switch (target)
        {
            case IDiscardOperation:
                return;
            case ITupleOperation targets when targets.Elements.Length == nested.Length:
                for (var index = 0; index < nested.Length; index++)
                    ReportDeconstruction(sink, nested[index], GetPartType(info, source, index), targets.Elements[index]);

                return;
            default:
                _ = FindLosses(sink, source, target.Type);
                return;
        }
    }

    /// <summary>
    /// Reports where a returned value turns from Task&lt;T&gt; into Task. The compiler converts either the whole value
    /// or, when its branches have different types, each branch on its own, so the search follows the value into the
    /// branches of conditional, switch and null-coalescing expressions, and through a conversion that only gives such
    /// an expression its Task type. An explicit cast states the intent and ends it.
    /// </summary>
    private static void ReportLostResults(in OperationAnalysisContext context, IOperation? value, INamedTypeSymbol delegateType)
    {
        switch (value)
        {
            case IConversionOperation { IsImplicit: true } conversion when TaskTypeFacts.IsPlainTask(conversion.Type):
                if (TaskTypeFacts.TryGetResultType(conversion.Operand.Type, out var result))
                    Report(context, conversion.Syntax, delegateType, result);
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
                if (TaskTypeFacts.IsPlainTask(coalesce.Type) && TaskTypeFacts.TryGetResultType(coalesce.Value.Type, out var left))
                    Report(context, coalesce.Value.Syntax, delegateType, left);

                ReportLostResults(context, coalesce.WhenNull, delegateType);
                break;
        }
    }

    /// <summary>
    /// Visits every return of the function itself; nested lambdas and local functions have their own returns. The walk
    /// keeps its own stack, because an operation tree can be deeper than the call stack allows.
    /// </summary>
    private static void ReportReturns(in OperationAnalysisContext context, IOperation body, INamedTypeSymbol delegateType)
    {
        var pending = new Stack<IOperation>();
        pending.Push(body);
        while (pending.Count > 0)
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

    /// <summary>
    /// Returns whether a value of the source type, used as the target type, loses a result: a delegate that returns
    /// Task&lt;T&gt; used as one that returns Task, or a tuple with such elements. With a sink given, every loss is reported
    /// to it, one for each element of a tuple that loses its result.
    /// </summary>
    private static bool FindLosses(LossSink? sink, ITypeSymbol? source, ITypeSymbol? target)
    {
        source = TaskTypeFacts.Unwrap(source);
        target = TaskTypeFacts.Unwrap(target);
        if (TaskTypeFacts.ReturnsPlainTask(target, out var delegateType))
        {
            if (!TaskTypeFacts.TryGetDelegateResultType(source, out var result))
                return false;

            sink?.Add(delegateType, result);
            return true;
        }

        if (source is not INamedTypeSymbol { IsTupleType: true } sourceTuple || target is not INamedTypeSymbol { IsTupleType: true } targetTuple)
            return false;

        if (sourceTuple.TupleElements.Length != targetTuple.TupleElements.Length)
            return false;

        var found = false;
        for (var index = 0; index < sourceTuple.TupleElements.Length; index++)
        {
            if (!FindLosses(sink, sourceTuple.TupleElements[index].Type, targetTuple.TupleElements[index].Type))
                continue;

            // Without a sink the answer is enough; with one, the remaining elements still have to be reported.
            if (sink == null)
                return true;

            found = true;
        }

        return found;
    }

    /// <summary>
    /// Collects the losses found for one expression and reports each once: two elements of a tuple that lose the same
    /// result to the same delegate would otherwise give two diagnostics with the same text at one place. Losses are
    /// told apart by that text, the delegate name and the result as the message shows them.
    /// </summary>
    private sealed class LossSink
    {
        private readonly OperationAnalysisContext _context;
        private readonly SyntaxNode _place;
        private List<(string DelegateName, string Result)>? _reported;

        public LossSink(in OperationAnalysisContext context, SyntaxNode place)
        {
            _context = context;
            _place = place;
        }

        public void Add(INamedTypeSymbol delegateType, string result)
        {
            var name = GetDisplayName(delegateType);
            _reported ??= [];
            foreach (var (reportedName, reportedResult) in _reported)
            {
                if (reportedName == name && reportedResult == result)
                    return;
            }

            _reported.Add((name, result));
            Report(_context, _place, delegateType, result);
        }
    }
}
