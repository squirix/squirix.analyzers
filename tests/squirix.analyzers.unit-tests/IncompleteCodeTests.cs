using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

/// <summary>
/// Runs every shipped analyzer on code that does not compile, as an IDE does while the user types. Each sample breaks the shapes that the rules
/// inspect: missing tokens, unresolved types and members, and invocations typed halfway. The last sample keeps a shape for each rule intact
/// next to such errors, so every rule gets past its early exits. A run passes when no analyzer throws.
/// </summary>
public sealed class IncompleteCodeTests
{
    private const string BrokenControlFlow = """
                                             class C
                                             {
                                                 int M(int[] items, bool flag)
                                                 {
                                                     for (var i = 0; i < items.Length; i++)
                                                     {
                                                         foreach (var item in
                                                     }

                                                     while (flag)
                                                         if (flag
                                                             return 1

                                                     if (flag) { return 1; } else
                                                     if (flag)
                                                         return
                                                     return flag ? : 0;
                                                 }

                                                 void N()
                                                 {
                                                     try { } catch (Exception) { throw; } catch (Unknown) { throw; } catch (
                                                     for (;;)
                                                 }
                                             """;

    private const string BrokenDisposePattern = """
                                                using System;

                                                class Stream : UnknownBase
                                                {
                                                    private readonly Unknown _gate;
                                                    private bool _disposed;
                                                    private int _flag;

                                                    public Stream(Unknown gate) : base(gate) { _gate = gate; _flag = }

                                                    ~Stream(

                                                    protected override void Dispose(bool disposing)
                                                    {
                                                        if (disposing is ) _gate.
                                                        if (Interlocked.Exchange(ref _flag, ) == 1)
                                                            _gate.Exit(
                                                        _disposed = _disposed is not
                                                        Volatile.Read(ref
                                                        base.Dispose(
                                                    }

                                                    void Dispose(bool a, bool b) => _gate?.
                                                }
                                                """;

    private const string BrokenInvocations = """
                                             using System;
                                             using System.Threading.Tasks;

                                             class C
                                             {
                                                 static void M(int a = 1, string b = null, TimeSpan t = default) { }

                                                 async Task N(object value, TimeSpan timeout, Unknown u)
                                                 {
                                                     M(a: , b: "x");
                                                     M(1, b: null, t: default(TimeSpan);
                                                     ArgumentNullException.ThrowIfNull(
                                                     var x = value ?? throw new ArgumentNullException(nameof(
                                                     var y = u ?? throw Throw.
                                                     if (value == null) throw new ArgumentNullException(nameof(value)
                                                     if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(
                                                     if (value is null
                                                     await Task.Factory.StartNew(async () => await
                                                     Func<Task> f = () => Task.FromResult(
                                                     Func<Task> g = Undefined;
                                                     _ = Assert.Throws<InvalidOperationException>(() => u.
                                                     await Assert.ThrowsAsync<Exception>(async () => await
                                                     var token = TestContext.Current.Execution.
                                                     var options = new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler(), DisposeHttpClient =
                                                 }
                                             }
                                             """;

    private const string BrokenStructs = """
                                         readonly struct Large
                                         {
                                             public readonly long A, B, C;
                                             public readonly Unknown D;
                                         }

                                         struct Mutable { public long A, B, C; public void Touch() { } }

                                         class C
                                         {
                                             private readonly Mutable _mutable;
                                             private readonly Unknown _unknown;

                                             void M(Large large, Unknown other, in Large byRef, Large)
                                             {
                                                 _mutable.Touch(
                                                 _unknown.Touch();
                                                 var copy = large.
                                             }

                                             int TryGet(out
                                             bool TryParse(string s, out Unknown result) => result =
                                         }
                                         """;

    private const string BrokenTasksAndScopes = """
                                                using System;
                                                using System.IO;
                                                using System.Threading.Tasks;

                                                class C
                                                {
                                                    Task M()
                                                    {
                                                        using var stream = new MemoryStream(
                                                        using (var other = new Unknown())
                                                        {
                                                            return Task.Run(() => other.
                                                        }

                                                        var pending = stream.WriteAsync(
                                                        return Task.WhenAll(pending, Unknown.
                                                    }

                                                    Task<Task> Nested() => Task.Factory.StartNew(() =>
                                                }
                                                """;

    private const string BrokenTypes = """
                                       namespace Company.Product.Storage
                                       {
                                           class StorageTypeWithAVeryLongNameThatKeepsGoingAndGoingForever<T> : where T :
                                           {
                                               private int _fieldWithAVeryLongNameThatKeepsGoingAndGoingForever = ;
                                               private Unknown _a, _b, _c, _d, _e, _f, _g, _h, _i, _j, _k, _l, _m, _n, _o, _p;
                                               public int Property { get => ; set }
                                               void MethodWithAVeryLongNameThatKeepsGoingAndGoingForeverAndEver(
                                               void M1() { } void M2() { } void M3() { } void M4() { } void M5() { } void M6() { } void M7() { }
                                               void M8() { } void M9() { } void M10() { } void M11() { } void M12() { } void M13() { } void M14() { }
                                               void M15() { } void M16() { } void M17() { } void M18() { } void M19() { } void M20() { } void M21(
                                           }

                                           partial class
                                           record R(int X, Unknown Y
                                       """;

    private const string BrokenTopLevel = """
                                          using System;

                                          var x = Unknown(
                                          if (x is not null or
                                          foreach (var item in x)
                                              foreach (var inner in item)
                                          Console.WriteLine(x is 1 ? : );

                                          static bool TryRun(
                                          """;

    private const string ValidShapesNextToErrors = """
                                                   #nullable enable
                                                   using System;
                                                   using System.Net.Http;
                                                   using System.Threading.Tasks;
                                                   using Grpc.Net.Client;
                                                   using TUnit.Core;

                                                   namespace Grpc.Net.Client
                                                   {
                                                       public sealed class GrpcChannelOptions
                                                       {
                                                           public HttpMessageHandler? HttpHandler { get; set; }
                                                           public bool DisposeHttpClient { get; set; }
                                                       }
                                                   }

                                                   namespace TUnit.Core
                                                   {
                                                       class TestContext
                                                       {
                                                           public static TestContext? Current { get; } = new TestContext();

                                                           public System.Threading.CancellationToken CancellationToken => default;
                                                       }
                                                   }

                                                   readonly struct Large { public readonly long A, B, C; }

                                                   struct Mutable { public long A; public void Touch() { } }

                                                   class Base : IDisposable
                                                   {
                                                       ~Base() => Dispose(false);

                                                       public void Dispose() => Dispose(true);

                                                       protected virtual void Dispose(bool disposing) { }
                                                   }

                                                   sealed class C : Base
                                                   {
                                                       private readonly Mutable _mutable;
                                                       private readonly object _value;
                                                       private readonly Unknown _broken = Missing(;

                                                       public C(object value)
                                                       {
                                                           _value = value ?? throw new ArgumentNullException(nameof(value));
                                                       }

                                                       static Task<int> GetAsync() => Task.FromResult(1);

                                                       int Pick(bool flag, object? other, Large large, string text, int count)
                                                       {
                                                           _mutable.Touch();
                                                           if (other == null) throw new ArgumentNullException(nameof(other));
                                                           if (other is null) { return 0; }
                                                           if (string.IsNullOrEmpty(text))
                                                               throw new ArgumentException("Text is required.", nameof(text));
                                                           if (count is 42)
                                                               _mutable.Touch();
                                                           if (flag) return 1;
                                                           return 2;
                                                       }

                                                       async Task Run()
                                                       {
                                                           Func<Task> f = () => GetAsync();
                                                           await Task.Factory.StartNew(async () => await Task.Delay(1));
                                                           var token = TestContext.Current!.CancellationToken;
                                                           var options = new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() };
                                                           var broken = Unknown.Call(1,
                                                       }

                                                       protected override void Dispose(bool disposing) => _value.ToString();

                                                       void Broken() { var a = ; Missing( }
                                                   }
                                                   """;

    /// <summary>Returns every analyzer and sample combination; analyzers are found by reflection so that a new rule is covered without editing this list.</summary>
    public static IEnumerable<(string Analyzer, string Sample)> Cases()
    {
        var samples = new[]
        {
            nameof(BrokenControlFlow),
            nameof(BrokenDisposePattern),
            nameof(BrokenInvocations),
            nameof(BrokenStructs),
            nameof(BrokenTasksAndScopes),
            nameof(BrokenTypes),
            nameof(BrokenTopLevel),
            nameof(ValidShapesNextToErrors),
        };
        foreach (var type in typeof(FinalizerDisposeFieldAnalyzer).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                continue;

            foreach (var sample in samples)
                yield return (type.Name, sample);
        }
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task AnalyzerDoesNotThrow(string analyzer, string sample, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunOnIncompleteCodeAsync(CreateAnalyzer(analyzer), GetSample(sample), cancellationToken);

        _ = await Assert.That(diagnostics.IsDefault).IsFalse();
    }

    [Test]
    public async Task CoversEveryAnalyzer(CancellationToken cancellationToken)
    {
        var analyzers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (analyzer, _) in Cases())
            _ = analyzers.Add(analyzer);

        _ = await Assert.That(analyzers).Contains(nameof(FinalizerDisposeFieldAnalyzer));
        _ = await Assert.That(analyzers).Contains(nameof(OmitOuterLoopBracesAnalyzer));
    }

    private static DiagnosticAnalyzer CreateAnalyzer(string name)
    {
        var type = typeof(FinalizerDisposeFieldAnalyzer).Assembly.GetType("Squirix.Analyzers." + name, true)!;
        return (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
    }

    private static string GetSample(string name) => name switch
    {
        nameof(BrokenControlFlow) => BrokenControlFlow,
        nameof(BrokenDisposePattern) => BrokenDisposePattern,
        nameof(BrokenInvocations) => BrokenInvocations,
        nameof(BrokenStructs) => BrokenStructs,
        nameof(BrokenTasksAndScopes) => BrokenTasksAndScopes,
        nameof(BrokenTypes) => BrokenTypes,
        nameof(BrokenTopLevel) => BrokenTopLevel,
        nameof(ValidShapesNextToErrors) => ValidShapesNextToErrors,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown sample."),
    };
}
