using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a field dereference in <c>Dispose(bool)</c> of a finalizable class when the dereference is outside the
/// <c>disposing</c> branch (SQR0033). When a constructor throws, the object still gets finalized, but the fields that
/// the constructor body assigns are still null, so the finalizer's <c>Dispose(false)</c> throws
/// <see cref="NullReferenceException" /> on the finalizer thread and the process dies. The value of the field is
/// followed through a read by <see cref="Interlocked" /> or <see cref="Volatile" /> and through a local that is never
/// written again; a field-like event invoked directly counts as well.
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
        "{0} '{1}' is dereferenced outside the disposing branch of Dispose(bool); it is null when the finalizer runs after a constructor threw";

    private static readonly LocalizableString Title = "Do not dereference a field outside the disposing branch of a finalizable Dispose(bool)";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, "Reliability", DiagnosticSeverity.Warning, true, Description,
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
        context.RegisterOperationBlockAction(AnalyzeOperationBlock);
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context)
    {
        if (context.OwningSymbol is not IMethodSymbol method || !IsDisposeBool(method))
            return;

        var type = method.ContainingType;
        if (type.TypeKind != TypeKind.Class || !(DeclaresFinalizer(type) || (method.IsOverride && InheritsFinalizer(type))))
            return;

        var cancellationToken = context.CancellationToken;
        var guards = new DisposingPathGuards(method.Parameters[0], type, CollectConstructorAssignedFields(type, cancellationToken), cancellationToken);
        var copies = CollectCopies(context.OperationBlocks, type, cancellationToken);
        foreach (var block in context.OperationBlocks)
            foreach (var operation in block.DescendantsAndSelf())
            {
                switch (operation)
                {
                    case IFieldReferenceOperation reference when IsUnsafeFieldReference(reference, type, cancellationToken) && IsDereferenced(reference):
                        Report(context, guards, reference, reference.Field, reference.Field);
                        break;
                    case IEventReferenceOperation raised when IsUnsafeEventReference(raised, type, cancellationToken) && IsDereferenced(raised):
                        Report(context, guards, raised, raised.Event, raised.Event);
                        break;
                    case IInvocationOperation read when GetFieldRead(read, type, cancellationToken) is { } source && IsDereferenced(read):
                        Report(context, guards, source, source.Field, source.Field);
                        break;
                    case ILocalReferenceOperation local when copies.TryGetValue(local.Local, out var copied) && IsDereferenced(local):
                        // A check of the local guards its dereference, and so does a check of the field it was copied from.
                        if (!guards.IsGuarded(local, copied))
                            Report(context, guards, local, local.Local, copied);

                        break;
                }
            }
    }

    private static HashSet<string> CollectConstructorAssignedFields(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constructor in type.InstanceConstructors)
            foreach (var reference in constructor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax declaration)
                    continue;

                // Object initializers assign members of the created object, not fields of this one.
                foreach (var node in declaration.DescendantNodes(static child => child is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or InitializerExpressionSyntax)))
                {
                    if (node is AssignmentExpressionSyntax assignment && !IsDefaultLiteral(assignment.Right) && TryGetAssignedName(assignment.Left, out var name))
                        _ = names.Add(name);
                }
            }

        return names;
    }

    /// <summary>
    /// Finds the locals that hold the value of an unsafe field or event and nothing else: declared with it, or with a read of the field
    /// through <see cref="Interlocked" /> or <see cref="Volatile" />, and never written again.
    /// </summary>
    private static Dictionary<ILocalSymbol, ISymbol> CollectCopies(ImmutableArray<IOperation> blocks, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var copies = new Dictionary<ILocalSymbol, ISymbol>(SymbolEqualityComparer.Default);
        foreach (var block in blocks)
            foreach (var operation in block.DescendantsAndSelf())
            {
                // A value-type local is not the reference itself: a span over a null array is empty, and unboxing throws at the cast.
                if (operation is not IVariableDeclaratorOperation { Symbol: { IsRef: false, Type.IsValueType: false } } declarator
                    || declarator.GetVariableInitializer() is not { } initializer)
                    continue;

                var source = DisposingPathGuards.Unwrap(initializer.Value) switch
                {
                    IFieldReferenceOperation reference when IsUnsafeFieldReference(reference, type, cancellationToken) => reference.Field,
                    IEventReferenceOperation raised when IsUnsafeEventReference(raised, type, cancellationToken) => raised.Event,
                    IInvocationOperation read => GetFieldRead(read, type, cancellationToken)?.Field,
                    _ => (ISymbol?)null,
                };

                if (source != null)
                    copies[declarator.Symbol] = source;
            }

        if (copies.Count == 0)
            return copies;

        foreach (var block in blocks)
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is ILocalReferenceOperation local && IsWritten(local))
                    _ = copies.Remove(local.Local);
            }

        return copies;
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

    /// <summary>
    /// Returns the unsafe field that <paramref name="invocation" /> reads by reference and returns the value of:
    /// <c>Interlocked.Exchange</c>, <c>Interlocked.CompareExchange</c> or <c>Volatile.Read</c>. The result is as null as the field was.
    /// </summary>
    private static IFieldReferenceOperation? GetFieldRead(IInvocationOperation invocation, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var method = invocation.TargetMethod;
        var owner = method.ContainingType;
        if (owner?.ContainingNamespace is not { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
            return null;

        if (!((owner.Name == "Interlocked" && method.Name is "Exchange" or "CompareExchange") || (owner.Name == "Volatile" && method.Name == "Read")))
            return null;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is { Ordinal: 0, RefKind: not RefKind.None }
                && DisposingPathGuards.Unwrap(argument.Value) is IFieldReferenceOperation reference
                && IsUnsafeFieldReference(reference, type, cancellationToken))
                return reference;
        }

        return null;
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

    private static bool IsDereferenced(IOperation value)
    {
        var current = value;
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
            IThrowOperation thrown => thrown.Exception == current,
            _ => false,
        };
    }

    /// <summary>Returns whether <paramref name="value" /> leaves a field at its default value: <c>false</c>, a numeric zero, <c>null</c>, <c>default</c> or <c>default(T)</c>.</summary>
    private static bool IsDefaultLiteral(ExpressionSyntax value) =>
        value is DefaultExpressionSyntax
        || (value is LiteralExpressionSyntax literal && literal.Token.Value is false or null or 0 or 0L or 0U or 0UL or 0F or 0D or 0M);

    private static bool IsDisposeBool(IMethodSymbol method) =>
        method is { Name: "Dispose", MethodKind: MethodKind.Ordinary, IsStatic: false, ReturnsVoid: true, Parameters.Length: 1 }
        && method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean;

    /// <summary>
    /// Returns whether the event is field-like, declared in <paramref name="type" /> without an initializer and read on this instance: it is
    /// null until someone subscribes.
    /// </summary>
    private static bool IsUnsafeEventReference(IEventReferenceOperation reference, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var raised = reference.Event;
        if (reference.Instance is not IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
            || !SymbolEqualityComparer.Default.Equals(raised.ContainingType.OriginalDefinition, type.OriginalDefinition))
            return false;

        foreach (var declaration in raised.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer: null })
                return true;
        }

        return false;
    }

    private static bool IsUnsafeFieldReference(IFieldReferenceOperation reference, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var field = reference.Field;
        return reference.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
               && !field.IsStatic
               && !field.Type.IsValueType
               && SymbolEqualityComparer.Default.Equals(field.ContainingType.OriginalDefinition, type.OriginalDefinition)
               && !DisposingPathGuards.HasInitializer(field, cancellationToken);
    }

    /// <summary>Returns whether the reference changes the local or hands it out by reference, so that it may hold another value afterwards.</summary>
    private static bool IsWritten(ILocalReferenceOperation local)
    {
        IOperation current = local;
        var parent = current.Parent;
        while (parent is IParenthesizedOperation or ITupleOperation)
        {
            current = parent;
            parent = parent.Parent;
        }

        return parent switch
        {
            IAssignmentOperation assignment => assignment.Target == current,
            IIncrementOrDecrementOperation => true,
            IArgumentOperation argument => argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out },
            IAddressOfOperation => true,

            // A dynamic call has no argument operations, so the keyword is read from the syntax.
            _ => local.Syntax.Parent is RefExpressionSyntax or ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword },
        };
    }

    private static void Report(OperationBlockAnalysisContext context, DisposingPathGuards guards, IOperation dereferenced, ISymbol subject, ISymbol named)
    {
        if (!guards.IsGuarded(dereferenced, subject))
            context.ReportDiagnostic(Diagnostic.Create(Rule, dereferenced.Syntax.GetLocation(), named is IEventSymbol ? "Event" : "Field", named.Name));
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
