using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class RedundantNamedArgumentAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0009";

    [Fact]
    public async Task FlagsInOrderNamedArgument()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantNamedArgumentAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsOutOfOrderNamedArgument()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RedundantNamedArgumentAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
