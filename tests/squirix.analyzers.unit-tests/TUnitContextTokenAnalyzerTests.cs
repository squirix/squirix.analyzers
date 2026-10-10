using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class TUnitContextTokenAnalyzerTests
{
    private const string RuleId = "SQR0017";

    private const string TUnitStub = """


                                     #nullable enable
                                     namespace TUnit.Core.Interfaces
                                     {
                                         interface ITestExecution
                                         {
                                             System.Threading.CancellationToken CancellationToken { get; }
                                         }
                                     }

                                     namespace TUnit.Core
                                     {
                                         class TestContext : TUnit.Core.Interfaces.ITestExecution
                                         {
                                             public static TestContext? Current { get; } = new TestContext();

                                             public string Id { get; } = "id";

                                             public TUnit.Core.Interfaces.ITestExecution Execution => this;

                                             System.Threading.CancellationToken TUnit.Core.Interfaces.ITestExecution.CancellationToken => default;
                                         }
                                     }

                                     namespace Other.Core
                                     {
                                         class TestExecution
                                         {
                                             public System.Threading.CancellationToken CancellationToken => default;
                                         }

                                         class TestContext
                                         {
                                             public static TestContext? Current { get; } = new TestContext();

                                             public System.Threading.CancellationToken CancellationToken => default;

                                             public TestExecution Execution { get; } = new TestExecution();
                                         }
                                     }
                                     """;

    /// <summary>Returns every spelling of the TUnit token access that must be reported in full.</summary>
    public static IEnumerable<string> ReportedAccesses() =>
    [
        "TestContext.Current!.Execution.CancellationToken",
        "TestContext.Current?.Execution.CancellationToken",
        "TestContext.Current!.Execution?.CancellationToken",
        "TestContext.Current?.Execution?.CancellationToken",
        "(TestContext.Current!).Execution.CancellationToken",
        "(TestContext.Current)?.Execution.CancellationToken",
        "TUnit.Core.TestContext.Current!.Execution.CancellationToken",
        "global::TUnit.Core.TestContext.Current?.Execution.CancellationToken",
    ];

    /// <summary>Returns expressions that look like the TUnit token access but must not be reported.</summary>
    public static IEnumerable<string> AllowedExpressions() =>
    [
        "TestContext.Current?.Id",
        "TestContext.Current!.Execution",
        "Other.Core.TestContext.Current!.Execution.CancellationToken",
        "Other.Core.TestContext.Current?.CancellationToken",
    ];

    [Test]
    [MethodDataSource(nameof(ReportedAccesses))]
    public async Task FlagsTokenReadFromCurrentContext(string access, CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M() { var token = " + access + "; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)).IsEqualTo(access);
    }

    [Test]
    [MethodDataSource(nameof(AllowedExpressions))]
    public async Task AllowsOtherExpressions(string expression, CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M() { var value = " + expression + "; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AdvisesTheTestMethodParameter(CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M() { var token = TestContext.Current!.Execution.CancellationToken; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "Do not use TestContext.Current.Execution.CancellationToken directly; take the CancellationToken parameter of the test method and pass it down instead");
    }

    [Test]
    public async Task FlagsChainedUse(CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M() { var cancelled = TestContext.Current?.Execution.CancellationToken.IsCancellationRequested; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsUseInsideLambda(CancellationToken cancellationToken)
    {
        var source = Wrap("class C { System.Func<System.Threading.CancellationToken> M() => () => TestContext.Current!.Execution.CancellationToken; }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    /// <summary>A shared token member does not excuse the read, because TUnit passes the token to the test method.</summary>
    [Test]
    public async Task FlagsUseWhenClassExposesSharedToken(CancellationToken cancellationToken)
    {
        var source = Wrap("""
                          class C
                          {
                              protected System.Threading.CancellationToken Token => TestContext.Current!.Execution.CancellationToken;
                          }
                          """);

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsUseInsideStaticClass(CancellationToken cancellationToken)
    {
        var source = Wrap("static class C { static void M() { var token = TestContext.Current!.Execution.CancellationToken; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsTokenFromLocalContext(CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M() { var context = TestContext.Current!; var token = context.Execution.CancellationToken; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsTokenParameter(CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M(System.Threading.CancellationToken cancellationToken) { var token = cancellationToken; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static string Wrap(string code) => "using TUnit.Core;\n\n" + code + TUnitStub;
}
