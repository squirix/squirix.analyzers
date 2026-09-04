using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using Xunit;

namespace Squirix.Analyzers.UnitTests;

public sealed class CoalesceThrowHelperAnalyzerTests : AnalyzerTestBase
{
    private const string RuleId = "SQR0024";

    [Fact]
    public async Task AllowsCoalesceThrowingNullException()
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = value ?? throw new System.ArgumentNullException(nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsCoalesceWithFallbackValue()
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = value ?? new object();
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsGloballyQualifiedNullException()
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = value ?? throw new global::System.ArgumentNullException(nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, DefaultCancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FlagsCoalesceThrowingArgumentException()
    {
        const string source = """
                              class C
                              {
                                  void M(object? value)
                                  {
                                      var record = value ?? throw new System.ArgumentException("Record must not be null.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }

    [Fact]
    public async Task FlagsCoalesceThrowingInvalidOperation()
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object? value)
                                  {
                                      _value = value ?? throw new System.InvalidOperationException("Value is missing.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, DefaultCancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RuleId, diagnostic.Id);
    }
}
