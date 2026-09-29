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
}
