using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class TypeNamespacePrefixAnalyzerTests
{
    private const string RuleId = "SQR0007";

    [Fact]
    public async Task FlagsTypeThatRepeatsParentNamespaceSegment()
    {
        const string source = """
            namespace Acme
            {
                class AcmeCache
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNamespacePrefixAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsTypeThatDoesNotRepeatParentNamespaceSegment()
    {
        const string source = """
            namespace Acme
            {
                class Cache
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNamespacePrefixAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
