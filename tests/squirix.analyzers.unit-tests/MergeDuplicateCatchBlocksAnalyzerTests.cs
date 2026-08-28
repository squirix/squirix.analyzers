using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class MergeDuplicateCatchBlocksAnalyzerTests
{
    private const string RuleId = "SQR0020";

    [Fact]
    public async Task FlagsConsecutiveCatchBlocksWithIdenticalBodies()
    {
        const string source = """
            class C
            {
                void M()
                {
                    try
                    {
                        DoWork();
                    }
                    catch (System.InvalidOperationException)
                    {
                        Handle();
                    }
                    catch (System.ArgumentException)
                    {
                        Handle();
                    }
                }

                void DoWork()
                {
                }

                void Handle()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsCatchBlocksWithDifferentBodies()
    {
        const string source = """
            class C
            {
                void M()
                {
                    try
                    {
                        DoWork();
                    }
                    catch (System.InvalidOperationException)
                    {
                        HandleOne();
                    }
                    catch (System.ArgumentException)
                    {
                        HandleTwo();
                    }
                }

                void DoWork()
                {
                }

                void HandleOne()
                {
                }

                void HandleTwo()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
