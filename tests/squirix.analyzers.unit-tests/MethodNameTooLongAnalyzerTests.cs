using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class MethodNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0005";

    [Test]
    public async Task AllowsShortMethodName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void DoWork()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOverLongMethodName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void ThisMethodNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsEventAtLimitWithCustomAccessors(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              class C
                              {
                                  event EventHandler AbcdefghijAbcdefghijAbcdefghijAbcdefghij
                                  {
                                      add { }
                                      remove { }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsLongEventOnceWithCustomAccessors(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              class C
                              {
                                  event EventHandler AbcdefghijAbcdefghijAbcdefghijAbcdefghijK
                                  {
                                      add { }
                                      remove { }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsPropertyAtLimit(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghij
                                  {
                                      get { return 0; }
                                      set { }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsLongPropertyOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghijK
                                  {
                                      get { return 0; }
                                      set { }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsLongAutoPropertyOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghijK { get; set; }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsInitAccessorAtLimit(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghij
                                  {
                                      get { return 0; }
                                      init { }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsLongInitPropertyOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghijK
                                  {
                                      get { return 0; }
                                      init { }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsMethodAtLimit(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void AbcdefghijAbcdefghijAbcdefghijAbcdefghij()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsMethodOverLimit(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task SkipsOverrides(CancellationToken cancellationToken)
    {
        const string source = """
                              abstract class B
                              {
                                  public abstract void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();
                              
                                  public abstract int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP { get; }
                              }
                              
                              class C : B
                              {
                                  public override void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                  {
                                  }
                              
                                  public override int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SkipsImplicitInterfaceImplementation(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();
                              
                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP { get; }
                              }
                              
                              class C : I
                              {
                                  public void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                  {
                                  }
                              
                                  public int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SkipsExplicitInterfaceImplementation(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  void M();
                              
                                  int P { get; }
                              }
                              
                              class C : I
                              {
                                  void I.M()
                                  {
                                  }
                              
                                  int I.P => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task IgnoresLongLocalFunction(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M()
                                  {
                                      void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                      {
                                      }
                              
                                      AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsLambdas(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              class C
                              {
                                  void M()
                                  {
                                      Action a = () => { };
                                      a();
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task SkipsGeneratedMember(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  [System.CodeDom.Compiler.GeneratedCode("tool", "1.0")]
                                  void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                  {
                                  }

                                  [System.Runtime.CompilerServices.CompilerGenerated]
                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP => 0;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The interface dictates the name of both parts of a partial member; only the declaration in the interface is reported.</summary>
    [Test]
    public async Task SkipsPartialInterfaceImplementation(CancellationToken cancellationToken)
    {
        const string source = """
                              interface I
                              {
                                  void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();

                                  int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP { get; }

                                  event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE;
                              }

                              partial class C : I
                              {
                                  public partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();

                                  public partial int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP { get; }

                                  public partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE;
                              }

                              partial class C
                              {
                                  public partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                  {
                                  }

                                  public partial int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP => 0;

                                  public partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE
                                  {
                                      add
                                      {
                                      }

                                      remove
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics.Length).IsEqualTo(3);
        foreach (var diagnostic in diagnostics)
            _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsLessThan(7);
    }

    [Test]
    public async Task ReportsPartialMemberOnce(CancellationToken cancellationToken)
    {
        const string source = """
                              partial class C
                              {
                                  public partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                  {
                                  }

                                  public partial int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP => 0;

                                  public partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE
                                  {
                                      add
                                      {
                                      }

                                      remove
                                      {
                                      }
                                  }
                              }

                              partial class C
                              {
                                  public partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();

                                  public partial int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP { get; }

                                  public partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var lines = new List<int>();
        foreach (var diagnostic in diagnostics)
            lines.Add(diagnostic.Location.GetLineSpan().StartLinePosition.Line);

        lines.Sort();
        _ = await Assert.That(string.Join(",", lines)).IsEqualTo("22,24,26");
    }

    /// <summary>A generator that writes the definition dictates the name; generated code itself is not analyzed.</summary>
    [Test]
    public async Task SkipsPartialWithGeneratedDefinition(CancellationToken cancellationToken)
    {
        const string definition = """
                                  // <auto-generated/>
                                  partial class C
                                  {
                                      partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();
                                  }
                                  """;

        const string implementation = """
                                      partial class C
                                      {
                                          partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                          {
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new MethodNameTooLongAnalyzer(), [definition, implementation], cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>A person names the member; the generator only adds its body and marks that part as generated.</summary>
    [Test]
    public async Task FlagsDefinitionWithGeneratedBody(CancellationToken cancellationToken)
    {
        const string definition = """
                                  partial class C
                                  {
                                      public static partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();

                                      public static partial int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP { get; }

                                      public static partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE;
                                  }
                                  """;

        const string implementation = """
                                      // <auto-generated/>
                                      partial class C
                                      {
                                          [System.CodeDom.Compiler.GeneratedCode("generator", "1.0")]
                                          public static partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                          {
                                          }

                                          [System.CodeDom.Compiler.GeneratedCode("generator", "1.0")]
                                          public static partial int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP => 0;

                                          [System.CodeDom.Compiler.GeneratedCode("generator", "1.0")]
                                          public static partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE
                                          {
                                              add
                                              {
                                              }

                                              remove
                                              {
                                              }
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new MethodNameTooLongAnalyzer(), [definition, implementation], cancellationToken);

        var lines = new List<int>();
        foreach (var diagnostic in diagnostics)
        {
            _ = await Assert.That(diagnostic.Location.SourceTree!.FilePath).IsEqualTo("File0.cs");
            lines.Add(diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        }

        lines.Sort();
        _ = await Assert.That(string.Join(",", lines)).IsEqualTo("2,4,6");
    }

    /// <summary>The marker on the definition part says that a generator chose the name.</summary>
    [Test]
    public async Task SkipsDefinitionMarkedAsGenerated(CancellationToken cancellationToken)
    {
        const string definition = """
                                  partial class C
                                  {
                                      [System.Runtime.CompilerServices.CompilerGenerated]
                                      public static partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();

                                      [System.Runtime.CompilerServices.CompilerGenerated]
                                      public static partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE;
                                  }
                                  """;

        const string implementation = """
                                      partial class C
                                      {
                                          public static partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                          {
                                          }

                                          public static partial event System.Action AbcdefghijAbcdefghijAbcdefghijAbcdefghijKE
                                          {
                                              add
                                              {
                                              }

                                              remove
                                              {
                                              }
                                          }
                                      }
                                      """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new MethodNameTooLongAnalyzer(), [definition, implementation], cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>The marker counts only where it is written: here it sits on the implementation, in the same file as the definition.</summary>
    [Test]
    public async Task FlagsDefinitionNextToMarkedBody(CancellationToken cancellationToken)
    {
        const string source = """
                              partial class C
                              {
                                  public static partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();

                                  [System.Runtime.CompilerServices.CompilerGenerated]
                                  public static partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new MethodNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(2);
    }

    /// <summary>A type marked as generated covers its members. The alias keeps the attribute name out of the file, where the driver would hide the finding itself.</summary>
    [Test]
    public async Task SkipsPartialOfGeneratedType(CancellationToken cancellationToken)
    {
        const string marker = """
                              global using Marker = System.CodeDom.Compiler.GeneratedCodeAttribute;
                              """;

        const string type = """
                            [Marker("generator", "1.0")]
                            partial class C
                            {
                                public partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK();

                                public partial void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                {
                                }
                            }
                            """;

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new MethodNameTooLongAnalyzer(), [marker, type], cancellationToken);

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
                                     public void AbcdefghijAbcdefghijAbcdefghijAbcdefghijK()
                                     {
                                     }

                                     public int AbcdefghijAbcdefghijAbcdefghijAbcdefghijKP => 0;
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

        var diagnostics = await AnalyzerRunner.RunOnFilesAsync(new MethodNameTooLongAnalyzer(), [generated, written], cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }
}
