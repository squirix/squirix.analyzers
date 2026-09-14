using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class OmitOuterLoopBracesAnalyzerTests
{
    private const string RuleId = "SQR0001";

    [Test]
    public async Task DoesNotFlagOuterLoopWithNonLoopBody(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOuterLoopContainingOnlyNestedLoop(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
