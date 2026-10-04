using System.Threading;
using System.Threading.Tasks;
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
