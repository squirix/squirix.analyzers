using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a static field or property that holds an ILogger or an ILoggerFactory (SQR0040).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoStaticLoggerAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0040";

    private static readonly LocalizableString Description =
        "A logger in static state is shared by every user of the type and is set outside its constructor, so nothing shows that the type logs or where its " +
        "logs go. Pass the logger through a constructor or a method argument.";

    private static readonly LocalizableString MessageFormat = "Static member '{0}' holds a logger; pass the logger through a constructor or a method argument";

    private static readonly LocalizableString Title = "Do not keep a logger in static state";

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
        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Field, SymbolKind.Property);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        var member = context.Symbol;
        if (!member.IsStatic || member.IsImplicitlyDeclared)
            return;

        var type = member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

        // A logger type that exposes its own singleton, such as NullLogger.Instance, is not a logger somebody else stores.
        if (type == null || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, member.ContainingType.OriginalDefinition) || !IsLogger(type))
            return;

        var location = AnalyzerHelpers.GetBestLocation(member);
        if (location != null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, member.Name));
    }

    private static bool IsLogger(ITypeSymbol type)
    {
        if (IsLoggerInterface(type))
            return true;

        foreach (var implemented in type.AllInterfaces)
        {
            if (IsLoggerInterface(implemented))
                return true;
        }

        return false;
    }

    private static bool IsLoggerInterface(ITypeSymbol type) =>
        type is { TypeKind: TypeKind.Interface, Name: "ILogger" or "ILoggerFactory" } && type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Logging";
}
