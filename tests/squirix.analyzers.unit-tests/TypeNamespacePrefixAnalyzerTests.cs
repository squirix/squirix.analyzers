using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class TypeNamespacePrefixAnalyzerTests
{
    private const string RuleId = "SQR0007";

    [Test]
    public async Task AllowsNonRepeatingNamespaceSegment(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Acme
                              {
                                  class Cache
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNamespacePrefixAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsRepeatingNamespaceSegment(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Acme
                              {
                                  class AcmeCache
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNamespacePrefixAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
