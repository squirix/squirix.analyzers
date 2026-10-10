using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class OmitOuterLoopBracesAnalyzerTests
{
    private const string RuleId = "SQR0001";

    [Test]
    public async Task DoesNotFlagOuterLoopWithNonLoopBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      int i = 0;
                                      while (i < 10)
                                      {
                                          i++;
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOuterLoopContainingOnlyNestedLoop(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      int i = 0;
                                      while (i < 10)
                                      {
                                          for (int j = 0; j < 10; j++)
                                          {
                                          }
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    /// <summary>Without the braces, the statement inside the directive becomes the whole loop body when the symbol is defined.</summary>
    [Test]
    public async Task AllowsBracesAroundDirective(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int[] xs, int[] ys)
                                  {
                                      foreach (var x in xs)
                                      {
                              #if TRACE_ON
                                          Log();
                              #endif
                                          foreach (var y in ys) Log();
                                      }
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Without the braces, the else binds to the inner if.</summary>
    [Test]
    public async Task AllowsBracesThatKeepElseOnOuterIf(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool c, bool d, int[] ys)
                                  {
                                      if (c)
                                          while (d) { foreach (var y in ys) if (y > 0) Log(); }
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsBracesBeforeElseInLoopChain(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool c, bool d, int[] xs, int[] ys)
                                  {
                                      if (c)
                                          foreach (var x in xs)
                                              while (d) { for (var i = 0; i < 2; i++) foreach (var y in ys) if (y > 0) Log(); }
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBracesBeforeElseWithMatchedIf(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool c, bool d, int[] ys)
                                  {
                                      if (c)
                                          while (d) { foreach (var y in ys) if (y > 0) Log(); else Log(); }
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsBracesInElseBranch(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool c, bool d, int[] ys)
                                  {
                                      if (c)
                                          Log();
                                      else
                                          while (d) { foreach (var y in ys) if (y > 0) Log(); }
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsBracesNextToOutsideDirective(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int[] xs, int[] ys)
                                  {
                              #if TRACE_ON
                                      Log();
                              #endif
                                      foreach (var x in xs)
                                      {
                                          foreach (var y in ys) Log();
                                      }
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    /// <summary>The closing 'while' of a do loop keeps the else on the outer if, so the braces can go.</summary>
    [Test]
    public async Task FlagsDoLoopBracesBeforeElse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool c, bool d, int[] ys)
                                  {
                                      if (c)
                                          do { foreach (var y in ys) if (y > 0) Log(); } while (d);
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    /// <summary>The inner braces may be removed by another rule, so they do not make the outer ones safe to drop.</summary>
    [Test]
    public async Task AllowsBracesBeforeElseWithBracedLoop(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool a, bool c, bool d, int[] xs, int[] ys)
                                  {
                                      if (c)
                                          while (d) { foreach (var y in ys) { if (y > 0) Log(); } }
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Dropping both pairs at once would hand the else to the inner if, so only one pair is reported.</summary>
    [Test]
    public async Task FlagsOnlyInnermostBracesBeforeElse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool a, bool c, bool d, int[] xs, int[] ys)
                                  {
                                      if (c)
                                          while (d) { for (var i = 0; i < 1; i++) { foreach (var y in ys) if (y > 0) Log(); } }
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("outer for");
    }

    [Test]
    public async Task FlagsBracesAroundNestedDoBeforeElse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool a, bool c, bool d, int[] xs, int[] ys)
                                  {
                                      if (c)
                                          while (d) { do foreach (var y in ys) if (y > 0) Log(); while (d); }
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("outer while");
    }

    [Test]
    public async Task AllowsBracesInMiddleOfElseIfChain(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool a, bool c, bool d, int[] xs, int[] ys)
                                  {
                                      if (a)
                                          Log();
                                      else if (c)
                                          while (d) { foreach (var y in ys) if (y > 0) Log(); }
                                      else
                                          Log();
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsBracesWithDirectiveBeforeClose(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool a, bool c, bool d, int[] xs, int[] ys)
                                  {
                                      foreach (var x in xs)
                                      {
                                          foreach (var y in ys) Log();
                              #if TRACE_ON
                                          Log();
                              #endif
                                      }
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Only conditional compilation can change which statements the block holds.</summary>
    [Test]
    public async Task FlagsBracesAroundPragmaInNestedLoop(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool a, bool c, bool d, int[] xs, int[] ys)
                                  {
                                      foreach (var x in xs)
                                      {
                                          foreach (var y in ys)
                                          {
                              #pragma warning disable CS0168
                                              Log();
                              #pragma warning restore CS0168
                                              Log();
                                          }
                                      }
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsBracesAroundRegionInNestedLoop(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(bool a, bool c, bool d, int[] xs, int[] ys)
                                  {
                                      foreach (var x in xs)
                                      {
                                          foreach (var y in ys)
                                          {
                                              #region Work
                                              Log();
                                              #endregion
                                              Log();
                                          }
                                      }
                                  }

                                  void Log() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new OmitOuterLoopBracesAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
