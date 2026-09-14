using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class PreferEqualityOperatorAnalyzerTests
{
    private const string IsConstantRuleId = "SQR0013";
    private const string IsNotConstantRuleId = "SQR0014";
    private const string NullCheckRuleId = "SQR0012";

    [Test]
    public async Task AllowsEqualityOperatorForConstant(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int value)
                                  {
                                      if (value == 42)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsEqualityOperatorForNullCheck(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (value == null)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsInequalityOperatorForConstant(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int value)
                                  {
                                      if (value != 42)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIsNullWithStructConstraint(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M<T>(T value) where T : struct
                                  {
                                      return value is null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsOrPatternWithoutNullArm(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(string value)
                                  {
                                      return value is { Length: 0 } or { Length: 1 };
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsIsConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int value)
                                  {
                                      if (value is 42)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IsConstantRuleId);
    }

    [Test]
    public async Task FlagsIsNotConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int value)
                                  {
                                      if (value is not 42)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IsNotConstantRuleId);
    }

    [Test]
    public async Task FlagsIsNotNullPatternOnRecord(CancellationToken cancellationToken)
    {
        const string source = """
                              record R(string Value);

                              class C
                              {
                                  void M(R value)
                                  {
                                      if (value is not null)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }

    [Test]
    public async Task FlagsIsNullForUnconstrainedTypeParameter(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M<T>(T? value)
                                  {
                                      return value is null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }

    [Test]
    public async Task FlagsIsNullOnInterfaceType(CancellationToken cancellationToken)
    {
        const string source = """
                              interface IFoo
                              {
                              }

                              class C
                              {
                                  bool M(IFoo? value)
                                  {
                                      return value is null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }

    [Test]
    public async Task FlagsIsNullPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (value is null)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }

    [Test]
    public async Task FlagsIsNullPatternOnRecord(CancellationToken cancellationToken)
    {
        const string source = """
                              record R(string Value);

                              class C
                              {
                                  void M(R value)
                                  {
                                      if (value is null)
                                          return;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }

    [Test]
    public async Task FlagsNullArmInOrPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  string M(string? name)
                                  {
                                      if (name is null or { Length: 0 })
                                          return string.Empty;

                                      return name;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }
}
