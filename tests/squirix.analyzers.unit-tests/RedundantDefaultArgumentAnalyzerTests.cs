using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class RedundantDefaultArgumentAnalyzerTests
{
    private const string RuleId = "SQR0011";

    [Test]
    public async Task AllowsArgumentNotEqualToDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(5);
                                  }

                                  void Foo(int a = 0)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsDefaultForNonNullDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(default);
                                  }

                                  void Foo(string s = "hi")
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsDefaultWhenNotEqual(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(default);
                                  }

                                  void Foo(int a = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsArgumentEqualToParameterDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(5);
                                  }

                                  void Foo(int a = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsDefaultForNullDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(default);
                                  }

                                  void Foo(string? s = null)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsDefaultLiteralWhenEqualToDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(default);
                                  }

                                  void Foo(int a = 0)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsCallerMemberName(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Runtime.CompilerServices;
                              class C
                              {
                                  void M()
                                  {
                                      Log(1, "");
                                  }

                                  void Log(int a, [CallerMemberName] string v = "")
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsCallerFilePath(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Runtime.CompilerServices;
                              class C
                              {
                                  void M()
                                  {
                                      Log(1, "");
                                  }

                                  void Log(int a, [CallerFilePath] string v = "")
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsCallerLineNumber(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Runtime.CompilerServices;
                              class C
                              {
                                  void M()
                                  {
                                      Log(1, 0);
                                  }

                                  void Log(int a, [CallerLineNumber] int v = 0)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsCallerArgumentExpression(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Runtime.CompilerServices;
                              class C
                              {
                                  void M(int x)
                                  {
                                      Check(x, null);
                                  }

                                  void Check(int value, [CallerArgumentExpression("value")] string? name = null)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsThrowIfNullExplicitParamName(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              class C
                              {
                                  void M(object x)
                                  {
                                      ArgumentNullException.ThrowIfNull(x, null);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsNamedDefaultArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Foo(b: false);
                                  }

                                  void Foo(int a = 1, bool b = false)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTargetTypedNewLocal(CancellationToken cancellationToken)
    {
        const string source = """
                              class Foo
                              {
                                  public Foo(int a, int b = 0)
                                  {
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Foo f = new(1, 0);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsTargetTypedNewNonDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class Foo
                              {
                                  public Foo(int a, int b = 0)
                                  {
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Foo f = new(1, 2);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsTargetTypedNewField(CancellationToken cancellationToken)
    {
        const string source = """
                              class Foo
                              {
                                  public Foo(int a, int b = 0)
                                  {
                                  }
                              }

                              class C
                              {
                                  private readonly Foo _f = new(1, 0);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTargetTypedNewReturn(CancellationToken cancellationToken)
    {
        const string source = """
                              class Foo
                              {
                                  public Foo(int a, int b = 0)
                                  {
                                  }
                              }

                              class C
                              {
                                  Foo M()
                                  {
                                      return new(1, 0);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTargetTypedNewArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              class Foo
                              {
                                  public Foo(int a, int b = 0)
                                  {
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Take(new(1, 0));
                                  }

                                  void Take(Foo f)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
