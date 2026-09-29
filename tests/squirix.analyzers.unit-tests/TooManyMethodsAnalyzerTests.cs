using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class TooManyMethodsAnalyzerTests
{
    private const string RuleId = "SQR0002";

    [Test]
    public async Task DoesNotFlagTypeWithOnlyConstants(CancellationToken cancellationToken)
    {
        const string header = "class Constants\n{\n    public const int A = 1;\n    public const int B = 2;\n\n";
        const string footer = "\n}\n";
        var constMethodLines = new string[21];
        for (var i = 0; i < constMethodLines.Length; i++)
            constMethodLines[i] = $"    static void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", constMethodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task DoesNotFlagTypeWithinLimit(CancellationToken cancellationToken)
    {
        const string header = "class Small\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var smallMethodLines = new string[3];
        for (var i = 0; i < smallMethodLines.Length; i++)
            smallMethodLines[i] = $"    void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", smallMethodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsStatelessTypeWithoutFields(CancellationToken cancellationToken)
    {
        const string header = "class Util\n{\n";
        const string footer = "\n}\n";
        var staticMethodLines = new string[21];
        for (var i = 0; i < staticMethodLines.Length; i++)
            staticMethodLines[i] = $"    static void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", staticMethodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTypeWithMoreThanTwentyMethods(CancellationToken cancellationToken)
    {
        const string header = "class Big\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var methodLines = new string[21];
        for (var i = 0; i < methodLines.Length; i++)
            methodLines[i] = $"    void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", methodLines);
        var source = header + methods + footer;

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public Task FlagsClassWithAutoProperty(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("class C\n{\n    public int P { get; set; }\n\n", "\n}\n", cancellationToken);

    [Test]
    public Task FlagsRecordWithPositionalParams(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("record R(int A)\n{\n", "\n}\n", cancellationToken);

    [Test]
    public Task FlagsStructWithAutoProperty(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("struct S\n{\n    public int P { get; set; }\n\n", "\n}\n", cancellationToken);

    [Test]
    public Task FlagsClassWithEventBackingField(CancellationToken cancellationToken) =>
        AssertFlaggedAsync(
            "class E\n{\n    public event System.EventHandler Ev;\n\n    void Raise() { Ev?.Invoke(this, System.EventArgs.Empty); }\n\n",
            "\n}\n",
            cancellationToken);

    [Test]
    public Task FlagsPrimaryConstructorClass(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("class P(int a)\n{\n    int Get() => a;\n\n", "\n}\n", cancellationToken);

    [Test]
    public Task FlagsConstAndInstanceField(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("class C\n{\n    public const int A = 1;\n    private int _state;\n\n", "\n}\n", cancellationToken);

    [Test]
    public Task FlagsStaticClass(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("static class U\n{\n", "\n}\n", cancellationToken, true);

    [Test]
    public async Task DoesNotFlagStaticClassWithOnlyConstants(CancellationToken cancellationToken)
    {
        var source = BuildSource("static class K\n{\n    public const int A = 1;\n\n", "\n}\n", true);

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task UsesConfigurableThreshold(CancellationToken cancellationToken)
    {
        const string header = "class Big\n{\n    private readonly int _state = 1;\n\n";
        const string footer = "\n}\n";
        var limitedMethodLines = new string[5];
        for (var i = 0; i < limitedMethodLines.Length; i++)
            limitedMethodLines[i] = $"    void M{i + 1:00}() {{ }}";
        var methods = string.Join("\n", limitedMethodLines);
        var source = header + methods + footer;
        var options = ImmutableDictionary.Create<string, string>().Add("SQR0002.max_methods_per_type", "3");

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, options, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static async Task AssertFlaggedAsync(string header, string footer, CancellationToken cancellationToken, bool isStatic = false)
    {
        var source = BuildSource(header, footer, isStatic);

        var diagnostics = await AnalyzerRunner.RunAsync(new TooManyMethodsAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static string BuildSource(string header, string footer, bool isStatic)
    {
        var modifier = isStatic ? "static " : string.Empty;
        var lines = new string[25];
        for (var i = 0; i < lines.Length; i++)
            lines[i] = $"    {modifier}void M{i + 1:00}() {{ }}";

        return header + string.Join("\n", lines) + footer;
    }
}
