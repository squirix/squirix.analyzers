using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class MethodNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0005";

    [Fact]
    public async Task FlagsOverLongMethodName()
    {
        const string source = """
            class C
            {
                void ThisMethodNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsShortMethodName()
    {
        const string source = """
            class C
            {
                void DoWork()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
