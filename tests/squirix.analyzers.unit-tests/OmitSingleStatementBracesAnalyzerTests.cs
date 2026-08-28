using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class OmitSingleStatementBracesAnalyzerTests
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

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
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

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
