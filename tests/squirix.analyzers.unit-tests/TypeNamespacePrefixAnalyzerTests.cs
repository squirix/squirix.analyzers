using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class TypeNamespacePrefixAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0007";

    [Fact]
    public async Task AllowsNonRepeatingNamespaceSegment()
    {
        const string source = """
                              namespace Acme
                              {
                                  class Cache
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNamespacePrefixAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsRepeatingNamespaceSegment()
    {
        const string source = """
                              namespace Acme
                              {
                                  class AcmeCache
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNamespacePrefixAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}
