using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoSystemClockDefaultAnalyzerTests
{
    private const string RuleId = "SQR0036";

    [Test]
    public async Task AllowsExplicitArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public Worker(TimeProvider clock) { }

                                  static Worker Create() => new Worker(TimeProvider.System);

                                  static Task Wait() => Task.Delay(TimeSpan.Zero, TimeProvider.System);
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsObjectInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using System;

                              class Options
                              {
                                  public TimeProvider? Clock { get; init; }

                                  static Options Create() => new Options { Clock = TimeProvider.System };
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStaticFieldInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Clocks
                              {
                                  private static readonly TimeProvider Shared = TimeProvider.System;

                                  public static TimeProvider Default { get; } = TimeProvider.System;

                                  static TimeProvider Get() => Shared;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsAssignmentToOwnField(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  private readonly TimeProvider _clock;

                                  public Worker()
                                  {
                                      _clock = TimeProvider.System;
                                  }

                                  TimeProvider Get() => _clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsCoalesceAssignment(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using System;

                              class Worker
                              {
                                  public TimeProvider Get(TimeProvider? clock)
                                  {
                                      clock ??= TimeProvider.System;
                                      return clock;
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsCoalesceDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using System;

                              class Worker
                              {
                                  private readonly TimeProvider _clock;

                                  public Worker(TimeProvider? clock)
                                  {
                                      _clock = clock ?? (TimeProvider.System);
                                  }

                                  TimeProvider Get() => _clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsConstructorInitializerArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  public Worker(int size)
                                      : this(size, TimeProvider.System)
                                  {
                                  }

                                  public Worker(int size, TimeProvider clock) { }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsInstanceFieldInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  private TimeProvider _clock = TimeProvider.System;

                                  TimeProvider Get() => _clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsInstancePropertyInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Options
                              {
                                  public TimeProvider Clock { get; init; } = TimeProvider.System;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task IgnoresUnrelatedSystemProperty(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              namespace Acme
                              {
                                  class TimeProvider
                                  {
                                      public static TimeProvider System { get; } = new TimeProvider();

                                      public static TimeProvider Get(TimeProvider? clock) => clock ?? TimeProvider.System;
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source, CancellationToken cancellationToken) =>
        AnalyzerRunner.RunAsync(new NoSystemClockDefaultAnalyzer(), source, cancellationToken);
}
