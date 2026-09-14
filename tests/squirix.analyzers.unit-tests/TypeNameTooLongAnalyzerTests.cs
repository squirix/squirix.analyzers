using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class TypeNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0004";

    [Test]
    public async Task AllowsShortTypeName(CancellationToken cancellationToken)
    {
        const string source = """
                              class Cache
                              {
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOverLongTypeName(CancellationToken cancellationToken)
    {
        const string source = """
                              class ThisTypeNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit
                              {
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
