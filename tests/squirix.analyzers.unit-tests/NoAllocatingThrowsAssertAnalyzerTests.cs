using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoAllocatingThrowsAssertAnalyzerTests
{
    private const string RuleId = "SQR0019";

    [Test]
    public async Task AllowsBareThrowsCallWithoutMemberAccess(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Throws<System.InvalidOperationException>(() => { x++; });
                                  }

                                  static void Throws<T>(System.Action action) where T : System.Exception
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsEmptyLambdaWithoutCapture(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => { });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNonCapturingLambdaWithoutStatic(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => StaticHelper());
                                  }

                                  static void StaticHelper()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStaticLambdaWithoutCapture(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(static () => { });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsThrowMethodWithoutDelegateArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void ThrowExactly(System.Type type, System.Func<object> action)
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.ThrowExactly(typeof(System.InvalidOperationException), MyFunc);
                                  }

                                  object MyFunc() => null;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUnrelatedMethod(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      DoWork();
                                  }

                                  void DoWork()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsAnyThrowsMethodWithDelegateArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Other.Assert.Throws<System.InvalidOperationException>(() => { x++; });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsFluentThrowWithDelegateArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(System.Action action)
                                  {
                                      action.Should().Throw<System.InvalidOperationException>(() => action());
                                  }
                              }

                              static class ShouldExtensions
                              {
                                  public static T Should<T>(this T value) => value;

                                  public static void Throw<TException>(this System.Action subject, System.Action action) where TException : System.Exception
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsQualifiedThrowsWithDelegateArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Fully.Qualified.Tests
                              {
                                  static class AssertHelpers
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Fully.Qualified.Tests.AssertHelpers.Throws<System.InvalidOperationException>(() => { x++; });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsThrowExactlyWithDelegateArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              static class AssertThrows
                              {
                                  public static void ThrowExactly(System.Type type, System.Action action)
                                  {
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      AssertThrows.ThrowExactly(typeof(System.InvalidOperationException), () => { x++; });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsMemberAccessAfterDot(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class Parser
                              {
                                  public int Parse(object o) => 0;
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => new Parser().Parse(null));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStringLengthAfterDot(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  int Length;

                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => _ = "abc".Length);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsObjectInitializerMember(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class O
                              {
                                  public int X { get; set; }
                              }

                              class C
                              {
                                  int X;

                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => _ = new O { X = 1 });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNamedArgumentLabel(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  int value;

                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => F(value: 1));
                                  }

                                  static void F(int value)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNonCapturingLocalFunctionCall(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => Local());

                                      void Local()
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNameofOfInstanceMember(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  int field;

                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => _ = nameof(field));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsLambdaLocalsAndParameters(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() =>
                                      {
                                          var y = 1;
                                          System.Func<int, int> f = a => a + y;
                                          _ = f(y);
                                      });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsCapturedLocal(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Other.Assert.Throws<System.InvalidOperationException>(() => _ = x);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsCapturedParameter(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M(int p)
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => _ = p);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsExplicitThis(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => _ = this);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsInstanceMemberCall(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => Instance());
                                  }

                                  void Instance()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsInstanceField(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  int field;

                                  void M()
                                  {
                                      Other.Assert.Throws<System.InvalidOperationException>(() => _ = field);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsNestedLambdaCapture(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Other.Assert.Throws<System.InvalidOperationException>(() =>
                                      {
                                          System.Func<int> f = () => x;
                                          _ = f();
                                      });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsCapturingLocalFunctionCall(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Other.Assert.Throws<System.InvalidOperationException>(() => Local());

                                      void Local()
                                      {
                                          _ = x;
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTransitivelyCapturingLocalFunction(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Other.Assert.Throws<System.InvalidOperationException>(() => Outer());

                                      void Outer() => Inner();

                                      void Inner()
                                      {
                                          _ = x;
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsAnonymousMethodCapture(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  static class Assert
                                  {
                                      public static void Throws<T>(System.Action action) where T : System.Exception
                                      {
                                      }
                                  }
                              }

                              class C
                              {
                                  void M()
                                  {
                                      var x = 0;
                                      Other.Assert.Throws<System.InvalidOperationException>(delegate { _ = x; });
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
