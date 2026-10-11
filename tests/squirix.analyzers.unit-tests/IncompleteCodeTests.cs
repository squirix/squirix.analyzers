using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

/// <summary>
/// Runs every shipped analyzer on code that does not compile, as an IDE does while the user types. Each sample breaks the shapes that the rules
/// inspect: missing tokens, unresolved types and members, invocations typed halfway, unterminated literals and comments, and newer syntax.
/// The last sample keeps a reported shape for each rule intact next to such errors, so every rule gets past its early exits.
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
                                                     Throws<Unknown>(() => u.
                                                     Assert.That(() => u.).Throws<
                                                     u?.Should().Throw(
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
                                                       class TestExecution
                                                       {
                                                           public System.Threading.CancellationToken CancellationToken => default;
                                                       }

                                                       class TestContext
                                                       {
                                                           public static TestContext? Current { get; } = new TestContext();

                                                           public TestExecution Execution { get; } = new TestExecution();
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
                                                       private int _disposed;

                                                       public C(object value)
                                                       {
                                                           _value = value ?? throw new ArgumentNullException(nameof(value));
                                                       }

                                                       static Task<int> GetAsync() => Task.FromResult(1);

                                                       int Pick(bool flag, object? other, Large large, string text, int count, C? peer, Unknown missing)
                                                       {
                                                           _mutable.Touch();
                                                           if (other == null) throw new ArgumentNullException(nameof(other));
                                                           if (other is null) { return 0; }
                                                           if (string.IsNullOrEmpty(text))
                                                               throw new ArgumentException("Text is required.", nameof(text));
                                                           if (count is 42)
                                                               _mutable.Touch();
                                                           if (count is not 3)
                                                               _mutable.Touch();
                                                           if (peer is null || missing is null)
                                                               _mutable.Touch();
                                                           if (flag) return 1;
                                                           return 2;
                                                       }

                                                       async Task Run()
                                                       {
                                                           Func<Task> f = () => GetAsync();
                                                           await Task.Factory.StartNew(async () => await Task.Delay(1));
                                                           var token = TestContext.Current!.Execution.CancellationToken;
                                                           var options = new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() };
                                                           var broken = Unknown.Call(1,
                                                       }

                                                       protected override void Dispose(bool disposing) => _value.ToString();

                                                       void Close() => _disposed = 1;

                                                       void Broken() { var a = ; Missing( }
                                                   }
                                                   """;

    private const string BrokenComment = """
                                         class C
                                         {
                                             void M() { if (true) { } }

                                             /* never closed
                                             void N() { }
                                         }
                                         """;

    private const string BrokenPreprocessor = """
                                              #define DEBUG
                                              using System;
                                              #region Types
                                              #if DEBUG

                                              [Obsolete(
                                              class C
                                              {
                                                  [return: ]
                                                  int M(bool flag) { if (flag) { return 1; } return 2; }
                                              #elif
                                              """;

    private const string BrokenRawString = """"
                                           class C
                                           {
                                               string R() => """
                                                   raw text that never ends

                                               void N() { }
                                           }
                                           """";

    private const string BrokenStringLiteral = """
                                               class C
                                               {
                                                   string M() => "unterminated;

                                                   void N() { for (;;) { } }
                                               }
                                               """;

    private const string BrokenModernSyntax = """
                                              namespace App;

                                              record struct Point(int X, Unknown Y);

                                              class Service(Unknown logger, int count) : Base(count)
                                              {
                                                  int M(object value) => value switch { int i when i > => 1, string { Length: } => 2, _ => };

                                                  void N(int[] values)
                                                  {
                                                      int Local(int x) => x +
                                                      Func<int, int> f = x => ;
                                                      var point = new Point() { X = };
                                                      int[] items = [1, , 2];
                                                      switch (values.Length) { case : break; case 1 }
                                                  }
                                              }

                                              namespace ;
                                              """;

    private static readonly (string Name, string Source)[] Samples =
    [
        (nameof(BrokenControlFlow), BrokenControlFlow),
        (nameof(BrokenDisposePattern), BrokenDisposePattern),
        (nameof(BrokenInvocations), BrokenInvocations),
        (nameof(BrokenStructs), BrokenStructs),
        (nameof(BrokenTasksAndScopes), BrokenTasksAndScopes),
        (nameof(BrokenTypes), BrokenTypes),
        (nameof(BrokenTopLevel), BrokenTopLevel),
        (nameof(BrokenComment), BrokenComment),
        (nameof(BrokenPreprocessor), BrokenPreprocessor),
        (nameof(BrokenRawString), BrokenRawString),
        (nameof(BrokenStringLiteral), BrokenStringLiteral),
        (nameof(BrokenModernSyntax), BrokenModernSyntax),
        (nameof(ValidShapesNextToErrors), ValidShapesNextToErrors),
    ];

    private static readonly Lazy<Dictionary<string, Type>> Analyzers = new(FindAnalyzers);

    /// <summary>Returns every analyzer and sample combination; analyzers are found by reflection so that a new rule is covered without editing this list.</summary>
    public static IEnumerable<(string Analyzer, string Sample)> Cases()
    {
        foreach (var analyzer in Analyzers.Value.Keys)
            foreach (var (sample, _) in Samples)
                yield return (analyzer, sample);
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task AnalyzerDoesNotThrow(string analyzer, string sample, CancellationToken cancellationToken) =>
        _ = await AnalyzerRunner.RunOnIncompleteCodeAsync(CreateAnalyzer(analyzer), GetSample(sample), cancellationToken);

    /// <summary>Every rule must report on at least one sample, so that each rule's own logic runs on code with errors and not only its early exits.</summary>
    [Test]
    public async Task EveryRuleReportsOnSomeSample(CancellationToken cancellationToken)
    {
        var silent = new List<string>();
        foreach (var name in Analyzers.Value.Keys)
        {
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, source) in Samples)
                foreach (var diagnostic in await AnalyzerRunner.RunOnIncompleteCodeAsync(CreateAnalyzer(name), source, cancellationToken))
                    _ = reported.Add(diagnostic.Id);

            foreach (var descriptor in CreateAnalyzer(name).SupportedDiagnostics)
            {
                if (!reported.Contains(descriptor.Id))
                    silent.Add(descriptor.Id);
            }
        }

        _ = await Assert.That(Analyzers.Value.Keys).Contains(nameof(FinalizerDisposeFieldAnalyzer));
        _ = await Assert.That(silent).IsEmpty();
    }

    private static DiagnosticAnalyzer CreateAnalyzer(string name) => (DiagnosticAnalyzer)Activator.CreateInstance(Analyzers.Value[name])!;

    private static Dictionary<string, Type> FindAnalyzers()
    {
        var analyzers = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in typeof(FinalizerDisposeFieldAnalyzer).Assembly.GetTypes())
        {
            if (!type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type) && type.IsDefined(typeof(DiagnosticAnalyzerAttribute), false))
                analyzers.Add(type.Name, type);
        }

        return analyzers;
    }

    private static string GetSample(string name)
    {
        foreach (var (sample, source) in Samples)
        {
            if (sample == name)
                return source;
        }

        throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown sample.");
    }
}
