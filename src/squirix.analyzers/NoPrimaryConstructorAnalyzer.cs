using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags primary constructors on classes and structs (SQR0035). Records are not matched.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoPrimaryConstructorAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0035";

    private static readonly LocalizableString Description =
        "Classes and structs declare explicit constructors instead of primary constructors, whose parameters are captured implicitly when a member uses them.";

    private static readonly LocalizableString MessageFormat = "Type '{0}' uses a primary constructor; declare an explicit constructor instead";

    private static readonly LocalizableString Title = "Avoid primary constructors on classes and structs";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Style", DiagnosticSeverity.Warning, true, Description,
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
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (declaration.ParameterList == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.ParameterList.GetLocation(), declaration.Identifier.ValueText));
    }
}
