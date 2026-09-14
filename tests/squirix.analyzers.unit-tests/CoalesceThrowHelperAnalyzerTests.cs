using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class CoalesceThrowHelperAnalyzerTests
{
    private const string RuleId = "SQR0024";

    [Test]
    public async Task AllowsCoalesceThrowingNullException(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsCoalesceWithFallbackValue(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsGloballyQualifiedNullException(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsCoalesceThrowingArgumentException(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsCoalesceThrowingInvalidOperation(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
