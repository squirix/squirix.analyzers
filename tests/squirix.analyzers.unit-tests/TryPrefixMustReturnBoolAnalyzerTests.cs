using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class TryPrefixMustReturnBoolAnalyzerTests
{
    private const string RuleId = "SQR0025";

    [Test]
    public async Task AllowsTryMethodReturningBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public bool TryParse(string s, out int result) => int.TryParse(s, out result);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNonTryMethodReturningNonBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public int Parse(string s) => int.Parse(s);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsTryMethodReturningInt(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public int TryGetValue(string key) => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTryMethodReturningString(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public string TryFormat(object value) => value.ToString();
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTryMethodReturningVoid(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public void TryDoSomething() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsTryMethodReturningTaskOfBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.Task<bool> TryPingAsync() => System.Threading.Tasks.Task.FromResult(true);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsTryMethodReturningValueTaskBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.ValueTask<bool> TryPingAsync() => new System.Threading.Tasks.ValueTask<bool>(true);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsTryMethodReturningBareTask(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.Task TryPingAsync() => System.Threading.Tasks.Task.CompletedTask;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTryMethodReturningTaskOfInt(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.Task<int> TryGetAsync() => System.Threading.Tasks.Task.FromResult(0);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsBaseButSkipsOverride(CancellationToken cancellationToken)
    {
        const string source = """
                              abstract class B
                              {
                                  public abstract System.Threading.Tasks.Task<int> TryGetAsync();
                              }

                              sealed class C : B
                              {
                                  public override System.Threading.Tasks.Task<int> TryGetAsync() => System.Threading.Tasks.Task.FromResult(0);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1).IsEqualTo(3);
    }
}
