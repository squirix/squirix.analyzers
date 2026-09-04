using System.Collections.Immutable;
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
        var methodLines = new string[21];
        for (var i = 0; i < methodLines.Length; i++)
            methodLines[i] = $"    void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", methodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotFlagTypeWithinLimit()
    {
        const string header = "class Small\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var smallMethodLines = new string[3];
        for (var i = 0; i < smallMethodLines.Length; i++)
            smallMethodLines[i] = $"    void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", smallMethodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UsesConfigurableThreshold()
    {
        const string header = "class Big\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var limitedMethodLines = new string[5];
        for (var i = 0; i < limitedMethodLines.Length; i++)
            limitedMethodLines[i] = $"    void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", limitedMethodLines);
        var source = header + methods + footer;
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0002.max_methods_per_type", "3");

        var diagnostics = await AnalyzerRunner.RunAsync(
            new TooManyMethodsAnalyzer(),
            source,
            TestContext.Current.CancellationToken,
            options);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsStatelessTypeWithoutFields()
    {
        const string header = "class Util\n{\n";
        const string footer = "\n}\n";
        var staticMethodLines = new string[21];
        for (var i = 0; i < staticMethodLines.Length; i++)
            staticMethodLines[i] = $"    static void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", staticMethodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotFlagTypeWithOnlyConstants()
    {
        const string header = "class Constants\n{\n    public const int A = 1;\n    public const int B = 2;\n\n";
        const string footer = "\n}\n";
        var constMethodLines = new string[21];
        for (var i = 0; i < constMethodLines.Length; i++)
            constMethodLines[i] = $"    static void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", constMethodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
