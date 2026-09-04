using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags types with too many methods (SQR0002).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TooManyMethodsAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0002";

    private const string MaxMethodsPerTypeOptionName = "SQR0002.max_methods_per_type";

    private const int DefaultMaxMethodsPerType = 20;

    private static readonly LocalizableString Description = "Types with too many instance/static methods " +
                                                            "(excluding constructors, property/event accessors, configurable threshold, default 20) tend to have too many responsibilities. " +
                                                            "Stateless types with only constants are not matched.";

    private static readonly LocalizableString MessageFormat = "Type '{0}' has {1} methods (limit {2}); prefer splitting responsibilities";
    private static readonly LocalizableString Title = "Avoid types with too many methods";
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
        if (type.TypeKind == TypeKind.Interface || AnalyzerHelpers.IsCompilerOrGenerated(type))
            return;

        if (!TryCountMethods(type, out var methodCount))
            return;

        var maxMethods = AnalyzerHelpers.GetIntOption(context, type, MaxMethodsPerTypeOptionName, DefaultMaxMethodsPerType);

        if (methodCount <= maxMethods)
            return;

        var location = AnalyzerHelpers.GetBestLocation(type);
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, methodCount, maxMethods));
    }

    private static bool ShouldCountMethod(IMethodSymbol method)
    {
        if (AnalyzerHelpers.IsCompilerOrGenerated(method))
            return false;

        return method.MethodKind switch
        {
            MethodKind.Constructor or MethodKind.StaticConstructor => false,
            MethodKind.PropertyGet or MethodKind.PropertySet => false,
            MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise => false,
            MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion => true,
            _ => false,
        };
    }

    private static bool TryCountMethods(INamedTypeSymbol type, out int methodCount)
    {
        methodCount = 0;
        var hasField = false;
        var allFieldsAreConstants = true;

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field:
                {
                    hasField = true;
                    if (!field.IsConst && !AnalyzerHelpers.IsCompilerOrGenerated(field))
                        allFieldsAreConstants = false;

                    continue;
                }
                case IMethodSymbol method when ShouldCountMethod(method):
                    methodCount++;
                    break;
            }
        }

        // Only stateless types whose every field is a constant are exempt (per the rule description).
        return !(hasField && allFieldsAreConstants);
    }
}
