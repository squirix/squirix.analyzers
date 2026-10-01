using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a by-value parameter whose type is a readonly struct larger than the by-value limit (SQR0028).
/// Such a struct is copied on every call, and a readonly struct can be passed by <c language="csharp">in</c>
/// without the hidden copies a mutable struct would need.
/// The limit is configurable via the <c language="csharp">SQR0028.max_by_value_size</c> .editorconfig option
/// (default 16 bytes).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LargeStructByValueParameterAnalyzer : DiagnosticAnalyzer
{
    private const int DefaultMaxByValueSize = 16;
    private const string DiagnosticId = "SQR0028";
    private const string MaxByValueSizeOptionName = "SQR0028.max_by_value_size";

    private static readonly LocalizableString Description = "A readonly struct larger than the by-value limit (default 16 bytes) is copied on every call when passed " +
                                                            "by value. Pass it by 'in' to hand over a reference instead.";

    private static readonly LocalizableString MessageFormat = "Parameter '{0}' copies the {2}-byte readonly struct '{1}' on every call; pass it by 'in'";
    private static readonly LocalizableString Title = "Pass large readonly structs by 'in'";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Performance", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var sizes = new StructSizeEstimator();
            start.RegisterSyntaxNodeAction(node => AnalyzeDeclaration(node, sizes), SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration,
                SyntaxKind.OperatorDeclaration, SyntaxKind.ConversionOperatorDeclaration);
        });
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context, StructSizeEstimator sizes)
    {
        var declaration = (BaseMethodDeclarationSyntax)context.Node;
        if (!HasByValueParameter(declaration) || !HasBody(declaration) || declaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } method || !CanChangeParameters(method))
            return;

        var maxSize = 0;
        var checkedSignature = false;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsReadOnly: true } type)
                continue;

            if (maxSize == 0)
                maxSize = AnalyzerHelpers.GetIntOption(context.Options, declaration.SyntaxTree, MaxByValueSizeOptionName, DefaultMaxByValueSize);

            if (!sizes.TryGetSize(type, out var size) || size <= maxSize)
                continue;

            if (!checkedSignature)
            {
                if (ImplementsInterfaceMember(method))
                    return;

                checkedSignature = true;
            }

            if (HasByReferenceOverload(method, parameter) || !CanBeReadOnlyReference(parameter, declaration, context.SemanticModel))
                continue;

            var location = GetLocation(parameter, context.CancellationToken);
            if (location != null)
            {
                var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, parameter.Name, typeName, size));
            }
        }
    }

    /// <summary>
    /// Returns whether the body treats the parameter in a way an <c language="csharp">in</c> parameter allows:
    /// never assigned, passed by reference, address-taken or captured by a lambda or local function.
    /// </summary>
    private static bool CanBeReadOnlyReference(IParameterSymbol parameter, BaseMethodDeclarationSyntax declaration, SemanticModel semanticModel)
    {
        if (IsPassedByReference(declaration, parameter.Name))
            return false;

        if (declaration is ConstructorDeclarationSyntax { Initializer: { } initializer } && !IsUntouched(semanticModel.AnalyzeDataFlow(initializer), parameter))
            return false;

        if (declaration.Body != null)
            return IsUntouched(semanticModel.AnalyzeDataFlow(declaration.Body), parameter);

        return declaration.ExpressionBody != null && IsUntouched(semanticModel.AnalyzeDataFlow(declaration.ExpressionBody.Expression), parameter);
    }

    /// <summary>
    /// Returns whether the method owns its signature and can take <c language="csharp">in</c> parameters.
    /// A reference handed to a method that returns a ref struct or a reference may escape through the result, so such
    /// methods are left alone.
    /// </summary>
    private static bool CanChangeParameters(IMethodSymbol method)
    {
        if (method.IsOverride || method.IsIterator || method.ExplicitInterfaceImplementations.Length > 0 || method.ContainingType.TypeKind == TypeKind.Interface)
            return false;

        if (method.ReturnsByRef || method.ReturnsByRefReadonly || method.ReturnType.IsRefLikeType)
            return false;

        if (method.MethodKind == MethodKind.Constructor && method.ContainingType.IsRefLikeType)
            return false;

        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "UnmanagedCallersOnlyAttribute")
                return false;
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && parameter.Type.IsRefLikeType)
                return false;
        }

        return true;
    }

    private static Location? GetLocation(IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        foreach (var reference in parameter.DeclaringSyntaxReferences)
            return reference.GetSyntax(cancellationToken).GetLocation();

        return null;
    }

    private static bool HasBody(BaseMethodDeclarationSyntax declaration) => declaration.Body != null || declaration.ExpressionBody != null;

    // An 'in' parameter cannot coexist with an overload that differs only by taking the same parameter by reference.
    private static bool HasByReferenceOverload(IMethodSymbol method, IParameterSymbol parameter)
    {
        foreach (var member in method.ContainingType.GetMembers(method.Name))
        {
            if (member is not IMethodSymbol other || SymbolEqualityComparer.Default.Equals(other, method) || other.Parameters.Length != method.Parameters.Length)
                continue;

            var twin = other.Parameters[parameter.Ordinal];
            if (twin.RefKind != RefKind.None && SymbolEqualityComparer.Default.Equals(twin.Type, parameter.Type))
                return true;
        }

        return false;
    }

    private static bool HasByValueParameter(BaseMethodDeclarationSyntax declaration)
    {
        foreach (var parameter in declaration.ParameterList.Parameters)
        {
            if (parameter.Modifiers.Count == 0 || (parameter.Modifiers.Count == 1 && parameter.Modifiers[0].IsKind(SyntaxKind.ThisKeyword)))
                return true;
        }

        return false;
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        // The implementation lookup returns the definition part of a partial method.
        var target = method.PartialDefinitionPart ?? method;
        var type = method.ContainingType;
        foreach (var contract in type.AllInterfaces)
        {
            foreach (var member in contract.GetMembers(method.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), target))
                    return true;
            }
        }

        return false;
    }

    // Data flow does not treat 'ref' passed to a 'ref readonly' parameter as a write, but 'ref' on an 'in' parameter does not compile.
    private static bool IsPassedByReference(BaseMethodDeclarationSyntax declaration, string parameterName)
    {
        foreach (var node in declaration.DescendantNodes())
        {
            if (node is ArgumentSyntax { Expression: IdentifierNameSyntax name } argument && name.Identifier.ValueText == parameterName &&
                (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)))
                return true;
        }

        return false;
    }

    private static bool IsUntouched(DataFlowAnalysis? flow, IParameterSymbol parameter) =>
        flow is { Succeeded: true } && !flow.WrittenInside.Contains(parameter) && !flow.Captured.Contains(parameter) && !flow.UnsafeAddressTaken.Contains(parameter);
}
