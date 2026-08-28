using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class TooManyFieldsAnalyzerTests
{
    private const string RuleId = "SQR0003";

    [Fact]
    public async Task FlagsTypeWithMoreThanFifteenFields()
    {
        const string header = "class Big\n{\n";
        const string footer = "\n}\n";
        var fields = string.Join("\n", Enumerable.Range(1, 16).Select(static i => $"    private int _f{i:00};"));
        var source = header + fields + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyFieldsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task DoesNotFlagTypeWithinLimit()
    {
        const string header = "class Small\n{\n";
        const string footer = "\n}\n";
        var fields = string.Join("\n", Enumerable.Range(1, 3).Select(static i => $"    private int _f{i:00};"));
        var source = header + fields + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyFieldsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UsesConfigurableThreshold()
    {
        const string header = "class Big\n{\n";
        const string footer = "\n}\n";
        var fields = string.Join("\n", Enumerable.Range(1, 5).Select(static i => $"    private int _f{i:00};"));
        var source = header + fields + footer;
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0003.max_fields_per_type", "3");

        var diagnostics = await AnalyzerRunner.RunAsync(
            new TooManyFieldsAnalyzer(),
            source,
            TestContext.Current.CancellationToken,
            options);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }
}
