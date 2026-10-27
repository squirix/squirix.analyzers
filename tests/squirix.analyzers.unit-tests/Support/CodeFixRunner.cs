using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.UnitTests.Support;

/// <summary>Applies the fixes of a code fix provider to C# source, one diagnostic at a time, as a user accepting each light bulb would.</summary>
internal static class CodeFixRunner
{
    private const int MaxFixes = 100;

    /// <summary>Applies the first fix offered for each fixable diagnostic until none is offered, and returns the resulting source.</summary>
    public static async Task<string> ApplyAsync(DiagnosticAnalyzer analyzer, CodeFixProvider codeFix, string source, CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        for (var applied = 0; applied < MaxFixes; applied++)
        {
            var (action, _) = await FindFirstFixAsync(analyzer, codeFix, document, cancellationToken);
            if (action == null)
                return (await document.GetTextAsync(cancellationToken)).ToString();

            document = await ApplyAsync(action, document, cancellationToken);
        }

        throw new InvalidOperationException($"The code fix did not converge after {MaxFixes} fixes.");
    }

    /// <summary>Runs the fix-all of the provider over the document with the equivalence key of the first fix, as "Fix all in document" does.</summary>
    public static async Task<string> FixAllAsync(DiagnosticAnalyzer analyzer, CodeFixProvider codeFix, string source, CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var (action, diagnostics) = await FindFirstFixAsync(analyzer, codeFix, document, cancellationToken);
        if (action == null)
            return source;

        var provider = codeFix.GetFixAllProvider() ?? ThrowMissing<FixAllProvider>("The code fix has no fix-all provider.");
        var context = new FixAllContext(document, codeFix, FixAllScope.Document, action.EquivalenceKey, codeFix.FixableDiagnosticIds,
            new FixedDiagnosticProvider(diagnostics), cancellationToken);
        var fixAll = await provider.GetFixAsync(context) ?? ThrowMissing<CodeAction>("The fix-all produced no action.");
        document = await ApplyAsync(fixAll, document, cancellationToken);
        return (await document.GetTextAsync(cancellationToken)).ToString();
    }

    private static async Task<Document> ApplyAsync(CodeAction action, Document document, CancellationToken cancellationToken)
    {
        foreach (var operation in await action.GetOperationsAsync(cancellationToken))
        {
            if (operation is ApplyChangesOperation applyChanges)
                return applyChanges.ChangedSolution.GetDocument(document.Id) ?? ThrowMissing<Document>("The code fix removed the document.");
        }

        throw new InvalidOperationException("The code fix produced no change.");
    }

    private static Document CreateDocument(Workspace workspace, string source)
    {
        var project = workspace.CurrentSolution
            .AddProject("Squirix.Analyzers.UnitTests", "Squirix.Analyzers.UnitTests", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithMetadataReferences(AnalyzerRunner.RuntimeReferences);
        return project.AddDocument("File0.cs", source);
    }

    private static async Task<(CodeAction? Action, ImmutableArray<Diagnostic> Diagnostics)> FindFirstFixAsync(DiagnosticAnalyzer analyzer, CodeFixProvider codeFix, Document document, CancellationToken cancellationToken)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellationToken) ?? ThrowMissing<Compilation>("The project has no compilation.");
        ThrowIfSourceDoesNotCompile(compilation, cancellationToken);

        var diagnostics = await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(cancellationToken);
        var fixableIds = new HashSet<string>(codeFix.FixableDiagnosticIds);
        foreach (var diagnostic in diagnostics)
        {
            if (!fixableIds.Contains(diagnostic.Id))
                continue;

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);
            await codeFix.RegisterCodeFixesAsync(context);
            if (actions.Count > 0)
                return (actions[0], diagnostics);
        }

        return (null, diagnostics);
    }

    private static T ThrowMissing<T>(string message) => throw new InvalidOperationException(message);

    private static void ThrowIfSourceDoesNotCompile(Compilation compilation, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                errors.Add(diagnostic.ToString());
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Source has compiler errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    /// <summary>Hands the fix-all the diagnostics already computed for the single document.</summary>
    private sealed class FixedDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        public FixedDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);
    }
}
