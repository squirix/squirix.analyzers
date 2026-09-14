using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class OmitSingleStatementBracesAnalyzerTests
{
    private const string RuleId = "SQR0010";

    [Test]
    public async Task DoesNotFlagUnbracedIfBody(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsSingleLineBracedIfBody(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
