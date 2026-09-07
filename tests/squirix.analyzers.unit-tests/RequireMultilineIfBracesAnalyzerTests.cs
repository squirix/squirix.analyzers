using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class RequireMultilineIfBracesAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0018";

    [Fact]
    public async Task AllowsSingleLineEmbeddedIfBody()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsMultilineEmbeddedIfBody()
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

        var diagnostics = await AnalyzerRunner.RunAsync(new RequireMultilineIfBodyBracesAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}
