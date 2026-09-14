using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class MethodNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0005";

    [Test]
    public async Task AllowsShortMethodName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void DoWork()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOverLongMethodName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void ThisMethodNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
