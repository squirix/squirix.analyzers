using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Squirix.Analyzers;

/// <summary>
/// Estimates the size in bytes of a value type on a 64-bit runtime.
/// A struct without object references keeps its fields in declaration order with natural alignment; a struct that
/// holds references is laid out by the runtime, which reorders fields to avoid padding.
/// A layout that cannot be seen (a type parameter, or a framework struct whose reference assembly hides its fields)
/// yields no size, so callers stay silent instead of guessing.
/// </summary>
internal sealed class StructSizeEstimator
{
    private const int MaxDepth = 16;
    private const int PointerSize = 8;

    private static readonly Layout Pointer = new(PointerSize, PointerSize, true);

    private readonly ConcurrentDictionary<ITypeSymbol, Layout> _layouts = new(SymbolEqualityComparer.Default);

    internal bool TryGetSize(ITypeSymbol type, out int size)
    {
        var layout = GetLayout(type, 0);
        size = layout.Size;
        return layout.IsKnown;
    }

    private static int Align(int offset, int alignment) => (offset + alignment - 1) / alignment * alignment;

    private static AttributeData? FindAttribute(ISymbol symbol, string name)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == name)
                return attribute;
        }

        return null;
    }

    private static int GetFieldOffset(IFieldSymbol field) =>
        FindAttribute(field, "FieldOffsetAttribute") is { ConstructorArguments: [{ Value: int value }] } ? value : -1;

    private static Layout GetFrameworkLayout(INamedTypeSymbol type) => (type.ContainingNamespace?.ToDisplayString(), type.MetadataName) switch
    {
        ("System", "Guid") => new Layout(16, 4, false),
        ("System", "Int128" or "UInt128") => new Layout(16, 16, false),
        ("System", "DateTimeOffset") => new Layout(16, 8, false),
        ("System", "Memory`1" or "ReadOnlyMemory`1" or "Span`1" or "ReadOnlySpan`1" or "ArraySegment`1") => new Layout(16, 8, true),
        ("System", "TimeSpan" or "TimeOnly") => new Layout(8, 8, false),
        ("System", "Range") => new Layout(8, 4, false),
        ("System", "DateOnly" or "Index") => new Layout(4, 4, false),
        ("System", "Half") => new Layout(2, 2, false),
        ("System.Threading", "CancellationToken") => Pointer,
        ("System.Threading.Tasks", "ValueTask") => new Layout(16, 8, true),
        ("System.Runtime.InteropServices", "GCHandle") => new Layout(8, 8, false),
        _ => default,
    };

    private static int GetIntArgument(AttributeData? attribute, string name)
    {
        if (attribute == null)
            return 0;

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is int value)
                return value;
        }

        return 0;
    }

    private static Layout GetPrimitiveLayout(SpecialType specialType)
    {
        var size = specialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => 1,
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_DateTime => 8,
            SpecialType.System_Decimal => 16,
            _ => 0,
        };

        return size == 0 ? default : new Layout(size, size < PointerSize ? size : PointerSize, false);
    }

    // LayoutKind.Explicit is 2; the attribute takes either the enum or a short.
    private static bool IsExplicit(AttributeData? structLayout) => structLayout is { ConstructorArguments: [{ Value: 2 or (short)2 }] };

    // A tuple with element names exposes every element twice: as ItemN and under its name.
    private static bool IsTupleAlias(IFieldSymbol field) =>
        field.CorrespondingTupleField is { } item && !SymbolEqualityComparer.Default.Equals(item, field);

    private Layout ComputeStructLayout(INamedTypeSymbol type, int depth)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var value = GetLayout(type.TypeArguments[0], depth + 1);
            return value.IsKnown ? new Layout(Align(Align(1, value.Alignment) + value.Size, value.Alignment), value.Alignment, value.HasReference) : default;
        }

        var inSource = type.DeclaringSyntaxReferences.Length > 0;
        var structLayout = inSource ? FindAttribute(type, "StructLayoutAttribute") : null;
        var builder = new LayoutBuilder(IsExplicit(structLayout), GetIntArgument(structLayout, "Pack"));
        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field || IsTupleAlias(field))
                continue;

            // Reference assemblies replace the private fields of framework structs with these placeholders.
            if (!inSource && field.Name is "_dummy" or "_dummyPrimitive")
                return GetFrameworkLayout(type);

            var fieldLayout = GetFieldLayout(field, depth);
            if (!fieldLayout.IsKnown || !builder.TryAdd(fieldLayout, builder.IsExplicit ? GetFieldOffset(field) : 0))
                return default;
        }

        // A struct from another assembly with no visible field has its layout stripped, not an empty one.
        if (builder.IsEmpty && !inSource)
            return GetFrameworkLayout(type);

        var length = FindAttribute(type, "InlineArrayAttribute") is { ConstructorArguments: [{ Value: int count }] } ? count : 1;
        return builder.Build(length, GetIntArgument(structLayout, "Size"));
    }

    private Layout GetFieldLayout(IFieldSymbol field, int depth)
    {
        // A ref field stores a pointer whatever it points to.
        if (field.RefKind != RefKind.None)
            return Pointer;

        if (!field.IsFixedSizeBuffer || field.Type is not IPointerTypeSymbol pointer)
            return GetLayout(field.Type, depth + 1);

        var element = GetLayout(pointer.PointedAtType, depth + 1);
        return element.IsKnown ? new Layout(element.Size * field.FixedSize, element.Alignment, false) : default;
    }

    private Layout GetLayout(ITypeSymbol type, int depth)
    {
        if (depth > MaxDepth)
            return default;

        var primitive = GetPrimitiveLayout(type.SpecialType);
        if (primitive.IsKnown)
            return primitive;

        switch (type.TypeKind)
        {
            case TypeKind.Enum:
                return type is INamedTypeSymbol { EnumUnderlyingType: { } underlying } ? GetLayout(underlying, depth + 1) : default;
            case TypeKind.Struct when type is INamedTypeSymbol named:
                if (_layouts.TryGetValue(named, out var cached))
                    return cached;

                var computed = ComputeStructLayout(named, depth);

                // An unknown result found below the top level may only mean the depth limit was reached.
                if (computed.IsKnown || depth == 0)
                    _ = _layouts.TryAdd(named, computed);

                return computed;
            case TypeKind.TypeParameter:
                return type.IsReferenceType ? Pointer : default;
            case TypeKind.Class or TypeKind.Interface or TypeKind.Delegate or TypeKind.Array or TypeKind.Dynamic:
                return Pointer;
            case TypeKind.Pointer or TypeKind.FunctionPointer:
                return new Layout(PointerSize, PointerSize, false);
            default:
                return default;
        }
    }

    private readonly struct Layout
    {
        internal Layout(int size, int alignment, bool hasReference)
        {
            Size = size;
            Alignment = alignment;
            HasReference = hasReference;
        }

        internal int Alignment { get; }

        internal bool HasReference { get; }

        internal bool IsKnown => Size > 0;

        internal int Size { get; }
    }

    private struct LayoutBuilder
    {
        private readonly int _pack;
        private int _alignment;
        private bool _hasReference;
        private int _sequentialEnd;
        private int _total;

        internal LayoutBuilder(bool isExplicit, int pack)
        {
            IsExplicit = isExplicit;
            _pack = pack;
            _alignment = 1;
            _hasReference = false;
            _sequentialEnd = 0;
            _total = 0;
        }

        internal bool IsEmpty => _total == 0;

        internal bool IsExplicit { get; }

        internal Layout Build(int length, int declaredSize)
        {
            // An empty struct still occupies one byte.
            if (_total == 0)
                return new Layout(declaredSize > 1 ? declaredSize : 1, 1, false);

            // The runtime reorders the fields of a struct that holds references, so no padding is left between them.
            var end = _hasReference && !IsExplicit ? _total : _sequentialEnd;
            var size = Align(end, _alignment) * length;
            return new Layout(declaredSize > size ? declaredSize : size, _alignment, _hasReference);
        }

        internal bool TryAdd(Layout field, int explicitOffset)
        {
            if (explicitOffset < 0)
                return false;

            var alignment = _pack > 0 && _pack < field.Alignment ? _pack : field.Alignment;
            if (alignment > _alignment)
                _alignment = alignment;

            _hasReference |= field.HasReference;
            _total += field.Size;

            // Fields of an explicit layout may overlap, so the size is the furthest field end.
            var end = IsExplicit ? explicitOffset + field.Size : Align(_sequentialEnd, alignment) + field.Size;
            if (end > _sequentialEnd)
                _sequentialEnd = end;

            return true;
        }
    }
}
