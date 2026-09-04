using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoDirectTestContextCancelTokenAnalyzerTests
{
    private const string RuleId = "SQR0017";

    [Fact]
    public async Task FlagsDirectTestContextCancellationTokenUse()
    {
        const string source = """
            class C
            {
                void M()
                {
                    var token = TestContext.Current.CancellationToken;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsUseWhenTypeDeclaresDefaultCancellationToken()
    {
        const string source = """
            class C
            {
                private System.Threading.CancellationToken DefaultCancellationToken
                    => System.Threading.CancellationToken.None;

                void M()
                {
                    var token = TestContext.Current.CancellationToken;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsUseWhenDeclaringSharedTokenOfAnyName()
    {
        const string source = """
            class C
            {
                private System.Threading.CancellationToken SharedToken
                    => System.Threading.CancellationToken.None;

                void M()
                {
                    var token = TestContext.Current.CancellationToken;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotFlagPreviousUseInsideStaticClass()
    {
        const string source = """
            static class C
            {
                static void M()
                {
                    var token = TestContext.Current.CancellationToken;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsUseWhenBaseClassDoesNotExposeToken()
    {
        const string source = """
            class Base
            {
            }

            class Derived : Base
            {
                void M()
                {
                    var token = TestContext.Current.CancellationToken;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsUseWhenBaseClassExposesSharedToken()
    {
        const string source = """
            class Base
            {
                protected System.Threading.CancellationToken SharedToken
                    => System.Threading.CancellationToken.None;
            }

            class Derived : Base
            {
                void M()
                {
                    var token = TestContext.Current.CancellationToken;
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
