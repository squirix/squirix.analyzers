using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Enforces the Squirix dispose-flag convention: a disposure guard must be an <c language="csharp">int</c> flag
/// toggled through <see cref="System.Threading.Interlocked" /> (or observed through
/// <see cref="System.Threading.Volatile" />), never a plain <c language="csharp">bool</c> field.
/// <list type="bullet">
///     <item>
///         <description>SQR0015: flags a <c language="csharp">bool</c> field named exactly "_disposed" (case-sensitive).</description>
///     </item>
///     <item>
///         <description>SQR0016: flags an <c language="csharp">int</c> field named exactly "_disposed" (case-sensitive) when accessed outside Interlocked/Volatile.</description>
///     </item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoBoolDisposedFieldAnalyzer : DiagnosticAnalyzer
{
    private const string BoolRuleId = "SQR0015";
    private const string Category = "Concurrency";
    private const string IntRuleId = "SQR0016";

    private static readonly LocalizableString BoolDescription =
        "A plain bool _disposed field is not thread-safe. Squirix requires an int flag mutated via System.Threading.Interlocked and observed via System.Threading.Volatile.";

    private static readonly LocalizableString BoolMessage = "Field '{0}' is a bool dispose guard; use 'private int {0};' toggled with Interlocked.Exchange";
    private static readonly LocalizableString BoolTitle = "Dispose guard must be an int flag toggled with Interlocked";

    private static readonly LocalizableString IntDescription =
        "An int dispose flag must only be mutated via System.Threading.Interlocked and observed via System.Threading.Volatile to avoid torn reads and missing volatile semantics.";

    private static readonly LocalizableString IntMessage =
        "Dispose flag '{0}' is read or written without Interlocked/Volatile; use Interlocked.Exchange/CompareExchange or Volatile.Read/Write";

    private static readonly LocalizableString IntTitle = "Dispose guard must be accessed through Interlocked or Volatile";
    private static readonly DiagnosticDescriptor BoolRule = new(BoolRuleId, BoolTitle, BoolMessage, Category, DiagnosticSeverity.Warning, true, BoolDescription);

    private static readonly DiagnosticDescriptor IntRule = new(IntRuleId, IntTitle, IntMessage, Category, DiagnosticSeverity.Warning, true, IntDescription);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [BoolRule, IntRule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeFieldSymbol, SymbolKind.Field);
        context.RegisterOperationAction(AnalyzeFieldReference, OperationKind.FieldReference);
    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context)
    {
        var reference = (IFieldReferenceOperation)context.Operation;
        var field = reference.Field;
        if (!IsDisposedFieldName(field.Name) || field.Type.SpecialType != SpecialType.System_Int32)
            return;

        if (AnalyzerHelpers.IsCompilerOrGenerated(field))
            return;

        if (IsGuarded(reference))
            return;

        context.ReportDiagnostic(Diagnostic.Create(IntRule, GetNameLocation(reference.Syntax), field.Name));
    }

    private static void AnalyzeFieldSymbol(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (AnalyzerHelpers.IsCompilerOrGenerated(field))
            return;

        if (field.Type.SpecialType != SpecialType.System_Boolean)
            return;

        if (!IsDisposedFieldName(field.Name))
            return;

        context.ReportDiagnostic(Diagnostic.Create(BoolRule, field.Locations.IsDefaultOrEmpty ? Location.None : field.Locations[0], field.Name));
    }

    private static Location GetNameLocation(SyntaxNode syntax) => syntax switch
    {
        MemberAccessExpressionSyntax access => access.Name.GetLocation(),
        MemberBindingExpressionSyntax binding => binding.Name.GetLocation(),
        _ => syntax.GetLocation(),
    };

    private static bool IsDisposedFieldName(string name) => string.Equals(name, "_disposed", StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the access leaves the flag to Interlocked or Volatile: the flag itself, and not its value, is
    /// what such a method receives by reference. The question is asked of the bound operation, so the spelling of the
    /// operand (a qualifier, parentheses, <c language="csharp">checked</c>, the null-forgiving operator) does not matter.
    /// </summary>
    private static bool IsGuarded(IFieldReferenceOperation reference)
    {
        // A ref conditional yields the reference of the branch it picks, so the flag in a branch shares the fate of the
        // whole conditional. The flag in its condition is read.
        IOperation operand = reference;
        while (operand.Parent is IConditionalOperation { IsRef: true } conditional && conditional.Condition != operand)
            operand = conditional;

        return operand.Parent switch
        {
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter, Parent: IInvocationOperation invocation } =>
                IsInterlockedOrVolatile(invocation.TargetMethod.ContainingType),

            // Code that does not bind says nothing about how the flag is accessed.
            IInvalidOperation => true,
            _ => IsInsideNameOf(reference),
        };
    }

    /// <summary>Returns whether the reference sits in a nameof, which produces the name without touching the field.</summary>
    private static bool IsInsideNameOf(IOperation operation)
    {
        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is INameOfOperation)
                return true;
        }

        return false;
    }

    private static bool IsInterlockedOrVolatile(INamedTypeSymbol? type) =>
        type is { Name: "Interlocked" or "Volatile", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };
}
