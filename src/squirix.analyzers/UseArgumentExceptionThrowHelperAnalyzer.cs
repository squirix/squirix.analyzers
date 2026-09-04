using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags <c language="csharp">if</c> guards that check <c language="csharp">string.IsNullOrWhiteSpace</c> or
/// <c language="csharp">string.IsNullOrEmpty</c> and throw <c language="csharp">ArgumentException</c>, which read
/// more clearly as <c language="csharp">ArgumentException.ThrowIfNullOrWhiteSpace</c> or
/// <c language="csharp">ArgumentException.ThrowIfNullOrEmpty</c> (SQR0021). Only guards that throw a plain
/// <c language="csharp">ArgumentException</c> are considered; other exception types carry different semantics and
/// <c language="csharp">switch</c> arms cannot use a throw helper.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseArgumentExceptionThrowHelperAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0021";

    private static readonly LocalizableString Description =
        "Guards that check string.IsNullOrWhiteSpace or string.IsNullOrEmpty and throw ArgumentException read more " +
        "clearly as ArgumentException.ThrowIfNullOrWhiteSpace or ArgumentException.ThrowIfNullOrEmpty. The helper " +
        "keeps the throwing path out of the caller, which keeps the caller small and inlineable.";

    private static readonly LocalizableString MessageFormat = "Use '{0}' instead of an 'if' check with 'throw'";

    private static readonly LocalizableString Title = "Prefer ArgumentException throw helpers over manual guards";

    private static readonly DiagnosticDescriptor Rule =
        new(DiagnosticId, Title, MessageFormat, "Usage", DiagnosticSeverity.Info, true, Description);

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

        // No else clause: a guard must be a lone 'if'.
        if (ifStatement.Else != null)
            return;

        var helperName = GetThrowHelperName(context, ifStatement.Condition);
        if (helperName == null)
            return;

        if (!ThrowsPlainArgumentException(context, ifStatement.Statement, ifStatement.Condition))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.IfKeyword.GetLocation(), helperName));
    }

    private static string? GetThrowHelperName(SyntaxNodeAnalysisContext context, ExpressionSyntax condition)
    {
        if (condition is not InvocationExpressionSyntax invocation)
            return null;

        if (invocation.ArgumentList.Arguments.Count != 1)
            return null;

        // Resolve semantically so unrelated 'IsNullOrEmpty' helpers, extension methods,
        // and 'using static' imports of non-string types are not flagged. This also
        // handles 'string', 'String', 'System.String', 'global::System.String', and aliases.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return null;

        if (method.ContainingType?.SpecialType != SpecialType.System_String)
            return null;

        return method.Name switch
        {
            "IsNullOrWhiteSpace" => "ArgumentException.ThrowIfNullOrWhiteSpace",
            "IsNullOrEmpty" => "ArgumentException.ThrowIfNullOrEmpty",
            _ => null,
        };
    }

    private static bool ThrowsPlainArgumentException(SyntaxNodeAnalysisContext context, StatementSyntax statement, ExpressionSyntax condition)
    {
        ThrowStatementSyntax? throwStatement;
        switch (statement)
        {
            case BlockSyntax block:
            {
                if (block.Statements is not [ThrowStatementSyntax single])
                    return false;

                throwStatement = single;
                break;
            }
            case ThrowStatementSyntax direct:
                throwStatement = direct;
                break;
            default:
                return false;
        }

        if (throwStatement.Expression is not ObjectCreationExpressionSyntax creation)
            return false;

        // Resolve semantically so 'global::System.ArgumentException' is recognized and a
        // user-defined 'ArgumentException' in another namespace is not. Exactly
        // 'System.ArgumentException'; ArgumentNullException and
        // ArgumentOutOfRangeException have their own helpers and semantics.
        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol constructor)
            return false;

        var containingType = constructor.ContainingType;
        if (containingType?.Name != "ArgumentException" || containingType.ContainingNamespace?.ToDisplayString() != "System")
            return false;

        // The thrown ParamName must match the guarded variable; otherwise replacing the
        // guard with ThrowIfNullOrEmpty/ThrowIfNullOrWhiteSpace would change ParamName.
        if (condition is not InvocationExpressionSyntax syntax || syntax.ArgumentList.Arguments.Count != 1 || creation.ArgumentList == null || constructor.Parameters.Length < 2)
            return true;
        var index = -1;
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            if (constructor.Parameters[i].Name != "paramName")
                continue;
            index = i;
            break;
        }

        if (index < 0 || creation.ArgumentList.Arguments.Count <= index)
            return true;
        var guardedName = GetSimpleName(syntax.ArgumentList.Arguments[0].Expression);
        var thrownName = GetParamNameValue(creation.ArgumentList.Arguments[index].Expression);
        return guardedName == null || thrownName == null || string.Equals(guardedName, thrownName, StringComparison.Ordinal);
    }

    private static string? GetSimpleName(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null,
        };
    }

    private static string? GetParamNameValue(ExpressionSyntax expression)
    {
        return expression switch
        {
            // nameof(x) / nameof(Foo.Bar)
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments.Count: 1 } syntax => GetSimpleName(
                syntax.ArgumentList.Arguments[0].Expression),
            // "name" literal
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            _ => null,
        };
    }
}
