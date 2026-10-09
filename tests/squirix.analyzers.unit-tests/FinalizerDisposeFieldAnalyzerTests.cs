using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

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

    private static async Task AssertCountAsync(string source, int expected, CancellationToken cancellationToken, bool withBase = true)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new FinalizerDisposeFieldAnalyzer(), withBase ? FinalizableBase + source : source, cancellationToken);

        _ = await Assert.That(diagnostics).Count().IsEqualTo(expected);
        foreach (var diagnostic in diagnostics)
            _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
