using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class RedundantNamedArgumentAnalyzerTests
{
    private const string RuleId = "SQR0009";

    [Test]
    public async Task AllowsOutOfOrderNamedArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(b: 2, a: 1);
                                  }

                                  void Foo(int a, int b)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantNamedArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsInOrderNamedArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(a: 1);
                                  }

                                  void Foo(int a)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantNamedArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public Task FlagsExplicitNewInOrder(CancellationToken cancellationToken) => AssertSingleAsync("""
        class Foo
        {
            public Foo(int a)
            {
            }
        }

        class C
        {
            void M()
            {
                var f = new Foo(a: 1);
            }
        }
        """, cancellationToken);

    [Test]
    public Task FlagsTargetTypedNewLocal(CancellationToken cancellationToken) => AssertSingleAsync("""
        class Foo
        {
            public Foo(int a)
            {
            }
        }

        class C
        {
            void M()
            {
                Foo f = new(a: 1);
            }
        }
        """, cancellationToken);

    [Test]
    public Task FlagsTargetTypedNewField(CancellationToken cancellationToken) => AssertSingleAsync("""
        class Foo
        {
            public Foo(int a)
            {
            }
        }

        class C
        {
            private readonly Foo _f = new(a: 1);
        }
        """, cancellationToken);

    [Test]
    public Task FlagsTargetTypedNewReturn(CancellationToken cancellationToken) => AssertSingleAsync("""
        class Foo
        {
            public Foo(int a)
            {
            }
        }

        class C
        {
            Foo M()
            {
                return new(a: 1);
            }
        }
        """, cancellationToken);

    [Test]
    public Task FlagsTargetTypedNewArgument(CancellationToken cancellationToken) => AssertSingleAsync("""
        class Foo
        {
            public Foo(int a)
            {
            }
        }

        class C
        {
            void M()
            {
                Take(new(a: 1));
            }

            void Take(Foo f)
            {
            }
        }
        """, cancellationToken);

    [Test]
    public Task FlagsTargetTypedNewInCollection(CancellationToken cancellationToken) => AssertSingleAsync("""
        class Foo
        {
            public Foo(int a)
            {
            }
        }

        class C
        {
            void M()
            {
                System.Collections.Generic.List<Foo> list = [new(a: 1)];
            }
        }
        """, cancellationToken);

    [Test]
    public Task FlagsTargetTypedNewPrefix(CancellationToken cancellationToken) => AssertSingleAsync("""
        class Foo
        {
            public Foo(int a, int b = 0, int c = 0)
            {
            }
        }

        class C
        {
            void M()
            {
                Foo f = new(a: 1, c: 3);
            }
        }
        """, cancellationToken);

    [Test]
    public Task AllowsTargetTypedNewOutOfOrder(CancellationToken cancellationToken) => AssertEmptyAsync("""
        class Foo
        {
            public Foo(int a, int b)
            {
            }
        }

        class C
        {
            void M()
            {
                Foo f = new(b: 2, a: 1);
            }
        }
        """, cancellationToken);

    [Test]
    public Task AllowsTargetTypedNewSkippingOptional(CancellationToken cancellationToken) => AssertEmptyAsync("""
        class Foo
        {
            public Foo(int a = 0, int b = 0)
            {
            }
        }

        class C
        {
            void M()
            {
                Foo f = new(b: 2);
            }
        }
        """, cancellationToken);

    [Test]
    public Task AllowsExplicitNewOutOfOrder(CancellationToken cancellationToken) => AssertEmptyAsync("""
        class Foo
        {
            public Foo(int a, int b)
            {
            }
        }

        class C
        {
            void M()
            {
                var f = new Foo(b: 2, a: 1);
            }
        }
        """, cancellationToken);

    private static async Task AssertEmptyAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantNamedArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static async Task AssertSingleAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantNamedArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
