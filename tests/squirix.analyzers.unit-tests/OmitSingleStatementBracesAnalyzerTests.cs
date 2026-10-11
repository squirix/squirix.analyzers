using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class OmitSingleStatementBracesAnalyzerTests
{
    private const string RuleId = "SQR0010";

    [Test]
    public async Task DoesNotFlagUnbracedIfBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag)
                                  {
                                      if (flag)
                                          Call();
                                  }

                                  void Call()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsSingleLineBracedIfBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool flag)
                                  {
                                      if (flag)
                                      {
                                          Call();
                                      }
                                  }

                                  void Call()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task SkipsDanglingElseViaPlainIf(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  if (other) Call();
                                              }
                                              else
                                                  Other();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsDanglingElseViaElseIfChain(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                                  Other();
                                              else if (other)
                                              {
                                                  if (flag) Call();
                                              }
                                              else
                                                  Other();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsDanglingElseViaNestedLoopTail(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  while (other)
                                                      if (flag) Call();
                                              }
                                              else
                                                  Other();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsDanglingElseViaForeachAndLock(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  foreach (var item in items)
                                                      lock (items)
                                                          if (other) Call();
                                              }
                                              else
                                                  Other();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsIfWithoutElseAroundInnerIf(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  if (other) Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task AllowsMultilineInnerIfElse(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  if (other)
                                                      Call();
                                                  else
                                                      Other();
                                              }
                                              else
                                                  Other();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLocalDeclarationInIf(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  var y = 1;
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLocalFunctionInIf(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  void Local()
                                                  {
                                                  }
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLabeledStatementInIf(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  label: Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsDirectiveInIfBlock(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                              #if DEBUG
                                                  Call();
                                              #endif
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLocalDeclarationInLoop(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              while (flag)
                                              {
                                                  var y = 1;
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLocalFunctionInForeach(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              foreach (var item in items)
                                              {
                                                  void Local()
                                                  {
                                                  }
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLabeledStatementInFor(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              for (var i = 0; i < 1; i++)
                                              {
                                                  label: Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsDirectiveInLoopBlock(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              while (flag)
                                              {
                                              #if DEBUG
                                                  Call();
                                              #endif
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLoopDanglingElse(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                                  while (other)
                                                  {
                                                      if (flag) Call();
                                                  }
                                              else
                                                  Other();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsLoopWithInnerIfNoElse(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              while (flag)
                                              {
                                                  if (other) Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task SkipsLocalDeclarationInUsing(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              using (new System.IO.MemoryStream())
                                              {
                                                  var y = 1;
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsLocalDeclarationInLock(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              lock (items)
                                              {
                                                  var y = 1;
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsUsingDanglingElse(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                                  using (new System.IO.MemoryStream())
                                                  {
                                                      if (other) Call();
                                                  }
                                              else
                                                  Other();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBracedElseBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                                  Call();
                                              else
                                              {
                                                  Other();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task FlagsBracedElseIfBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                                  Call();
                                              else if (other)
                                              {
                                                  Other();
                                              }
                                              else
                                                  Call();
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task FlagsBracedForeachBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              foreach (var item in items)
                                              {
                                                  Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task FlagsBracedForBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              for (var i = 0; i < 1; i++)
                                              {
                                                  Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task FlagsBracedWhileBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              while (flag)
                                              {
                                                  Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task FlagsBracedUsingBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              using (new System.IO.MemoryStream())
                                              {
                                                  Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task FlagsBracedLockBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              lock (items)
                                              {
                                                  Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task FlagsBracedFixedBody(CancellationToken cancellationToken)
    {
        const string source = """
                                      unsafe class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              fixed (int* p = items)
                                              {
                                                  Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task IgnoresMultiStatementBranches(CancellationToken cancellationToken)
    {
        const string source = """
                                      class C
                                      {
                                          void M(bool flag, bool other, int[] items)
                                          {
                                              if (flag)
                                              {
                                                  Call();
                                                  Other();
                                              }
                                              else
                                              {
                                                  Other();
                                                  Call();
                                              }
                                          }

                                          void Call()
                                          {
                                          }

                                          void Other()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Dropping both pairs at once would hand the else to the inner if, so only the inner pair is reported.</summary>
    [Test]
    public async Task FlagsOnlyInnerBracesBeforeElse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool c, bool d, bool x)
                                  {
                                      if (c) { while (d) { if (x) Log(); } }
                                      else Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitSingleStatementBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("while");
    }
}
