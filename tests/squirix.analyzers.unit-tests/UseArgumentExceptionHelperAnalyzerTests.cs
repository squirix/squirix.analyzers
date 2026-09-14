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
}
