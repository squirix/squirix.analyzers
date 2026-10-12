using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags TimeProvider.System used as the value of a TimeProvider dependency the caller did not supply (SQR0036).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoSystemClockDefaultAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0036";

    private static readonly LocalizableString Description =
        "A TimeProvider dependency that falls back to TimeProvider.System reads the system clock whenever the caller forgets to pass a provider, so a test " +
        "cannot rely on its own clock being used. Require the caller to pass the provider.";

    private static readonly LocalizableString MessageFormat = "TimeProvider dependency defaults to TimeProvider.System; require the caller to pass the provider";

    private static readonly LocalizableString Title = "Do not default a TimeProvider dependency to the system clock";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Design", DiagnosticSeverity.Warning, true, Description,
        HelpLinks.For(DiagnosticId));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (!DependencyDefaultFacts.IsTimeProviderSystem(context.SemanticModel, memberAccess, context.CancellationToken))
            return;

        if (DependencyDefaultFacts.IsHiddenDefault(context.SemanticModel, memberAccess, context.CancellationToken))
            context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation()));
    }
}
