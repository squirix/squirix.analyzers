using System.Threading;
using Xunit;

namespace Squirix.Analyzers.UnitTests.Support;

/// <summary>Base class exposing the shared xUnit cancellation token.</summary>
public abstract class AnalyzerTestBase
{
    /// <summary>Gets the shared test cancellation token.</summary>
    protected static CancellationToken DefaultCancellationToken => TestContext.Current.CancellationToken;
}
