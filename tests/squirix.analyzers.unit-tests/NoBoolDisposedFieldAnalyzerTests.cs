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
    public async Task AllowsInterlockedExchangeRef(CancellationToken cancellationToken)
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
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsInterlockedCompareExchange(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose()
                                  {
                                      Interlocked.CompareExchange(ref _disposed, 1, 0);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsVolatileReadAndWrite(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose()
                                  {
                                      Volatile.Write(ref _disposed, 1);
                                  }

                                  bool IsDisposed => Volatile.Read(ref _disposed) != 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsThisQualifiedRefOperand(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose()
                                  {
                                      Interlocked.Exchange(ref this._disposed, 1);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsGuardedFlagOfAnotherInstance(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;
                                  private readonly C _owner = null!;

                                  static void Close(C other) => Interlocked.Exchange(ref other._disposed, 1);

                                  static bool IsClosed(C other) => Volatile.Read(in other._disposed) != 0;

                                  bool IsOwnerClosed() => Volatile.Read(ref this._owner._disposed) != 0;

                                  void CloseOwner() => Interlocked.CompareExchange(ref _owner._disposed, 1, 0);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsPointerAndStaticQualifiedFlag(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              struct State
                              {
                                  public int _disposed;
                              }

                              class C
                              {
                                  private static int _disposed;

                                  static void Close() => Interlocked.Exchange(ref C._disposed, 1);

                                  static unsafe void Close(State* state) => Interlocked.Exchange(ref state->_disposed, 1);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsPlainReadOfAnotherInstanceFlag(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int _disposed;

                                  static bool IsClosed(C other) => other._disposed != 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IntRuleId);
    }

    /// <summary>Only the ref operand is guarded; the flag of the other instance is a plain read here.</summary>
    [Test]
    public async Task FlagsOtherFlagAsInterlockedValue(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void CopyFrom(C other) => Interlocked.Exchange(ref _disposed, other._disposed);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task FlagsOtherFlagPassedToOwnMethod(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int _disposed;

                                  static void Close(C other) => Set(ref other._disposed);

                                  static void Set(ref int flag) { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task FlagsReadAsInterlockedValueArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose()
                                  {
                                      Interlocked.Exchange(ref _disposed, _disposed + 1);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task FlagsReadNestedInInterlockedCall(CancellationToken cancellationToken)
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

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task FlagsReadAsVolatileWriteValue(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose()
                                  {
                                      Volatile.Write(ref _disposed, _disposed);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task FlagsReadAsCompareExchangeComparand(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose()
                                  {
                                      Interlocked.CompareExchange(ref _disposed, 1, _disposed);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task FlagsPlainWriteAndRead(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose()
                                  {
                                      _disposed = 1;
                                  }

                                  bool IsDisposed => _disposed == 1;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(IntRuleId);
        _ = await Assert.That(diagnostics[1].Id).IsEqualTo(IntRuleId);
    }

    [Test]
    public async Task IgnoresDisposedNameWithOtherCasing(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private bool _Disposed;
                                  private int Disposed;

                                  void Dispose()
                                  {
                                      Disposed = 1;
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
