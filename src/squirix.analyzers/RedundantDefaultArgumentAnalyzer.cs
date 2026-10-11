using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Squirix.Analyzers;

/// <summary>
/// Flags call-site arguments that equal the parameter's default value (SQR0011).
/// Matches Rider "The parameter '…' has the same default value".
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantDefaultArgumentAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0011";

    private static readonly LocalizableString Description = "Omit arguments that equal the parameter default; the default may change at the declaration.";

    private const string RunAdvice = ", and so do the arguments after it; omit them together";

    private static readonly LocalizableString MessageFormat = "The parameter '{0}' has the same default value{1}";

    private static readonly LocalizableString Title = "Avoid redundant default argument values";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Style", DiagnosticSeverity.Info, true, Description);

    private static readonly HashSet<SyntaxKind> RedundantDefaultCandidateKinds =
    [
        SyntaxKind.DefaultLiteralExpression,
        SyntaxKind.DefaultExpression,
        SyntaxKind.NullLiteralExpression,
        SyntaxKind.NumericLiteralExpression,
        SyntaxKind.StringLiteralExpression,
        SyntaxKind.CharacterLiteralExpression,
        SyntaxKind.TrueLiteralExpression,
        SyntaxKind.FalseLiteralExpression,
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeImplicitObjectCreation, SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeArgumentList(SyntaxNodeAnalysisContext context, ArgumentListSyntax argumentList, IMethodSymbol method,
        Func<SyntaxNode, ArgumentListSyntax, ExpressionSyntax> withArgumentList)
    {
        var parameters = method.Parameters;
        if (parameters.Length == 0 || argumentList.Arguments.Count == 0)
            return;

        // Map each argument index to a parameter index (positional + named).
        var argumentToParameter = new int[argumentList.Arguments.Count];
        var nextPositional = 0;
        for (var i = 0; i < argumentList.Arguments.Count; i++)
        {
            var argument = argumentList.Arguments[i];
            if (argument.NameColon == null)
            {
                if (nextPositional >= parameters.Length)
                    return;

                // params absorbs remaining positionals.
                if (parameters[nextPositional].IsParams)
                {
                    argumentToParameter[i] = nextPositional;
                    continue;
                }

                argumentToParameter[i] = nextPositional;
                nextPositional++;
                continue;
            }

            var name = argument.NameColon.Name.Identifier.ValueText;
            var parameterIndex = FindParameterIndex(parameters, name);
            if (parameterIndex < 0)
                return;

            argumentToParameter[i] = parameterIndex;
            if (parameterIndex >= nextPositional && !parameters[parameterIndex].IsParams)
                nextPositional = parameterIndex + 1;
        }

        for (var i = 0; i < argumentList.Arguments.Count; i++)
        {
            var parameterIndex = argumentToParameter[i];
            var parameter = parameters[parameterIndex];
            if (parameter.IsParams || !parameter.IsOptional)
                continue;

            if (!TryGetParameterDefault(parameter, out var defaultValue))
                continue;

            var argument = argumentList.Arguments[i];
            if (!ArgumentEqualsDefault(context, argument.Expression, parameter, defaultValue))
                continue;

            // A named argument can always be dropped on its own.
            if (argument.NameColon != null)
            {
                if (RemainsBoundAfterRemoving(context, argumentList, i, 1, withArgumentList, method))
                    context.ReportDiagnostic(Diagnostic.Create(Rule, argument.GetLocation(), parameter.Name, string.Empty));

                continue;
            }

            // A positional argument can go only together with everything after it: dropping it alone would hand the
            // next argument to its parameter. So the rest of the list must be redundant defaults too, and it is
            // reported once, as one run.
            if (!TrailingDefaultsCanBeOmitted(argumentList, argumentToParameter, parameters, i, context))
                continue;

            var count = argumentList.Arguments.Count - i;
            if (!RemainsBoundAfterRemoving(context, argumentList, i, count, withArgumentList, method))
                continue;

            var last = argumentList.Arguments[argumentList.Arguments.Count - 1];
            var location = Location.Create(argument.SyntaxTree, TextSpan.FromBounds(argument.SpanStart, last.Span.End));
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, parameter.Name, count > 1 ? RunAdvice : string.Empty));
            return;
        }
    }

    private static void AnalyzeImplicitObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ImplicitObjectCreationExpressionSyntax creation)
            return;

        if (!HasRedundantDefaultCandidate(creation.ArgumentList))
            return;

        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        // Re-binding a bare 'new(...)' in isolation has no target type, so rewrite it to an explicit creation of the bound type.
        if (context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type is not { } createdType)
            return;

        var typeSyntax = SyntaxFactory.ParseTypeName(createdType.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        AnalyzeArgumentList(context, creation.ArgumentList, method, (_, list) => SyntaxFactory.ObjectCreationExpression(typeSyntax).WithArgumentList(list));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        if (!HasRedundantDefaultCandidate(invocation.ArgumentList))
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        AnalyzeArgumentList(context, invocation.ArgumentList, method, static (node, list) => ((InvocationExpressionSyntax)node).WithArgumentList(list));
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ObjectCreationExpressionSyntax creation || creation.ArgumentList == null)
            return;

        if (!HasRedundantDefaultCandidate(creation.ArgumentList))
            return;

        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        AnalyzeArgumentList(context, creation.ArgumentList, method, static (node, list) => ((ObjectCreationExpressionSyntax)node).WithArgumentList(list));
    }

    private static bool ArgumentEqualsDefault(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, IParameterSymbol parameter, object? defaultValue)
    {
        // 'default' / 'default(T)' equals the parameter default only when the parameter
        // default itself is the default value of the parameter type (e.g. null for
        // reference types, 0 for int). 'M(default)' with 'void M(int x = 5)' must not flag.
        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) || expression.IsKind(SyntaxKind.DefaultExpression))
            return IsDefaultValueOfParameterType(defaultValue, parameter.Type);

        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        return constant.HasValue && EqualsNormalized(constant.Value, defaultValue);
    }

    private static bool EqualsNormalized(object? left, object? right)
    {
        if (Equals(left, right))
            return true;

        // Roslyn may box enum defaults as the underlying integral type.
        if (left is Enum leftEnum && right != null)
        {
            return Equals(Convert.ChangeType(leftEnum, Enum.GetUnderlyingType(leftEnum.GetType()), CultureInfo.InvariantCulture), right);
        }

        if (right is Enum rightEnum && left != null)
        {
            return Equals(left, Convert.ChangeType(rightEnum, Enum.GetUnderlyingType(rightEnum.GetType()), CultureInfo.InvariantCulture));
        }

        return false;
    }

    private static int FindParameterIndex(ImmutableArray<IParameterSymbol> parameters, string name)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static object? GetNumericZero(SpecialType specialType)
    {
        return specialType switch
        {
            SpecialType.System_Boolean => false,
            SpecialType.System_Byte => (byte)0,
            SpecialType.System_SByte => (sbyte)0,
            SpecialType.System_Int16 => (short)0,
            SpecialType.System_UInt16 => (ushort)0,
            SpecialType.System_Int32 => 0,
            SpecialType.System_UInt32 => 0u,
            SpecialType.System_Int64 => 0L,
            SpecialType.System_UInt64 => 0UL,
            SpecialType.System_Single => 0f,
            SpecialType.System_Double => 0d,
            SpecialType.System_Decimal => 0m,
            SpecialType.System_Char => '\0',
            SpecialType.System_IntPtr => IntPtr.Zero,
            SpecialType.System_UIntPtr => UIntPtr.Zero,
            _ => null,
        };
    }

    private static object? GetValueTypeDefault(ITypeSymbol type)
    {
        if (type.TypeKind is not TypeKind.Enum || type is not INamedTypeSymbol enumType)
            return GetNumericZero(type.SpecialType);
        // Enum default is 0 converted to the underlying type's zero value.
        var underlying = enumType.EnumUnderlyingType;
        if (underlying == null)
            return 0;

        return GetNumericZero(underlying.SpecialType);
    }

    private static bool HasOptionalAttribute(IParameterSymbol parameter)
    {
        var attributes = parameter.GetAttributes();
        foreach (var attribute in attributes)
        {
            var attr = attribute.AttributeClass;
            if (attr?.Name is "OptionalAttribute" && attr.ContainingNamespace?.ToDisplayString() is "System.Runtime.InteropServices")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRedundantDefaultCandidate(ArgumentListSyntax? argumentList)
    {
        if (argumentList is null)
            return false;

        foreach (var argument in argumentList.Arguments)
        {
            if (argument.NameColon != null)
                return true;

            if (RedundantDefaultCandidateKinds.Contains(argument.Expression.Kind()))
                return true;
        }

        return false;
    }

    private static bool IsDefaultValueOfParameterType(object? defaultValue, ITypeSymbol parameterType)
    {
        if (parameterType.IsReferenceType || parameterType is IPointerTypeSymbol || parameterType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return defaultValue is null;

        var typeDefault = GetValueTypeDefault(parameterType);
        if (typeDefault is null)
            return false;

        return EqualsNormalized(typeDefault, defaultValue);
    }

    private static bool RemainsBoundAfterRemoving(SyntaxNodeAnalysisContext context, ArgumentListSyntax argumentList, int argumentIndex, int count,
        Func<SyntaxNode, ArgumentListSyntax, ExpressionSyntax> withArgumentList, IMethodSymbol method)
    {
        var rewrittenArgs = argumentList.Arguments;
        for (var removed = 0; removed < count; removed++)
            rewrittenArgs = rewrittenArgs.RemoveAt(argumentIndex);

        var rewrittenList = argumentList.WithArguments(rewrittenArgs);
        var rewrittenCall = withArgumentList(context.Node, rewrittenList);

        var speculative = context.SemanticModel.GetSpeculativeSymbolInfo(context.Node.SpanStart, rewrittenCall, SpeculativeBindingOption.BindAsExpression);

        return speculative.Symbol is IMethodSymbol speculativeMethod && SymbolEqualityComparer.Default.Equals(speculativeMethod.OriginalDefinition, method.OriginalDefinition);
    }

    private static bool TrailingDefaultsCanBeOmitted(ArgumentListSyntax argumentList, int[] argumentToParameter, ImmutableArray<IParameterSymbol> parameters, int startIndex,
        SyntaxNodeAnalysisContext context)
    {
        for (var i = startIndex; i < argumentList.Arguments.Count; i++)
        {
            if (argumentList.Arguments[i].NameColon != null)
                return false;

            var parameter = parameters[argumentToParameter[i]];
            if (parameter.IsParams || !parameter.IsOptional || !TryGetParameterDefault(parameter, out var defaultValue))
                return false;

            if (!ArgumentEqualsDefault(context, argumentList.Arguments[i].Expression, parameter, defaultValue))
                return false;
        }

        return true;
    }

    private static bool HasCallerInfoAttribute(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            var attr = attribute.AttributeClass;
            if (attr?.ContainingNamespace?.ToDisplayString() is not "System.Runtime.CompilerServices")
                continue;

            if (attr.Name is "CallerMemberNameAttribute" or "CallerFilePathAttribute" or "CallerLineNumberAttribute" or "CallerArgumentExpressionAttribute")
                return true;
        }

        return false;
    }

    private static bool TryGetParameterDefault(IParameterSymbol parameter, out object? defaultValue)
    {
        // Omitting a caller-info argument makes the compiler substitute caller data, so an explicit value is never redundant.
        if (HasCallerInfoAttribute(parameter))
        {
            defaultValue = null;
            return false;
        }

        if (parameter.HasExplicitDefaultValue)
        {
            defaultValue = parameter.ExplicitDefaultValue;
            return true;
        }

        // [Optional] without C# default uses the type's default value.
        if (HasOptionalAttribute(parameter))
        {
            defaultValue = parameter.Type.IsReferenceType || parameter.Type is IPointerTypeSymbol ? null : GetValueTypeDefault(parameter.Type);
            return true;
        }

        defaultValue = null;
        return false;
    }
}
