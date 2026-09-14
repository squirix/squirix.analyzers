using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineIfBracesAnalyzerTests
{
    private const string RuleId = "SQR0018";

    [Test]
    public async Task AllowsSingleLineEmbeddedIfBody(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsMultilineEmbeddedIfBody(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
