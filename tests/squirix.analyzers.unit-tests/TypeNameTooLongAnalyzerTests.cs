using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class TypeNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0004";

    [Fact]
    public async Task FlagsOverLongTypeName()
    {
        const string source = """
            class ThisTypeNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit
            {
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNameTooLongAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsShortTypeName()
    {
        const string source = """
            class Cache
            {
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TypeNameTooLongAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
