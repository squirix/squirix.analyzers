using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoAllocatingXunitThrowsAssertAnalyzerTests
{
    private const string RuleId = "SQR0019";

    [Fact]
    public async Task FlagsXunitAssertThrows()
    {
        const string source = """
            namespace Xunit
            {
                static class Assert
                {
                    public static void Throws<T>(System.Action action) where T : System.Exception
                    {
                    }
                }
            }

            class C
            {
                void M()
                {
                    Xunit.Assert.Throws<System.InvalidOperationException>(() => { });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingXunitThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsNonThrowsAssertion()
    {
        const string source = """
            class C
            {
                void M()
                {
                    DoWork();
                }

                void DoWork()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingXunitThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
