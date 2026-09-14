using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class SimplifyIfReturnAnalyzerTests
{
    private const string RuleId = "SQR0026";

    [Test]
    public async Task FlagsUnbracedIfReturnFollowedByReturn(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  string? M(string? value)
                                  {
                                      if (value == null)
                                          return null;

                                      return value.Length == 0 ? string.Empty : value;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsBracedSingleReturnFollowedByReturn(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int M(int value)
                                  {
                                      if (value < 0)
                                      {
                                          return -1;
                                      }

                                      return value;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsIfWithElse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int M(int value)
                                  {
                                      if (value < 0)
                                          return -1;
                                      else
                                          return 0;

                                      return value;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIfBodyWithMultipleStatements(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int M(int value)
                                  {
                                      if (value < 0)
                                      {
                                          Log();
                                          return -1;
                                      }

                                      return value;
                                  }

                                  void Log()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIfReturnWithoutTrailingReturn(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int M(int value)
                                  {
                                      if (value < 0)
                                          return -1;

                                      value++;
                                      return value;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsVoidReturns(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int value)
                                  {
                                      if (value < 0)
                                          return;

                                      return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsIfReturnFollowedByThrow(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  string M(string? value)
                                  {
                                      if (value == null)
                                          return string.Empty;

                                      throw new System.ArgumentException("Unsupported.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsIfThrowFollowedByReturn(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  string M(string? value)
                                  {
                                      if (value == null)
                                          throw new System.ArgumentNullException(nameof(value));

                                      return value;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsThrowFollowedByThrow(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string? value)
                                  {
                                      if (value == null)
                                          throw new System.ArgumentNullException(nameof(value));

                                      throw new System.ArgumentException("Unsupported.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsRefReturns(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int[] _data = new int[1];

                                  public ref int GetItem(int index)
                                  {
                                      if (index < 0)
                                          return ref _data[0];

                                      return ref _data[index];
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }
}
