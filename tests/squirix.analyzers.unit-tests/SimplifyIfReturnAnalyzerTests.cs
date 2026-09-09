using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class SimplifyIfReturnAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0026";

    [Fact]
    public async Task FlagsUnbracedIfReturnFollowedByReturn()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsBracedSingleReturnFollowedByReturn()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsIfWithElse()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsIfBodyWithMultipleStatements()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsIfReturnWithoutTrailingReturn()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsVoidReturns()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsIfReturnFollowedByThrow()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsIfThrowFollowedByReturn()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task AllowsThrowFollowedByThrow()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new SimplifyIfReturnAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }
}
