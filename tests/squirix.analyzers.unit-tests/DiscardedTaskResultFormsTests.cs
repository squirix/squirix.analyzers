using System.Collections.Generic;
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

                                      static Task<bool> SaveAsync() => Task.FromResult(true);

                                      static Task<bool> SaveCopyAsync() => Task.FromResult(true);

                                      static void RunLater(Func<Task> work) { }

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
    public async Task FlagsEveryReturnOfBlockBody(CancellationToken cancellationToken)
    {
        const string body = "Func<Task> f = () => { if (primary) return SaveAsync(); return mode switch { 0 => SaveAsync(), _ => SaveCopyAsync() }; }; return f;";

        var diagnostics = await AnalyzerRunner.RunAsync(new DiscardedTaskResultDelegateAnalyzer(), Prefix + body + Suffix, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
    }
}
