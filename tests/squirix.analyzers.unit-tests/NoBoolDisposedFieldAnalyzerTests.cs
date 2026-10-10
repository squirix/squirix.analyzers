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
    public async Task AllowsParenthesizedRefOperand(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose() => Interlocked.Exchange(ref (_disposed), 1);

                                  bool IsDisposed() => Volatile.Read(ref (this._disposed)) != 0;

                                  static bool IsClosed(C other) => Volatile.Read(in ((other._disposed))) != 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The call receives a reference to whichever flag the condition picks, so both branches are guarded.</summary>
    /// <summary>These wrappers leave the operand the same reference to the flag.</summary>
    [Test]
    public async Task AllowsCheckedAndNullForgivingOperand(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  void Dispose() => Interlocked.Exchange(ref checked(_disposed), 1);

                                  void Reset() => Interlocked.Exchange(ref unchecked((this._disposed)), 0);

                                  bool IsDisposed() => Volatile.Read(in _disposed!) != 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Volatile.Read takes its location by readonly reference, so the flag is passed by reference without a keyword too.</summary>
    [Test]
    public async Task AllowsReferenceOperandWithoutKeyword(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  bool IsDisposed() => Volatile.Read(_disposed) != 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Until a call binds, the rule cannot tell whether it guards the flag.</summary>
    [Test]
    public async Task LeavesCallThatDoesNotBindAlone(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int _disposed;

                                  void Dispose() => Missing.Exchange(ref _disposed, 1);

                                  bool IsDisposed() => _disposed != 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunOnIncompleteCodeAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(6);
    }

    [Test]
    public async Task AllowsRefConditionalOperand(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;

                                  static void Close(bool first, C a, C b) => Interlocked.Exchange(ref (first ? ref a._disposed : ref b._disposed), 1);

                                  void CloseOne(bool mine, bool first, C a, C b) =>
                                      Interlocked.Exchange(ref (mine ? ref _disposed : ref (first ? ref (a._disposed) : ref b._disposed)), 1);

                                  static void CloseBare(bool first, C a, C b) => Interlocked.Exchange(ref first ? ref a._disposed : ref b._disposed, 1);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The condition reads the flag; only the branches of a ref conditional stand for the reference.</summary>
    [Test]
    public async Task FlagsFlagInRefConditionalCondition(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;
                                  private int _first;
                                  private int _second;

                                  void M() => Interlocked.Exchange(ref (_disposed == 0 ? ref _first : ref _second), 1);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IntRuleId);
    }

    /// <summary>Only the ref operand is guarded; as the value argument the flags are read.</summary>
    [Test]
    public async Task FlagsRefConditionalAsInterlockedValue(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Threading;

                              class C
                              {
                                  private int _disposed;
                                  private int _state;

                                  static void Copy(bool first, C a, C b) => Interlocked.Exchange(ref a._state, (first ? ref a._disposed : ref b._disposed));

                                  void CopyOwn() => Interlocked.Exchange(ref _state, (_disposed));
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(3);
    }

    [Test]
    public async Task FlagsRefConditionalOutsideInterlocked(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int _disposed;

                                  static void Close(bool first, C a, C b)
                                  {
                                      ref var flag = ref (first ? ref a._disposed : ref b._disposed);
                                      flag = 1;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
    }

    [Test]
    public async Task FlagsParenthesizedPlainAccess(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int _disposed;

                                  bool IsDisposed() => (_disposed) != 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoBoolDisposedFieldAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IntRuleId);
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
