using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class PreferEqualityOperatorCodeFixTests
{
    [Test]
    public async Task KeepsCommentsAroundTheCheck(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(string value) => /* before */ value is not null /* after */;
                              }
                              """;

        const string expected = """
                                class C
                                {
                                    bool M(string value) => /* before */ value != null /* after */;
                                }
                                """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(expected);
    }

    [Test]
    public async Task LeavesConstantPatternsAlone(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(int value) => value is 42;
                              }
                              """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(source);
    }

    [Test]
    public async Task LeavesNullArmOfOrPatternAlone(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(string value) => value is null or { Length: 0 };
                              }
                              """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(source);
    }

    [Test]
    public async Task ParenthesizesCheckUnderEqualityOperator(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(bool flag, string value) => flag == value is null;
                              }
                              """;

        const string expected = """
                                class C
                                {
                                    bool M(bool flag, string value) => flag == (value == null);
                                }
                                """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReplacesEveryCheckInLogicalExpression(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  bool M(string first, string second) => first is null && second is not null || !(first is not null);
                              }
                              """;

        const string expected = """
                                class C
                                {
                                    bool M(string first, string second) => first == null && second != null || !(first != null);
                                }
                                """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReplacesIsNotNullWithInequality(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(string value)
                                  {
                                      if (value is not null)
                                          return;
                                  }
                              }
                              """;

        const string expected = """
                                class C
                                {
                                    void M(string value)
                                    {
                                        if (value != null)
                                            return;
                                    }
                                }
                                """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReplacesIsNullWithEquality(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  void M(int? value)
                                  {
                                      if (value is null)
                                          return;
                                  }
                              }
                              """;

        const string expected = """
                                class C
                                {
                                    void M(int? value)
                                    {
                                        if (value == null)
                                            return;
                                    }
                                }
                                """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReplacesCheckOfMemberAccessAndInvocation(CancellationToken cancellationToken)
    {
        const string source = """
                              class C
                              {
                                  string? Field;

                                  string? Get() => Field;

                                  bool M(C other) => other.Field is null || other.Get() is not null;
                              }
                              """;

        const string expected = """
                                class C
                                {
                                    string? Field;

                                    string? Get() => Field;

                                    bool M(C other) => other.Field == null || other.Get() != null;
                                }
                                """;

        _ = await Assert.That(await FixAsync(source, cancellationToken)).IsEqualTo(expected);
    }

    private static Task<string> FixAsync(string source, CancellationToken cancellationToken) =>
        CodeFixRunner.ApplyAsync(new PreferEqualityOperatorAnalyzer(), new PreferEqualityOperatorCodeFixProvider(), source, cancellationToken);
}
