using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

/// <summary>
/// Checks that the rule finds a capturing delegate wherever the assert libraries take it: in the assert call itself,
/// in an earlier call of the chain, and in a call imported through <c language="csharp">using static</c>. The stubs
/// copy the namespaces and the shape of the real TUnit, FluentAssertions and xUnit APIs.
/// </summary>
public sealed class ThrowsAssertChainTests
{
    private const string RuleId = "SQR0019";

    private const string Prefix = """
                                  using System;
                                  using System.Linq.Expressions;
                                  using System.Threading.Tasks;
                                  using FluentAssertions;
                                  using Mocks;
                                  using static Xunit.Assert;

                                  class Cache
                                  {
                                      public int Get(int key) => key;

                                      public Task<int> GetAsync(int key) => Task.FromResult(key);
                                  }

                                  class C
                                  {
                                      private readonly Cache _cache = new Cache();

                                      void M(int key)
                                      {

                                  """;

    private const string Suffix = """

                                      }
                                  }

                                  namespace TUnit.Assertions
                                  {
                                      static class Assert
                                      {
                                          public static Builder That(Action action) => new Builder();

                                          public static Builder That<T>(Func<T> function) => new Builder();

                                          public static Builder That(Func<Task> function) => new Builder();

                                          public static void ThrowsExactly<T>(Action action) where T : Exception { }
                                      }

                                      class Builder
                                      {
                                          public Builder And => this;

                                          public Builder Throws<T>() where T : Exception => this;

                                          public Builder ThrowsExactly<T>() where T : Exception => this;

                                          public Builder ThrowsException() => this;

                                          public Builder IsNotNull() => this;
                                      }
                                  }

                                  namespace FluentAssertions
                                  {
                                      static class FluentActions
                                      {
                                          public static Action Invoking(Action action) => action;

                                          public static Func<Task> Awaiting(Func<Task> action) => action;
                                      }

                                      static class AssertionExtensions
                                      {
                                          public static Action Invoking<T>(this T subject, Action<T> action) => () => action(subject);

                                          public static ActionAssertions Should(this Action action) => new ActionAssertions();

                                          public static AsyncAssertions Should(this Func<Task> action) => new AsyncAssertions();
                                      }

                                      class ActionAssertions
                                      {
                                          public void Throw<T>() where T : Exception { }

                                          public void ThrowExactly<T>() where T : Exception { }
                                      }

                                      class AsyncAssertions
                                      {
                                          public Task ThrowAsync<T>() where T : Exception => Task.CompletedTask;

                                          public Task ThrowExactlyAsync<T>() where T : Exception => Task.CompletedTask;
                                      }
                                  }

                                  namespace Xunit
                                  {
                                      static class Assert
                                      {
                                          public static void Throws<T>(Action action) where T : Exception { }

                                          public static void ThrowsAny<T>(Action action) where T : Exception { }

                                          public static Task ThrowsAsync<T>(Func<Task> action) where T : Exception => Task.CompletedTask;
                                      }
                                  }

                                  namespace Mocks
                                  {
                                      class Mock<T>
                                      {
                                          public Setup Setup(Expression<Func<T, int>> call) => new Setup();
                                      }

                                      class Setup
                                      {
                                          public void Throws<TException>() where TException : Exception { }
                                      }
                                  }
                                  """;

    /// <summary>Returns statements that hand a capturing delegate to an exception assert; each gives one diagnostic.</summary>
    public static IEnumerable<string> ReportedStatements() =>
    [
        "TUnit.Assertions.Assert.That(() => _cache.Get(key)).Throws<InvalidOperationException>();",
        "TUnit.Assertions.Assert.That(() => _cache.Get(key)).ThrowsExactly<InvalidOperationException>();",
        "TUnit.Assertions.Assert.That(() => _cache.Get(key)).ThrowsException();",
        "TUnit.Assertions.Assert.That(async () => await _cache.GetAsync(key)).Throws<InvalidOperationException>();",
        "TUnit.Assertions.Assert.That(() => _cache.Get(key)).And.Throws<InvalidOperationException>();",
        "TUnit.Assertions.Assert.That(() => _cache.Get(key)).IsNotNull().And.Throws<InvalidOperationException>();",
        "(TUnit.Assertions.Assert.That(() => _cache.Get(key)))!.Throws<InvalidOperationException>();",
        "TUnit.Assertions.Assert.ThrowsExactly<InvalidOperationException>(() => _cache.Get(key));",
        "FluentActions.Invoking(() => _cache.Get(key)).Should().Throw<InvalidOperationException>();",
        "_cache.Invoking(c => c.Get(key)).Should().ThrowExactly<InvalidOperationException>();",
        "_ = FluentActions.Awaiting(() => _cache.GetAsync(key)).Should().ThrowAsync<InvalidOperationException>();",
        "_ = FluentActions.Awaiting(() => _cache.GetAsync(key)).Should().ThrowExactlyAsync<InvalidOperationException>();",
        "Throws<InvalidOperationException>(() => _cache.Get(key));",
        "ThrowsAny<Exception>(() => _cache.Get(key));",
        "_ = ThrowsAsync<InvalidOperationException>(() => _cache.GetAsync(key));",
    ];

    /// <summary>Returns statements that allocate nothing per call, or that are not an exception assert over a delegate.</summary>
    public static IEnumerable<string> AllowedStatements() =>
    [
        "TUnit.Assertions.Assert.That(static () => new Cache().Get(1)).Throws<InvalidOperationException>();",
        "TUnit.Assertions.Assert.That(() => new Cache().Get(1)).Throws<InvalidOperationException>();",
        "TUnit.Assertions.Assert.That(() => _cache.Get(key)).IsNotNull();",
        "FluentActions.Invoking(static () => new Cache().Get(1)).Should().Throw<InvalidOperationException>();",
        "_cache.Invoking(static c => c.Get(1)).Should().Throw<InvalidOperationException>();",
        "Throws<InvalidOperationException>(static () => new Cache().Get(1));",

        // The delegate is created where the variable is assigned; the assert only receives it.
        "Action act = () => _cache.Get(key); act.Should().Throw<InvalidOperationException>();",

        // A mock setup takes an expression tree, which is data for the library, and Throws there is not an assert.
        "new Mock<Cache>().Setup(c => c.Get(key)).Throws<InvalidOperationException>();",
    ];

    [Test]
    [MethodDataSource(nameof(ReportedStatements))]
    public async Task FlagsCapturingDelegateInAssert(string statement, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), Prefix + statement + Suffix, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }

    [Test]
    [MethodDataSource(nameof(AllowedStatements))]
    public async Task AllowsAssertWithoutCapture(string statement, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), Prefix + statement + Suffix, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>A method of the enclosing type or of its base is the code's own helper, whatever its name.</summary>
    [Test]
    public async Task AllowsOwnHelperCalledByBareName(CancellationToken cancellationToken)
    {
        const string source = """
                              using System;

                              class Base
                              {
                                  protected static void ThrowsAny<T>(Action action) where T : Exception { }
                              }

                              class C : Base
                              {
                                  void M(int key)
                                  {
                                      Throws<InvalidOperationException>(() => key.ToString());
                                      ThrowsAny<Exception>(() => key.ToString());
                                      Local(() => key.ToString());

                                      static void Local(Action action) { }
                                  }

                                  static void Throws<T>(Action action) where T : Exception { }
                              }
                              """;

        var diagnostics = await AnalyzerRunner.RunAsync(new NoAllocatingThrowsAssertAnalyzer(), source, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }
}
