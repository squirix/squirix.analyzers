using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class DefensiveStructCopyAnalyzerTests
{
    private const string RuleId = "SQR0029";

    // Counter mutates itself; Frozen and the readonly members never do.
    private const string Types = """

                                 struct Counter
                                 {
                                     private int _count;

                                     public int Next() => ++_count;

                                     public int Current => _count;

                                     public int this[int offset] => _count + offset;

                                     public readonly int Peek() => _count;

                                     public readonly int Stable => _count;

                                     public override string ToString() => _count.ToString();
                                 }

                                 readonly struct Frozen
                                 {
                                     public readonly int Value;

                                     public int Read() => Value;
                                 }
                                 """;

    [Test]
    public async Task FlagsMethodOnReadOnlyField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private readonly Counter _counter;

            int M() => _counter.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task ReportsMemberVariableAndStruct(CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync("class C { private readonly Counter _counter; int M() => _counter.Next(); }", null, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("'Next' is not readonly, so calling it on the readonly '_counter' runs on a hidden copy of 'Counter'");
    }

    [Test]
    public async Task FlagsPropertyOnReadOnlyField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private readonly Counter _counter;

            int M() => _counter.Current;
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsIndexerOnReadOnlyField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private readonly Counter _counter;

            int M() => _counter[1];
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsOverriddenMethodOnReadOnlyField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private readonly Counter _counter;

            string M() => _counter.ToString();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsStaticReadOnlyField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private static readonly Counter Shared;

            static int M() => Shared.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsInParameter(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            int M(in Counter counter) => counter.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsRefReadOnlyParameter(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            int M(ref readonly Counter counter) => counter.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsRefReadOnlyLocal(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private Counter _counter;

            int M()
            {
                ref readonly var counter = ref _counter;
                return counter.Next();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsRefReadOnlyReturn(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private Counter _counter;

            ref readonly Counter Get() => ref _counter;

            int M() => Get().Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsFieldOfReadOnlyStructVariable(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        struct Holder { public Counter Inner; }

        class C
        {
            int M(in Holder holder) => holder.Inner.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsFieldInsideReadOnlyMember(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        struct Holder
        {
            private Counter _inner;

            public readonly int M() => _inner.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReadOnlyMembers(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            private readonly Counter _counter;

            int M() => _counter.Peek() + _counter.Stable;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReadOnlyStruct(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            private readonly Frozen _frozen;

            int M(in Frozen other) => _frozen.Read() + other.Read();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsWritableVariables(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            private Counter _field;

            int M(Counter byValue, ref Counter byRef)
            {
                var local = new Counter();
                return _field.Next() + byValue.Next() + byRef.Next() + local.Next();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReadOnlyFieldInsideConstructor(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            private static readonly Counter Shared;
            private readonly Counter _counter;

            static C()
            {
                _ = Shared.Next();
            }

            C()
            {
                _ = _counter.Next();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsTemporaryValue(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            static Counter Create() => new Counter();

            int M() => Create().Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsInheritedMember(CancellationToken cancellationToken) => await AssertCleanAsync("""
        struct Plain { public int Value; }

        class C
        {
            private readonly Plain _plain;

            int M() => _plain.GetHashCode();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReferenceTypeField(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class Box { public int Next() => 1; }

        class C
        {
            private readonly Box _box = new Box();

            int M() => _box.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsFieldOfWritableStructVariable(CancellationToken cancellationToken) => await AssertCleanAsync("""
        struct Holder
        {
            public Counter Inner;

            public int M() => Inner.Next();
        }

        class C
        {
            int M(ref Holder holder) => holder.Inner.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsLambdaInsideConstructor(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System;

        class C
        {
            private readonly Counter _counter;
            private readonly Func<int> _next;

            C()
            {
                _next = () => _counter.Next();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsOtherInstanceInsideConstructor(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        class C
        {
            private readonly Counter _counter;

            C(C other)
            {
                _ = other._counter.Next();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsRefReadOnlyIndexer(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System;

        class C
        {
            int M(ReadOnlySpan<Counter> counters) => counters[0].Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsNestedFieldChain(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        struct Inner { public Counter Deep; }

        struct Outer { public Inner Middle; }

        class C
        {
            int M(in Outer outer) => outer.Middle.Deep.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsNameOf(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            private readonly Counter _counter;

            string M() => nameof(_counter.Current);
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsInitAccessor(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            private readonly Counter _counter;
            private readonly int _first;

            public int First
            {
                get => _first;
                init => _first = _counter.Next() + value;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsInitializers(CancellationToken cancellationToken) => await AssertCleanAsync("""
        class C
        {
            private static readonly Counter Shared;
            private static readonly int First = Shared.Next();

            private static int Second { get; } = Shared.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReadOnlySetter(CancellationToken cancellationToken) => await AssertCleanAsync("""
        struct Sink
        {
            public int Value
            {
                get => 0;
                readonly set { }
            }
        }

        class C
        {
            private readonly Sink _sink;

            void M() => _sink.Value = 1;
        }
        """, cancellationToken);

    [Test]
    public async Task LeavesThisCallToCompiler(CancellationToken cancellationToken) => await AssertCleanAsync("""
        struct Holder
        {
            private int _count;

            public int Next() => ++_count;

            // The compiler already warns here with CS8656.
            public readonly int M() => Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsExtensionMethod(CancellationToken cancellationToken) => await AssertCleanAsync("""
        static class Extensions
        {
            public static int Twice(this Counter counter) => counter.Peek() * 2;
        }

        class C
        {
            private readonly Counter _counter;

            int M() => _counter.Twice();
        }
        """, cancellationToken);

    /// <summary>The readonly before ref only fixes where the reference points, so the member runs on the original struct.</summary>
    [Test]
    public async Task AllowsReadOnlyRefField(CancellationToken cancellationToken) => await AssertCleanAsync("""
        ref struct Writer
        {
            private readonly ref Counter _counter;

            public Writer(ref Counter counter) => _counter = ref counter;

            public int M() => _counter.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsRefFieldInsideReadOnlyMember(CancellationToken cancellationToken) => await AssertCleanAsync("""
        ref struct Writer
        {
            private ref Counter _counter;

            public Writer(ref Counter counter) => _counter = ref counter;

            public readonly int M() => _counter.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsRefFieldOfReadOnlyVariable(CancellationToken cancellationToken) => await AssertCleanAsync("""
        struct Inner { public Counter Deep; }

        ref struct Writer
        {
            public ref Counter Direct;
            public ref Inner Nested;
        }

        class C
        {
            int M(in Writer writer) => writer.Direct.Next() + writer.Nested.Deep.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsRefReadOnlyField(CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync("""
            ref struct Reader
            {
                private ref readonly Counter _counter;

                public Reader(ref Counter counter) => _counter = ref counter;

                public int M() => _counter.Next();
            }
            """, null, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("'Next' is not readonly, so calling it on the readonly '_counter' runs on a hidden copy of 'Counter'");
    }

    [Test]
    public async Task FlagsReadOnlyRefReadOnlyField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        readonly ref struct Reader
        {
            private readonly ref readonly Counter _counter;

            public Reader(ref Counter counter) => _counter = ref counter;

            public int M() => _counter.Next();
        }
        """, cancellationToken);

    /// <summary>The struct behind a ref readonly field cannot be changed even while its holder is being initialized.</summary>
    [Test]
    public async Task FlagsRefReadOnlyFieldInConstructor(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        readonly ref struct Reader
        {
            private readonly ref readonly Counter _counter;

            public Reader(ref Counter counter)
            {
                _counter = ref counter;
                _ = _counter.Next();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsFieldBehindRefReadOnlyField(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        struct Inner { public Counter Deep; }

        ref struct Reader
        {
            public ref readonly Inner Nested;
        }

        class C
        {
            int M(Reader reader) => reader.Nested.Deep.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task SkipsUnknownSizeWithConfiguredSize(CancellationToken cancellationToken)
    {
        const string source = """
                              struct Box<T>
                              {
                                  private T _value;

                                  public int Next() => 1;
                              }

                              class C<T>
                              {
                                  private readonly Box<T> _box;

                                  int M() => _box.Next();
                              }
                              """;
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0029.min_struct_size", "16");

        var diagnostics = await RunAsync(source, options, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsGeneratedCode(CancellationToken cancellationToken) => await AssertCleanAsync("""
        // <auto-generated/>
        class C
        {
            private readonly Counter _counter;

            int M() => _counter.Next();
        }
        """, cancellationToken);

    [Test]
    public async Task SkipsStructBelowConfiguredSize(CancellationToken cancellationToken)
    {
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0029.min_struct_size", "16");

        var diagnostics = await RunAsync("class C { private readonly Counter _counter; int M() => _counter.Next(); }", options, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsStructAtConfiguredSize(CancellationToken cancellationToken)
    {
        const string source = """
                              struct Wide
                              {
                                  private long _a, _b;

                                  public long Sum() => _a + _b;
                              }

                              class C
                              {
                                  private readonly Wide _wide;

                                  long M() => _wide.Sum();
                              }
                              """;
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0029.min_struct_size", "16");

        var diagnostics = await RunAsync(source, options, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static async Task AssertCleanAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync(source, null, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static async Task AssertFlaggedAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await RunAsync(source, null, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> RunAsync(string source, ImmutableDictionary<string, string>? options,
        CancellationToken cancellationToken) => AnalyzerRunner.RunAsync(new DefensiveStructCopyAnalyzer(), source + Types, options, cancellationToken);
}
