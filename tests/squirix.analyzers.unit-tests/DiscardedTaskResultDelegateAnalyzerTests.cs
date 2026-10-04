using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class DiscardedTaskResultDelegateAnalyzerTests
{
    private const string RuleId = "SQR0032";

    [Test]
    public async Task FlagsFuncTaskLambdaExpressionBody(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M()
                                     {
                                         Func<Task> f = () => GetAsync();
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsEachReturnInBlockBody(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M(bool flag)
                                     {
                                         Func<Task> f = () =>
                                         {
                                             if (flag)
                                                 return GetAsync();

                                             return GetAsync();
                                         };
                                     }
                                 }
                                 """, 2, cancellationToken);

    [Test]
    public async Task FlagsCustomDelegateType(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System.Threading.Tasks;

                                 delegate Task FooAsync();

                                 class C
                                 {
                                     static Task<string> GetAsync() => Task.FromResult("a");

                                     void M()
                                     {
                                         FooAsync f = () => GetAsync();
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsLambdaArgumentForFuncTask(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     static void Register(Func<Task> action) { }

                                     void M()
                                     {
                                         Register(() => GetAsync());
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsMethodGroupAssignment(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M()
                                     {
                                         Func<Task> f = GetAsync;
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsMethodGroupArgument(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     static void Register(Func<Task> action) { }

                                     void M()
                                     {
                                         Register(GetAsync);
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsLocalFunctionAsMethodGroup(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     void M()
                                     {
                                         Task<int> Local() => Task.FromResult(1);

                                         Func<Task> f = Local;
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsAnonymousMethodReturningTaskOfT(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M()
                                     {
                                         Func<Task> f = delegate { return GetAsync(); };
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task SkipsReturnsOfNestedLambdas(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M()
                                     {
                                         Func<Task> f = () =>
                                         {
                                             Func<Task<int>> inner = () => { return GetAsync(); };
                                             return GetAsync();
                                         };
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task AllowsAsyncLambda(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M()
                                     {
                                         Func<Task> f = async () => _ = await GetAsync();
                                     }
                                 }
                                 """, 0, cancellationToken);

    [Test]
    public async Task AllowsFuncTaskOfT(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M()
                                     {
                                         Func<Task<int>> f = () => GetAsync();
                                         Func<Task<int>> g = GetAsync;
                                     }
                                 }
                                 """, 0, cancellationToken);

    [Test]
    public async Task AllowsExplicitCast(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M()
                                     {
                                         Func<Task> f = () => (Task)GetAsync();
                                     }
                                 }
                                 """, 0, cancellationToken);

    [Test]
    public async Task AllowsTaskRunOverloadForTaskOfT(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     Task<int> M() => Task.Run(() => GetAsync());
                                 }
                                 """, 0, cancellationToken);

    [Test]
    public async Task AllowsTaskAlreadyReturned(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task GetAsync() => Task.CompletedTask;

                                     void M()
                                     {
                                         Func<Task> f = () => GetAsync();
                                         Func<Task> g = GetAsync;
                                     }
                                 }
                                 """, 0, cancellationToken);

    [Test]
    public async Task AllowsNestedTaskResults(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<Task> GetAsync() => Task.FromResult(Task.CompletedTask);

                                     static Task<ValueTask<int>> GetValueAsync() => Task.FromResult(new ValueTask<int>(1));

                                     void M()
                                     {
                                         Func<Task> f = () => GetAsync();
                                         Func<Task> g = GetAsync;
                                         Func<Task> h = () => GetValueAsync();
                                     }
                                 }
                                 """, 0, cancellationToken);

    [Test]
    public async Task FlagsTaskSubclassReturnedFromFuncTask(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     class MyTask : Task<int>
                                     {
                                         public MyTask() : base(static () => 1)
                                         {
                                         }
                                     }

                                     void M()
                                     {
                                         Func<Task> f = () => new MyTask();
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsConditionalArm(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M(bool b)
                                     {
                                         Func<Task> f = () => b ? GetAsync() : Task.CompletedTask;
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsSwitchExpressionArm(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     static Task<int> GetAsync() => Task.FromResult(1);

                                     void M(int n)
                                     {
                                         Func<Task> f = () => n switch { 0 => GetAsync(), _ => Task.CompletedTask };
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task FlagsDelegateCreatedFromDelegate(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     void M(Func<Task<int>> source)
                                     {
                                         var f = new Func<Task>(source);
                                     }
                                 }
                                 """, 1, cancellationToken);

    [Test]
    public async Task AllowsThrowExpressionBody(CancellationToken cancellationToken) => await AssertCountAsync("""
                                 using System;
                                 using System.Threading.Tasks;

                                 class C
                                 {
                                     void M()
                                     {
                                         Func<Task> f = () => throw new InvalidOperationException();
                                     }
                                 }
                                 """, 0, cancellationToken);

    private static async Task AssertCountAsync(string source, int expected, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new DiscardedTaskResultDelegateAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(expected);
        foreach (var diagnostic in diagnostics)
            _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
