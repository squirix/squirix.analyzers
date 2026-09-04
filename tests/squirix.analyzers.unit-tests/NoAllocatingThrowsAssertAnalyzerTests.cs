using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoAllocatingThrowsAssertAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0019";

    [Fact]
    public async Task FlagsAnyThrowsMethodWithDelegateArgument()
    {
        const string source = """
            namespace Other
            {
                static class Assert
                {
                    public static void Throws<T>(System.Action action) where T : System.Exception
                    {
                    }
                }
            }

            class C
            {
                void M()
                {
                    var x = 0;
                    Other.Assert.Throws<System.InvalidOperationException>(() => { x++; });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsFluentThrowWithDelegateArgument()
    {
        const string source = """
            class C
            {
                void M(System.Action action)
                {
                    action.Should().Throw<System.InvalidOperationException>(() => action());
                }
            }

            static class ShouldExtensions
            {
                public static T Should<T>(this T value) => value;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        _ = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostics[0].Id);
    }

    [Fact]
    public async Task FlagsThrowExactlyWithDelegateArgument()
    {
        const string source = """
            static class AssertThrows
            {
                public static void ThrowExactly(System.Type type, System.Action action)
                {
                }
            }

            class C
            {
                void M()
                {
                    var x = 0;
                    AssertThrows.ThrowExactly(typeof(System.InvalidOperationException), () => { x++; });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        _ = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostics[0].Id);
    }

    [Fact]
    public async Task AllowsThrowMethodWithoutDelegateArgument()
    {
        const string source = """
            class C
            {
                void M()
                {
                    ThrowExactly(System.InvalidOperationException, MyFunc);
                }

                static void ThrowExactly(System.Type type, System.Func<object?> action)
                {
                }

                static object? MyFunc() => null;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsStaticLambdaWithoutCapture()
    {
        const string source = """
            namespace Other
            {
                static class Assert
                {
                    public static void Throws<T>(System.Action action) where T : System.Exception
                    {
                    }
                }
            }

            class C
            {
                void M()
                {
                    Other.Assert.Throws<System.InvalidOperationException>(static () => { });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsNonCapturingLambdaWithoutStatic()
    {
        const string source = """
            namespace Other
            {
                static class Assert
                {
                    public static void Throws<T>(System.Action action) where T : System.Exception
                    {
                    }
                }
            }

            class C
            {
                void M()
                {
                    Other.Assert.Throws<System.InvalidOperationException>(() => StaticHelper());
                }

                static void StaticHelper()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsEmptyLambdaWithoutCapture()
    {
        const string source = """
            namespace Other
            {
                static class Assert
                {
                    public static void Throws<T>(System.Action action) where T : System.Exception
                    {
                    }
                }
            }

            class C
            {
                void M()
                {
                    Other.Assert.Throws<System.InvalidOperationException>(() => { });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsUnrelatedMethod()
    {
        const string source = """
            class C
            {
                void M()
                {
                    DoWork();
                }

                void DoWork()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsQualifiedThrowsWithDelegateArgument()
    {
        const string source = """
            namespace Fully.Qualified.Tests
            {
                static class AssertHelpers
                {
                    public static void Throws<T>(System.Action action) where T : System.Exception
                    {
                    }
                }
            }

            class C
            {
                void M()
                {
                    var x = 0;
                    Fully.Qualified.Tests.AssertHelpers.Throws<System.InvalidOperationException>(() => { x++; });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        _ = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostics[0].Id);
    }

    [Fact]
    public async Task AllowsBareThrowsCallWithoutMemberAccess()
    {
        const string source = """
            class C
            {
                void M()
                {
                    Throws<System.InvalidOperationException>(() => { });
                }

                static void Throws<T>(System.Action action) where T : System.Exception
                {
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
