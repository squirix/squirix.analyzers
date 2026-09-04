using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class CoalesceThrowIfNullAnalyzerTests
{
    private const string RuleId = "SQR0023";

    [Fact]
    public async Task FlagsCoalesceThrowingArgumentNullException()
    {
        const string source = """
            class C
            {
                private readonly object _value;

                C(object value)
                {
                    _value = value ?? throw new System.ArgumentNullException(nameof(value));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsCoalesceThrowingOtherExceptionType()
    {
        const string source = """
            class C
            {
                private readonly object _value;

                C(object value)
                {
                    _value = value ?? throw new System.InvalidOperationException("Missing.");
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsCoalesceWithFallbackValue()
    {
        const string source = """
            class C
            {
                private readonly object _value;

                C(object value)
                {
                    _value = value ?? new object();
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsAlreadyUsingThrowHelper()
    {
        const string source = """
            class C
            {
                private readonly object _value;

                C(object value)
                {
                    System.ArgumentNullException.ThrowIfNull(value);
                    _value = value;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsGloballyQualifiedArgumentNullException()
    {
        const string source = """
            class C
            {
                private readonly object _value;

                C(object value)
                {
                    _value = value ?? throw new global::System.ArgumentNullException(nameof(value));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}
