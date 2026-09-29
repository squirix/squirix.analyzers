using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineLoopBracesAnalyzerTests
{
    private const string RuleId = "SQR0008";

    [Test]
    public async Task AllowsSingleLineEmbeddedLoopBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      int i = 0;
                                      while (i < 10)
                                          i++;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsMultilineEmbeddedLoopBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      int i = 0;
                                      while (i < 10)
                                          DoSomething(
                                              i);
                                  }

                                  void DoSomething(int value)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
