using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoBoolDisposedFieldAnalyzerTests : AnalyzerTestBase
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(BoolRuleId, diagnostic.Id);
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsBareIntDisposedField()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, DefaultCancellationToken);

        Assert.Equal(2, diagnostics.Length);
        Assert.Equal(IntRuleId, diagnostics[0].Id);
        Assert.Equal(IntRuleId, diagnostics[1].Id);
    }

    [Fact]
    public async Task AllowsIntFlagViaInterlockedAndVolatile()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsIntFlagNestedInInterlockedCall()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsQualifiedInterlockedVolatile()
    {
        const string source = """
            class C
            {
                private int _disposed;

                void Dispose()
                {
                    System.Threading.Interlocked.Exchange(ref _disposed, 1);
                }

                bool IsDisposed => System.Threading.Volatile.Read(ref _disposed) != 0;
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsNameofDisposedField()
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

                string Name => nameof(_disposed);
            }
            """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
