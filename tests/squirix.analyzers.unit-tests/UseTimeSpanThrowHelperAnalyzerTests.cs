using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class UseTimeSpanThrowHelperAnalyzerTests
{
    private const string RuleId = "SQR0022";

    [Test]
    public async Task AllowsNullableTimeSpanGuard(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(System.TimeSpan? value)
                                  {
                                      if (value < System.TimeSpan.Zero)
                                          throw new System.ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNumericComparisonGuard(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int value)
                                  {
                                      if (value < 0)
                                          throw new System.ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsTimeSpanGuardOtherException(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(System.TimeSpan value)
                                  {
                                      if (value < System.TimeSpan.Zero)
                                          throw new System.InvalidOperationException("Not ready.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUserDefinedTimeSpanZero(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  public struct TimeSpan
                                  {
                                      public static readonly TimeSpan Zero = default;
                                      public static bool operator <(TimeSpan a, TimeSpan b) => false;
                                      public static bool operator >(TimeSpan a, TimeSpan b) => false;
                                  }
                              }

                              class C
                              {
                                  void M(Other.TimeSpan value)
                                  {
                                      if (value < Other.TimeSpan.Zero)
                                          throw new System.ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsGloballyQualifiedTimeSpanZero(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(System.TimeSpan value)
                                  {
                                      if (value < global::System.TimeSpan.Zero)
                                          throw new global::System.ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsLessOrEqualZeroGuardBracedBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(System.TimeSpan value)
                                  {
                                      if (value <= System.TimeSpan.Zero)
                                      {
                                          throw new System.ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsLessThanZeroGuard(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(System.TimeSpan value)
                                  {
                                      if (value < System.TimeSpan.Zero)
                                          throw new System.ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task MessageOmitsUncompilableBclHelpers(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(System.TimeSpan value)
                                  {
                                      if (value <= System.TimeSpan.Zero)
                                          throw new System.ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).DoesNotContain("ArgumentOutOfRangeException.ThrowIf");
    }
}
