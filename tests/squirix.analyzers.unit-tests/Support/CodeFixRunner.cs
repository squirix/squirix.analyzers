using System;
using System.Collections.Generic;
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
        var project = workspace.CurrentSolution
            .AddProject("Squirix.Analyzers.UnitTests", "Squirix.Analyzers.UnitTests", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithMetadataReferences(AnalyzerRunner.RuntimeReferences);
        var document = project.AddDocument("File0.cs", source);

        for (var applied = 0; applied < MaxFixes; applied++)
        {
            var action = await FindFirstFixAsync(analyzer, codeFix, document, cancellationToken);
            if (action == null)
                return (await document.GetTextAsync(cancellationToken)).ToString();

            document = await ApplyAsync(action, document, cancellationToken);
        }

        throw new InvalidOperationException($"The code fix did not converge after {MaxFixes} fixes.");
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

    private static async Task<CodeAction?> FindFirstFixAsync(DiagnosticAnalyzer analyzer, CodeFixProvider codeFix, Document document, CancellationToken cancellationToken)
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
                return actions[0];
        }

        return null;
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
}
