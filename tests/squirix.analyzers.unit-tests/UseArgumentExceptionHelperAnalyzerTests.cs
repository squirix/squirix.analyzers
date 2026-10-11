using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class UseArgumentExceptionHelperAnalyzerTests
{
    private const string RuleId = "SQR0021";

    [Test]
    public async Task AllowsAlreadyUsingThrowHelper(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      System.ArgumentException.ThrowIfNullOrWhiteSpace(value);
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsGuardThrowingOtherExceptionType(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (string.IsNullOrWhiteSpace(value))
                                          throw new System.InvalidOperationException("Not ready.");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsMismatchedParamName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string a, string b)
                                  {
                                      if (string.IsNullOrEmpty(a))
                                          throw new System.ArgumentException("Value is required.", nameof(b));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNonStringArgumentCheck(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(byte[] buffer)
                                  {
                                      if (buffer.Length == 0)
                                          throw new System.ArgumentException("Buffer is empty.", nameof(buffer));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUnrelatedIsNullOrEmptyHelper(CancellationToken cancellationToken)
    {
        const string source = """
                              static class Helpers
                              {
                                  public static bool IsNullOrEmpty(string? value) => value == null;
                              }

                              class C
                              {
                                  void M(string value)
                                  {
                                      if (Helpers.IsNullOrEmpty(value))
                                          throw new System.ArgumentException("Value is required.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUserDefinedArgumentException(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Other
                              {
                                  public class ArgumentException : System.Exception
                                  {
                                      public ArgumentException(string message, string paramName) : base(message) { }
                                  }
                              }

                              class C
                              {
                                  void M(string value)
                                  {
                                      if (string.IsNullOrEmpty(value))
                                          throw new Other.ArgumentException("Value is required.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsGloballyQualifiedForms(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (global::System.String.IsNullOrEmpty(value))
                                          throw new global::System.ArgumentException("Value is required.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsIsNullOrEmptyGuardWithBracedBody(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (string.IsNullOrEmpty(value))
                                      {
                                          throw new System.ArgumentException("Value is required.", nameof(value));
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public Task FlagsNamedParamNameMatchingGuard(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("""throw new System.ArgumentException(paramName: "value", message: "Value is required.");""", cancellationToken);

    [Test]
    public Task FlagsNamedParamNameWithNameof(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("""throw new System.ArgumentException(message: "Value is required.", paramName: nameof(value));""", cancellationToken);

    [Test]
    public Task FlagsPositionalNameofParamName(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("""throw new System.ArgumentException("Value is required.", nameof(value));""", cancellationToken);

    [Test]
    public Task FlagsPositionalLiteralParamName(CancellationToken cancellationToken) =>
        AssertFlaggedAsync("""throw new System.ArgumentException("Value is required.", "value");""", cancellationToken);

    [Test]
    public Task AllowsNamedParamNameMismatch(CancellationToken cancellationToken) =>
        AssertNotFlaggedAsync("""throw new System.ArgumentException(paramName: "other", message: "value");""", cancellationToken);

    [Test]
    public Task AllowsSwappedNamedMismatchNameof(CancellationToken cancellationToken) =>
        AssertNotFlaggedAsync("""throw new System.ArgumentException(message: "value", paramName: nameof(other));""", cancellationToken);

    [Test]
    public Task AllowsPositionalLiteralMismatch(CancellationToken cancellationToken) =>
        AssertNotFlaggedAsync("""throw new System.ArgumentException("value", "other");""", cancellationToken);

    [Test]
    public Task AllowsArgumentNullExceptionNamed(CancellationToken cancellationToken) =>
        AssertNotFlaggedAsync("""throw new System.ArgumentNullException(paramName: "value");""", cancellationToken);

    [Test]
    public Task AllowsArgumentNullExceptionMismatch(CancellationToken cancellationToken) =>
        AssertNotFlaggedAsync("""throw new System.ArgumentNullException(message: "value", paramName: "other");""", cancellationToken);

    [Test]
    public Task AllowsOutOfRangeExceptionNamed(CancellationToken cancellationToken) =>
        AssertNotFlaggedAsync("""throw new System.ArgumentOutOfRangeException(paramName: "value", message: "Value is required.");""", cancellationToken);

    [Test]
    public Task AllowsOutOfRangeExceptionMismatch(CancellationToken cancellationToken) =>
        AssertNotFlaggedAsync("""throw new System.ArgumentOutOfRangeException(message: "value", paramName: "other");""", cancellationToken);

    [Test]
    public async Task FlagsNullOrWhitespaceGuard(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (string.IsNullOrWhiteSpace(value))
                                          throw new System.ArgumentException("Value is required.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static async Task AssertFlaggedAsync(string throwStatement, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), CreateSource(throwStatement), cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static async Task AssertNotFlaggedAsync(string throwStatement, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), CreateSource(throwStatement), cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static string CreateSource(string throwStatement) =>
        "class C\n{\n    void M(string value, string other)\n    {\n        if (string.IsNullOrEmpty(value))\n            " + throwStatement + "\n    }\n}\n";

    [Test]
    public async Task ReportsPlainAdviceForParameter(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (string.IsNullOrEmpty(value))
                                          throw new System.ArgumentException("Required.", nameof(value));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("Use 'ArgumentException.ThrowIfNullOrEmpty' instead of an 'if' check with 'throw'");
    }

    /// <summary>The helper would name the parameter 'options.Name', while the guard throws 'Name'.</summary>
    [Test]
    public async Task AdvisesExplicitNameForMember(CancellationToken cancellationToken)
    {
        const string source = """
                              class Options
                              {
                                  public string Name { get; set; } = "";
                              }

                              class C
                              {
                                  void M(Options options)
                                  {
                                      if (string.IsNullOrEmpty(options.Name))
                                          throw new System.ArgumentException("Required.", nameof(options.Name));
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("Use 'ArgumentException.ThrowIfNullOrEmpty' instead of an 'if' check with 'throw'; pass 'nameof(options.Name)' as the second argument to keep the parameter name");
    }

    [Test]
    public async Task AdvisesExplicitNameForLiteral(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private string value = "";

                                  void M()
                                  {
                                      if (string.IsNullOrEmpty(this.value))
                                          throw new System.ArgumentException("Required.", "value");
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new UseArgumentExceptionThrowHelperAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo("Use 'ArgumentException.ThrowIfNullOrEmpty' instead of an 'if' check with 'throw'; pass '\"value\"' as the second argument to keep the parameter name");
    }
}
