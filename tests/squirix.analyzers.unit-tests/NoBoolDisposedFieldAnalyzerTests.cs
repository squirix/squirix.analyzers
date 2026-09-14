using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoBoolDisposedFieldAnalyzerTests
{
    private const string BoolRuleId = "SQR0015";
    private const string IntRuleId = "SQR0016";

    [Test]
    public async Task AllowsIntDisposedField(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIntFlagNestedInInterlockedCall(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIntFlagViaInterlockedAndVolatile(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNameofDisposedField(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsQualifiedInterlockedVolatile(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBareIntDisposedField(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(IntRuleId);
        _ = await Assert.That(diagnostics[1].Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task FlagsBoolDisposedField(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private bool _disposed;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(BoolRuleId);
    }
}
