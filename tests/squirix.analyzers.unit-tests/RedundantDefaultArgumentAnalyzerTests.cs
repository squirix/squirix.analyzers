using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class RedundantDefaultArgumentAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0011";

    [Fact]
    public async Task FlagsArgumentEqualToParameterDefault()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsArgumentNotEqualToDefault()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsDefaultWhenNotEqual()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsDefaultLiteralWhenEqualToDefault()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsDefaultForNullDefault()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsDefaultForNonNullDefault()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
