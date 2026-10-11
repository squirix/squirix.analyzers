using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags consecutive <c language="csharp">catch</c> blocks that catch different exception types but
/// contain identical bodies. Such blocks read more clearly as a single
/// <c language="csharp">catch (Exception ex) when (ex is TOne or TTwo)</c> clause using pattern
/// matching, which keeps the duplicated handler body in one place (SQR0020).
/// Only clauses without a <c language="csharp">when</c> filter whose exception variable is not
/// referenced in the body are considered, so the merge never changes behavior. A conditional
/// compilation directive in a clause or between two clauses ends the run: such clauses are not the
/// same in every configuration. A clause whose type derives from the type of another clause of the
/// run is left out of the pattern, since that other clause already catches its exceptions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MergeDuplicateCatchBlocksAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0020";

    private static readonly LocalizableString Description = "Consecutive catch blocks catch different exception types with identical bodies. " +
                                                            "Combine them into a single catch clause with a 'when' filter pattern, for example " +
                                                            "'catch (Exception ex) when (ex is IOException or ObjectDisposedException)', to keep the " +
                                                            "duplicated handler body in one place. A clause whose type derives from the type of another one is redundant.";

    private static readonly LocalizableString MessageFormat = "Consecutive catch blocks for the same body should be {0}";

    private static readonly LocalizableString Title = "Merge duplicate catch blocks with identical bodies";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Warning, true, Description,
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
        context.RegisterSyntaxNodeAction(AnalyzeTryStatement, SyntaxKind.TryStatement);
    }

    private static void AnalyzeTryStatement(SyntaxNodeAnalysisContext context)
    {
        var tryStatement = (TryStatementSyntax)context.Node;
        var catches = tryStatement.Catches;
        if (catches.Count < 2)
            return;

        for (var start = 0; start < catches.Count; start++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // The directives before the first clause of a run lie outside it, so only those after its 'catch' count.
            if (!CanMerge(catches[start], catches[start].SpanStart))
                continue;

            var firstType = GetExceptionTypeName(catches[start]);
            if (firstType == null)
                continue;

            var runTypes = new List<string> { firstType };

            var end = start + 1;
            while (end < catches.Count && CanMerge(catches[end], catches[end].FullSpan.Start) && BodiesAreEquivalent(catches[start], catches[end]))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var type = GetExceptionTypeName(catches[end]);
                if (type == null || runTypes.Contains(type))
                    break;

                runTypes.Add(type);
                end++;
            }

            if (runTypes.Count >= 2)
                context.ReportDiagnostic(Diagnostic.Create(Rule, catches[start].CatchKeyword.GetLocation(), GetAdvice(context, catches, start, runTypes)));

            start = end - 1;
        }
    }

    private static bool BodiesAreEquivalent(CatchClauseSyntax left, CatchClauseSyntax right) => left.Block.IsEquivalentTo(right.Block, false);

    /// <summary>
    /// A clause can only be offered for merging when it has a declared exception type, no
    /// <c language="csharp">when</c> filter, no conditional directive at or after <paramref name="directivesFrom" />,
    /// and does not reference its exception variable in the body.
    /// </summary>
    private static bool CanMerge(CatchClauseSyntax clause, int directivesFrom)
    {
        return clause.Declaration != null
            && clause.Filter == null
            && !AnalyzerHelpers.HasConditionalDirective(clause, directivesFrom)
            && !ExceptionVariableIsReferenced(clause);
    }

    private static bool DerivesFromAnother(ITypeSymbol?[] types, int index)
    {
        for (var ancestor = types[index]?.BaseType; ancestor != null; ancestor = ancestor.BaseType)
            for (var other = 0; other < types.Length; other++)
            {
                if (other != index && SymbolEqualityComparer.Default.Equals(ancestor, types[other]))
                    return true;
            }

        return false;
    }

    private static bool ExceptionVariableIsReferenced(CatchClauseSyntax clause)
    {
        var identifier = clause.Declaration?.Identifier;
        if (identifier == null || identifier.Value.IsMissing || string.IsNullOrEmpty(identifier.Value.ValueText))
            return false;

        var name = identifier.Value.ValueText;
        foreach (var node in clause.Block.DescendantNodes())
        {
            if (node is IdentifierNameSyntax { Identifier.ValueText: var text } && text == name)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the advice for a run of clauses with the same body. A clause whose type derives from the type of another
    /// clause of the run adds nothing: that other clause catches its exceptions and handles them the same way.
    /// </summary>
    private static string GetAdvice(SyntaxNodeAnalysisContext context, SyntaxList<CatchClauseSyntax> catches, int start, List<string> runTypes)
    {
        var types = new ITypeSymbol?[runTypes.Count];
        for (var i = 0; i < types.Length; i++)
            types[i] = context.SemanticModel.GetTypeInfo(catches[start + i].Declaration!.Type, context.CancellationToken).Type;

        var kept = new List<string>();
        for (var i = 0; i < types.Length; i++)
        {
            if (!DerivesFromAnother(types, i))
                kept.Add(runTypes[i]);
        }

        return kept.Count == 1
            ? $"reduced to the one for '{kept[0]}', which catches the others as well"
            : $"combined into one 'when' filter pattern; e.g. 'catch (Exception ex) when (ex is {string.Join(" or ", kept)})'";
    }

    private static string? GetExceptionTypeName(CatchClauseSyntax clause)
    {
        var declaration = clause.Declaration;
        return declaration?.Type.ToString();
    }
}
