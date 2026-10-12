using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoNullLoggerDefaultAnalyzerTests
{
    private const string RuleId = "SQR0039";

    private const string Stubs = """
                              namespace Microsoft.Extensions.Logging
                              {
                                  public interface ILogger { }

                                  public interface ILogger<out TCategoryName> : ILogger { }

                                  public interface ILoggerFactory { }
                              }

                              namespace Microsoft.Extensions.Logging.Abstractions
                              {
                                  public class NullLogger : ILogger
                                  {
                                      public static NullLogger Instance { get; } = new NullLogger();
                                  }

                                  public class NullLogger<T> : ILogger<T>
                                  {
                                      public static readonly NullLogger<T> Instance = new NullLogger<T>();
                                  }

                                  public class NullLoggerFactory : ILoggerFactory
                                  {
                                      public static readonly NullLoggerFactory Instance = new NullLoggerFactory();
                                  }
                              }
                              """;

    [Test]
    public async Task AllowsExplicitArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  public Worker(ILogger log) { }

                                  static Worker Create() => new Worker(NullLogger.Instance);
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
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Options
                              {
                                  public ILogger? Log { get; init; }

                                  static Options Create() => new Options { Log = NullLogger.Instance };
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStaticFieldInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Holder
                              {
                                  private static readonly ILoggerFactory Factory = NullLoggerFactory.Instance;

                                  static ILoggerFactory Get() => Factory;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsAssignmentToOwnField(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private readonly ILogger _log;

                                  public Worker()
                                  {
                                      _log = NullLogger.Instance;
                                  }

                                  ILogger Get() => _log;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("NullLogger.Instance");
    }

    [Test]
    public async Task FlagsCoalesceAssignment(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  public ILogger Get(ILogger? log)
                                  {
                                      log ??= NullLogger.Instance;
                                      return log;
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("NullLogger.Instance");
    }

    [Test]
    public async Task FlagsCoalesceDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private readonly ILogger _log;

                                  public Worker(ILogger<Worker>? log = null)
                                  {
                                      _log = log ?? NullLogger<Worker>.Instance;
                                  }

                                  ILogger Get() => _log;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("NullLogger.Instance");
    }

    [Test]
    public async Task FlagsConstructorInitializerArgument(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  public Worker()
                                      : this(NullLogger.Instance)
                                  {
                                  }

                                  public Worker(ILogger log) { }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("NullLogger.Instance");
    }

    [Test]
    public async Task FlagsInstanceFieldInitializer(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private ILogger _log = NullLogger.Instance;

                                  ILogger Get() => _log;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("NullLogger.Instance");
    }

    [Test]
    public async Task FlagsNullLoggerFactoryDefault(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private readonly ILoggerFactory _factory;

                                  public Worker(ILoggerFactory? factory)
                                  {
                                      _factory = factory ?? NullLoggerFactory.Instance;
                                  }

                                  ILoggerFactory Get() => _factory;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("NullLoggerFactory.Instance");
    }

    [Test]
    public async Task IgnoresUnrelatedInstanceMember(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Cache
                              {
                                  public static Cache Instance { get; } = new Cache();

                                  public static Cache Get(Cache? cache) => cache ?? Cache.Instance;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source, CancellationToken cancellationToken) =>
        AnalyzerRunner.RunOnFilesAsync(new NoNullLoggerDefaultAnalyzer(), [source, Stubs], cancellationToken);
}
