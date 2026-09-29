using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class FieldNameTooLongAnalyzerTests
{
    private const string RuleId = "SQR0006";

    [Test]
    public async Task AllowsShortFieldName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int _state;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsOverLongFieldName(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  private int ThisFieldNameIsSoExtremelyLongThatItExceedsTheFortyCharacterLimit;
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task AllowsFortyCharacterField(CancellationToken cancellationToken)
    {
        var source = $$"""
                       class C
                       {
                           private int {{new string('a', 40)}};
                       }
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsFortyOneCharacterField(CancellationToken cancellationToken)
    {
        var source = $$"""
                       class C
                       {
                           private int {{new string('a', 41)}};
                       }
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsOverLongConstField(CancellationToken cancellationToken)
    {
        var source = $$"""
                       class C
                       {
                           private const int {{new string('a', 41)}} = 1;
                       }
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
    }

    [Test]
    public async Task FlagsOverLongStaticReadonlyField(CancellationToken cancellationToken)
    {
        var source = $$"""
                       class C
                       {
                           private static readonly int {{new string('a', 41)}} = 1;
                       }
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).HasSingleItem();
    }

    [Test]
    public async Task IgnoresOverLongEnumMember(CancellationToken cancellationToken)
    {
        var source = $$"""
                       enum E
                       {
                           {{new string('a', 41)}},
                       }
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task IgnoresAutoPropertyBackingField(CancellationToken cancellationToken)
    {
        var source = $$"""
                       class C
                       {
                           public int {{new string('a', 41)}} { get; set; }
                       }
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task IgnoresFieldLikeEventBackingField(CancellationToken cancellationToken)
    {
        var source = $$"""
                       using System;

                       class C
                       {
                           public event Action {{new string('a', 41)}};
                       }
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task IgnoresRecordPositionalParameter(CancellationToken cancellationToken)
    {
        var source = $$"""
                       record R(int {{new string('a', 41)}});
                       """;

        var diagnostics = await AnalyzerRunner.RunAsync(new FieldNameTooLongAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }
}
