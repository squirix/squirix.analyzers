using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class TryPrefixMustReturnBoolAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0025";

    [Fact]
    public async Task AllowsTryMethodReturningBool()
    {
        const string source = """
                              class C
                              {
                                  public bool TryParse(string s, out int result) => int.TryParse(s, out result);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsNonTryMethodReturningNonBool()
    {
        const string source = """
                              class C
                              {
                                  public int Parse(string s) => int.Parse(s);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsTryMethodReturningInt()
    {
        const string source = """
                              class C
                              {
                                  public int TryGetValue(string key) => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsTryMethodReturningString()
    {
        const string source = """
                              class C
                              {
                                  public string TryFormat(object value) => value.ToString();
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsTryMethodReturningVoid()
    {
        const string source = """
                              class C
                              {
                                  public void TryDoSomething() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}