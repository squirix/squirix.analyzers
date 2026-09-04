using System.Collections.Immutable;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class TooManyFieldsAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0003";

    [Fact]
    public async Task FlagsTypeWithMoreThanFifteenFields()
    {
        const string header = "class Big\n{\n";
        const string footer = "\n}\n";
        var bigFieldLines = new string[16];
        for (var i = 0; i < bigFieldLines.Length; i++)
            bigFieldLines[i] = $"    private int _f{i + 1:00};";
        var fields = string.Join("\n", bigFieldLines);
        var source = header + fields + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyFieldsAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotFlagTypeWithinLimit()
    {
        const string header = "class Small\n{\n";
        const string footer = "\n}\n";
        var smallFieldLines = new string[3];
        for (var i = 0; i < smallFieldLines.Length; i++)
            smallFieldLines[i] = $"    private int _f{i + 1:00};";
        var fields = string.Join("\n", smallFieldLines);
        var source = header + fields + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyFieldsAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UsesConfigurableThreshold()
    {
        const string header = "class Big\n{\n";
        const string footer = "\n}\n";
        var limitedFieldLines = new string[5];
        for (var i = 0; i < limitedFieldLines.Length; i++)
            limitedFieldLines[i] = $"    private int _f{i + 1:00};";
        var fields = string.Join("\n", limitedFieldLines);
        var source = header + fields + footer;
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0003.max_fields_per_type", "3");

        var diagnostics = await AnalyzerRunner.RunAsync(
            new TooManyFieldsAnalyzer(),
            source,
            DefaultCancellationToken,
            options);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}
