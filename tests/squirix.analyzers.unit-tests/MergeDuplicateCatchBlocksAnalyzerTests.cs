using System;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class MergeDuplicateCatchBlocksAnalyzerTests
{
    private const string RuleId = "SQR0020";

    [Test]
    public async Task AllowsCatchBlocksWithDifferentBodies(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      try
                                      {
                                          DoWork();
                                      }
                                      catch (System.InvalidOperationException)
                                      {
                                          HandleOne();
                                      }
                                      catch (System.ArgumentException)
                                      {
                                          HandleTwo();
                                      }
                                  }

                                  void DoWork()
                                  {
                                  }

                                  void HandleOne()
                                  {
                                  }

                                  void HandleTwo()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsConsecutiveIdenticalCatchBlocks(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      try
                                      {
                                          DoWork();
                                      }
                                      catch (System.InvalidOperationException)
                                      {
                                          Handle();
                                      }
                                      catch (System.ArgumentException)
                                      {
                                          Handle();
                                      }
                                  }

                                  void DoWork()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsThreeBlocksOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          Handle();
                                      }
                                      catch (ArgumentException)
                                      {
                                          Handle();
                                      }
                                      catch (IOException)
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is InvalidOperationException or ArgumentException or IOException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 0));
    }

    [Test]
    public async Task FlagsEveryRun(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          Handle();
                                      }
                                      catch (ArgumentException)
                                      {
                                          Handle();
                                      }
                                      catch (IOException)
                                      {
                                          Other();
                                      }
                                      catch (TimeoutException)
                                      {
                                          Other();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(2);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is InvalidOperationException or ArgumentException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 0));
        _ = await Assert.That(diagnostics[1].GetMessage()).Contains("(ex is IOException or TimeoutException)");
        _ = await Assert.That(diagnostics[1].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 2));
    }

    [Test]
    public async Task AllowsDuplicatesThatAreNotAdjacent(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          Handle();
                                      }
                                      catch (ArgumentException)
                                      {
                                          Other();
                                      }
                                      catch (IOException)
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>A clause with a filter of its own cannot share one with its neighbours.</summary>
    [Test]
    public async Task AllowsFilteredClauses(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException) when (flag)
                                      {
                                          Handle();
                                      }
                                      catch (ArgumentException)
                                      {
                                          Handle();
                                      }
                                      catch (IOException) when (flag)
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsRunAfterFilteredClause(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException) when (flag)
                                      {
                                          Handle();
                                      }
                                      catch (ArgumentException)
                                      {
                                          Handle();
                                      }
                                      catch (IOException)
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is ArgumentException or IOException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 1));
    }

    /// <summary>Merged, the variable would have another type.</summary>
    [Test]
    public async Task AllowsClausesThatUseTheirException(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException exception)
                                      {
                                          Log(exception);
                                      }
                                      catch (ArgumentException exception)
                                      {
                                          Log(exception);
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The same text names the caught exception in one body and the field in the other.</summary>
    [Test]
    public async Task AllowsBodiesWhereOneNamesItsException(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  private readonly Exception failure = new Exception();

                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException failure)
                                      {
                                          Log(failure);
                                      }
                                      catch (ArgumentException)
                                      {
                                          Log(failure);
                                      }

                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          Log(failure);
                                      }
                                      catch (ArgumentException failure)
                                      {
                                          Log(failure);
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsExceptionUsedOnlyInLambda(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException exception)
                                      {
                                          Action log = () => Log(exception);
                                          log();
                                      }
                                      catch (ArgumentException exception)
                                      {
                                          Action log = () => Log(exception);
                                          log();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsBodiesThatDifferInSecondStatement(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          Handle();
                                          Other();
                                      }
                                      catch (ArgumentException)
                                      {
                                          Handle();
                                          Work();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsClausesWithUnusedVariables(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException first)
                                      {
                                          Handle();
                                      }
                                      catch (ArgumentException second)
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is InvalidOperationException or ArgumentException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 0));
    }

    [Test]
    public async Task AllowsGeneralCatch(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          Handle();
                                      }
                                      catch
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsEmptyBodies(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                      }
                                      catch (ArgumentException)
                                      {
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is InvalidOperationException or ArgumentException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 0));
    }

    [Test]
    public async Task FlagsBodiesThatDifferOnlyInComments(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          // The state is already consistent.
                                          Handle();
                                      }
                                      catch (ArgumentException)
                                      {
                                          Handle(); // Nothing was written.
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is InvalidOperationException or ArgumentException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 0));
    }

    [Test]
    public async Task FlagsQualifiedTypesAsWritten(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (System.InvalidOperationException)
                                      {
                                          Handle();
                                      }
                                      catch (System.IO.IOException)
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is System.InvalidOperationException or System.IO.IOException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 0));
    }

    [Test]
    public async Task ChecksNestedTryOnItsOwn(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.IO;

                              class C
                              {
                                  void M(bool flag)
                                  {
                                      try
                                      {
                                          Work();
                                      }
                                      catch (InvalidOperationException)
                                      {
                                          try
                                          {
                                              Work();
                                          }
                                          catch (ArgumentException)
                                          {
                                              Handle();
                                          }
                                          catch (IOException)
                                          {
                                              Handle();
                                          }
                                      }
                                      catch (TimeoutException)
                                      {
                                          Handle();
                                      }
                                  }

                                  void Work()
                                  {
                                  }

                                  void Handle()
                                  {
                                  }

                                  void Other()
                                  {
                                  }

                                  void Log(Exception exception)
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MergeDuplicateCatchBlocksAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(1);
        _ = await Assert.That(diagnostics[0].GetMessage()).Contains("(ex is ArgumentException or IOException)");
        _ = await Assert.That(diagnostics[0].Location.SourceSpan.Start).IsEqualTo(IndexOfCatch(source, 1));
    }

    private static int IndexOfCatch(string source, int index)
    {
        var position = -1;
        for (var i = 0; i <= index; i++)
            position = source.IndexOf("catch", position + 1, StringComparison.Ordinal);

        return position;
    }
}
