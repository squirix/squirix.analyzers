using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags methods prefixed with "Try" that do not return <c>bool</c>, <c>Task&lt;bool&gt;</c>,
/// or <c>ValueTask&lt;bool&gt;</c> (SQR0025).
/// Override methods are ignored: their names are dictated by the base declaration, which is flagged instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TryPrefixMustReturnBoolAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0025";

    private static readonly LocalizableString Description = "Methods with 'Try' prefix must return bool, Task<bool>, or ValueTask<bool>.";

    private static readonly LocalizableString MessageFormat = "Method '{0}' has 'Try' prefix but returns '{1}', expected bool, Task<bool>, or ValueTask<bool>";

    private static readonly LocalizableString Title = "Try-prefixed method must return a Boolean result";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Naming", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (AnalyzerHelpers.IsCompilerOrGenerated(method))
            return;

        if (method.IsOverride)
            return;

        var name = method.Name;
        if (!name.StartsWith("Try", StringComparison.Ordinal))
            return;

        if (ReturnsBoolLike(method.ReturnType))
            return;

        var location = AnalyzerHelpers.GetBestLocation(method);
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, method.ReturnType.Name));
    }

    /// <summary>
    /// Accepts <c>bool</c> directly or wrapped in a single <c>Task&lt;bool&gt;</c> or
    /// <c>ValueTask&lt;bool&gt;</c> from <c>System.Threading.Tasks</c>.
    /// </summary>
    private static bool ReturnsBoolLike(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Boolean)
            return true;

        if (type is not INamedTypeSymbol named || named.Arity != 1)
            return false;

        if (named.Name != "Task" && named.Name != "ValueTask")
            return false;

        if (!IsSystemThreadingTasks(named.ContainingNamespace))
            return false;

        return named.TypeArguments is [var result] && result.SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsSystemThreadingTasks(INamespaceSymbol? ns) =>
        ns?.Name == "Tasks" &&
        ns?.ContainingNamespace?.Name == "Threading" &&
        ns?.ContainingNamespace?.ContainingNamespace?.Name == "System" &&
        ns?.ContainingNamespace?.ContainingNamespace?.ContainingNamespace?.IsGlobalNamespace == true;
}
