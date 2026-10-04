using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class NestedTaskAnalyzerTests
{
    private const string RuleId = "SQR0031";

    [Test]
    public async Task FlagsAwaitStatement(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     async Task M()
                                     {
                                         await Task.Factory.StartNew(async () => await Task.Delay(1));
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsAwaitAssignedToDiscard(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     async Task M()
                                     {
                                         _ = await Task.Factory.StartNew(() => Task.Delay(1));
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsAwaitWithConfigureAwait(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     async Task M()
                                     {
                                         await new TaskFactory().StartNew(() => Task.Delay(1)).ConfigureAwait(false);
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsLocalTaskConversion(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     void M()
                                     {
                                         Task t = Task.Factory.StartNew(async () => await Task.Delay(1));
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsReturnFromTaskMethod(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     Task M()
                                     {
                                         return Task.Factory.StartNew(async () => await Task.Delay(1));
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsReturnFromTaskLambda(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     void M()
                                     {
                                         Func<Task> f = () =>
                                         {
                                             return Task.Factory.StartNew(async () => await Task.Delay(1));
                                         };
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsArgumentToWhenAll(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     Task M()
                                     {
                                         var nested = Task.Factory.StartNew(() => Task.Delay(1));
                                         return Task.WhenAll(nested, Task.Delay(1));
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsValueTaskAwaitDiscarded(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     ValueTask<Task> Make() => default;

                                     async Task M()
                                     {
                                         await Make();
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task AllowsUnwrap(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M()
                                   {
                                       await Task.Factory.StartNew(async () => await Task.Delay(1)).Unwrap();
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsDoubleAwait(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M()
                                   {
                                       await await Task.Factory.StartNew(() => Task.Delay(1));
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsAwaitResultUsed(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M()
                                   {
                                       var inner = await Task.Factory.StartNew(() => Task.Delay(1));
                                       await inner;
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsTaskRun(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M()
                                   {
                                       await Task.Run(async () => await Task.Delay(1));
                                       Task t = Task.Run(() => Task.Delay(1));
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsExplicitCast(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Threading.Tasks;

                               class C
                               {
                                   void M()
                                   {
                                       Task t = (Task)Task.Factory.StartNew(() => Task.Delay(1));
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsTupleAwaiterExtension(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Runtime.CompilerServices;
                               using System.Threading.Tasks;

                               static class Extensions
                               {
                                   public static TaskAwaiter GetAwaiter(this (Task, Task) tasks) => Task.WhenAll(tasks.Item1, tasks.Item2).GetAwaiter();
                               }

                               class C
                               {
                                   async Task M()
                                   {
                                       await (Task.Delay(1), Task.Delay(1));
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsNonNestedGenericTask(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M()
                                   {
                                       await Task.Factory.StartNew(() => 1);
                                       Task t = Task.FromResult(1);
                                   }
                               }
                               """, cancellationToken);

    private static async Task AssertCleanAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new NestedTaskAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static async Task AssertFlaggedAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new NestedTaskAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
