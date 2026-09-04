using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineLoopBodyBracesAnalyzerTests
{
    private const string RuleId = "SQR0008";

    [Fact]
    public async Task FlagsMultilineEmbeddedLoopBody()
    {
        const string source = """
            class C
            {
                void M()
                {
                    int i = 0;
                    while (i < 10)
                        DoSomething(
                            i);
                }

                void DoSomething(int value)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsSingleLineEmbeddedLoopBody()
    {
        const string source = """
            class C
            {
                void M()
                {
                    int i = 0;
                    while (i < 10)
                        i++;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
