using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class PreferEqualityOperatorAnalyzerTests : AnalyzerTestBase
{
    private const string NullCheckRuleId = "SQR0012";
    private const string IsConstantRuleId = "SQR0013";
    private const string IsNotConstantRuleId = "SQR0014";

    [Fact]
    public async Task FlagsIsNullPattern()
    {
        const string source = """
            class C
            {
                void M(string value)
                {
                    if (value is null)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(NullCheckRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsEqualityOperatorForNullCheck()
    {
        const string source = """
            class C
            {
                void M(string value)
                {
                    if (value == null)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsIsConstantPattern()
    {
        const string source = """
            class C
            {
                void M(int value)
                {
                    if (value is 42)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(IsConstantRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsEqualityOperatorForConstant()
    {
        const string source = """
            class C
            {
                void M(int value)
                {
                    if (value == 42)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsIsNotConstantPattern()
    {
        const string source = """
            class C
            {
                void M(int value)
                {
                    if (value is not 42)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(IsNotConstantRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsInequalityOperatorForConstant()
    {
        const string source = """
            class C
            {
                void M(int value)
                {
                    if (value != 42)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsIsNullPatternOnRecord()
    {
        const string source = """
            record R(string Value);

            class C
            {
                void M(R value)
                {
                    if (value is null)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(NullCheckRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsIsNotNullPatternOnRecord()
    {
        const string source = """
            record R(string Value);

            class C
            {
                void M(R value)
                {
                    if (value is not null)
                        return;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(NullCheckRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsIsNullForUnconstrainedTypeParameter()
    {
        const string source = """
            class C
            {
                bool M<T>(T? value)
                {
                    return value is null;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(NullCheckRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsIsNullWithStructConstraint()
    {
        const string source = """
            class C
            {
                bool M<T>(T value) where T : struct
                {
                    return value is null;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsNullArmInOrPattern()
    {
        const string source = """
            class C
            {
                string M(string? name)
                {
                    if (name is null or { Length: 0 })
                        return string.Empty;

                    return name;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(NullCheckRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsIsNullOnInterfaceType()
    {
        const string source = """
            interface IFoo
            {
            }

            class C
            {
                bool M(IFoo? value)
                {
                    return value is null;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(NullCheckRuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsOrPatternWithoutNullArm()
    {
        const string source = """
            class C
            {
                bool M(string value)
                {
                    return value is { Length: 0 } or { Length: 1 };
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
