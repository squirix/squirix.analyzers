using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
        context.RegisterSyntaxNodeAction(AnalyzeFieldAccess, SyntaxKind.IdentifierName);
    }

    private static void AnalyzeFieldAccess(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not IdentifierNameSyntax identifier)
            return;

        if (!IsDisposedFieldName(identifier.Identifier.Text))
            return;

        if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not IFieldSymbol symbol)
            return;

        if (AnalyzerHelpers.IsCompilerOrGenerated(symbol))
            return;

        if (!IsDisposedFieldName(symbol.Name))
            return;

        if (symbol.Type.SpecialType != SpecialType.System_Int32)
            return;

        if (IsInsideNameofOperator(context.Node))
            return;

        if (IsGuardedByInterlockedOrVolatile(context.Node, context.SemanticModel, context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(IntRule, context.Node.GetLocation(), symbol.Name));
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

    private static bool IsDisposedFieldName(string name) => string.Equals(name, "_disposed", StringComparison.Ordinal);

    private static bool IsGuardedByInterlockedOrVolatile(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        // The flag is guarded only when it is itself the ref/in operand of an Interlocked/Volatile
        // call (optionally qualified as this._disposed). Any other occurrence inside the call's
        // arguments, such as Interlocked.Exchange(ref _disposed, _disposed + 1), is a plain read.
        var operand = node;
        if (node.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess && memberAccess.Name == node)
            operand = memberAccess;

        if (operand.Parent is not ArgumentSyntax argument || argument.Expression != operand)
            return false;

        if (!argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) && !argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword))
            return false;

        return argument.Parent?.Parent is InvocationExpressionSyntax invocation && IsInterlockedOrVolatileInvocation(invocation, semanticModel, cancellationToken);
    }

    private static bool IsInsideNameofOperator(SyntaxNode node)
    {
        // nameof(_disposed) does not read the field at runtime; it only produces its name.
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } ancestor && IsWithin(ancestor.ArgumentList, node))
                return true;
        }

        return false;
    }

    private static bool IsInterlockedOrVolatileInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol { ContainingType: not null } method)
        {
            var containing = method.ContainingType;
            return containing.Name is "Interlocked" or "Volatile" && containing.ContainingNamespace is { Name: "Threading", ContainingNamespace.Name: "System" };
        }

        // Fallback for unresolved symbols: exact match on the rightmost receiver name.
        // Handles fully qualified 'System.Threading.Interlocked.Exchange' (receiver is a
        // MemberAccess ending in 'Interlocked') and rejects 'InterlockedFoo.Exchange'.
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
            return false;

        var receiverName = member.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null,
        };

        return string.Equals(receiverName, "Interlocked", StringComparison.Ordinal) || string.Equals(receiverName, "Volatile", StringComparison.Ordinal);
    }

    private static bool IsWithin(SyntaxNode ancestor, SyntaxNode descendant)
    {
        for (var current = descendant; current != null; current = current.Parent)
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }
}
