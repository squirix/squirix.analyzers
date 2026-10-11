using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class AnalyzerRunnerTests
{
    [Test]
    public async Task FailsWhenAnalyzerThrows(CancellationToken cancellationToken)
    {
        InvalidOperationException? failure = null;
        try
        {
            _ = await AnalyzerRunner.RunAsync(new ThrowingAnalyzer(), "class C { }", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        _ = await Assert.That(failure).IsNotNull();
        _ = await Assert.That(failure!.Message).Contains("AD0001");
        _ = await Assert.That(failure.Message).Contains(ThrowingAnalyzer.Reason);
    }

    [Test]
    public async Task FailsWhenAnalyzerThrowsOnIncompleteCode(CancellationToken cancellationToken)
    {
        InvalidOperationException? failure = null;
        try
        {
            _ = await AnalyzerRunner.RunOnIncompleteCodeAsync(new ThrowingAnalyzer(), "class C { void M( }", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        _ = await Assert.That(failure).IsNotNull();
        _ = await Assert.That(failure!.Message).Contains(ThrowingAnalyzer.Reason);
    }

    [Test]
    public async Task FailsWhenIncompleteCodeCompiles(CancellationToken cancellationToken)
    {
        InvalidOperationException? failure = null;
        try
        {
            _ = await AnalyzerRunner.RunOnIncompleteCodeAsync(new FinalizerDisposeFieldAnalyzer(), "class C { }", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        _ = await Assert.That(failure).IsNotNull();
        _ = await Assert.That(failure!.Message).Contains("compiles without errors");
    }

    /// <summary>
    /// Throws on every type. Roslyn skips an analyzer without supported diagnostics, so it borrows a shipped descriptor instead of declaring a rule
    /// in the test project.
    /// </summary>
    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1001:Missing diagnostic analyzer attribute",
        Justification = "Test double passed to the runner directly; the attribute would register it as a compiler extension of the test assembly (RS1036, RS1041).")]
    private sealed class ThrowingAnalyzer : DiagnosticAnalyzer
    {
        internal const string Reason = "Analyzer failure for the runner test.";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = new FinalizerDisposeFieldAnalyzer().SupportedDiagnostics;

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(static _ => throw new InvalidOperationException(Reason), SymbolKind.NamedType);
        }
    }
}
