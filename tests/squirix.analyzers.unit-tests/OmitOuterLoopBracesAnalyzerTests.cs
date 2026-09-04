using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class OmitOuterLoopBracesAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0001";

    [Fact]
    public async Task FlagsOuterLoopContainingOnlyNestedLoop()
    {
        const string source = """
            class C
            {
                void M()
                {
                    int i = 0;
                    while (i < 10)
                    {
                        for (int j = 0; j < 10; j++)
                        {
                        }
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotFlagOuterLoopWithNonLoopBody()
    {
        const string source = """
            class C
            {
                void M()
                {
                    int i = 0;
                    while (i < 10)
                    {
                        i++;
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
