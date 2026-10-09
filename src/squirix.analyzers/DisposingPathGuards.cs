using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Decides whether a field dereference in <c>Dispose(bool)</c> runs only on a path that the finalizer of a half-built
/// object cannot reach (SQR0033): inside a branch where <c>disposing</c> is true, where the field is not null, or
/// where a field that a constructor assigns has been checked.
/// </summary>
internal sealed class DisposingPathGuards
{
    private readonly HashSet<string> _constructorAssignedFields;

    private readonly IParameterSymbol _disposing;

    private readonly INamedTypeSymbol _type;

    internal DisposingPathGuards(IParameterSymbol disposing, INamedTypeSymbol type, HashSet<string> constructorAssignedFields)
    {
        _disposing = disposing;
        _type = type;
        _constructorAssignedFields = constructorAssignedFields;
    }

    internal static bool HasInitializer(IFieldSymbol field)
    {
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null })
                return true;
        }

        return false;
    }

    internal bool IsGuarded(IFieldReferenceOperation reference)
    {
        IOperation child = reference;
        for (var parent = reference.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    // A lambda or local function runs when it is called, so the enclosing branch does not tell which path it is on.
                    return true;
                case IConditionalOperation conditional when (child == conditional.WhenTrue && Implies(conditional.Condition, true, reference.Field))
                                                            || (child == conditional.WhenFalse && Implies(conditional.Condition, false, reference.Field)):
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction when child == conjunction.RightOperand && Implies(conjunction.LeftOperand, true, reference.Field):
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } disjunction when child == disjunction.RightOperand && Implies(disjunction.LeftOperand, false, reference.Field):
                case IBlockOperation block when HasExitGuardBefore(block, child, reference.Field):
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

    private static bool IsNullConstant(IOperation operation) => Unwrap(operation).ConstantValue is { HasValue: true, Value: null };

    private static bool IsSameField(IOperation operation, IFieldSymbol field) =>
        Unwrap(operation) is IFieldReferenceOperation { Instance: IInstanceReferenceOperation } reference
        && SymbolEqualityComparer.Default.Equals(reference.Field.OriginalDefinition, field.OriginalDefinition);

    /// <summary>Returns whether <paramref name="pattern" /> matches a null value; only the null-related pattern shapes are distinguished.</summary>
    private static bool MatchesNull(IPatternOperation pattern) => pattern switch
    {
        IConstantPatternOperation constant => IsNullConstant(constant.Value),
        INegatedPatternOperation negated => !MatchesNull(negated.Pattern),
        IDeclarationPatternOperation declaration => declaration.MatchesNull,
        IDiscardPatternOperation => true,
        _ => false,
    };

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation { Conversion.IsUserDefined: false })
        {
            operation = operation is IParenthesizedOperation parenthesized ? parenthesized.Operand : ((IConversionOperation)operation).Operand;
        }

        return operation;
    }

    private bool HasExitGuardBefore(IBlockOperation block, IOperation statement, IFieldSymbol field)
    {
        foreach (var operation in block.Operations)
        {
            if (operation == statement)
                return false;

            if (operation is IConditionalOperation conditional
                && ((AlwaysExits(conditional.WhenTrue) && Implies(conditional.Condition, false, field))
                    || (AlwaysExits(conditional.WhenFalse) && Implies(conditional.Condition, true, field))))
                return true;
        }

        return false;
    }

    /// <summary>Returns whether <paramref name="condition" /> evaluating to <paramref name="value" /> shows the object is being disposed explicitly or <paramref name="field" /> is set.</summary>
    private bool Implies(IOperation condition, bool value, IFieldSymbol field)
    {
        condition = Unwrap(condition);
        return condition switch
        {
            IParameterReferenceOperation parameter when SymbolEqualityComparer.Default.Equals(parameter.Parameter, _disposing) => value,
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } negation => Implies(negation.Operand, !value, field),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction => value
                ? Implies(conjunction.LeftOperand, true, field) || Implies(conjunction.RightOperand, true, field)
                : Implies(conjunction.LeftOperand, false, field) && Implies(conjunction.RightOperand, false, field),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } disjunction => value
                ? Implies(disjunction.LeftOperand, true, field) && Implies(disjunction.RightOperand, true, field)
                : Implies(disjunction.LeftOperand, false, field) || Implies(disjunction.RightOperand, false, field),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison
                when (IsSameField(comparison.LeftOperand, field) && IsNullConstant(comparison.RightOperand))
                     || (IsSameField(comparison.RightOperand, field) && IsNullConstant(comparison.LeftOperand))
                => value == (comparison.OperatorKind == BinaryOperatorKind.NotEquals),
            IIsPatternOperation pattern when IsSameField(pattern.Value, field) => value == !MatchesNull(pattern.Pattern),
            _ => ReadsConstructorAssignedField(condition),
        };
    }

    /// <summary>
    /// Returns whether <paramref name="condition" /> reads a field of the type that a constructor assigns and that has no initializer:
    /// such a field still has its default value in an object whose constructor body did not run, so the check tells the two apart.
    /// The direction of the check is not verified.
    /// </summary>
    private bool ReadsConstructorAssignedField(IOperation condition)
    {
        foreach (var operation in condition.DescendantsAndSelf())
        {
            if (operation is IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: { IsStatic: false } field }
                && SymbolEqualityComparer.Default.Equals(field.ContainingType.OriginalDefinition, _type.OriginalDefinition)
                && _constructorAssignedFields.Contains(field.Name)
                && !HasInitializer(field))
                return true;
        }

        return false;
    }
}
