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
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Naming", DiagnosticSeverity.Warning, true, Description,
        HelpLinks.For(DiagnosticId));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Generated code is analyzed to reach a hand-written partial method whose implementation a generator adds; the rest of it is skipped by hand.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        // The name and the return type come first: they rule out nearly every method, and the checks after them walk attributes and interfaces.
        var name = method.Name;
        if (!HasTryPrefix(name) || ReturnsBoolLike(method.ReturnType))
            return;

        // A partial method is reported once, on its definition part.
        if (!ReferenceEquals(AnalyzerHelpers.GetPartialDefinition(method), method))
            return;

        if (AnalyzerHelpers.IsGeneratedMember(context))
            return;

        if (method.IsOverride || ImplementsInterfaceMember(method))
            return;

        var location = AnalyzerHelpers.GetBestLocation(method);
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    /// Matches <c>Try</c> as a whole word: the name is exactly <c>Try</c> or the next character is not a lowercase letter.
    /// </summary>
    private static bool HasTryPrefix(string name) => name.StartsWith("Try", StringComparison.Ordinal) && (name.Length == 3 || !char.IsLower(name[3]));

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        if (!method.ExplicitInterfaceImplementations.IsDefaultOrEmpty)
            return true;

        var type = method.ContainingType;
        if (type == null || type.TypeKind == TypeKind.Interface)
            return false;

        foreach (var iface in type.AllInterfaces)
            foreach (var member in iface.GetMembers(method.Name))
            {
                if (member is IMethodSymbol && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method))
                    return true;
            }

        return false;
    }

    private static bool IsSystemThreadingTasks(INamespaceSymbol? ns) => ns is { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace.Name: "System" } } &&
                                                                        ns.ContainingNamespace?.ContainingNamespace?.ContainingNamespace?.IsGlobalNamespace == true;

    /// <summary>
    /// Accepts <c>bool</c> directly or wrapped in a single <c>Task&lt;bool&gt;</c> or
    /// <c>ValueTask&lt;bool&gt;</c> from <c>System.Threading.Tasks</c>.
    /// </summary>
    private static bool ReturnsBoolLike(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_Boolean
            || (type is INamedTypeSymbol { Arity: 1 } named
            && (named.Name == "Task" || named.Name == "ValueTask")
            && IsSystemThreadingTasks(named.ContainingNamespace)
            && named.TypeArguments[0].SpecialType == SpecialType.System_Boolean);
    }
}
