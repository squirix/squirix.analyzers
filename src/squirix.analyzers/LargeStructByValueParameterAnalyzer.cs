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
                if (ImplementsInterfaceMember(method) || IsUsedAsMethodGroup(method, context.SemanticModel, context.CancellationToken))
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

    /// <summary>Returns whether a name is the thing being called, as in <c language="csharp">Sum(x)</c> or <c language="csharp">this.Sum(x)</c>.</summary>
    private static bool IsCalled(SimpleNameSyntax name)
    {
        ExpressionSyntax callee = name;
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax access when access.Name == name:
                callee = access;
                break;
            case MemberBindingExpressionSyntax binding when binding.Name == name:
                callee = binding;
                break;
        }

        return callee.Parent is InvocationExpressionSyntax invocation && invocation.Expression == callee;
    }

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
                return true;
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

    /// <summary>
    /// Returns whether the method is used as a value somewhere in its outermost type or in a type nested there:
    /// converted to a delegate, subscribed to an event, or taken the address of. A delegate type fixes the signature of
    /// such a method, so <c language="csharp">in</c> would stop that use compiling. The search reads the syntax of every
    /// part of the type, generated ones included. A mention in this file is bound; one in another file of a partial
    /// type is taken as a use without binding, since any name outside a call is close enough to be careful about.
    /// </summary>
    private static bool IsUsedAsMethodGroup(IMethodSymbol method, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var outermost = method.ContainingType;
        while (outermost.ContainingType != null)
            outermost = outermost.ContainingType;

        foreach (var reference in outermost.DeclaringSyntaxReferences)
        {
            var part = reference.GetSyntax(cancellationToken);
            foreach (var node in part.DescendantNodes())
            {
                if (node is not SimpleNameSyntax name || name.Identifier.ValueText != method.Name || IsCalled(name) || IsInsideNameOf(name))
                    continue;

                if (part.SyntaxTree != semanticModel.SyntaxTree || RefersTo(semanticModel.GetSymbolInfo(name, cancellationToken), method))
                    return true;
            }
        }

        return false;
    }

    private static bool IsUntouched(DataFlowAnalysis? flow, IParameterSymbol parameter) =>
        flow is { Succeeded: true } && !flow.WrittenInside.Contains(parameter) && !flow.Captured.Contains(parameter) && !flow.UnsafeAddressTaken.Contains(parameter);

    /// <summary>Returns the symbol a method is known by wherever it is used: unconstructed, unreduced, and the definition part of a partial method.</summary>
    private static IMethodSymbol Normalize(IMethodSymbol method)
    {
        method = (method.ReducedFrom ?? method).OriginalDefinition;
        return method.PartialDefinitionPart ?? method;
    }

    /// <summary>
    /// Returns whether a bound name is the method, an override of it (which must follow its signature), or the static
    /// form of it as an extension-block member.
    /// </summary>
    private static bool RefersTo(SymbolInfo info, IMethodSymbol method)
    {
        var target = Normalize(method);
        if (RefersTo(info.Symbol, target))
            return true;

        foreach (var candidate in info.CandidateSymbols)
        {
            if (RefersTo(candidate, target))
                return true;
        }

        return false;
    }

    private static bool RefersTo(ISymbol? symbol, IMethodSymbol target)
    {
        for (var found = symbol as IMethodSymbol; found != null; found = found.OverriddenMethod)
        {
            var known = Normalize(found);
            if (SymbolEqualityComparer.Default.Equals(known, target) || SymbolEqualityComparer.Default.Equals(known, target.AssociatedExtensionImplementation))
                return true;
        }

        return false;
    }
}
