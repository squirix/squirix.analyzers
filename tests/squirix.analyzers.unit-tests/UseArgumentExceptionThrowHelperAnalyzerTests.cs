using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class UseArgumentExceptionThrowHelperAnalyzerTests
{
    private const string RuleId = "SQR0021";

    [Fact]
    public async Task FlagsIsNullOrWhiteSpaceGuardThrowingArgumentException()
    {
        const string source = """
            class C
            {
                void M(string value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        throw new System.ArgumentException("Value is required.", nameof(value));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsIsNullOrEmptyGuardWithBracedBody()
    {
        const string source = """
            class C
            {
                void M(string value)
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        throw new System.ArgumentException("Value is required.", nameof(value));
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsGuardThrowingOtherExceptionType()
    {
        const string source = """
            class C
            {
                void M(string value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        throw new System.InvalidOperationException("Not ready.");
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsNonStringCheckThrowingArgumentException()
    {
        const string source = """
            class C
            {
                void M(byte[] buffer)
                {
                    if (buffer.Length == 0)
                        throw new System.ArgumentException("Buffer is empty.", nameof(buffer));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsAlreadyUsingThrowHelper()
    {
        const string source = """
            class C
            {
                void M(string value)
                {
                    System.ArgumentException.ThrowIfNullOrWhiteSpace(value);
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsUnrelatedIsNullOrEmptyHelper()
    {
        const string source = """
            static class Helpers
            {
                public static bool IsNullOrEmpty(string? value) => value == null;
            }

            class C
            {
                void M(string value)
                {
                    if (Helpers.IsNullOrEmpty(value))
                        throw new System.ArgumentException("Value is required.", nameof(value));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsGloballyQualifiedForms()
    {
        const string source = """
            class C
            {
                void M(string value)
                {
                    if (global::System.String.IsNullOrEmpty(value))
                        throw new global::System.ArgumentException("Value is required.", nameof(value));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsMismatchedParamName()
    {
        const string source = """
            class C
            {
                void M(string a, string b)
                {
                    if (string.IsNullOrEmpty(a))
                        throw new System.ArgumentException("Value is required.", nameof(b));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsUserDefinedArgumentException()
    {
        const string source = """
            namespace Other
            {
                public class ArgumentException : System.Exception
                {
                    public ArgumentException(string message, string paramName) : base(message) { }
                }
            }

            class C
            {
                void M(string value)
                {
                    if (string.IsNullOrEmpty(value))
                        throw new Other.ArgumentException("Value is required.", nameof(value));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
