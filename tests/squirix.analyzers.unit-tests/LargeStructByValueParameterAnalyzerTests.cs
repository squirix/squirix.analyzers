using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class LargeStructByValueParameterAnalyzerTests
{
    private const string RuleId = "SQR0028";

    // Three longs are 24 bytes, two longs are exactly the 16-byte limit.
    private const string Types = """

                                 readonly struct Big { public readonly long A, B, C; }
                                 readonly struct Limit { public readonly long A, B; }
                                 struct Mutable { public long A, B, C; }
                                 """;

    [Test]
    public async Task FlagsLargeReadOnlyStructParameter(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            long M(Big value) => value.A;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task ReportsEstimatedSizeAndNames(CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync("class C { long M(Big value) => value.A; }", null, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("Parameter 'value' copies the 24-byte readonly struct 'Big' on every call; pass it by 'in'");
    }

    [Test]
    public async Task FlagsConstructorParameter(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private readonly long _a;

            C(Big value)
            {
                _a = value.A;
            }
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsOperatorParameters(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        readonly struct Wide
        {
            public readonly long A, B, C;

            public static bool operator <(Wide left, Wide right) => left.A < right.A;

            public static bool operator >(Wide left, Wide right) => left.A > right.A;
        }
        """, 4, cancellationToken);

    [Test]
    public async Task FlagsExtensionReceiver(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        static class Extensions
        {
            public static long Sum(this Big value) => value.A + value.B;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsStructOfThreeReferences(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        readonly struct Refs { public readonly string A, B, C; }

        class C
        {
            int M(Refs value) => value.A.Length;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsStructWithPaddedFields(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        // byte, long, byte lay out as 1 + 7 padding + 8 + 1 + 7 padding = 24 bytes.
        readonly struct Padded { public readonly byte A; public readonly long B; public readonly byte C; }

        class C
        {
            long M(Padded value) => value.B;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsStructNestingAnotherStruct(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        readonly struct Outer { public readonly Limit Inner; public readonly int Tail; }

        class C
        {
            int M(Outer value) => value.Tail;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsFrameworkStructFields(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        readonly struct Stamped { public readonly System.Guid Id; public readonly long Version; }

        class C
        {
            long M(Stamped value) => value.Version;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task AllowsStructAtTheLimit(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            long M(Limit value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsMutableStruct(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            long M(Mutable value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReferenceModifiers(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            long ByIn(in Big value) => value.A;

            long ByRef(ref Big value) => value.A;

            void ByOut(out Big value) => value = default;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsAsyncMethod(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Threading.Tasks;

        class C
        {
            async Task<long> M(Big value)
            {
                await Task.Yield();
                return value.A;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsIterator(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Collections.Generic;

        class C
        {
            IEnumerable<long> M(Big value)
            {
                yield return value.A;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsOverride(CancellationToken cancellationToken) => await AssertCleanAsync("""
        abstract class Base
        {
            public abstract long M(Big value);
        }

        class Derived : Base
        {
            public override long M(Big value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsInterfaceImplementation(CancellationToken cancellationToken) => await AssertCleanAsync("""
        interface IContract
        {
            long Implicit(Big value);

            long Explicit(Big value);
        }

        class C : IContract
        {
            public long Implicit(Big value) => value.A;

            long IContract.Explicit(Big value) => value.B;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsDeclarationWithoutBody(CancellationToken cancellationToken) => await AssertCleanAsync("""
        abstract class C
        {
            public abstract long M(Big value);

            partial class Nested
            {
                partial void Hook(Big value);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReassignedParameter(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            long M(Big value)
            {
                value = default;
                return value.A;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsParameterPassedByRef(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            void M(Big value) => Fill(ref value);

            static void Fill(ref Big value) => value = default;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsParameterCapturedByLambda(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;

        class C
        {
            Func<long> M(Big value) => () => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsParameterCapturedByLocalFunction(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            long M(Big value)
            {
                return Read();

                long Read() => value.A;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsClassAndTypeParameter(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            int M(string text) => text.Length;

            string G<T>(T value) where T : struct => value.ToString();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsOverlappingExplicitLayout(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Runtime.InteropServices;

        // Three overlapping longs take 8 bytes, not 24.
        [StructLayout(LayoutKind.Explicit)]
        readonly struct Union
        {
            [FieldOffset(0)] public readonly long A;
            [FieldOffset(0)] public readonly long B;
            [FieldOffset(0)] public readonly long C;
        }

        class C
        {
            long M(Union value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsLibraryStructWithHiddenLayout(CancellationToken cancellationToken)
    {
        // Reference assemblies hide framework struct fields behind this placeholder, so the size is unknown.
        const string library = "public readonly struct Opaque { private readonly int _dummyPrimitive; public readonly long A, B, C; }";

        var diagnostics = await AnalyzerRunner.RunWithLibraryAsync(new LargeStructByValueParameterAnalyzer(), library, "class C { long M(Opaque value) => value.A; }",
            cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsLibraryStruct(CancellationToken cancellationToken)
    {
        const string library = "public readonly struct Wide { public readonly long A, B, C; }";

        var diagnostics = await AnalyzerRunner.RunWithLibraryAsync(new LargeStructByValueParameterAnalyzer(), library, "class C { long M(Wide value) => value.A; }",
            cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsReorderedReferenceStruct(CancellationToken cancellationToken) => await AssertCleanAsync("""
        // The runtime reorders a struct that holds references: 8 + 4 + 1 bytes round up to 16, not 24.
        readonly struct Mixed { public readonly bool Ok; public readonly string Text; public readonly int Count; }

        class C
        {
            int M(Mixed value) => value.Count;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsNamedTupleField(CancellationToken cancellationToken) => await AssertCleanAsync("""
        readonly struct Wrap { public readonly (long First, long Second) Pair; }

        class C
        {
            long M(Wrap value) => value.Pair.First;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsRefField(CancellationToken cancellationToken) => await AssertCleanAsync("""
        // A ref field is a pointer, so this struct is 16 bytes whatever Big weighs.
        readonly ref struct View
        {
            public readonly ref readonly Big Target;
            public readonly int Length;
        }

        class C
        {
            int M(View value) => value.Length;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsPackedStruct(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Runtime.InteropServices;

        // Pack = 1 removes the padding: 1 + 8 + 1 = 10 bytes.
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        readonly struct Packed { public readonly byte A; public readonly long B; public readonly byte C; }

        class C
        {
            long M(Packed value) => value.B;
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsInlineArrayField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.Runtime.CompilerServices;

        [InlineArray(3)]
        struct Slots { private long _first; }

        readonly struct Holder { public readonly Slots Values; }

        class C
        {
            int M(Holder value) => value.GetHashCode();
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsNullableField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        // long? is 16 bytes, so the struct is 24.
        readonly struct Optional { public readonly long? A; public readonly long B; }

        class C
        {
            long M(Optional value) => value.B;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsConversionOperator(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        readonly struct Wide
        {
            public readonly long A, B, C;

            public static explicit operator long(Wide value) => value.A;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task AllowsRefStructResult(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;

        class C
        {
            // With 'in', the returned span could capture the parameter and callers stop compiling.
            static ReadOnlySpan<byte> Slice(Big key, ReadOnlySpan<byte> data) => data.Slice((int)key.A);
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsRefStructConstructor(CancellationToken cancellationToken) => await AssertCleanAsync("""
        ref struct Reader
        {
            private long _position;

            public Reader(Big start)
            {
                _position = start.A;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsRefInConstructorInitializer(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class Base
        {
            protected Base(ref Big value) => value = default;
        }

        class Derived : Base
        {
            Derived(Big value) : base(ref value)
            {
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsRefToRefReadOnlyParameter(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            static long Read(ref readonly Big value) => value.A;

            long M(Big value) => Read(ref value);
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsUnmanagedCallersOnly(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Runtime.InteropServices;

        static class Exports
        {
            [UnmanagedCallersOnly]
            static long Callback(Big value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsPartialInterfaceImplementation(CancellationToken cancellationToken) => await AssertCleanAsync("""
        interface IContract
        {
            long M(Big value);
        }

        partial class C : IContract
        {
            public partial long M(Big value);
        }

        partial class C
        {
            public partial long M(Big value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsDefaultInterfaceMethod(CancellationToken cancellationToken) => await AssertCleanAsync("""
        interface IContract
        {
            long M(Big value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsByReferenceOverload(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            long M(Big value) => value.A;

            long M(ref Big value) => value.B;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsParamsParameter(CancellationToken cancellationToken)
    {
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0028.max_by_value_size", "8");

        var diagnostics = await RunAsync("class C { int M(params System.ReadOnlySpan<long> values) => values.Length; }", options, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>A delegate type fixes the signature of a method used as a method group, so 'in' would not compile there.</summary>
    [Test]
    public async Task AllowsMethodUsedAsMethodGroup(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;
        using System.Collections.Generic;
        using System.Linq;

        class C
        {
            private event Action<Big>? Changed;

            C() => Changed += OnChanged;

            long Total(IEnumerable<Big> items) => items.Select(Sum).Sum();

            Func<Big, long> Pick() => Twice;

            static long Sum(Big value) => value.A;

            static long Twice(Big value) => value.A * 2;

            void OnChanged(Big value) => _ = value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsMethodGroupFromNestedType(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;

        partial class C
        {
            private static long Sum(Big value) => value.A;

            static unsafe long Call()
            {
                delegate*<Big, long> pointer = &Address;
                return pointer(default);
            }

            private static long Address(Big value) => value.B;
        }

        partial class C
        {
            class Nested
            {
                Func<Big, long> Pick() => Sum;

                class Deeper
                {
                    internal static long Inner(Big value) => value.C;
                }

                Func<Big, long> PickInner() => Deeper.Inner;
            }
        }
        """, cancellationToken);

    /// <summary>Only the overload that is used as a method group keeps its signature.</summary>
    [Test]
    public async Task FlagsOverloadNotUsedAsMethodGroup(CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync("""
            using System;

            class C
            {
                Func<Big, long> Pick() => Sum;

                static long Sum(Big value) => value.A;

                static long Sum(Big value, long extra) => value.A + extra;
            }
            """, null, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(8);
    }

    [Test]
    public async Task FlagsMethodNamedOrOnlyCalled(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            string Name() => nameof(Sum);

            long Call(Limit seed) => Sum(default) + seed.A;

            static long Sum(Big value) => value.A;
        }
        """, 1, cancellationToken);

    /// <summary>A use from another type is not seen: the rule reads the method's own type, not the whole compilation.</summary>
    [Test]
    public async Task FlagsMethodGroupUsedInAnotherType(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System;

        class C
        {
            internal static long Sum(Big value) => value.A;
        }

        class Other
        {
            Func<Big, long> Pick() => C.Sum;
        }
        """, 1, cancellationToken);

    /// <summary>An override must follow the signature it overrides, so a delegate made from it holds the base method too.</summary>
    [Test]
    public async Task AllowsMethodWhoseOverrideIsAGroup(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;

        class C
        {
            protected virtual long Read(Big value) => value.A;

            class Derived : C
            {
                protected override long Read(Big value) => value.B;

                Func<Big, long> Pick() => Read;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsGenericAndExtensionGroups(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;

        static class Extensions
        {
            static Func<Big, long> Pick(string text) => text.Weigh;

            static Func<Big, int, long> PickGeneric() => Add<int>;

            static Func<string, Big, long> PickBlock() => Scale;

            static long Weigh(this string text, Big value) => text.Length + value.A;

            static long Add<T>(Big value, T extra) => value.A;

            extension(string text)
            {
                public long Scale(Big value) => text.Length * value.A;
            }
        }
        """, cancellationToken);

    /// <summary>A name that binds to something else, such as a local, is not a use of the method.</summary>
    [Test]
    public async Task FlagsMethodWhoseNameIsALocal(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            long Call()
            {
                var Sum = 1L;
                return Sum + C.Sum(default);
            }

            static long Sum(Big value) => value.A;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsMethodOfNestedType(CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync("""
            using System;

            class C
            {
                Func<Big, long> Pick() => Sum;

                static long Sum(Big value) => value.A;

                class Nested
                {
                    static long Other(Big value) => value.B;
                }
            }
            """, null, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(10);
    }

    /// <summary>The use sits in another file of the type, in the accessor of a partial event and in a generated part.</summary>
    [Test]
    public async Task AllowsMethodGroupInAnotherFile(CancellationToken cancellationToken)
    {
        const string declarations = """
                                    using System;

                                    partial class C
                                    {
                                        private static Func<Big, long>? handler;

                                        public partial event Func<Big, long> Changed;

                                        static long OnChanged(Big value) => value.A;

                                        static long Generated(Big value) => value.B;

                                        static long OnlyCalled(Big value) => value.C;
                                    }
                                    """;
        const string accessors = """
                                 using System;

                                 partial class C
                                 {
                                     public partial event Func<Big, long> Changed { add => handler = OnChanged; remove => handler = OnChanged; }

                                     static long Call() => OnlyCalled(default);
                                 }
                                 """;
        const string generated = """
                                 // <auto-generated/>
                                 using System;

                                 partial class C
                                 {
                                     class Wiring
                                     {
                                         Func<Big, long> Pick() => Generated;
                                     }
                                 }
                                 """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new LargeStructByValueParameterAnalyzer(), [declarations + Types, accessors, generated], cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).Contains("'value'");
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(12);
    }

    /// <summary>A partial constructor must not keep the findings of its type from being reported.</summary>
    [Test]
    public async Task FlagsMethodsNextToPartialConstructor(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        partial class C
        {
            public partial C(int seed);

            public partial C(int seed) { }

            static long Sum(Big value) => value.A;

            class Nested
            {
                long Other(Big value) => value.B;
            }
        }
        """, 2, cancellationToken);

    [Test]
    public async Task AllowsPartialMethodUsedAsGroup(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;

        partial class C
        {
            private static partial long Sum(Big value);

            private static partial long Sum(Big value) => value.A;

            Func<Big, long> Pick() => Sum;
        }
        """, cancellationToken);

    /// <summary>A call after ?. is a call like any other, so it does not hold the report back.</summary>
    [Test]
    public async Task FlagsMethodCalledConditionally(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            long? Call(C? other) => other?.Sum(default);

            long Sum(Big value) => value.A;
        }
        """ + "\n#nullable enable", 1, cancellationToken);

    /// <summary>While a conversion does not bind, every method the name may mean keeps its signature.</summary>
    [Test]
    public async Task AllowsCandidatesOfBrokenConversion(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class C
                              {
                                  Func<int> Pick() => Sum;

                                  static long Sum(Big value) => value.A;

                                  static long Sum(Big value, long extra) => value.A + extra;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunOnIncompleteCodeAsync(new LargeStructByValueParameterAnalyzer(), source + Types, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Another file is not bound: there a mention of the name outside a call counts, whatever it names.</summary>
    [Test]
    public async Task AllowsSameNameInAnotherFile(CancellationToken cancellationToken)
    {
        const string declarations = """
                                    partial class C
                                    {
                                        static long Length(Big value) => value.A;

                                        static long Named(Big value) => value.B;
                                    }
                                    """;
        const string other = """
                             partial class C
                             {
                                 static int Size(string text) => text.Length;

                                 static string Name() => nameof(Named);
                             }
                             """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new LargeStructByValueParameterAnalyzer(), [declarations + Types, other], cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(4);
    }

    /// <summary>A constructor has no name to be used by, so a mention of anything else does not hold it back.</summary>
    [Test]
    public async Task FlagsConstructorNextToMethodGroups(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System;

        class C
        {
            C(Big value) => _ = value.A;

            Func<Big, long> Pick() => Sum;

            static long Sum(Big value) => value.A;
        }
        """, 1, cancellationToken);

    [Test]
    public async Task SkipsGeneratedCode(CancellationToken cancellationToken) => await AssertCleanAsync("""
        // <auto-generated/>
        class C
        {
            long M(Big value) => value.A;
        }
        """, cancellationToken);

    [Test]
    public async Task HonorsRaisedLimit(CancellationToken cancellationToken)
    {
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0028.max_by_value_size", "24");

        var diagnostics = await RunAsync("class C { long M(Big value) => value.A; }", options, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task HonorsLoweredLimit(CancellationToken cancellationToken)
    {
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0028.max_by_value_size", "8");

        var diagnostics = await RunAsync("class C { long M(Limit value) => value.A; }", options, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static async Task AssertCleanAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync(source, null, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static async Task AssertFlaggedAsync(string source, int count, CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync(source, null, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(count);
        foreach (var diagnostic in diagnostics)
            _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> RunAsync(string source, ImmutableDictionary<string, string>? options,
        CancellationToken cancellationToken) => AnalyzerRunner.RunAsync(new LargeStructByValueParameterAnalyzer(), source + Types, options, cancellationToken);
}
