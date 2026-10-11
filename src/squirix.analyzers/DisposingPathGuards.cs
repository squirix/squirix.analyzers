using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Decides whether a field dereference in <c>Dispose(bool)</c> runs only on a path that the finalizer of a half-built
/// object cannot reach (SQR0033): inside a branch where <c>disposing</c> is true, where the field is not null, or
/// where a value-type field that a constructor sets to a non-default value has been checked. An event or a local that
/// holds the value of the field can stand in for the field in a null check.
/// </summary>
internal sealed class DisposingPathGuards
{
    private readonly CancellationToken _cancellationToken;

    private readonly HashSet<string> _constructorAssignedFields;

    private readonly IParameterSymbol _disposing;

    private readonly INamedTypeSymbol _type;

    internal DisposingPathGuards(IParameterSymbol disposing, INamedTypeSymbol type, HashSet<string> constructorAssignedFields, CancellationToken cancellationToken)
    {
        _disposing = disposing;
        _type = type;
        _constructorAssignedFields = constructorAssignedFields;
        _cancellationToken = cancellationToken;
    }

    internal static bool HasInitializer(IFieldSymbol field, CancellationToken cancellationToken)
    {
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer: not null })
                return true;
        }

        return false;
    }

    internal static IOperation Unwrap(IOperation operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation { Conversion.IsUserDefined: false })
        {
            operation = operation is IParenthesizedOperation parenthesized ? parenthesized.Operand : ((IConversionOperation)operation).Operand;
        }

        return operation;
    }

    /// <summary>
    /// Returns whether <paramref name="dereferenced" /> sits on a path that the finalizer of a half-built object cannot reach. The
    /// <paramref name="subject" /> is what a null check has to name to count as a guard: the field, the event or a local copy of the field.
    /// </summary>
    internal bool IsGuarded(IOperation dereferenced, ISymbol subject)
    {
        var child = dereferenced;
        for (var parent = dereferenced.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    // A lambda or local function runs when it is called, so the enclosing branch does not tell which path it is on.
                    return true;
                case INameOfOperation:
                    // nameof does not evaluate its argument.
                    return true;
                case IConditionalOperation conditional when (child == conditional.WhenTrue && Implies(conditional.Condition, true, subject))
                                                            || (child == conditional.WhenFalse && Implies(conditional.Condition, false, subject)):
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction
                    when child == conjunction.RightOperand && Implies(conjunction.LeftOperand, true, subject):
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } disjunction
                    when child == disjunction.RightOperand && Implies(disjunction.LeftOperand, false, subject):
                case IBlockOperation block when HasExitGuardBefore(block, child, subject):
                    return true;
            }
        }

        return false;
    }

    private static bool AlwaysExits(IOperation? operation) => operation switch
    {
        IReturnOperation or IThrowOperation or IExpressionStatementOperation { Operation: IThrowOperation } => true,
        IBlockOperation { Operations.Length: > 0 } block => AlwaysExits(block.Operations[block.Operations.Length - 1]),
        _ => false,
    };

    private static bool IsBoolConstant(IOperation operation, out bool constant)
    {
        if (Unwrap(operation).ConstantValue is { HasValue: true, Value: bool value })
        {
            constant = value;
            return true;
        }

        constant = false;
        return false;
    }

    private static bool IsNullConstant(IOperation operation) => Unwrap(operation).ConstantValue is { HasValue: true, Value: null };

    private static bool IsSame(ISymbol symbol, ISymbol subject) => SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, subject.OriginalDefinition);

    private static bool IsSubject(IOperation operation, ISymbol subject) => Unwrap(operation) switch
    {
        IFieldReferenceOperation { Instance: IInstanceReferenceOperation } reference => IsSame(reference.Field, subject),
        IEventReferenceOperation { Instance: IInstanceReferenceOperation } reference => IsSame(reference.Event, subject),
        ILocalReferenceOperation reference => IsSame(reference.Local, subject),
        _ => false,
    };

    /// <summary>Returns whether <paramref name="pattern" /> matches a null value, or <see langword="null" /> when the pattern shape is not recognized.</summary>
    private static bool? MatchesNull(IPatternOperation pattern) => pattern switch
    {
        IConstantPatternOperation constant => IsNullConstant(constant.Value),
        INegatedPatternOperation negated => !MatchesNull(negated.Pattern),
        IDeclarationPatternOperation declaration => declaration.MatchesNull,
        IDiscardPatternOperation => true,
        IRecursivePatternOperation or ITypePatternOperation or IRelationalPatternOperation => false,
        IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.Or } either => MatchesNull(either.LeftPattern) | MatchesNull(either.RightPattern),
        IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.And } both => MatchesNull(both.LeftPattern) & MatchesNull(both.RightPattern),
        _ => null,
    };

    private bool HasExitGuardBefore(IBlockOperation block, IOperation statement, ISymbol subject)
    {
        foreach (var operation in block.Operations)
        {
            if (operation == statement)
                return false;

            if (operation is IConditionalOperation conditional
                && ((AlwaysExits(conditional.WhenTrue) && Implies(conditional.Condition, false, subject))
                    || (AlwaysExits(conditional.WhenFalse) && Implies(conditional.Condition, true, subject))))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="condition" /> evaluating to <paramref name="value" /> shows the object is being disposed explicitly or
    /// <paramref name="subject" /> is set.
    /// </summary>
    private bool Implies(IOperation condition, bool value, ISymbol subject)
    {
        condition = Unwrap(condition);
        return condition switch
        {
            IParameterReferenceOperation parameter when IsDisposing(parameter) => value,
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } negation => Implies(negation.Operand, !value, subject),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction => value
                ? Implies(conjunction.LeftOperand, true, subject) || Implies(conjunction.RightOperand, true, subject)
                : Implies(conjunction.LeftOperand, false, subject) && Implies(conjunction.RightOperand, false, subject),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } disjunction => value
                ? Implies(disjunction.LeftOperand, true, subject) && Implies(disjunction.RightOperand, true, subject)
                : Implies(disjunction.LeftOperand, false, subject) || Implies(disjunction.RightOperand, false, subject),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison
                when (IsSubject(comparison.LeftOperand, subject) && IsNullConstant(comparison.RightOperand))
                     || (IsSubject(comparison.RightOperand, subject) && IsNullConstant(comparison.LeftOperand))
                => value == (comparison.OperatorKind == BinaryOperatorKind.NotEquals),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison
                when IsDisposingComparedToConstant(comparison.LeftOperand, comparison.RightOperand, out var constant)
                     || IsDisposingComparedToConstant(comparison.RightOperand, comparison.LeftOperand, out constant)
                => constant == (value == (comparison.OperatorKind == BinaryOperatorKind.Equals)),
            IIsPatternOperation { Pattern: IConstantPatternOperation constantPattern } pattern
                when IsDisposing(pattern.Value) && IsBoolConstant(constantPattern.Value, out var constant) => constant == value,
            IIsPatternOperation { Pattern: INegatedPatternOperation { Pattern: IConstantPatternOperation constantPattern } } pattern
                when IsDisposing(pattern.Value) && IsBoolConstant(constantPattern.Value, out var constant) => constant != value,
            IIsPatternOperation pattern when IsSubject(pattern.Value, subject) => MatchesNull(pattern.Pattern) is { } matchesNull && value != matchesNull,
            _ => ReadsConstructorFlag(condition),
        };
    }

    private bool IsDisposing(IOperation operation) =>
        Unwrap(operation) is IParameterReferenceOperation parameter && SymbolEqualityComparer.Default.Equals(parameter.Parameter, _disposing);

    private bool IsDisposingComparedToConstant(IOperation operand, IOperation other, out bool constant)
    {
        constant = false;
        return IsDisposing(operand) && IsBoolConstant(other, out constant);
    }

    /// <summary>
    /// Returns whether <paramref name="condition" /> reads a value-type field of the type that a constructor sets to a non-default value and
    /// that has no initializer: such a flag still has its default value in an object whose constructor body did not run, so the check tells
    /// the two apart. The direction of the check is not verified.
    /// </summary>
    private bool ReadsConstructorFlag(IOperation condition)
    {
        foreach (var operation in condition.DescendantsAndSelf())
        {
            if (operation is IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: { IsStatic: false, Type.IsValueType: true } field }
                && SymbolEqualityComparer.Default.Equals(field.ContainingType.OriginalDefinition, _type.OriginalDefinition)
                && _constructorAssignedFields.Contains(field.Name)
                && !HasInitializer(field, _cancellationToken))
                return true;
        }

        return false;
    }
}
