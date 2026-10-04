using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class TaskOutlivesUsingScopeAnalyzerTests
{
    private const string RuleId = "SQR0030";

    [Test]
    public async Task FlagsFireAndForgetInUsingStatement(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;

        class C
        {
            void M(Stream dest)
            {
                using (var stream = new MemoryStream())
                {
                    stream.CopyToAsync(dest);
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsReturnInUsingDeclaration(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M(Stream destination)
            {
                using var source = new MemoryStream();
                return source.CopyToAsync(destination);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsWhenAnyReturnInUsingStatement(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task<Task> M()
            {
                using (var cts = new CancellationTokenSource())
                {
                    var a = Task.Delay(1, cts.Token);
                    var b = Task.Delay(2, cts.Token);
                    return Task.WhenAny(a, b);
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsWhenAnyReturnInUsingDeclaration(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task<Task> M()
            {
                using var cts = new CancellationTokenSource();
                var a = Task.Delay(1, cts.Token);
                var b = Task.Delay(2, cts.Token);
                return Task.WhenAny(a, b);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsReturnedTaintedLocal(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var cts = new CancellationTokenSource();
                var a = Task.Delay(1, cts.Token);
                return a;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsAliasToken(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var cts = new CancellationTokenSource();
                var token = cts.Token;
                return Task.Delay(1, token);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsDiscardAssignment(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;

        class C
        {
            void M()
            {
                using var r = new MemoryStream();
                _ = r.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsAssignedResource(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;

        class C
        {
            void M()
            {
                MemoryStream r;
                using (r = new MemoryStream())
                {
                    r.FlushAsync();
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsAwaitedCall(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            async Task M(Stream dest)
            {
                using var stream = new MemoryStream();
                await stream.CopyToAsync(dest);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsAwaitWithConfigureAwait(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            async Task M(Stream dest)
            {
                using (var stream = new MemoryStream())
                {
                    await stream.CopyToAsync(dest).ConfigureAwait(false);
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsAwaitedWhenAll(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            async Task M()
            {
                using var cts = new CancellationTokenSource();
                var a = Task.Delay(1, cts.Token);
                var b = Task.Delay(2, cts.Token);
                await Task.WhenAll(a, b);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsTaskWithoutResource(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var stream = new MemoryStream();
                return Task.Delay(1);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsCallInsideLambda(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System;
        using System.IO;

        class C
        {
            void M(Stream dest)
            {
                using var stream = new MemoryStream();
                Action run = () => stream.CopyToAsync(dest);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReturnAfterUsingStatement(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using (var stream = new MemoryStream())
                {
                    stream.WriteByte(1);
                }

                return Task.Delay(1);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsUsingExpressionWithoutDeclaration(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;

        class C
        {
            void M()
            {
                var stream = new MemoryStream();
                using (stream)
                {
                    stream.FlushAsync();
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsSynchronousReturn(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;

        class C
        {
            long M()
            {
                using var stream = new MemoryStream();
                return stream.Length;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsArrayDerivedArgument(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var image = new MemoryStream();
                return Consume(image.ToArray());
            }

            static Task Consume(byte[] data) => Task.CompletedTask;
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsPrimitiveDerivedArgument(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var s = new MemoryStream();
                return Task.Delay((int)s.Length);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task ReportsNestedScopeOnce(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new TaskOutlivesUsingScopeAnalyzer(), """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            class C
            {
                void M()
                {
                    using var cts = new CancellationTokenSource();
                    using (var stream = new MemoryStream())
                    {
                        stream.CopyToAsync(Stream.Null, 81920, cts.Token);
                    }
                }
            }
            """, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).Contains("'stream'");
    }

    [Test]
    public async Task AllowsFromResultOfNonCarryingArgument(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            static int Parse(Stream s) => 1;

            Task<int> M()
            {
                using var stream = new MemoryStream();
                return Task.FromResult(Parse(stream));
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsFromResultOfHashData(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Security.Cryptography;
        using System.Threading.Tasks;

        class C
        {
            Task<byte[]> M()
            {
                using var stream = new MemoryStream();
                return Task.FromResult(SHA256.HashData(stream));
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsFromCanceled(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                return Task.FromCanceled(cts.Token);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsFromResultOfClonedElement(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Text.Json;
        using System.Threading.Tasks;

        class C
        {
            Task<JsonElement> M()
            {
                using var doc = JsonDocument.Parse("{}");
                return Task.FromResult(doc.RootElement.Clone());
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsValueTaskConstructedFromResult(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            static int Read(Stream s) => 1;

            ValueTask<int> M()
            {
                using var s = new MemoryStream();
                return new ValueTask<int>(Read(s));
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsLambdaCapturingResource(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var stream = new MemoryStream();
                return Task.Run(() => stream.WriteByte(1));
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsReceiverSplitIntoLocalFromCreation(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task<string> M()
            {
                using var stream = new MemoryStream();
                var reader = new StreamReader(stream);
                return reader.ReadToEndAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsReceiverSplitIntoLocalFromCall(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System;
        using System.IO;
        using System.Threading.Tasks;

        class Holder : IDisposable
        {
            public Stream Create() => new MemoryStream();

            public void Dispose()
            {
            }
        }

        class C
        {
            Task M()
            {
                using var holder = new Holder();
                var inner = holder.Create();
                return inner.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsConditionalAccessCall(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var stream = new MemoryStream();
                return stream?.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsTernaryBranch(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M(bool flag)
            {
                using var stream = new MemoryStream();
                return flag ? stream.FlushAsync() : Task.CompletedTask;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsCastCall(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var stream = new MemoryStream();
                return (Task)stream.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReassignedAlias(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var cts = new CancellationTokenSource();
                var token = cts.Token;
                token = CancellationToken.None;
                return Task.Delay(1, token);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReturnAfterWait(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var cts = new CancellationTokenSource();
                var t = Task.Delay(1, cts.Token);
                t.Wait();
                return t;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReturnAfterGetResult(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var cts = new CancellationTokenSource();
                var t = Task.Delay(1, cts.Token);
                t.GetAwaiter().GetResult();
                return t;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsReturnAfterResult(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task<int> M()
            {
                using var cts = new CancellationTokenSource();
                var t = Task.Run(() => cts.Token.CanBeCanceled ? 1 : 0);
                _ = t.Result;
                return t;
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsTopLevelStatements(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;

        using var s = new MemoryStream();
        s.FlushAsync();
        """, cancellationToken);

    [Test]
    public async Task FlagsAwaitUsingStatement(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            async Task M()
            {
                await using (var s = new MemoryStream())
                {
                    s.FlushAsync();
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsAwaitUsingDeclaration(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            async Task M()
            {
                await using var s = new MemoryStream();
                s.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsValueTaskFireAndForget(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System;
        using System.IO;

        class C
        {
            void M()
            {
                using var s = new MemoryStream();
                _ = s.WriteAsync(ReadOnlyMemory<byte>.Empty);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task AllowsLocalFunctionInsideScope(CancellationToken cancellationToken) => await AssertCleanAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            void M()
            {
                using var s = new MemoryStream();
                Local();

                Task Local() => s.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task ReportsLambdaUsingOnceWithInnerResource(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new TaskOutlivesUsingScopeAnalyzer(), """
            using System;
            using System.IO;
            using System.Threading.Tasks;

            class C
            {
                void M()
                {
                    using var outer = new MemoryStream();
                    Func<Task> f = () =>
                    {
                        using var inner = new MemoryStream();
                        return inner.FlushAsync();
                    };
                }
            }
            """, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).Contains("'inner'");
    }

    [Test]
    public async Task FlagsNestedBlockUnderUsingDeclaration(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using var s = new MemoryStream();
                {
                    return s.FlushAsync();
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsSecondDeclarator(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                using MemoryStream a = new MemoryStream(), b = new MemoryStream();
                return b.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsUsingInCatch(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System;
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
                try
                {
                    return Task.CompletedTask;
                }
                catch (Exception)
                {
                    using var s = new MemoryStream();
                    return s.FlushAsync();
                }
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsTaskSubclassReturn(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class MyTask : Task
        {
            public MyTask(Stream s)
                : base(static () => { })
            {
            }
        }

        class C
        {
            Task M()
            {
                using var s = new MemoryStream();
                return new MyTask(s);
            }
        }
        """, cancellationToken);

    [Test]
    public async Task FlagsLabeledUsingDeclaration(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
        using System.IO;
        using System.Threading.Tasks;

        class C
        {
            Task M()
            {
            L:
                using var s = new MemoryStream();
                return s.FlushAsync();
            }
        }
        """, cancellationToken);

    [Test]
    public async Task ToleratesIncompleteCode(CancellationToken cancellationToken)
    {
        const string Source = """
                              using System.IO;
                              using System.Threading.Tasks;

                              class C
                              {
                                  Task M()
                                  {
                                      using var s = new MemoryStream();
                                      var t = s.;
                                      using (var q = )
                                      {
                                          return Task.WhenAny(t, s.FlushAsync(;
                                      }
                                      using var
                                  }
                              }
                              """;

        var references = new List<MetadataReference>();
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            references.Add(MetadataReference.CreateFromFile(path));

        var tree = CSharpSyntaxTree.ParseText(Source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create("Incomplete", [tree], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostics = await compilation.WithAnalyzers([new TaskOutlivesUsingScopeAnalyzer()]).GetAnalyzerDiagnosticsAsync(cancellationToken);

        foreach (var diagnostic in diagnostics)
            _ = await Assert.That(diagnostic.Id).IsNotEqualTo("AD0001");
    }

    private static async Task AssertCleanAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new TaskOutlivesUsingScopeAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static async Task AssertFlaggedAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new TaskOutlivesUsingScopeAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
