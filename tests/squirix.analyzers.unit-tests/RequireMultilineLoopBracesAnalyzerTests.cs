using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineLoopBracesAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0008";

    [Fact]
    public async Task AllowsSingleLineEmbeddedLoopBody()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsMultilineEmbeddedLoopBody()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineLoopBodyBracesAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}
