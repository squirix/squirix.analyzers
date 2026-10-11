using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class TryPrefixMustReturnBoolAnalyzerTests
{
    private const string RuleId = "SQR0025";

    [Test]
    public async Task AllowsTryMethodReturningBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public bool TryParse(string s, out int result) => int.TryParse(s, out result);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNonTryMethodReturningNonBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public int Parse(string s) => int.Parse(s);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsTryMethodReturningInt(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public int TryGetValue(string key) => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTryMethodReturningString(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public string TryFormat(object value) => value.ToString();
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTryMethodReturningVoid(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public void TryDoSomething() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsTryMethodReturningTaskOfBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.Task<bool> TryPingAsync() => System.Threading.Tasks.Task.FromResult(true);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsTryMethodReturningValueTaskBool(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.ValueTask<bool> TryPingAsync() => new System.Threading.Tasks.ValueTask<bool>(true);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsTryMethodReturningBareTask(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.Task TryPingAsync() => System.Threading.Tasks.Task.CompletedTask;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsTryMethodReturningTaskOfInt(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public System.Threading.Tasks.Task<int> TryGetAsync() => System.Threading.Tasks.Task.FromResult(0);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsBaseButSkipsOverride(CancellationToken cancellationToken)
    {
        const string source = """
                              abstract class B
                              {
                                  public abstract System.Threading.Tasks.Task<int> TryGetAsync();
                              }

                              sealed class C : B
                              {
                                  public override System.Threading.Tasks.Task<int> TryGetAsync() => System.Threading.Tasks.Task.FromResult(0);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1).IsEqualTo(3);
    }

    [Test]
    public async Task SkipsImplicitInterfaceImplementation(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  void TryIt();
                              }

                              class C : I
                              {
                                  public void TryIt() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1).IsEqualTo(3);
    }

    [Test]
    public async Task SkipsExplicitInterfaceImplementation(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  void TryIt();
                              }

                              class C : I
                              {
                                  void I.TryIt() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1).IsEqualTo(3);
    }

    [Test]
    public async Task FlagsInterfaceDeclaration(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  int TryIt();
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsNonImplementingMethodOnImplementer(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  void Run();
                              }

                              class C : I
                              {
                                  public void Run() { }

                                  public void TryOther() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
    }

    [Test]
    public async Task IgnoresNameWithoutWordBoundary(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public void Tryout() { }

                                  public int Trying() => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsBareTryName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public void Try() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
    }

    [Test]
    public async Task FlagsDigitAndUnderscoreBoundaries(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  public void Try1() { }

                                  public void Try_x() { }

                                  public void TryParse() { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(3);
    }

    [Test]
    public async Task MessageShowsVoidReturnType(CancellationToken cancellationToken)
    {
        var message = await GetSingleMessageAsync("class C { public void TryIt() { } }", cancellationToken);

        _ = await Assert.That(message).IsEqualTo("Method 'TryIt' has 'Try' prefix but returns 'void', expected bool, Task<bool>, or ValueTask<bool>");
    }

    [Test]
    public async Task MessageShowsArrayReturnType(CancellationToken cancellationToken)
    {
        var message = await GetSingleMessageAsync("class C { public int[] TryIt() => null; }", cancellationToken);

        _ = await Assert.That(message).Contains("returns 'int[]'");
    }

    [Test]
    public async Task MessageShowsGenericReturnType(CancellationToken cancellationToken)
    {
        var message = await GetSingleMessageAsync("class C { public System.Collections.Generic.List<int> TryIt() => null; }", cancellationToken);

        _ = await Assert.That(message).Contains("returns 'List<int>'");
    }

    [Test]
    public async Task MessageShowsTupleReturnType(CancellationToken cancellationToken)
    {
        var message = await GetSingleMessageAsync("class C { public (int, string) TryIt() => (0, \"\"); }", cancellationToken);

        _ = await Assert.That(message).Contains("returns '(int, string)'");
    }

    private static async Task<string> GetSingleMessageAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);
        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        return diagnostic.GetMessage();
    }

    [Test]
    public async Task SkipsGeneratedMember(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  [System.CodeDom.Compiler.GeneratedCode("tool", "1.0")]
                                  int TryGet() => 0;

                                  [System.Runtime.CompilerServices.CompilerGenerated]
                                  void TryRun()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The interface dictates the name of both parts of a partial member; only the declaration in the interface is reported.</summary>
    [Test]
    public async Task SkipsPartialInterfaceImplementation(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  int TryGet();
                              }

                              partial class C : I
                              {
                                  public partial int TryGet();
                              }

                              partial class C
                              {
                                  public partial int TryGet() => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(2);
    }

    [Test]
    public async Task SkipsPartialImplementationInOtherFile(CancellationToken cancellationToken)
    {
        const string implementation = """
                                      partial class C
                                      {
                                          public partial int TryGet() => 0;
                                      }
                                      """;

        const string definition = """
                                  interface I
                                  {
                                      int TryGet();
                                  }

                                  partial class C : I
                                  {
                                      public partial int TryGet();
                                  }
                                  """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new TryPrefixMustReturnBoolAnalyzer(), [implementation, definition], cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.SourceTree!.FilePath).IsEqualTo("File1.cs");
    }

    [Test]
    public async Task ReportsPartialMemberOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              partial class C
                              {
                                  public partial int TryGet() => 0;
                              }

                              partial class C
                              {
                                  public partial int TryGet();
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new TryPrefixMustReturnBoolAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(7);
    }

    /// <summary>A generator that writes the definition dictates the name; generated code itself is not analyzed.</summary>
    [Test]
    public async Task SkipsPartialWithGeneratedDefinition(CancellationToken cancellationToken)
    {
        const string definition = """
                                  // <auto-generated/>
                                  partial class C
                                  {
                                      private partial int TryGet();
                                  }
                                  """;

        const string implementation = """
                                      partial class C
                                      {
                                          private partial int TryGet() => 0;
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new TryPrefixMustReturnBoolAnalyzer(), [definition, implementation], cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>A person names the member; the generator only adds its body and marks that part as generated.</summary>
    [Test]
    public async Task FlagsDefinitionWithGeneratedBody(CancellationToken cancellationToken)
    {
        const string definition = """
                                  partial class C
                                  {
                                      public static partial int TryGet();
                                  }
                                  """;

        const string implementation = """
                                      // <auto-generated/>
                                      partial class C
                                      {
                                          [System.CodeDom.Compiler.GeneratedCode("generator", "1.0")]
                                          public static partial int TryGet() => 0;
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new TryPrefixMustReturnBoolAnalyzer(), [definition, implementation], cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.SourceTree!.FilePath).IsEqualTo("File0.cs");
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(2);
    }

    /// <summary>The marker on the definition part says that a generator chose the name.</summary>
    [Test]
    public async Task SkipsDefinitionMarkedAsGenerated(CancellationToken cancellationToken)
    {
        const string definition = """
                                  partial class C
                                  {
                                      [System.Runtime.CompilerServices.CompilerGenerated]
                                      public static partial int TryGet();
                                  }
                                  """;

        const string implementation = """
                                      partial class C
                                      {
                                          public static partial int TryGet() => 0;
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new TryPrefixMustReturnBoolAnalyzer(), [definition, implementation], cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The rule runs on generated code only to reach hand-written definitions; the code of a generated file is still left alone.</summary>
    [Test]
    public async Task SkipsMembersOfGeneratedFile(CancellationToken cancellationToken)
    {
        const string generated = """
                                 // <auto-generated/>
                                 partial class C
                                 {
                                     public int TryGet() => 0;
                                 }
                                 """;

        const string written = """
                               partial class C
                               {
                                   public void Run()
                                   {
                                   }
                               }
                               """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new TryPrefixMustReturnBoolAnalyzer(), [generated, written], cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }
}
