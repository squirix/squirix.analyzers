using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags NullLogger.Instance, NullLogger&lt;T&gt;.Instance and NullLoggerFactory.Instance used as the value of a logger
/// dependency the caller did not supply (SQR0039).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoNullLoggerDefaultAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0039";

    private static readonly LocalizableString Description =
        "A logger dependency that falls back to a null logger drops every log entry whenever the caller forgets to pass a logger, without any error. " +
        "Require the caller to pass the logger.";

    private static readonly LocalizableString MessageFormat = "Logger dependency defaults to '{0}.Instance'; require the caller to pass the logger";

    private static readonly LocalizableString Title = "Do not default a logger dependency to a null logger";

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
        if (memberAccess.Name.Identifier.ValueText != "Instance")
            return;

        var owner = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol?.ContainingType;
        if (owner is not { Name: "NullLogger" or "NullLoggerFactory" } || owner.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.Logging.Abstractions")
            return;

        if (DependencyDefaultFacts.IsHiddenDefault(context.SemanticModel, memberAccess, context.CancellationToken))
            context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation(), owner.Name));
    }
}
