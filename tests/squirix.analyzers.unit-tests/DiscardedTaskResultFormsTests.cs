using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

/// <summary>
/// Checks that the rule finds a lost result however the returned value or the delegate is written.
/// </summary>
public sealed class DiscardedTaskResultFormsTests
{
    private const string RuleId = "SQR0032";

    private const string Prefix = """
                                  #nullable enable
                                  using System;
                                  using System.Collections.Generic;
                                  using System.Threading.Tasks;

                                  class C
                                  {
                                      delegate T Maker<out T>();

                                      sealed class Wrapper
                                      {
                                          public static explicit operator Wrapper(Func<Task> work) => new Wrapper();
                                      }

                                      static Task<bool> SaveAsync() => Task.FromResult(true);

                                      static Task<bool> SaveCopyAsync() => Task.FromResult(true);

                                      static void RunLater(Func<Task> work) { }

                                      static Task RunNothing() => Task.CompletedTask;

                                      static object M(bool primary, int mode, Task<bool>? cached, Task? plain, Func<Task<bool>> save)
                                      {

                                  """;

    private const string Suffix = """

                                      }
                                  }
                                  """;

    /// <summary>Returns a method body and the expression in it whose result is lost; each body has exactly one.</summary>
    public static IEnumerable<(string Body, string Lost)> ReportedForms() =>
    [
        ("Func<Task> f = () => primary ? SaveAsync() : SaveCopyAsync(); return f;", "primary ? SaveAsync() : SaveCopyAsync()"),
        ("Func<Task> f = () => mode switch { 0 => SaveAsync(), _ => SaveCopyAsync() }; return f;", "mode switch { 0 => SaveAsync(), _ => SaveCopyAsync() }"),
        ("Func<Task> f = () => { return primary ? SaveAsync() : SaveCopyAsync(); }; return f;", "primary ? SaveAsync() : SaveCopyAsync()"),
        ("Func<Task> f = () => primary ? SaveAsync() : mode switch { 0 => SaveAsync(), _ => SaveCopyAsync() }; return f;",
            "primary ? SaveAsync() : mode switch { 0 => SaveAsync(), _ => SaveCopyAsync() }"),
        ("Func<Task> f = () => primary ? Task.CompletedTask : mode switch { 0 => SaveAsync(), _ => Task.CompletedTask }; return f;", "SaveAsync()"),
        ("Func<Task> f = () => cached ?? SaveAsync(); return f;", "cached ?? SaveAsync()"),
        ("Func<Task> f = () => cached ?? Task.CompletedTask; return f;", "cached"),
        ("Func<Task> f = () => plain ?? SaveAsync(); return f;", "SaveAsync()"),
        ("Func<Task> f = () => (SaveAsync()); return f;", "SaveAsync()"),
        ("Func<Task> f = save; return f;", "save"),
        ("RunLater(save); return mode;", "save"),
        ("Func<Task> Local() => save; return Local();", "save"),
        ("return new List<Func<Task>> { save };", "save"),
        ("Func<Task> f = primary ? save : save; return f;", "primary ? save : save"),
        ("Maker<Task<bool>> maker = SaveAsync; Maker<Task> lost = maker; return lost;", "maker"),
        ("Func<Task> f = () => primary ? (mode == 0 ? SaveAsync() : SaveCopyAsync()) : Task.CompletedTask; return f;", "mode == 0 ? SaveAsync() : SaveCopyAsync()"),
        ("Func<Task<bool>>? maybe = save; Func<Task> nothing = RunNothing; Func<Task> f = maybe ?? nothing; return f;", "maybe"),
        ("Func<Task> f = () => checked(SaveAsync()); return f;", "checked(SaveAsync())"),
        ("Func<Task> f = () => cached!; return f;", "cached!"),
        ("foreach (Func<Task> f in new[] { save }) return f; return mode;", "new[] { save }"),
        ("var typed = new List<Func<Task<bool>>> { save }; Func<Task>[] all = [..typed]; return all;", "typed"),
        ("var typed = new List<Func<Task<bool>>> { save }; List<Func<Task>> all = [..typed]; return all;", "typed"),
        ("(Func<Task<bool>>, int) pair = (save, mode); (Func<Task>, int) lost = pair; return lost;", "pair"),
        ("Func<Task> Wrap<T>(T value) where T : Task<bool> => () => value; return Wrap(SaveAsync());", "value"),
        ("Func<Task> Pass<T>(Func<T> make) where T : Task<bool> => make; return Pass(save);", "make"),
        ("Func<Task> Wrap<T, TBase>(T value) where T : TBase where TBase : Task<bool> => () => value; return Wrap<Task<bool>, Task<bool>>(SaveAsync());", "value"),
        ("Func<Task> f = () => unchecked(SaveAsync()); return f;", "unchecked(SaveAsync())"),
        ("Func<Task<bool>>? maybe = save; Func<Task> f = (maybe ?? save)!; return f;", "(maybe ?? save)!"),
        ("var typed = new List<Func<Task<bool>>> { save }; foreach (Func<Task> f in typed!) return f; return mode;", "typed!"),
        ("var typed = new List<Func<Task<bool>>> { save }; ReadOnlySpan<Func<Task>> all = [..typed]; return all.Length;", "typed"),
        ("((Func<Task<bool>>, int), int) deep = ((save, mode), mode); ((Func<Task>, int), int) lost = deep; return lost;", "deep"),
        ("(Func<Task<bool>>, int) pair = (save, mode); (Func<Task>, int)? lost = pair; return lost;", "pair"),
        ("var pairs = new List<(Func<Task<bool>>, int)> { (save, mode) }; foreach ((Func<Task>, int) p in pairs) return p; return mode;", "pairs"),
        ("Func<Task> nothing = RunNothing; var o = (object)(primary ? save : nothing); return o;", "save"),
        ("var w = (Wrapper)save; return w;", "save"),
        ("(Func<Task<bool>>, int) pair = (save, mode); (Func<Task> work, int n) = pair; return work;", "pair"),
        ("(Func<Task<bool>>, int) pair = (save, mode); Func<Task> work; int n; (work, n) = pair; return work;", "pair"),
        ("(Func<Task<bool>>, int) pair = (save, mode); (Func<Task> work, int n) = (save, mode); return work;", "save"),
        ("((Func<Task<bool>>, int), int) deep = ((save, mode), mode); ((Func<Task> work, int n), int m) = deep; return work;", "deep"),
        ("var pairs = new List<(Func<Task<bool>>, int)> { (save, mode) }; foreach ((Func<Task> work, int n) in pairs) return work; return mode;", "pairs"),
        ("var pairs = new List<(Func<Task<bool>>, int)> { (save, mode) }; foreach ((Func<Task> work, var n) in pairs) return work; return mode;", "pairs"),
        ("(Func<Task<bool>>, int) pair = (save, mode); ((Func<Task> work, int n), int m) = (pair, mode); return work;", "(pair, mode)"),
        ("(Func<Task<bool>>, int) Get() => (save, mode); ((Func<Task> work, int n), int m) = (Get(), mode); return work;", "(Get(), mode)"),
        ("(Func<Task<bool>>, int) pair = (save, mode); (Func<Task> _, int n) = pair; return n;", "pair"),
        ("Func<Task> nothing = RunNothing; var o = (object)(primary ? save : nothing, mode); return o;", "save"),
        ("Func<Task> nothing = RunNothing; var all = (object[])[primary ? save : nothing]; return all;", "save"),
    ];

    /// <summary>Returns method bodies with two lost results: branches of different result types have no common type but Task.</summary>
    public static IEnumerable<string> TwoLossForms() =>
    [
        "Func<Task> f = () => primary ? SaveAsync() : Task.FromResult(1); return f;",
        "Func<Task> f = () => mode switch { 0 => SaveAsync(), _ => Task.FromResult(1) }; return f;",
        "Func<Task> f = () => { return primary ? SaveAsync() : Task.FromResult(1); }; return f;",
        "Func<Task> f = () => primary ? Task.CompletedTask : mode switch { 0 => SaveAsync(), 1 => Task.FromResult(1), _ => Task.CompletedTask }; return f;",
        "Func<Task> f = () => { if (primary) return SaveAsync(); return mode switch { 0 => SaveAsync(), _ => SaveCopyAsync() }; }; return f;",
        "Func<Task> f = () => cached ?? cached ?? Task.CompletedTask; return f;",
        "Func<Task<int>> count = () => Task.FromResult(1); Func<Task> f = primary ? save : count; return f;",
        "Func<Task<int>> count = () => Task.FromResult(1); (Func<Task>, Func<Task>) lost = (save, count); return lost;",
        "Func<Task<int>> count = () => Task.FromResult(1); var both = (save, count); (Func<Task>, Func<Task>) lost = both; return lost;",
        "Func<Task<int>> count = () => Task.FromResult(1); var both = (save, count); (Func<Task> a, Func<Task> b) = both; return a;",
        "var both = (save, save); (Func<Task>, Func<Task>) lost = both; return lost;",
    ];

    /// <summary>Returns method bodies where no result is lost, or where the code says so itself.</summary>
    public static IEnumerable<string> AllowedForms() =>
    [
        "Func<Task> f = (Func<Task>)save; return f;",
        "Func<Task> f = () => (Task)SaveAsync(); return f;",
        "Func<Task> f = () => primary ? (Task)SaveAsync() : (Task)SaveCopyAsync(); return f;",
        "Func<Task> f = () => (Task)(primary ? SaveAsync() : SaveCopyAsync()); return f;",
        "Func<Task<bool>> f = save; return f;",
        "Func<Task<bool>> f = () => primary ? SaveAsync() : SaveCopyAsync(); return f;",
        "Func<object> f = save; return f;",
        "Func<Task> f = async () => await (primary ? SaveAsync() : SaveCopyAsync()); return f;",
        "Func<Task> f = () => { Func<Task<bool>> inner = () => primary ? SaveAsync() : SaveCopyAsync(); return Task.CompletedTask; }; return f;",
        "Func<Task> f = () => plain ?? Task.CompletedTask; return f;",
        "Func<Task> f = RunNothing; return save == f || f != save;",
        "IEnumerable<Func<Task>> all = new List<Func<Task<bool>>> { save }; return all;",
        "Delegate d = save; object o = save; return d ?? o;",
        "Func<Task> nothing = RunNothing; var f = (Func<Task>)(primary ? save : nothing); return f;",
        "Func<Task<bool>>? maybe = save; Func<Task> nothing = RunNothing; var f = (Func<Task>)(maybe ?? nothing); return f;",
        "Func<Task> nothing = RunNothing; var f = (Func<Task>)(mode switch { 0 => save, _ => nothing }); return f;",
        "foreach (Func<Task<bool>> f in new[] { save }) return f; return mode;",
        "foreach (var f in new[] { save }) return f; return mode;",
        "var typed = new List<Func<Task<bool>>> { save }; Func<Task<bool>>[] all = [..typed]; return all;",
        "(Func<Task<bool>>, int) pair = (save, mode); (Func<Task<bool>>, long) wide = pair; return wide;",
        "Func<Task> Wrap<T>(T value) where T : Task => () => value; return Wrap(RunNothing());",
        "Func<Task> Wrap<T>(Task<T> value) where T : Task => () => value; return Wrap(Task.FromResult(RunNothing()));",
        "(Func<Task<bool>>, int) pair = (save, mode); (Func<Task>, int) other = (RunNothing, mode); return pair == other || other != pair;",
        "(Func<Task<bool>>, int) pair = (save, mode); var lost = ((Func<Task>, int))pair; return lost;",
        "Func<Task> nothing = RunNothing; var f = (primary ? save : nothing) as Func<Task>; return f ?? nothing;",
        "foreach (Func<Task> f in new object[] { new Func<Task>(RunNothing) }) return f; return mode;",
        "var pairs = new List<(Func<Task<bool>>, int)> { (save, mode) }; foreach (var (a, b) in pairs) return a; return mode;",
        "var typed = new List<Func<Task<bool>>> { save }; List<object> all = [..typed]; return all;",
        "var typed = new List<Func<Task<bool>>> { save }; Func<Task>[] all = [..(IEnumerable<Func<Task>>)typed]; return all;",
        "var typed = new List<Func<Task<bool>>> { save }; foreach (Func<Task> f in (IEnumerable<Func<Task>>)typed) return f; return mode;",
        "var lost = ((Func<Task>, int))(save, mode); return lost;",
        "var lost = ((Func<Task>, int))(save, mode)!; return lost;",
        "(Func<Task>, int) other = (RunNothing, mode); var lost = ((Func<Task>, int))(primary ? (save, mode) : other); return lost;",
        "var kv = new KeyValuePair<string, Func<Task<bool>>>(\"k\", save); (string k, Func<Task> work) = kv; return work;",
        "var lost = ((Func<Task>, int))(primary ? (save, mode) : (save, 0)); return lost;",
        "var all = (Func<Task>[])[save, save]; return all;",
        "var typed = new List<Func<Task<bool>>> { save }; var all = (Func<Task>[])[..typed, save]; return all;",
        "(Func<Task<bool>>, int) pair = (save, mode); var (work, n) = pair; return work;",
        "(Func<Task<bool>>, int) pair = (save, mode); (Func<Task<bool>> work, long n) = pair; return work;",
        "var pairs = new List<(Func<Task<bool>>, int)> { (save, mode) }; foreach ((Func<Task<bool>> work, long n) in pairs) return work; return mode;",
    ];

    [Test]
    [MethodDataSource(nameof(ReportedForms))]
    public async Task FlagsLostResultAtItsExpression(string body, string lost, CancellationToken cancellationToken)
    {
        var source = Prefix + body + Suffix;

        var diagnostics = await AnalyzerRunner.RunAsync(new DiscardedTaskResultDelegateAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)).IsEqualTo(lost);
    }

    [Test]
    [MethodDataSource(nameof(AllowedForms))]
    public async Task AllowsFormsThatLoseNothing(string body, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new DiscardedTaskResultDelegateAnalyzer(), Prefix + body + Suffix, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(TwoLossForms))]
    public async Task FlagsEachLostResult(string body, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new DiscardedTaskResultDelegateAnalyzer(), Prefix + body + Suffix, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
        foreach (var diagnostic in diagnostics)
            _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    /// <summary>A long chain of operators is a tree as deep as the chain; walking it must not run out of stack.</summary>
    [Test]
    public async Task HandlesVeryDeepLambdaBody(CancellationToken cancellationToken)
    {
        var chain = new StringBuilder("var sum = mode");
        for (var i = 0; i < 10_000; i++)
            _ = chain.Append(" + mode");

        var body = "Func<Task> f = () => { " + chain + "; return SaveAsync(); }; return f;";

        var diagnostics = await AnalyzerRunner.RunAsync(new DiscardedTaskResultDelegateAnalyzer(), Prefix + body + Suffix, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
    }
}
