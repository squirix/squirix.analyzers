using System.Linq;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoBoolDisposedFieldAnalyzerTests
{
    private const string BoolRuleId = "SQR0015";
    private const string IntRuleId = "SQR0016";

    [Fact]
    public async Task FlagsBoolDisposedField()
    {
        const string source = """
            class C
            {
                private bool _disposed;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([BoolRuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsIntDisposedField()
    {
        const string source = """
            using System.Threading;

            class C
            {
                private int _disposed;

                void Dispose()
                {
                    Interlocked.Exchange(ref _disposed, 1);
                }

                bool IsDisposed => Volatile.Read(ref _disposed) != 0;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsIntDisposedFieldAccessedWithoutInterlockedOrVolatile()
    {
        const string source = """
            class C
            {
                private int _disposed;

                void Dispose()
                {
                    _disposed = 1;
                }

                bool IsDisposed => _disposed != 0;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Equal([IntRuleId, IntRuleId], diagnostics.Select(static d => d.Id));
    }

    [Fact]
    public async Task AllowsIntDisposedFieldAccessedThroughInterlockedAndVolatile()
    {
        const string source = """
            using System.Threading;

            class C
            {
                private int _disposed;

                void Dispose()
                {
                    Interlocked.Exchange(ref _disposed, 1);
                }

                bool ShouldReturnBuffer => Volatile.Read(ref _disposed) != 0;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsIntDisposedFieldNestedInInterlockedArgument()
    {
        const string source = """
            using System.Threading;

            class C
            {
                private int _disposed;

                int Compute(int value) => value;

                void Dispose()
                {
                    Interlocked.Exchange(ref _disposed, Compute(_disposed));
                }
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
}
