using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a call to a non-readonly struct member through a readonly variable (SQR0029).
/// The compiler cannot let such a member mutate a readonly field, an <c language="csharp">in</c> parameter or a
/// <c language="csharp">ref readonly</c> value, so it runs the member on a hidden copy: the copy costs time and
/// any state the member changes is lost.
/// The smallest struct reported is configurable via the <c language="csharp">SQR0029.min_struct_size</c>
/// .editorconfig option (default 1 byte, which reports every struct).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefensiveStructCopyAnalyzer : DiagnosticAnalyzer
{
    private const int DefaultMinStructSize = 1;
    private const string DiagnosticId = "SQR0029";
    private const string MinStructSizeOptionName = "SQR0029.min_struct_size";

    private static readonly LocalizableString Description = "A non-readonly struct member called through a readonly field, an 'in' parameter or a 'ref readonly' " +
                                                            "value runs on a hidden copy of the struct. Mark the member readonly, or copy the value to a local " +
                                                            "when the member has to mutate it.";

    private static readonly LocalizableString MessageFormat = "'{0}' is not readonly, so calling it on the readonly '{1}' runs on a hidden copy of '{2}'";
    private static readonly LocalizableString Title = "Do not call non-readonly struct members through readonly variables";
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
            start.RegisterOperationAction(operation => AnalyzeInvocation(operation, sizes), OperationKind.Invocation);
            start.RegisterOperationAction(operation => AnalyzePropertyReference(operation, sizes), OperationKind.PropertyReference);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, StructSizeEstimator sizes)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.Instance != null)
            Analyze(context, sizes, invocation.Instance, invocation.TargetMethod, invocation.TargetMethod.Name);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context, StructSizeEstimator sizes)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;

        // A plain assignment runs the setter, which compiles through a readonly variable only when it is readonly.
        if (reference.Parent is ISimpleAssignmentOperation assignment && ReferenceEquals(assignment.Target, reference))
            return;

        if (reference.Instance != null && reference.Property.GetMethod is { } getter)
            Analyze(context, sizes, reference.Instance, getter, reference.Property.Name);
    }

    private static void Analyze(OperationAnalysisContext context, StructSizeEstimator sizes, IOperation instance, IMethodSymbol member, string memberName)
    {
        if (member.IsReadOnly || member.ContainingType is not { TypeKind: TypeKind.Struct } type)
            return;

        var variable = GetReadOnlyVariable(instance, context.ContainingSymbol);
        if (variable == null || HasAncestor<INameOfOperation>(context.Operation))
            return;

        var minSize = AnalyzerHelpers.GetIntOption(context.Options, context.Operation.Syntax.SyntaxTree, MinStructSizeOptionName, DefaultMinStructSize);

        // A struct whose size cannot be estimated is still reported at the default threshold: the copy happens either way.
        if (minSize > DefaultMinStructSize && (!sizes.TryGetSize(type, out var size) || size < minSize))
            return;

        var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(Diagnostic.Create(Rule, GetLocation(context.Operation), memberName, variable.Name, typeName));
    }

    private static Location GetLocation(IOperation operation)
    {
        var syntax = operation.Syntax is InvocationExpressionSyntax invocation ? invocation.Expression : operation.Syntax;
        return syntax is MemberAccessExpressionSyntax access ? access.Name.GetLocation() : syntax.GetLocation();
    }

    /// <summary>
    /// Returns the symbol of the readonly variable the receiver reads, or <c language="csharp">null</c> when the
    /// receiver is writable or a temporary value.
    /// </summary>
    private static ISymbol? GetReadOnlyVariable(IOperation receiver, ISymbol containingSymbol) => receiver switch
    {
        IParameterReferenceOperation { Parameter.RefKind: RefKind.In or RefKind.RefReadOnlyParameter } parameter => parameter.Parameter,
        ILocalReferenceOperation { Local.RefKind: RefKind.RefReadOnly } local => local.Local,
        IInvocationOperation { TargetMethod.ReturnsByRefReadonly: true } call => call.TargetMethod,
        IPropertyReferenceOperation { Property.ReturnsByRefReadonly: true } property => property.Property,
        IFieldReferenceOperation field => GetReadOnlyField(field, containingSymbol),
        _ => null,
    };

    private static ISymbol? GetReadOnlyField(IFieldReferenceOperation reference, ISymbol containingSymbol)
    {
        var field = reference.Field;

        // For a ref field, 'readonly' before 'ref' only fixes where the reference points. Whether the struct behind it can
        // change depends on the kind of reference alone, not on the holder and not on the object being initialized.
        switch (field.RefKind)
        {
            case RefKind.Ref:
                return null;
            case RefKind.RefReadOnly:
                return field;
        }

        if (field.IsReadOnly)
            return IsStillWritable(reference, containingSymbol) ? null : field;

        // A field of a struct is as readonly as the variable that holds the struct.
        if (field.IsStatic || reference.Instance is not { Type.IsValueType: true } owner)
            return null;

        if (owner is IInstanceReferenceOperation)
            return containingSymbol is IMethodSymbol { IsReadOnly: true, MethodKind: not MethodKind.Constructor } ? field : null;

        return GetReadOnlyVariable(owner, containingSymbol) != null ? field : null;
    }

    private static bool HasAncestor<TOperation>(IOperation operation)
        where TOperation : class, IOperation
    {
        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is TOperation)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether a readonly field is still writable where it is read, so that no copy is made: in a constructor,
    /// an init accessor or an initializer of its own type, on the instance being initialized, and outside any lambda
    /// or local function.
    /// </summary>
    private static bool IsStillWritable(IFieldReferenceOperation reference, ISymbol containingSymbol)
    {
        var field = reference.Field;
        if (containingSymbol.IsStatic != field.IsStatic || !SymbolEqualityComparer.Default.Equals(containingSymbol.ContainingType, field.ContainingType))
            return false;

        if (!field.IsStatic && reference.Instance is not IInstanceReferenceOperation)
            return false;

        var initializes = containingSymbol is IFieldSymbol or IPropertySymbol or
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } or IMethodSymbol { IsInitOnly: true };

        return initializes && !HasAncestor<IAnonymousFunctionOperation>(reference) && !HasAncestor<ILocalFunctionOperation>(reference);
    }
}
