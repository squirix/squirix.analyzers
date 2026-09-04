using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class RedundantDefaultArgumentAnalyzerTests
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsArgumentDifferentFromParameterDefault()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantDefaultArgumentAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
