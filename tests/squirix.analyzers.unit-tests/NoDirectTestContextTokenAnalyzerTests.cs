using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoDirectTestContextTokenAnalyzerTests
{
    private const string RuleId = "SQR0017";

    private const string Usings = """
                                  using Xunit;
                                  using static Xunit.TestContext;
                                  using Context = Xunit.TestContext;


                                  """;

    private const string XunitStub = """


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

                                             public System.Threading.CancellationToken CancellationToken => default;

                                             public object? Test => null;
                                         }

                                         sealed class TestContext<T>
                                         {
                                             public static TestContext<T>? Current { get; } = new TestContext<T>();

                                             public System.Threading.CancellationToken CancellationToken => default;
                                         }
                                     }

                                     namespace Xunit.Sub
                                     {
                                         sealed class TestContext
                                         {
                                             public static TestContext? Current { get; } = new TestContext();

                                             public System.Threading.CancellationToken CancellationToken => default;
                                         }
                                     }

                                     namespace My.Xunit
                                     {
                                         sealed class TestContext
                                         {
                                             public static TestContext? Current { get; } = new TestContext();

                                             public System.Threading.CancellationToken CancellationToken => default;
                                         }
                                     }

                                     class Other
                                     {
                                         public static Other? Current { get; } = new Other();

                                         public System.Threading.CancellationToken CancellationToken => default;
                                     }
                                     """;

    /// <summary>Returns every spelling of the xUnit token access that must be reported in full.</summary>
    public static IEnumerable<string> ReportedAccesses() =>
    [
        "TestContext.Current.CancellationToken",
        "TestContext.Current!.CancellationToken",
        "TestContext.Current?.CancellationToken",
        "(TestContext.Current).CancellationToken",
        "(TestContext.Current!).CancellationToken",
        "Xunit.TestContext.Current.CancellationToken",
        "global::Xunit.TestContext.Current!.CancellationToken",
        "global::Xunit.TestContext.Current?.CancellationToken",
        "Context.Current!.CancellationToken",
        "Current!.CancellationToken",
    ];

    /// <summary>Returns expressions that look like the xUnit token access but must not be reported.</summary>
    public static IEnumerable<string> AllowedExpressions() =>
    [
        "TestContext.Current?.Test",
        "TestContext.Current!.Test",
        "Other.Current!.CancellationToken",
        "Other.Current?.CancellationToken",
        "My.Xunit.TestContext.Current!.CancellationToken",
        "Xunit.Sub.TestContext.Current?.CancellationToken",
        "Xunit.TestContext<int>.Current!.CancellationToken",
    ];

    /// <summary>Returns code that reads the token where no shared token member is in reach.</summary>
    public static IEnumerable<string> ReportedPlaces() =>
    [
        "class C { System.Func<System.Threading.CancellationToken> M() => () => TestContext.Current!.CancellationToken; }",
        "class C { void M() { Local(); static void Local() { var token = TestContext.Current!.CancellationToken; } } }",
        "class C { private readonly bool _cancelled = TestContext.Current!.CancellationToken.IsCancellationRequested; }",
        "class Base { } class Derived : Base { void M() { var token = TestContext.Current!.CancellationToken; } }",
        "class Base { protected System.Threading.SemaphoreSlim Gate => null!; } class Derived : Base { void M() { var token = TestContext.Current!.CancellationToken; } }",
        "class Outer { private System.Threading.CancellationToken Token => default; class Inner { void M() { var token = TestContext.Current!.CancellationToken; } } }",
        "static class Outer { class Inner { void M() { var token = TestContext.Current!.CancellationToken; } } }",
    ];

    /// <summary>Returns code that reads the token where the rule does not apply.</summary>
    public static IEnumerable<string> AllowedPlaces() =>
    [
        "class C { private System.Threading.CancellationToken SharedToken => default; void M() { var token = TestContext.Current!.CancellationToken; } }",
        "class C { private System.Threading.CancellationToken _token; void M() { _token = TestContext.Current!.CancellationToken; } }",
        "class Base { protected System.Threading.CancellationToken SharedToken => default; } class Derived : Base { void M() { var token = TestContext.Current!.CancellationToken; } }",
        "static class C { static void M() { var token = TestContext.Current!.CancellationToken; } }",
        "struct S { void M() { var token = TestContext.Current!.CancellationToken; } }",
        "var token = TestContext.Current!.CancellationToken;",
        "class C { void M() { var context = TestContext.Current!; var token = context.CancellationToken; } }",
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
    [MethodDataSource(nameof(ReportedPlaces))]
    public async Task FlagsUseWithoutSharedToken(string code, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), Wrap(code), cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    [MethodDataSource(nameof(AllowedPlaces))]
    public async Task AllowsUseWhereRuleDoesNotApply(string code, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), Wrap(code), cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AdvisesTheSharedBaseClassToken(CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M() { var token = TestContext.Current!.CancellationToken; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "Do not use TestContext.Current.CancellationToken directly; consume the shared CancellationToken exposed by a base class instead");
    }

    [Test]
    public async Task FlagsChainedUseUpToTheToken(CancellationToken cancellationToken)
    {
        var source = Wrap("class C { void M() { var cancelled = TestContext.Current?.CancellationToken.IsCancellationRequested; } }");

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length))
            .IsEqualTo("TestContext.Current?.CancellationToken");
    }

    /// <summary>A class that only shares the name with the test framework type is not a test context.</summary>
    [Test]
    public async Task AllowsLookAlikeInGlobalNamespace(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              class C
                              {
                                  void M()
                                  {
                                      var token = TestContext.Current!.CancellationToken;
                                  }
                              }

                              class TestContext
                              {
                                  public static TestContext? Current { get; } = new TestContext();

                                  public System.Threading.CancellationToken CancellationToken => default;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoDirectTestContextCancelTokenAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static string Wrap(string code) => Usings + code + XunitStub;
}
