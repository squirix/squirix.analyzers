using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineIfBodyBracesAnalyzerTests
{
    private const string RuleId = "SQR0018";

    [Fact]
    public async Task FlagsMultilineEmbeddedIfBody()
    {
        const string source = """
            class C
            {
                void M(bool flag)
                {
                    if (flag)
                        DoSomething(
                            flag);
                }

                void DoSomething(bool value)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsSingleLineEmbeddedIfBody()
    {
        const string source = """
            class C
            {
                void M(bool flag)
                {
                    if (flag)
                        flag = false;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
