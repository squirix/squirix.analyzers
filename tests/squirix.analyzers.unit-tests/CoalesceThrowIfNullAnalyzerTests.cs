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

    [Test]
    public async Task AllowsConstructorInitializerArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              class B
                              {
                                  protected B(object value)
                                  {
                                  }
                              }

                              class C : B
                              {
                                  C(object value)
                                      : base(value ?? throw new System.ArgumentNullException(nameof(value)))
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsFieldInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private static readonly object Shared = new object();

                                  private readonly object _value = Shared ?? throw new System.ArgumentNullException(nameof(Shared));
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsExpressionBodiedMember(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  object Get(object value) => value ?? throw new System.ArgumentNullException(nameof(value));
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsCustomMessage(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = value ?? throw new System.ArgumentNullException(nameof(value), "Custom message.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsMismatchedParameterName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = value ?? throw new System.ArgumentNullException("other");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNestedInLargerExpression(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private readonly string _value;

                                  C(object value)
                                  {
                                      _value = (value ?? throw new System.ArgumentNullException(nameof(value))).ToString();
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsLocalDeclaration(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(object value)
                                  {
                                      var local = value ?? throw new System.ArgumentNullException(nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsStringLiteralParameterName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = value ?? throw new System.ArgumentNullException("value");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsParenthesizedCoalesce(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private readonly object _value;

                                  C(object value)
                                  {
                                      _value = (value ?? throw new System.ArgumentNullException(nameof(value)));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new CoalesceThrowIfNullAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
