using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class UseTimeSpanThrowHelperAnalyzerTests
{
    private const string RuleId = "SQR0022";

    [Fact]
    public async Task FlagsLessThanZeroGuard()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task FlagsLessThanOrEqualZeroGuardWithBracedBody()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsNumericComparisonGuard()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsTimeSpanGuardThrowingOtherExceptionType()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsNullableTimeSpanGuard()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsGloballyQualifiedTimeSpanZero()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsUserDefinedTimeSpanZero()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new UseTimeSpanThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
