using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class MergeDuplicateCatchBlocksAnalyzerTests
{
    private const string RuleId = "SQR0020";

    [Test]
    public async Task AllowsCatchBlocksWithDifferentBodies(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsConsecutiveIdenticalCatchBlocks(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
