using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class FieldNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0006";

    [Fact]
    public async Task FlagsOverLongFieldName()
    {
        const string source = """
            class C
            {
                private int ThisFieldNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsShortFieldName()
    {
        const string source = """
            class C
            {
                private int _state;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
