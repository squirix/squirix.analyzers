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

    [Test]
    public async Task AllowsSpanConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(System.Span<char> value)
                                  {
                                      return value is "abc";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsReadOnlySpanConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(System.ReadOnlySpan<char> value)
                                  {
                                      return value is "abc";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIsNotOnReadOnlySpan(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(System.ReadOnlySpan<char> value)
                                  {
                                      return value is not "abc";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsConstantOnInterfaceInput(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(System.IComparable value)
                                  {
                                      return value is 5;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsConstantOnEnumBaseInput(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(System.Enum value)
                                  {
                                      return value is System.DayOfWeek.Monday;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsConstantOnValueTypeInput(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(System.ValueType value)
                                  {
                                      return value is 5;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsConstantOnObjectInput(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(object value)
                                  {
                                      return value is 5;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStringConstantOnObjectInput(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(object value)
                                  {
                                      return value is "x";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIsNotConstantOnObjectInput(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(object value)
                                  {
                                      return value is not 5;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsConstantOnUnconstrainedTypeParam(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M<T>(T value)
                                  {
                                      return value is 5;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsOrPatternWithSeveralConstants(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(int value)
                                  {
                                      return value is 1 or 2;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsIsNotOrPatternConstants(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(int value)
                                  {
                                      return value is not (1 or 2);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNullCheckOnCustomEqualityBase(CancellationToken cancellationToken)
    {
        const string source = """
                              class B
                              {
                                  public static bool operator ==(B? left, B? right) => true;

                                  public static bool operator !=(B? left, B? right) => false;

                                  public override bool Equals(object? obj) => true;

                                  public override int GetHashCode() => 0;
                              }

                              class C
                              {
                                  bool M<T>(T? value) where T : B
                                  {
                                      return value is null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsStringConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(string value)
                                  {
                                      return value is "abc";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IsConstantRuleId);
    }

    [Test]
    public async Task FlagsNullableIntConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(int? value)
                                  {
                                      return value is 5;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IsConstantRuleId);
    }

    [Test]
    public async Task FlagsIsNotEnumConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(System.DayOfWeek value)
                                  {
                                      return value is not System.DayOfWeek.Monday;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IsNotConstantRuleId);
    }

    [Test]
    public async Task FlagsCharConstantPattern(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(char value)
                                  {
                                      return value is 'a';
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(IsConstantRuleId);
    }

    [Test]
    public async Task FlagsSingleConstantInOrPatternNullArm(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(string? value)
                                  {
                                      return value is null or "abc";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }

    [Test]
    public async Task FlagsNullCheckWithClassConstraint(CancellationToken cancellationToken)
    {
        const string source = """
                              class B
                              {
                              }

                              class C
                              {
                                  bool M<T>(T? value) where T : class
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
    public async Task FlagsNullCheckWithPlainBaseConstraint(CancellationToken cancellationToken)
    {
        const string source = """
                              class B
                              {
                              }

                              class C
                              {
                                  bool M<T>(T? value) where T : B
                                  {
                                      return value is null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new PreferEqualityOperatorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(NullCheckRuleId);
    }
}
