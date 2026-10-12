using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Squirix.Analyzers.UnitTests.Support;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class CancellationCatchFilterAnalyzerTests
{
    private const string RuleId = "SQR0038";

    [Test]
    public async Task AllowsCatchThatRethrows(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, cancellationToken);
                                      }
                                      catch (OperationCanceledException)
                                      {
                                          Console.WriteLine("canceled");
                                          throw;
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsFilterByExceptionToken(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, cancellationToken);
                                      }
                                      catch (OperationCanceledException error) when (error.CancellationToken == cancellationToken)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsFilterByOwnTokenSource(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                      try
                                      {
                                          await Task.Delay(1, timeout.Token);
                                      }
                                      catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsFilterByToken(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, cancellationToken);
                                      }
                                      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsMethodWithoutTokenParameter(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync()
                                  {
                                      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                                      try
                                      {
                                          await Task.Delay(1, timeout.Token);
                                      }
                                      catch (OperationCanceledException)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsNegatedFilterByToken(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, cancellationToken);
                                      }
                                      catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowsOtherExceptionTypes(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, cancellationToken);
                                      }
                                      catch (InvalidOperationException)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task FlagsCatchInLambdaWithToken(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public Func<CancellationToken, Task> Create() => async (CancellationToken token) =>
                                  {
                                      try
                                      {
                                          await Task.Delay(1, token);
                                      }
                                      catch (OperationCanceledException)
                                      {
                                      }
                                  };
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("token.IsCancellationRequested");
    }

    [Test]
    public async Task FlagsFilterUnrelatedToCancellation(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  private readonly bool _quiet = true;

                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, cancellationToken);
                                      }
                                      catch (OperationCanceledException) when (_quiet)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("cancellationToken");
    }

    [Test]
    public async Task FlagsTaskCanceledException(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task RunAsync(CancellationToken cancellationToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, cancellationToken);
                                      }
                                      catch (TaskCanceledException)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("TaskCanceledException");
    }

    [Test]
    public async Task FlagsUnfilteredCatch(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;
                              using System.Threading;
                              using System.Threading.Tasks;

                              class Worker
                              {
                                  public async Task ExecuteAsync(CancellationToken stoppingToken)
                                  {
                                      try
                                      {
                                          await Task.Delay(1, stoppingToken);
                                      }
                                      catch (OperationCanceledException)
                                      {
                                      }
                                  }
                              }
                              """;

        var diagnostics = await RunAsync(source, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
        _ = await Assert.That(diagnostic.GetMessage()).Contains("stoppingToken.IsCancellationRequested");
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source, CancellationToken cancellationToken) =>
        AnalyzerRunner.RunAsync(new CancellationCatchFilterAnalyzer(), source, cancellationToken);
}
