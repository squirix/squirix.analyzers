using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags an <c language="csharp">if</c> statement without <c language="csharp">else</c> whose body is a single
/// <c language="csharp">return</c> or <c language="csharp">throw</c> immediately followed by another
/// <c language="csharp">return</c> or <c language="csharp">throw</c>.
/// Such pairs read more clearly as one conditional return using a throw expression where needed, for example
/// <c language="csharp">return condition ? first : second;</c> or
/// <c language="csharp">return condition ? throw new ArgumentException(...) : second;</c> (SQR0026).
/// Only value-returning <c language="csharp">return</c> statements and <c language="csharp">throw</c> statements
/// with an explicit expression are considered, at least one side must be a <c language="csharp">return</c>,
/// and <c language="csharp">ref</c> returns are never flagged, so the rewrite never changes behavior.
/// This rule backports the newer <c>IDE0046</c> detections for SDK 10-era compilers, which stay silent on
/// several of these shapes (for example an <c language="csharp">if</c> return followed by a ternary return or
/// a trailing <c language="csharp">throw</c>).
/// On SDK 11 and later the two rules overlap on the same spans; consumers should keep only one of them enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SimplifyIfReturnAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0026";

    private static readonly LocalizableString Description = "An 'if' statement without 'else' whose body is a single 'return' or 'throw' " +
                                                            "immediately followed by another 'return' or 'throw' should be simplified to one " +
                                                            "conditional return, for example 'return condition ? first : second;'.";

    private static readonly LocalizableString MessageFormat = "Simplify this 'if' and the following statement into one conditional return";

    private static readonly LocalizableString Title = "Simplify if-return to conditional return";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Style", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);
    }

    private static void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        if (ifStatement.Else != null)
            return;

        if (!IsReturnOrThrow(ifStatement.Statement, out var ifIsReturn))
            return;

        if (ifStatement.Parent is not BlockSyntax parent)
            return;

        var statements = parent.Statements;
        var index = statements.IndexOf(ifStatement);
        if (index < 0 || index + 1 >= statements.Count)
            return;

        if (!IsReturnOrThrow(statements[index + 1], out var nextIsReturn))
            return;

        if (!ifIsReturn && !nextIsReturn)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.IfKeyword.GetLocation()));
    }

    /// <summary>
    /// Checks for a single value-returning <c language="csharp">return</c> or an explicit-expression
    /// <c language="csharp">throw</c>, either directly or as the only statement of a block.
    /// </summary>
    /// <param name="statement">Statement to inspect.</param>
    /// <param name="isReturn">True for <c language="csharp">return</c>, false for <c language="csharp">throw</c>.</param>
    private static bool IsReturnOrThrow(StatementSyntax statement, out bool isReturn)
    {
        var inner = statement;
        if (inner is BlockSyntax block)
        {
            if (block.Statements.Count != 1)
            {
                isReturn = false;
                return false;
            }

            inner = block.Statements[0];
        }

        if (inner is ReturnStatementSyntax valueReturn &&
            valueReturn.Expression is { } returned &&
            returned is not RefExpressionSyntax)
        {
            isReturn = true;
            return true;
        }

        if (inner is ThrowStatementSyntax valueThrow && valueThrow.Expression != null)
        {
            isReturn = false;
            return true;
        }

        isReturn = false;
        return false;
    }
}
