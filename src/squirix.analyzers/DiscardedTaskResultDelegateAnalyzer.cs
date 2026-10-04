using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Squirix.Analyzers;

/// <summary>
/// Flags a non-async lambda, anonymous method or method group that returns <c>Task&lt;T&gt;</c> while the target delegate
/// returns non-generic <c>Task</c> (SQR0032). <c>Task&lt;T&gt;</c> converts implicitly to <c>Task</c>, so the
/// <c>T</c> result is silently lost. The compiler rejects this only for async lambdas.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiscardedTaskResultDelegateAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SQR0032";

    private static readonly LocalizableString Description = "Task<T> converts implicitly to Task, so a delegate that returns Task drops the result of a Task<T> it returns. " +
                                                            "Use a delegate that returns Task<T>, or discard the result explicitly.";

    private static readonly LocalizableString MessageFormat = "Delegate '{0}' returns Task, so the {1} result of this Task<{1}> is discarded";
    private static readonly LocalizableString Title = "Do not convert a Task<T> to Task in a delegate return";
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
        context.RegisterOperationAction(AnalyzeDelegateCreation, OperationKind.DelegateCreation);
    }

    private static void AnalyzeBlock(OperationAnalysisContext context, SemanticModel semanticModel, BlockSyntax block, string delegateName)
    {
        foreach (var node in block.DescendantNodes(static child => child is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (node is ReturnStatementSyntax { Expression: { } returned })
                ReportReturned(context, semanticModel, returned, delegateName);
        }
    }

    private static void AnalyzeDelegateCreation(OperationAnalysisContext context)
    {
        var creation = (IDelegateCreationOperation)context.Operation;
        if (creation.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } delegateType || !IsPlainTask(invoke.ReturnType))
            return;

        var delegateName = delegateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        switch (creation.Target)
        {
            case IMethodReferenceOperation reference:
                if (TryGetResultType(reference.Method.ReturnType, out var methodResult))
                    context.ReportDiagnostic(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), delegateName, methodResult));

                break;
            case IAnonymousFunctionOperation { Symbol.IsAsync: false } function when function.Syntax is AnonymousFunctionExpressionSyntax syntax:
                AnalyzeFunction(context, syntax, delegateName);
                break;
        }
    }

    private static void AnalyzeFunction(OperationAnalysisContext context, AnonymousFunctionExpressionSyntax function, string delegateName)
    {
        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel == null)
            return;

        if (function.Body is ExpressionSyntax expressionBody)
        {
            ReportReturned(context, semanticModel, expressionBody, delegateName);
            return;
        }

        if (function.Body is BlockSyntax block)
            AnalyzeBlock(context, semanticModel, block, delegateName);
    }

    private static bool IsNamed(ITypeSymbol type, string name, bool generic) =>
        type is INamedTypeSymbol { ContainingNamespace: { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } } named
        && named.Name == name
        && named.IsGenericType == generic;

    private static bool IsPlainTask(ITypeSymbol type) => IsNamed(type, "Task", false);

    private static bool IsTaskLike(ITypeSymbol type) => IsNamed(type, "Task", false) || IsNamed(type, "Task", true) || IsNamed(type, "ValueTask", false) || IsNamed(type, "ValueTask", true);

    private static void ReportReturned(OperationAnalysisContext context, SemanticModel semanticModel, ExpressionSyntax returned, string delegateName)
    {
        var info = semanticModel.GetTypeInfo(returned, context.CancellationToken);
        if (info.Type == null || info.ConvertedType == null || !IsPlainTask(info.ConvertedType))
            return;

        if (TryGetResultType(info.Type, out var result))
            context.ReportDiagnostic(Diagnostic.Create(Rule, returned.GetLocation(), delegateName, result));
    }

    private static bool TryGetResultType(ITypeSymbol type, out string result)
    {
        result = string.Empty;
        if (!IsNamed(type, "Task", true))
            return false;

        var argument = ((INamedTypeSymbol)type).TypeArguments[0];
        if (IsTaskLike(argument))
            return false;

        result = argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return true;
    }
}
