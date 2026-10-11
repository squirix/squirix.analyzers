using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoPrimaryConstructorAnalyzerTests
{
    private const string RuleId = "SQR0035";

    [Test]
    public async Task AllowsExplicitConstructor(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Acme
                              {
                                  class Cache
                                  {
                                      private readonly int _capacity;

                                      public Cache(int capacity)
                                      {
                                          _capacity = capacity;
                                      }
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoPrimaryConstructorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsPrimaryConstructorOnRecord(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Acme
                              {
                                  record Cache(int Capacity);
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoPrimaryConstructorAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsPrimaryConstructorOnClass(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Acme
                              {
                                  class Cache(int capacity)
                                  {
                                      public int Capacity => capacity;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoPrimaryConstructorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsPrimaryConstructorOnStruct(CancellationToken cancellationToken)
    {
        const string source = """
                              namespace Acme
                              {
                                  struct Position(int x, int y)
                                  {
                                      public int Sum => x + y;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoPrimaryConstructorAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
