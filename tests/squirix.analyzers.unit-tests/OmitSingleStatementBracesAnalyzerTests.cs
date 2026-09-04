using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class OmitSingleStatementBracesAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0010";

    [Fact]
    public async Task FlagsSingleLineBracedIfBody()
    {
        const string source = """
            class C
            {
                void M(bool flag)
                {
                    if (flag)
                    {
                        Call();
                    }
                }

                void Call()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotFlagUnbracedIfBody()
    {
        const string source = """
            class C
            {
                void M(bool flag)
                {
                    if (flag)
                        Call();
                }

                void Call()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
