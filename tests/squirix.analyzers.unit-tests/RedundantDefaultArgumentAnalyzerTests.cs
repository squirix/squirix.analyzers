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

    /// <summary>Dropping the first of two trailing defaults alone would hand the second one to its parameter, so the run is one finding.</summary>
    [Test]
    public async Task FlagsTrailingRunOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M() => Foo(1, 0, 5);

                                  void Foo(int a, int b = 0, int c = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("The parameter 'b' has the same default value, and so do the arguments after it; omit them together");
        _ = await Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)).IsEqualTo("0, 5");
    }

    [Test]
    public async Task FlagsLastArgumentOfMixedTail(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M() => Foo(1, 2, 5);

                                  void Foo(int a, int b = 0, int c = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("The parameter 'c' has the same default value");
        _ = await Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)).IsEqualTo("5");
    }

    [Test]
    public async Task AllowsDefaultBeforeAnotherValue(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M() => Foo(1, 0, 7);

                                  void Foo(int a, int b = 0, int c = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Without both arguments the call would bind to the other overload, so only the last one can go.</summary>
    [Test]
    public async Task FlagsSuffixThatKeepsTheOverload(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M() => Foo(1, 0, 5);

                                  void Foo(int a)
                                  {
                                  }

                                  void Foo(int a, int b = 0, int c = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("The parameter 'c' has the same default value");
        _ = await Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)).IsEqualTo("5");
    }

    [Test]
    public async Task FlagsEachNamedDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M() => Foo(1, c: 5, b: 0);

                                  void Foo(int a, int b = 0, int c = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
    }

    /// <summary>Without the named argument the positional one after it would move into its place.</summary>
    [Test]
    public async Task AllowsNamedBeforePositional(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      Bar(a: 0, 7);
                                      Bar(0, b: 1, 9);
                                  }

                                  void Bar(int a = 0, int b = 1, int c = 2)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOnlyTailAfterNamedDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M() => Foo(1, b: 0, 5);

                                  void Foo(int a, int b = 0, int c = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("5");
    }

    /// <summary>A call cut out of its conditional access cannot be bound on its own; the check binds it on the plain receiver.</summary>
    [Test]
    public async Task FlagsRunAfterConditionalAccess(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              class C
                              {
                                  public C? Next;

                                  public C[] Items = new C[1];

                                  void M(C? c)
                                  {
                                      c?.Foo(1, 0, 5);
                                      c?.Next?.Foo(1, 2, 5);
                                      c?.Items[0].Foo(1, c: 5);
                                      c?.Use(Bar(1, 0, 5), c?.Next);
                                  }

                                  static int Bar(int a, int b = 0, int c = 5) => a;

                                  void Use(int value, C? other)
                                  {
                                  }

                                  void Foo(int a, int b = 0, int c = 5)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(4);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("0, 5");
        _ = await Assert.That(source.Substring(diagnostics[1].Location.SourceSpan.Start, diagnostics[1].Location.SourceSpan.Length)).IsEqualTo("5");
        _ = await Assert.That(source.Substring(diagnostics[2].Location.SourceSpan.Start, diagnostics[2].Location.SourceSpan.Length)).IsEqualTo("c: 5");
        _ = await Assert.That(source.Substring(diagnostics[3].Location.SourceSpan.Start, diagnostics[3].Location.SourceSpan.Length)).IsEqualTo("0, 5");
    }

    /// <summary>Without the second argument T would be inferred as string, no longer object.</summary>
    [Test]
    public async Task FlagsOnlyTailThatKeepsTypeArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M() => Pick("s", default(object), 5);

                                  static string Pick<T>(T a, T? b = default, int c = 5)
                                      where T : class => string.Empty;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("5");
    }

    [Test]
    public async Task FlagsRunInConstructorCalls(CancellationToken cancellationToken)
    {
        const string source = """
                              class B
                              {
                                  public B(int a, int b = 0, int c = 5, int d = 9)
                                  {
                                  }

                                  static B Make() => new B(1, 0, 5, 9);

                                  static B MakeShort() => new(1, 0, 5, 9);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("0, 5, 9");
        _ = await Assert.That(source.Substring(diagnostics[1].Location.SourceSpan.Start, diagnostics[1].Location.SourceSpan.Length)).IsEqualTo("0, 5, 9");
    }
}
