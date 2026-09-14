using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class CoalesceThrowIfNullAnalyzerTests
{
    private const string RuleId = "SQR0023";

    [Test]
    public async Task AllowsAlreadyUsingThrowHelper(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      System.ArgumentNullException.ThrowIfNull(value);
                                      _value = value;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsCoalesceThrowingOtherExceptionType(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = value ?? throw new System.InvalidOperationException("Missing.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsCoalesceThrowingNullException(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsGloballyQualifiedNullException(CancellationToken cancellationToken)
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

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
