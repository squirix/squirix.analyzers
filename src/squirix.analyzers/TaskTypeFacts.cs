using Microsoft.CodeAnalysis;

namespace Squirix.Analyzers;

/// <summary>
/// Answers questions about the Task and ValueTask types and the delegates that return them, from the type symbols alone.
/// </summary>
internal static class TaskTypeFacts
{
    /// <summary>
    /// Returns the item type of a collection: of an array, of a span, or the T of the one IEnumerable&lt;T&gt; the type is or
    /// implements. A type that is enumerable in several ways gives no answer.
    /// </summary>
    internal static ITypeSymbol? GetItemType(ITypeSymbol? collection)
    {
        switch (Unwrap(collection))
        {
            case IArrayTypeSymbol array:
                return array.ElementType;
            case INamedTypeSymbol { Name: "Span" or "ReadOnlySpan", TypeArguments.Length: 1, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } span:
                return span.TypeArguments[0];
            case INamedTypeSymbol named:
                if (IsEnumerableOfT(named))
                    return named.TypeArguments[0];

                ITypeSymbol? item = null;
                foreach (var implemented in named.AllInterfaces)
                {
                    if (!IsEnumerableOfT(implemented))
                        continue;

                    if (item != null)
                        return null;

                    item = implemented.TypeArguments[0];
                }

                return item;
            default:
                return null;
        }
    }

    internal static bool IsPlainTask(ITypeSymbol? type) => type != null && IsNamed(type, "Task", false);

    /// <summary>Returns whether the type is a task itself: Task, ValueTask, a type derived from Task, or a type parameter constrained to one.</summary>
    internal static bool IsTaskLike(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            foreach (var constraint in parameter.ConstraintTypes)
            {
                if (IsTaskLike(constraint))
                    return true;
            }

            return false;
        }

        for (var current = type; current != null; current = current.BaseType)
        {
            if (IsNamed(current, "Task", false) || IsNamed(current, "Task", true) || IsNamed(current, "ValueTask", false) || IsNamed(current, "ValueTask", true))
                return true;
        }

        return false;
    }

    internal static bool ReturnsPlainTask(ITypeSymbol? type, out INamedTypeSymbol delegateType)
    {
        delegateType = null!;
        if (type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } named || !IsPlainTask(invoke.ReturnType))
            return false;

        delegateType = named;
        return true;
    }

    internal static bool TryGetDelegateResultType(ITypeSymbol? type, out string result)
    {
        result = string.Empty;
        return type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke } && TryGetResultType(invoke.ReturnType, out result);
    }

    internal static bool TryGetResultType(ITypeSymbol? type, out string result)
    {
        result = string.Empty;
        if (type is ITypeParameterSymbol parameter)
        {
            // A type parameter is a Task<T> when one of its constraints is.
            foreach (var constraint in parameter.ConstraintTypes)
            {
                if (TryGetResultType(constraint, out result))
                    return true;
            }

            return false;
        }

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

    /// <summary>Returns the type inside a nullable value type, such as a nullable tuple; any other type as it is.</summary>
    internal static ITypeSymbol? Unwrap(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    private static bool IsEnumerableOfT(INamedTypeSymbol type) => type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;

    private static bool IsNamed(ITypeSymbol type, string name, bool generic) =>
        type is INamedTypeSymbol { ContainingNamespace: { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } } named
        && named.Name == name
        && named.IsGenericType == generic;
}
