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

    [Test]
    public async Task AllowsWhenAnyAwaitWithConfigureAwait(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System;
                               using System.Threading;
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M(Task a, Task b)
                                   {
                                       _ = await Task.WhenAny(a, b).ConfigureAwait(false);
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsWhenAnyAwaitWithWaitAsync(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System;
                               using System.Threading;
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M(Task a)
                                   {
                                       _ = await Task.WhenAny(a).WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, CancellationToken.None);
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsWhenAnyLocalPassedToTaskParameter(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System;
                               using System.Threading;
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M(Task a, Task b)
                                   {
                                       var next = Task.WhenAny(a);
                                       if (await Task.WhenAny(next, b).ConfigureAwait(false) != next)
                                           return;
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsWhenAnyLocalAwaitDiscarded(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System;
                               using System.Threading;
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M(Task a)
                                   {
                                       var next = Task.WhenAny(a);
                                       _ = await next;
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task FlagsStartNewPassedToWhenAny(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                               using System;
                               using System.Threading;
                               using System.Threading.Tasks;

                               class C
                               {
                                   Task M()
                                   {
                                       return Task.WhenAny(Task.Factory.StartNew(() => Task.Delay(1)), Task.Delay(1));
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsConditionalWhenAnyLocalInLoop(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Collections.Generic;
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M(List<Task> pending, Task progress)
                                   {
                                       var next = pending.Count > 0 ? Task.WhenAny(pending) : null;
                                       while (true)
                                       {
                                           if (next == null)
                                               continue;

                                           if (await Task.WhenAny(next, progress).ConfigureAwait(false) == progress)
                                               return;

                                           var completed = await next.ConfigureAwait(false);
                                           next = pending.Count > 0 ? Task.WhenAny(pending) : null;
                                       }
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task FlagsLocalReassignedFromStartNew(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                               using System.Collections.Generic;
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M(List<Task> pending, Task progress)
                                   {
                                       var next = Task.WhenAny(pending);
                                       next = Task.Factory.StartNew(() => Task.Delay(1));
                                       _ = await Task.WhenAny(next, progress);
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task FlagsConditionalWithStartNewBranch(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                               using System.Collections.Generic;
                               using System.Threading.Tasks;

                               class C
                               {
                                   async Task M(List<Task> pending, Task progress, bool flag)
                                   {
                                       var next = flag ? Task.WhenAny(pending) : Task.Factory.StartNew(() => Task.Delay(1));
                                       _ = await Task.WhenAny(next, progress);
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
