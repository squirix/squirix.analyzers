using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a field dereference in <c>Dispose(bool)</c> of a finalizable class when the dereference is outside the
/// <c>disposing</c> branch (SQR0033). When a constructor throws, the object still gets finalized, but the fields that
/// the constructor body assigns are still null, so the finalizer's <c>Dispose(false)</c> throws
/// <see cref="NullReferenceException" /> on the finalizer thread and the process dies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FinalizerDisposeFieldAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0033";

    private static readonly LocalizableString Description =
        "A finalizer runs Dispose(false) even when a constructor threw, for example the base constructor. The fields that the constructor body assigns " +
        "are then null, and a NullReferenceException on the finalizer thread kills the process. Dereference such a field only when disposing is true, " +
        "or check it for null or check a flag that the constructor sets.";

    private static readonly LocalizableString MessageFormat =
        "Field '{0}' is dereferenced outside the disposing branch of Dispose(bool); it is null when the finalizer runs after a constructor threw";

    private static readonly LocalizableString Title = "Do not dereference a field outside the disposing branch of a finalizable Dispose(bool)";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Reliability", DiagnosticSeverity.Warning, true, Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockAction(AnalyzeOperationBlock);
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
    {
        if (context.OwningSymbol is not IMethodSymbol method || !IsDisposeBool(method))
            return;

        var type = method.ContainingType;
        if (type.TypeKind != TypeKind.Class || !(DeclaresFinalizer(type) || (method.IsOverride && InheritsFinalizer(type))))
            return;

        var guards = new DisposingPathGuards(method.Parameters[0], type, CollectConstructorAssignedFields(type, context.CancellationToken));
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is IFieldReferenceOperation reference && IsUnsafeFieldReference(reference, type) && IsDereferenced(reference) && !guards.IsGuarded(reference))
                    context.ReportDiagnostic(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), reference.Field.Name));
            }
        }
    }

    private static HashSet<string> CollectConstructorAssignedFields(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constructor in type.InstanceConstructors)
        {
            foreach (var reference in constructor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax declaration)
                    continue;

                foreach (var node in declaration.DescendantNodes(static child => child is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
                {
                    if (node is AssignmentExpressionSyntax assignment && TryGetAssignedName(assignment.Left, out var name))
                        _ = names.Add(name);
                }
            }
        }

        return names;
    }

    private static bool DeclaresFinalizer(INamedTypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Object)
            return false;

        foreach (var member in type.GetMembers(WellKnownMemberNames.DestructorName))
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Destructor })
                return true;
        }

        return false;
    }

    private static bool InheritsFinalizer(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (DeclaresFinalizer(current))
                return true;
        }

        return false;
    }

    private static bool IsDereferenced(IFieldReferenceOperation reference)
    {
        IOperation current = reference;
        var parent = current.Parent;
        while (parent is IParenthesizedOperation or IConversionOperation { Conversion.IsUserDefined: false })
        {
            current = parent;
            parent = parent.Parent;
        }

        return parent switch
        {
            IInvocationOperation invocation => invocation.Instance == current,
            IMemberReferenceOperation member => member.Instance == current,
            IArrayElementReferenceOperation element => element.ArrayReference == current,
            IForEachLoopOperation loop => loop.Collection == current,
            ILockOperation gate => gate.LockedValue == current,
            IAwaitOperation => true,
            _ => false,
        };
    }

    private static bool IsDisposeBool(IMethodSymbol method) =>
        method is { Name: "Dispose", MethodKind: MethodKind.Ordinary, IsStatic: false, ReturnsVoid: true, Parameters.Length: 1 }
        && method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean;

    private static bool IsUnsafeFieldReference(IFieldReferenceOperation reference, INamedTypeSymbol type)
    {
        var field = reference.Field;
        return reference.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
               && !field.IsStatic
               && !field.Type.IsValueType
               && SymbolEqualityComparer.Default.Equals(field.ContainingType.OriginalDefinition, type.OriginalDefinition)
               && !DisposingPathGuards.HasInitializer(field);
    }

    private static bool TryGetAssignedName(ExpressionSyntax target, out string name)
    {
        switch (target)
        {
            case IdentifierNameSyntax identifier:
                name = identifier.Identifier.ValueText;
                return true;
            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax member }:
                name = member.Identifier.ValueText;
                return true;
            default:
                name = string.Empty;
                return false;
        }
    }
}
