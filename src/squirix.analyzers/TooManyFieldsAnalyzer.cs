using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags types with too many fields (SQR0003).
/// The limit is configurable via the <c language="csharp">SQR0003.max_fields_per_type</c>
/// .editorconfig option (default 15).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TooManyFieldsAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0003";

    private const string MaxFieldsPerTypeOptionName = "SQR0003.max_fields_per_type";

    private const int DefaultMaxFieldsPerType = 15;

    private static readonly LocalizableString Description = "Types with too many non-literal, non-static-readonly fields (configurable threshold, default 15) tend to hold too much state.";

    private static readonly LocalizableString MessageFormat = "Type '{0}' has {1} fields (limit {2}); prefer splitting state or introducing collaborators";
    private static readonly LocalizableString Title = "Avoid types with too many fields";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Design", DiagnosticSeverity.Info, true, Description);


    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind == TypeKind.Enum)
            return;

        if (AnalyzerHelpers.IsCompilerOrGenerated(type))
            return;

        var fieldCount = 0;
        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol field)
                continue;

            if (!ShouldCountField(field))
                continue;

            fieldCount++;
        }

        var maxFields = AnalyzerHelpers.GetIntOption(context, type, MaxFieldsPerTypeOptionName, DefaultMaxFieldsPerType);

        if (fieldCount <= maxFields)
            return;

        var location = AnalyzerHelpers.GetBestLocation(type);
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, fieldCount, maxFields));
    }

    private static bool ShouldCountField(IFieldSymbol field)
    {
        if (AnalyzerHelpers.IsCompilerOrGenerated(field))
            return false;

        if (field.IsConst)
            return false;

        return field is not { IsStatic: true, IsReadOnly: true };
    }
}
