using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class NoStaticLoggerAnalyzerTests
{
    private const string RuleId = "SQR0040";

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
    public async Task AllowsInstanceLogger(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private readonly ILogger _log;

                                  public Worker(ILogger log) => _log = log;

                                  public ILogger Log => _log;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsLoggerOwnSingleton(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              sealed class QuietLogger : ILogger
                              {
                                  public static QuietLogger Instance { get; } = new QuietLogger();
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsStaticMemberOfOtherType(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private static readonly object Gate = new object();

                                  public static int Count { get; set; }

                                  static object Get() => Gate;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsStaticGenericLoggerField(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private static readonly ILogger<Worker> Log = NullLogger<Worker>.Instance;

                                  static ILogger Get() => Log;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("Log");
    }

    [Test]
    public async Task FlagsStaticLoggerFactoryProperty(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              static class Logging
                              {
                                  public static ILoggerFactory? Factory { get; set; }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("Factory");
    }

    [Test]
    public async Task FlagsStaticLoggerField(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private static ILogger? Log;

                                  static ILogger? Get() => Log;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("Log");
    }

    [Test]
    public async Task FlagsStaticLoggerImplementation(CancellationToken cancellationToken)
    {
        const string source = """
                              #nullable enable
                              using Microsoft.Extensions.Logging;
                              using Microsoft.Extensions.Logging.Abstractions;

                              class Worker
                              {
                                  private static readonly NullLogger Shared = NullLogger.Instance;

                                  static ILogger Get() => Shared;
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("Shared");
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source, CancellationToken cancellationToken) =>
        AnalyzerRunner.RunOnFilesAsync(new NoStaticLoggerAnalyzer(), [source, Stubs], cancellationToken);
}
