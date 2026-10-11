using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineIfBracesAnalyzerTests
{
    private const string NestedAdvice = "; the nested if and its else go inside them";
    private const string RuleId = "SQR0018";

    [Test]
    public async Task AllowsSingleLineEmbeddedIfBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag)
                                  {
                                      if (flag)
                                          flag = false;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsMultilineEmbeddedIfBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag)
                                  {
                                      if (flag)
                                          DoSomething(
                                              flag);
                                  }

                                  void DoSomething(bool value)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsEveryBranchOfChain(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          Call(
                                              1);
                                      else if (xs.Length > 0)
                                          Call(
                                              2);
                                      else
                                          Call(
                                              3);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(3);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[1].GetMessage()).IsEqualTo("Add braces to else if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[1].Location.SourceSpan.Start, diagnostics[1].Location.SourceSpan.Length)).IsEqualTo("Call");
        _ = await Assert.That(diagnostics[2].GetMessage()).IsEqualTo("Add braces to else body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[2].Location.SourceSpan.Start, diagnostics[2].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    [Test]
    public async Task FlagsOnlyMultilineBranch(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          Call(1);
                                      else if (xs.Length > 0)
                                          Call(
                                              2);
                                      else
                                          Call(3);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to else if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    [Test]
    public async Task AllowsBracedBranches(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                      {
                                          Call(
                                              1);
                                      }
                                      else
                                      {
                                          Call(
                                              2);
                                      }
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBodyStartingOnTheIfLine(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag) Call(
                                          1);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    /// <summary>An if directly inside an if is exempt; the statement at the end is reported under its own if.</summary>
    [Test]
    public async Task FlagsLeafOfNestedIf(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          if (xs.Length > 0)
                                              Call(
                                                  1);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    /// <summary>Without braces it is easy to misread which if the else belongs to.</summary>
    [Test]
    public async Task FlagsNestedIfWithElse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          if (xs.Length > 0)
                                              Call(1);
                                          else
                                              Call(2);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines" + NestedAdvice);
        _ = await Assert.That(diagnostics[0].Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(5);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("if");
    }

    [Test]
    public async Task FlagsNestedIfWithElseUnderElseIf(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          Call(1);
                                      else if (xs.Length > 0)
                                          if (xs.Length > 1)
                                              Call(2);
                                          else
                                              Call(3);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to else if body when the embedded statement spans multiple lines" + NestedAdvice);
        _ = await Assert.That(diagnostics[0].Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(7);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("if");
    }

    /// <summary>The else belongs to the innermost if, so the if around it is the one whose body needs braces.</summary>
    [Test]
    public async Task FlagsInnermostIfWithElse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          if (xs.Length > 0)
                                              if (xs.Length > 1)
                                                  Call(1);
                                              else
                                                  Call(2);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines" + NestedAdvice);
        _ = await Assert.That(diagnostics[0].Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(6);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("if");
    }

    [Test]
    public async Task AllowsNestedIfWithElseOnOneLine(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          if (xs.Length > 0) Call(1); else Call(2);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsNestedIfAndItsMultilineBranch(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          if (xs.Length > 0)
                                              Call(1);
                                          else
                                              Call(
                                                  2);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines" + NestedAdvice);
        _ = await Assert.That(diagnostics[0].Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(5);
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("if");
        _ = await Assert.That(diagnostics[1].GetMessage()).IsEqualTo("Add braces to else body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[1].Location.SourceSpan.Start, diagnostics[1].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    /// <summary>The nested if is exempt as a body and checked as an if of its own: the statement gets one diagnostic.</summary>
    [Test]
    public async Task FlagsNestedIfUnderElseIfOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          Call(1);
                                      else if (xs.Length > 0)
                                          if (xs.Length > 1)
                                              Call(
                                                  2);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }

    /// <summary>Only a nested if is exempt: a loop on two lines is a multiline body like any other.</summary>
    [Test]
    public async Task FlagsIfAroundTwoLineLoop(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          foreach (var x in xs)
                                              Call(x);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("foreach");
    }

    [Test]
    public async Task AllowsCommentBeforeSingleLineBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag, int[] xs, (int, int)[] pairs)
                                  {
                                      if (flag)
                                          // The comment is not part of the statement.

                                          Call(1);
                                      else
                                          // Neither is this one.
                                          Call(2);
                                  }

                                  bool Next() => false;

                                  void Call(params int[] values)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

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
                                      if (flag)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

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
                                      if (flag)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).IsEqualTo("Add braces to if body when the embedded statement spans multiple lines");
        _ = await Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length)).IsEqualTo("Call");
    }
}
