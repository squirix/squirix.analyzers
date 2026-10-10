using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoDirectTestContextTokenAnalyzerTests
{
    private const string RuleId = "SQR0017";

    private const string TestContextStub = """


                                           #nullable enable
                                           namespace Xunit
                                           {
                                               interface ITestContext
                                               {
                                                   System.Threading.CancellationToken CancellationToken { get; }

                                                   object? Test { get; }
                                               }

                                               sealed class TestContext : ITestContext
                                               {
                                                   public static ITestContext? Current { get; } = new TestContext();

                                                   public System.Threading.CancellationToken CancellationToken => System.Threading.CancellationToken.None;

                                                   public object? Test => null;
                                               }
                                           }

                                           class Other
                                           {
                                               public static Other? Current { get; } = new Other();

                                               public System.Threading.CancellationToken CancellationToken => System.Threading.CancellationToken.None;
                                           }
                                           """;

    [Test]
    public async Task AllowsDeclaredSharedTokenOfAnyName(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  private System.Threading.CancellationToken SharedToken
                                      => System.Threading.CancellationToken.None;

                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsTypeDeclaredCancellationToken(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  private System.Threading.CancellationToken cancellationToken
                                      => System.Threading.CancellationToken.None;

                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUseWhenBaseClassExposesSharedToken(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

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
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task DoesNotFlagPreviousUseInsideStaticClass(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              static class C
                              {
                                  static void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBaseNonTokenThreadingType(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

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
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsDirectTestContextTokenUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "Do not use TestContext.Current.CancellationToken directly; consume the shared CancellationToken exposed by a base class instead");
    }

    [Test]
    public async Task FlagsUseWhenBaseClassDoesNotExposeToken(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

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
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsNullForgivingTokenUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current!.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsNullConditionalTokenUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current?.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsNullConditionalChainedUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current?.CancellationToken.IsCancellationRequested;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsParenthesizedTokenUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = (TestContext.Current).CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsParenthesizedForgivingUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = (TestContext.Current!).CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsQualifiedTokenUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = Xunit.TestContext.Current.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsGlobalQualifiedTokenUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = global::Xunit.TestContext.Current!.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsGlobalQualifiedNamespaceUse(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = global::Xunit.TestContext.Current?.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsTokenFromLocalContext(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var ctx = TestContext.Current!;
                                      var token = ctx.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNullConditionalOtherMember(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current?.Test;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNullForgivingOtherMember(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current!.Test;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUnrelatedCurrentType(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = Other.Current!.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsUnrelatedConditionalCurrent(CancellationToken cancellationToken)
    {
        const string source = """
                              using Xunit;

                              class C
                              {
                                  void M()
                                  {
                                      var token = Other.Current?.CancellationToken;
                                  }
                              }
                              """ + TestContextStub;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }
}
