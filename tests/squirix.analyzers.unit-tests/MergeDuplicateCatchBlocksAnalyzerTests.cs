using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class MergeDuplicateCatchBlocksAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0020";

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

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsConsecutiveIdenticalCatchBlocks()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}
