using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class ClockBypassesProviderAnalyzerTests
{
    private const string RuleId = "SQR0037";

    [Test]
    public async Task AllowsClockReadWithoutProvider(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  public long Read() => DateTime.UtcNow.Ticks + Environment.TickCount64;

                                  public TimeProvider Get() => TimeProvider.System;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsComparisonWithSystemProvider(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  private readonly TimeProvider _clock;

                                  public Worker(TimeProvider clock) => _clock = clock;

                                  public bool IsReal => _clock == TimeProvider.System;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsHiddenDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using System;

                              class Worker
                              {
                                  private readonly TimeProvider _clock;

                                  public Worker(TimeProvider? clock) => _clock = clock ?? TimeProvider.System;

                                  TimeProvider Get() => _clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNameof(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  public string Name(TimeProvider clock) => nameof(DateTime.UtcNow) + clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsOwnStaticFieldInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Clocks
                              {
                                  private static readonly TimeProvider Shared = TimeProvider.System;

                                  static TimeProvider Get() => Shared;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsPrivateFieldOfBaseType(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Base
                              {
                                  private readonly TimeProvider _clock = null!;

                                  protected TimeProvider Get() => _clock;
                              }

                              class Worker : Base
                              {
                                  public DateTime Read() => DateTime.UtcNow;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsObjectInitializerMember(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Options
                              {
                                  public required TimeProvider Clock { get; init; }
                              }

                              class Worker
                              {
                                  public Options Create(TimeProvider clock) => new Options { Clock = TimeProvider.System };
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsClockReadInsideTimeProvider(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              sealed class OffsetClock : TimeProvider
                              {
                                  public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(1);
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStaticLambdaInsideMethod(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  public Func<DateTime> Read(TimeProvider clock) => static () => DateTime.UtcNow;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStaticMethodWithInstanceField(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  private readonly TimeProvider _clock;

                                  public Worker(TimeProvider clock) => _clock = clock;

                                  public static DateTime Read() => DateTime.UtcNow;

                                  TimeProvider Get() => _clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsClockReadInComparison(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  public bool IsDue(TimeProvider clock, DateTime due) => DateTime.UtcNow == due;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsDateTimeNowWithField(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  private readonly TimeProvider _clock;

                                  public Worker(TimeProvider clock) => _clock = clock;

                                  public DateTime Read() => DateTime.UtcNow;

                                  TimeProvider Get() => _clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("TimeProvider '_clock'");
    }

    [Test]
    public async Task FlagsDateTimeOffsetNowWithProperty(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker
                              {
                                  public required TimeProvider Clock { get; init; }

                                  public DateTimeOffset Read() => System.DateTimeOffset.Now;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("DateTimeOffset.Now");
    }

    [Test]
    public async Task FlagsInheritedProtectedMember(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Base
                              {
                                  protected TimeProvider Clock { get; } = null!;
                              }

                              class Worker : Base
                              {
                                  public long Read() => Environment.TickCount64;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsLambdaCapturingParameter(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              static class Worker
                              {
                                  public static Func<int> Read(TimeProvider clock) => () => Environment.TickCount + clock.GetHashCode();
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("TimeProvider 'clock'");
    }

    [Test]
    public async Task FlagsPrimaryConstructorParameter(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Worker(TimeProvider clock)
                              {
                                  public DateTime Read() => DateTime.Now;

                                  public TimeProvider Get() => clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsStaticFieldInStaticMethod(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              static class Worker
                              {
                                  private static readonly TimeProvider Clock = TimeProvider.System;

                                  public static long Read() => Environment.TickCount64 + Clock.GetTimestamp();
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    public async Task FlagsSystemProviderWithField(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  private readonly TimeProvider _clock;

                                  public Worker(TimeProvider clock) => _clock = clock;

                                  public Task Wait() => Task.Delay(TimeSpan.Zero, TimeProvider.System);

                                  TimeProvider Get() => _clock;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("TimeProvider.System");
    }

    [Test]
    public async Task FlagsTypeDerivedFromTimeProvider(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              sealed class FixedClock : TimeProvider
                              {
                              }

                              class Worker
                              {
                                  public DateTime Read(FixedClock clock) => DateTime.UtcNow;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source, CancellationToken cancellationToken) =>
        AnalyzerRunner.RunAsync(new ClockBypassesTimeProviderAnalyzer(), source, cancellationToken);
}
