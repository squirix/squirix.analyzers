using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class TooManyMethodsAnalyzerTests
{
    private const string RuleId = "SQR0002";

    [Fact]
    public async Task FlagsTypeWithMoreThanTwentyMethods()
    {
        const string header = "class Big\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var methods = string.Join("\n", Enumerable.Range(1, 21).Select(static i => $"    void M{i:00}() {{ }}"));
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task DoesNotFlagTypeWithinLimit()
    {
        const string header = "class Small\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var methods = string.Join("\n", Enumerable.Range(1, 3).Select(static i => $"    void M{i:00}() {{ }}"));
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UsesConfigurableThreshold()
    {
        const string header = "class Big\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var methods = string.Join("\n", Enumerable.Range(1, 5).Select(static i => $"    void M{i:00}() {{ }}"));
        var source = header + methods + footer;
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0002.max_methods_per_type", "3");

        var diagnostics = await AnalyzerRunner.RunAsync(
            new TooManyMethodsAnalyzer(),
            source,
            TestContext.Current.CancellationToken,
            options);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task FlagsStatelessTypeWithoutFields()
    {
        const string header = "class Util\n{\n";
        const string footer = "\n}\n";
        var methods = string.Join("\n", Enumerable.Range(1, 21).Select(static i => $"    static void M{i:00}() {{ }}"));
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task DoesNotFlagTypeWithOnlyConstants()
    {
        const string header = "class Constants\n{\n    public const int A = 1;\n    public const int B = 2;\n\n";
        const string footer = "\n}\n";
        var methods = string.Join("\n", Enumerable.Range(1, 21).Select(static i => $"    static void M{i:00}() {{ }}"));
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
