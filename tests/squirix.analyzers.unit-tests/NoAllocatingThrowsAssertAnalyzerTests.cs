using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoAllocatingThrowsAssertAnalyzerTests
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
                    Other.Assert.Throws<System.InvalidOperationException>(() => { });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([RuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task FlagsFluentAssertionsThrowWithDelegateArgument()
    {
        const string source = """
            class C
            {
                void M(System.Action action)
                {
                    action.Should().Throw<System.InvalidOperationException>(() => { });
                }
            }

            static class ShouldExtensions
            {
                public static T Should<T>(this T value) => value;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

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
                    AssertThrows.ThrowExactly(typeof(System.InvalidOperationException), () => { });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsStaticLambdaWithoutCaptureAllocation()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsFullyQualifiedNamespaceThrowsWithDelegateArgument()
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
                    Fully.Qualified.Tests.AssertHelpers.Throws<System.InvalidOperationException>(() => { });
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
