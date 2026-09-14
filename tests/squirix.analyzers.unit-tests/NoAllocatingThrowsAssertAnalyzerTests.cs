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
                                      Throws<System.InvalidOperationException>(() => { });
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
                              class C
                              {
                                  void M()
                                  {
                                      ThrowExactly(System.InvalidOperationException, MyFunc);
                                  }

                                  static void ThrowExactly(System.Type type, System.Func<object?> action)
                                  {
                                  }

                                  static object? MyFunc() => null;
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
}
