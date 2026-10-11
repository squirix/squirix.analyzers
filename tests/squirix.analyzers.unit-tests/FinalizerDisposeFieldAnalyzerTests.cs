using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class FinalizerDisposeFieldAnalyzerTests
{
    private const string FinalizableBase = """
                                           using System;
                                           using System.Collections.Generic;
                                           using System.Threading;

                                           class Base : IDisposable
                                           {
                                               protected Base(int value) { }

                                               ~Base() => Dispose(false);

                                               public void Dispose()
                                               {
                                                   Dispose(true);
                                                   GC.SuppressFinalize(this);
                                               }

                                               protected virtual void Dispose(bool disposing) { }
                                           }

                                           class Gate
                                           {
                                               public bool IsOpen { get; set; }

                                               public void Exit() { }
                                           }

                                           """;

    private const string RuleId = "SQR0033";

    [Test]
    public async Task FlagsNetworkStreamOverride(CancellationToken cancellationToken)
    {
        const string source = """
                              using System.Net.Sockets;

                              class Connections
                              {
                                  public void Exit() { }
                              }

                              sealed class TrackedStream : NetworkStream
                              {
                                  private readonly Connections _connections;

                                  public TrackedStream(Socket socket, Connections connections)
                                      : base(socket, true)
                                  {
                                      _connections = connections;
                                  }

                                  protected override void Dispose(bool disposing)
                                  {
                                      base.Dispose(disposing);
                                      _connections.Exit();
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FinalizerDisposeFieldAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).Count().IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("'_connections'");
    }

    [Test]
    public async Task FlagsMethodPropertyAndThisAccess(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                _gate.Exit();
                _ = _gate.IsOpen;
                this._gate.Exit();
                base.Dispose(disposing);
            }
        }
        """, 3, cancellationToken);

    [Test]
    public async Task FlagsDereferenceInFinally(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                try
                {
                    base.Dispose(disposing);
                }
                finally
                {
                    _gate.Exit();
                }
            }
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsDereferenceInElseOfDisposing(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    base.Dispose(true);
                else
                    _gate.Exit();
            }
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsDereferenceInIfCondition(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                if (_gate.IsOpen)
                    base.Dispose(disposing);
            }
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsForEachLockAndArrayElement(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly List<Gate> _gates;
            private readonly object _sync;
            private readonly Gate[] _array;

            public C(List<Gate> gates, object sync, Gate[] array) : base(1)
            {
                _gates = gates;
                _sync = sync;
                _array = array;
            }

            protected override void Dispose(bool disposing)
            {
                foreach (var gate in _gates)
                    gate.Exit();

                lock (_sync)
                {
                    _array[0] = null;
                }
            }
        }
        """, 3, cancellationToken);

    [Test]
    public async Task FlagsOwnFinalizerWithVirtualDispose(CancellationToken cancellationToken) => await AssertCountAsync("""
        using System;

        class Gate
        {
            public void Exit() { }
        }

        class C : IDisposable
        {
            private readonly Gate _gate;

            public C(Gate gate)
            {
                ArgumentNullException.ThrowIfNull(gate);
                _gate = gate;
            }

            ~C() => Dispose(false);

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing) => _gate.Exit();
        }
        """, 1, cancellationToken, false);

    [Test]
    public async Task FlagsNullCheckOfAnotherField(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;
            private Gate _lazy;

            public C(Gate gate) : base(1) => _gate = gate;

            public void Open() => _lazy = new Gate();

            protected override void Dispose(bool disposing)
            {
                if (_lazy != null)
                    _gate.Exit();
            }
        }
        """, 1, cancellationToken);

    [Test]
    public async Task AllowsDereferenceInsideDisposingBranch(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _gate.Exit();
                }

                if (!disposing)
                {
                }
                else
                {
                    _gate.Exit();
                }

                _ = disposing && _gate.IsOpen;
                _ = disposing ? _gate.IsOpen : false;
                base.Dispose(disposing);
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task AllowsDereferenceAfterEarlyReturn(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (!disposing)
                    return;

                try
                {
                }
                finally
                {
                    _gate.Exit();
                }
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task AllowsNullGuardedAccess(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _a;
            private readonly Gate _b;
            private readonly Gate _c;
            private readonly Gate _d;
            private readonly Gate _e;

            public C(Gate a, Gate b, Gate c, Gate d, Gate e) : base(1)
            {
                _a = a;
                _b = b;
                _c = c;
                _d = d;
                _e = e;
            }

            protected override void Dispose(bool disposing)
            {
                _a?.Exit();
                if (_b is not null)
                    _b.Exit();

                if (_c != null)
                    _c.Exit();

                _ = _d != null && _d.IsOpen;
                if (_e is null)
                    return;

                _e.Exit();
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task AllowsConstructorFlagGuard(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;
            private int _tracked;

            public C(Gate gate) : base(1)
            {
                _gate = gate;
                _tracked = 1;
            }

            protected override void Dispose(bool disposing)
            {
                try
                {
                    base.Dispose(disposing);
                }
                finally
                {
                    if (Interlocked.Exchange(ref _tracked, 0) == 1)
                        _gate.Exit();
                }
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task FlagsFlagWithInitializerAsNoGuard(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;
            private bool _alive = true;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                if (_alive)
                    _gate.Exit();
            }
        }
        """, 1, cancellationToken);

    [Test]
    public async Task AllowsFieldWithInitializer(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate = new();

            public C() : base(1) { }

            protected override void Dispose(bool disposing) => _gate.Exit();
        }
        """, 0, cancellationToken);

    [Test]
    public async Task AllowsBaseWithoutFinalizer(CancellationToken cancellationToken) => await AssertCountAsync("""
        using System.IO;

        class Gate
        {
            public void Exit() { }
        }

        sealed class C : MemoryStream
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(16) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                _gate.Exit();
                base.Dispose(disposing);
            }
        }
        """, 0, cancellationToken, false);

    [Test]
    public async Task AllowsValueTypeStaticAndLambdaAccess(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private static readonly Gate Shared = new();
            private readonly Gate _gate;
            private readonly int _count;

            public C(Gate gate, int count) : base(1)
            {
                _gate = gate;
                _count = count;
            }

            protected override void Dispose(bool disposing)
            {
                _ = _count.ToString();
                Shared.Exit();
                Action exit = () => _gate.Exit();
                base.Dispose(disposing);
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task IgnoresOtherMethods(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            public void Close() => _gate.Exit();

            protected override void Dispose(bool disposing) => base.Dispose(disposing);
        }
        """, 0, cancellationToken);

    [Test]
    public async Task FlagsOppositeNullChecksOfTheField(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                if (ReferenceEquals(_gate, null))
                    _gate.Exit();

                if (_gate == null)
                    _gate.Exit();

                if (_gate is null or { IsOpen: true })
                    _gate.Exit();
            }
        }
        """, 3, cancellationToken);

    [Test]
    public async Task FlagsCheckOfReferenceOrDefaultedField(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;
            private readonly Gate _other;
            private bool _disposed;
            private long _state;
            private long _count;

            public C(Gate gate, Gate other) : base(1)
            {
                _gate = gate;
                _other = other;
                _disposed = false;
                _state = default(long);
                _count = 0L;
            }

            protected override void Dispose(bool disposing)
            {
                if (_other == null)
                    _gate.Exit();

                if (_disposed)
                    return;

                _gate.Exit();
                if (_state != 0 || _count != 0)
                    _gate.Exit();
            }
        }
        """, 3, cancellationToken);

    [Test]
    public async Task FlagsObjectInitializerAsNoFlag(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;
            private bool IsOpen;

            public C() : base(1) => _gate = new Gate { IsOpen = true };

            protected override void Dispose(bool disposing)
            {
                if (IsOpen)
                    _gate.Exit();
            }
        }
        """, 1, cancellationToken);

    [Test]
    public async Task FlagsInvertedOrBypassedDisposingGuard(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                _ = disposing || _gate.IsOpen;
                try
                {
                    if (!disposing)
                        return;
                }
                finally
                {
                    _gate.Exit();
                }

                if (disposing)
                    return;

                _gate.Exit();
            }
        }
        """, 3, cancellationToken);

    [Test]
    public async Task FlagsThroughIntermediateBaseClass(CancellationToken cancellationToken) => await AssertCountAsync("""
        class Mid : Base
        {
            protected Mid() : base(1) { }
        }

        sealed class C<T> : Mid
            where T : class
        {
            private readonly T _value;

            public C(T value) => _value = value;

            protected override void Dispose(bool disposing) => _ = _value.ToString();
        }
        """, 1, cancellationToken);

    [Test]
    public async Task AllowsEquivalentDisposingChecks(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                if (disposing == true)
                    _gate.Exit();

                if (disposing is not false)
                    _gate.Exit();

                if (!disposing || _gate is null)
                {
                    return;
                }

                _gate.Exit();
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task AllowsDereferenceAfterThrowOrPattern(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private readonly Gate _a;
            private readonly Gate _b;

            public C(Gate a, Gate b) : base(1)
            {
                _a = a;
                _b = b;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing is false)
                    throw new InvalidOperationException();

                _a.Exit();
                if (_b is { IsOpen: true })
                    _b.Exit();
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task AllowsNameofExtensionAndLocalFunction(CancellationToken cancellationToken) => await AssertCountAsync("""
        static class GateExtensions
        {
            public static void Close(this Gate gate) { }
        }

        sealed class C : Base
        {
            private readonly Gate _gate;

            public C(Gate gate) : base(1) => _gate = gate;

            protected override void Dispose(bool disposing)
            {
                _ = nameof(_gate.IsOpen);
                _gate.Close();
                Exit();

                void Exit() => _gate.Exit();
            }
        }
        """, 0, cancellationToken);

    /// <summary>The value such a call returns is as null as the field was.</summary>
    [Test]
    public async Task FlagsValueReadThroughInterlocked(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;
            private Gate _b;
            private Gate _c;

            public C(Gate a, Gate b, Gate c) : base(1)
            {
                _a = a;
                _b = b;
                _c = c;
            }

            protected override void Dispose(bool disposing)
            {
                Interlocked.Exchange(ref _a, null).Exit();
                Volatile.Read(ref _b).Exit();
                _ = Interlocked.CompareExchange(ref _c, null, _c).IsOpen;
            }
        }
        """, 3, cancellationToken);

    [Test]
    public async Task AllowsGuardedInterlockedRead(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;
            private Gate _b;
            private Gate _c;

            public C(Gate a, Gate b, Gate c) : base(1)
            {
                _a = a;
                _b = b;
                _c = c;
            }

            protected override void Dispose(bool disposing)
            {
                Interlocked.Exchange(ref _a, null)?.Exit();
                if (disposing)
                    Interlocked.Exchange(ref _b, null).Exit();

                if (_c != null)
                    Volatile.Read(ref _c).Exit();
            }
        }
        """, 0, cancellationToken);

    /// <summary>A local that holds the value of the field and nothing else is dereferenced in its place.</summary>
    [Test]
    public async Task FlagsLocalCopy(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;
            private Gate _b;
            private Gate _c;

            public C(Gate a, Gate b, Gate c) : base(1)
            {
                _a = a;
                _b = b;
                _c = c;
            }

            protected override void Dispose(bool disposing)
            {
                var a = _a;
                a.Exit();

                var b = Interlocked.Exchange(ref _b, null);
                b.Exit();

                Gate c = _c;
                _ = c.IsOpen;
            }
        }
        """, 3, cancellationToken);

    /// <summary>A check of the local guards it, and so does a check of the field it was copied from.</summary>
    [Test]
    public async Task AllowsGuardedLocalCopy(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;
            private Gate _b;
            private Gate _c;
            private Gate _d;
            private Gate _e;

            public C(Gate a, Gate b, Gate c, Gate d, Gate e) : base(1)
            {
                _a = a;
                _b = b;
                _c = c;
                _d = d;
                _e = e;
            }

            protected override void Dispose(bool disposing)
            {
                var a = _a;
                a?.Exit();

                var b = Interlocked.Exchange(ref _b, null);
                if (b != null)
                    b.Exit();

                if (_d != null)
                {
                    var d = _d;
                    d.Exit();
                }

                if (disposing)
                {
                    var e = _e;
                    e.Exit();
                }

                var c = _c;
                if (c is null)
                    return;

                c.Exit();
            }
        }
        """, 0, cancellationToken);

    /// <summary>Once the local is written again it may hold another value, so it is no longer followed.</summary>
    [Test]
    public async Task AllowsLocalWrittenAgain(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;
            private Gate _b;
            private Gate _c;

            private static void Fill(out Gate gate) => gate = new Gate();

            public C(Gate a, Gate b, Gate c) : base(1)
            {
                _a = a;
                _b = b;
                _c = c;
            }

            protected override void Dispose(bool disposing)
            {
                var a = _a;
                a = new Gate();
                a.Exit();

                var b = _b;
                Fill(out b);
                b.Exit();

                var c = _c;
                c ??= new Gate();
                c.Exit();
            }
        }
        """, 0, cancellationToken);

    /// <summary>A field-like event is null until someone subscribes, and nobody could subscribe to a half-built object.</summary>
    [Test]
    public async Task FlagsDirectEventInvocation(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;

            public event Action Changed;

            public event EventHandler Closed;

            public C(Gate a) : base(1)
            {
                _a = a;
            }

            protected override void Dispose(bool disposing)
            {
                Changed();
                Closed.Invoke(this, EventArgs.Empty);

                var handler = Changed;
                handler();
            }
        }
        """, 3, cancellationToken);

    [Test]
    public async Task AllowsGuardedOrInitializedEvent(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;

            public event Action Changed;

            public event EventHandler Closed;

            public event Action Opened = static () => { };

            public C(Gate a) : base(1)
            {
                _a = a;
            }

            protected override void Dispose(bool disposing)
            {
                Changed?.Invoke();
                if (Closed != null)
                    Closed(this, EventArgs.Empty);

                Opened();

                var handler = Changed;
                handler?.Invoke();
            }
        }
        """, 0, cancellationToken);

    /// <summary>Throwing null raises NullReferenceException at the throw.</summary>
    [Test]
    public async Task FlagsThrownField(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;

            private readonly Exception _failure;

            public C(Gate a) : base(1)
            {
                _a = a;
            }

            protected override void Dispose(bool disposing)
            {
                if (_a is null)
                    throw _failure;

                var copy = _failure;
                throw copy;
            }
        }
        """, 2, cancellationToken);

    [Test]
    public async Task AllowsGuardedThrow(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;

            private readonly Exception _failure;

            public C(Gate a) : base(1)
            {
                _a = a;
            }

            protected override void Dispose(bool disposing)
            {
                if (_failure != null)
                    throw _failure;

                if (disposing)
                    throw _failure;
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task NamesEventInMessage(CancellationToken cancellationToken)
    {
        const string source = """
                              sealed class C : Base
                              {
                                  public C() : base(1)
                                  {
                                  }

                                  public event Action Changed;

                                  protected override void Dispose(bool disposing) => Changed();
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FinalizerDisposeFieldAnalyzer(), FinalizableBase + source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).StartsWith("Event 'Changed' is dereferenced");
    }

    /// <summary>A value-type local is not the reference itself: a span over a null array is empty, and a null box gives an empty nullable.</summary>
    [Test]
    public async Task AllowsValueTypeLocal(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;

            private readonly byte[] _buffer;

            private readonly object _boxed;

            public C(Gate a) : base(1)
            {
                _a = a;
            }

            protected override void Dispose(bool disposing)
            {
                Span<byte> span = _buffer;
                span.Clear();

                int? number = _boxed as int?;
                _ = number.HasValue;
            }
        }
        """, 0, cancellationToken);

    [Test]
    public async Task AllowsLocalHandedOutByReference(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;
            private Gate _b;
            private Gate _c;

            public void Refill(ref Gate gate) => gate = new Gate();

            private static void Swap(ref Gate gate) => gate = new Gate();

            public C(Gate a, Gate b, Gate c) : base(1)
            {
                _a = a;
                _b = b;
                _c = c;
            }

            protected override void Dispose(bool disposing)
            {
                var a = _a;
                Swap(ref a);
                a.Exit();

                var b = _b;
                (b, _) = (new Gate(), 1);
                b.Exit();

                dynamic self = this;
                var c = _c;
                self.Refill(ref c);
                c.Exit();
            }
        }
        """, 0, cancellationToken);

    /// <summary>An initialized field, a look-alike of Interlocked, the field passed as the new value and the event of another instance are left alone.</summary>
    [Test]
    public async Task AllowsReadThatIsNotOfUnsafeField(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;

            private static readonly C Shared = new(new Gate());

            private Gate _ready = new();

            public event Action Changed;

            public C(Gate a) : base(1)
            {
                _a = a;
            }

            protected override void Dispose(bool disposing)
            {
                Interlocked.Exchange(ref _ready, null).Exit();
                var ready = _ready;
                ready.Exit();

                Local.Interlocked.Exchange(ref _a, null).Exit();

                object box = new object();
                _ = Interlocked.Exchange(ref box, _a).ToString();

                Shared.Changed();
            }
        }

        namespace Local
        {
            static class Interlocked
            {
                public static T Exchange<T>(ref T location, T value) => value;
            }
        }
        """, 0, cancellationToken);

    /// <summary>A check guards only what it names.</summary>
    [Test]
    public async Task FlagsCheckOfAnotherSubject(CancellationToken cancellationToken) => await AssertCountAsync("""
        sealed class C : Base
        {
            private Gate _a;
            private Gate _b;

            public event Action Changed;

            public event EventHandler Closed;

            public C(Gate a, Gate b) : base(1)
            {
                _a = a;
                _b = b;
            }

            protected override void Dispose(bool disposing)
            {
                var a = _a;
                var b = _b;
                if (a != null)
                    b.Exit();

                if (Closed != null)
                    Changed();
            }
        }
        """, 2, cancellationToken);

    [Test]
    public async Task ReportsReadAtFieldAndCopyAtLocal(CancellationToken cancellationToken)
    {
        const string source = """
                              sealed class C : Base
                              {
                                  private Gate _a;
                                  private Gate _b;

                                  public C(Gate a, Gate b) : base(1)
                                  {
                                      _a = a;
                                      _b = b;
                                  }

                                  protected override void Dispose(bool disposing)
                                  {
                                      Interlocked.Exchange(ref _a, null).Exit();

                                      object copy = _b;
                                      _ = copy.ToString();
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FinalizerDisposeFieldAnalyzer(), FinalizableBase + source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
        _ = await Assert.That(diagnostics[0].GetMessage()).StartsWith("Field '_a' is dereferenced");
        _ = await Assert.That(diagnostics[0].Location.SourceTree!.GetText(cancellationToken).ToString(diagnostics[0].Location.SourceSpan)).IsEqualTo("_a");
        _ = await Assert.That(diagnostics[1].GetMessage()).StartsWith("Field '_b' is dereferenced");
        _ = await Assert.That(diagnostics[1].Location.SourceTree!.GetText(cancellationToken).ToString(diagnostics[1].Location.SourceSpan)).IsEqualTo("copy");
    }

    private static async Task AssertCountAsync(string source, int expected, CancellationToken cancellationToken, bool withBase = true)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new FinalizerDisposeFieldAnalyzer(), withBase ? FinalizableBase + source : source, cancellationToken);

        _ = await Assert.That(diagnostics).Count().IsEqualTo(expected);
        foreach (var diagnostic in diagnostics)
            _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
