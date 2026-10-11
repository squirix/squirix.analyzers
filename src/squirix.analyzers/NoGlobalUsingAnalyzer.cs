using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags global using directives, including the ones the build generates from ImplicitUsings (SQR0034).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoGlobalUsingAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0034";

    private static readonly LocalizableString Description =
        "Global using directives hide namespace dependencies. Declare explicit using directives in each file and disable the properties that generate global usings, such as ImplicitUsings.";

    private static readonly LocalizableString MessageFormat =
        "Global using directive is not allowed; add explicit using directives to each file, and for a generated one disable the property behind it (for example ImplicitUsings)";

    private static readonly LocalizableString Title = "Avoid global using directives";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Style", DiagnosticSeverity.Warning, true, Description,
        HelpLinks.For(DiagnosticId));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // The global usings generated from ImplicitUsings sit in an auto-generated file, so generated code is analyzed and reported.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var directive = (UsingDirectiveSyntax)context.Node;
        if (!directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, directive.GlobalKeyword.GetLocation()));
    }
}
