using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class FieldNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0006";

    [Test]
    public async Task AllowsShortFieldName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int _state;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOverLongFieldName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int ThisFieldNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
