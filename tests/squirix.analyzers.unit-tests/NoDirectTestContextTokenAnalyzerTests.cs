using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoDirectTestContextTokenAnalyzerTests
{
    private const string RuleId = "SQR0017";

    [Test]
    public async Task AllowsDeclaredSharedTokenOfAnyName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private System.Threading.CancellationToken SharedToken
                                      => System.Threading.CancellationToken.None;

                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsTypeDeclaredCancellationToken(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private System.Threading.CancellationToken cancellationToken
                                      => System.Threading.CancellationToken.None;

                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUseWhenBaseClassExposesSharedToken(CancellationToken cancellationToken)
    {
        const string source = """
                              class Base
                              {
                                  protected System.Threading.CancellationToken SharedToken
                                      => System.Threading.CancellationToken.None;
                              }

                              class Derived : Base
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task DoesNotFlagPreviousUseInsideStaticClass(CancellationToken cancellationToken)
    {
        const string source = """
                              static class C
                              {
                                  static void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBaseNonTokenThreadingType(CancellationToken cancellationToken)
    {
        const string source = """
                              class Base
                              {
                                  protected System.Threading.SemaphoreSlim Semaphore
                                      => null!;
                              }

                              class Derived : Base
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsDirectTestContextTokenUse(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsUseWhenBaseClassDoesNotExposeToken(CancellationToken cancellationToken)
    {
        const string source = """
                              class Base
                              {
                              }

                              class Derived : Base
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
