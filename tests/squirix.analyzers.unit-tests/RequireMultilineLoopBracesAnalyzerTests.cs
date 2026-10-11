using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineLoopBracesAnalyzerTests
{
    private const string RuleId = "SQR0008";

    [Test]
    public async Task AllowsSingleLineEmbeddedLoopBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      int i = 0;
                                      while (i < 10)
                                          i++;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsMultilineEmbeddedLoopBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      int i = 0;
                                      while (i < 10)
                                          DoSomething(
                                              i);
                                  }

                                  void DoSomething(int value)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsEveryLoopKind(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      for (var i = 0; i < 3; i++)
                                          Call(
                                              i);

                                      foreach (var x in xs)
                                          Call(
                                              x);

                                      foreach (var (a, b) in pairs)
                                          Call(
                                              a, b);

                                      while (Next())
                                          Call(
                                              1);

                                      do
                                          Call(
                                              2);
                                      while (Next());
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(5);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to for body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[1].GetMessage()).IsEqualTo("Add braces to foreach body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[1].Location.SourceSpan.Start, diagnostics[1].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[2].GetMessage()).IsEqualTo("Add braces to foreach body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[2].Location.SourceSpan.Start, diagnostics[2].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[3].GetMessage()).IsEqualTo("Add braces to while body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[3].Location.SourceSpan.Start, diagnostics[3].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[4].GetMessage()).IsEqualTo("Add braces to do body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[4].Location.SourceSpan.Start, diagnostics[4].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    [Test]
    public async Task AllowsBracedMultilineBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      while (Next())
                                      {
                                          Call(
                                              1);
                                      }
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBodyStartingOnTheLoopLine(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      while (Next()) Call(
                                          1);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to while body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    /// <summary>A loop directly inside a loop is exempt; the statement at the end of the chain is reported under its own loop.</summary>
    [Test]
    public async Task FlagsLeafOfNestedLoops(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      foreach (var x in xs)
                                          for (var i = 0; i < x; i++)
                                              Call(
                                                  i);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to for body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    [Test]
    public async Task FlagsLeafUnderOtherLoopPairs(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      while (Next())
                                          foreach (var x in xs)
                                              Call(
                                                  x);

                                      for (var i = 0; i < 3; i++)
                                          while (Next())
                                              Call(
                                                  i);

                                      while (Next())
                                          do
                                              Call(
                                                  1);
                                          while (Next());
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(3);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to foreach body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[1].GetMessage()).IsEqualTo("Add braces to while body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[1].Location.SourceSpan.Start, diagnostics[1].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[2].GetMessage()).IsEqualTo("Add braces to do body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[2].Location.SourceSpan.Start, diagnostics[2].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    [Test]
    public async Task AllowsNestedLoopsWithSingleLineLeaf(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      foreach (var x in xs)
                                          for (var i = 0; i < x; i++)
                                              Call(i);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Only a nested loop is exempt: an if on two lines is a multiline body like any other.</summary>
    [Test]
    public async Task FlagsLoopAroundTwoLineIf(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      while (Next())
                                          if (flag)
                                              Call(1);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to while body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("if");
    }

    [Test]
    public async Task AllowsCommentBeforeSingleLineBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      while (Next())
                                          // The comment is not part of the statement.

                                          Call(1);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The branch that is compiled out is not part of the statement.</summary>
    [Test]
    public async Task AllowsSingleLineBodyUnderDirective(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      while (Next())
                              #if TRACE_ON
                                          Call(
                                              1);
                              #else
                                          Call(2);
                              #endif
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsMultilineBodyUnderDirective(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      while (Next())
                              #if TRACE_ON
                                          Call(1);
                              #else
                                          Call(
                                              2);
                              #endif
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to while body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }
}
