using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.Fuzz;

/// <summary>The analyzers to run on an input.</summary>
internal sealed class ComponentFilter
{
    private ComponentFilter(ImmutableArray<DiagnosticAnalyzer> analyzers)
    {
        Analyzers = analyzers;
    }

    public ImmutableArray<DiagnosticAnalyzer> Analyzers { get; }

    /// <summary>Creates a filter.</summary>
    /// <param name="only">The full names of the analyzers to run, or none to run all the analyzers that are not suppressed.</param>
    /// <param name="suppressed">The full names of the analyzers not to run.</param>
    public static ComponentFilter Create(IReadOnlyCollection<string> only, HashSet<string> suppressed)
    {
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        if (only.Count > 0)
        {
            foreach (var name in only)
                analyzers.Add(Components.FindByName(name) ?? ThrowAnalyzerNotFound(name));

            return new ComponentFilter(analyzers.ToImmutable());
        }

        foreach (var analyzer in Components.Analyzers)
        {
            if (!suppressed.Contains(Components.GetName(analyzer)))
                analyzers.Add(analyzer);
        }

        return new ComponentFilter(analyzers.ToImmutable());
    }

    private static DiagnosticAnalyzer ThrowAnalyzerNotFound(string name) => throw new ArgumentException($"Cannot find the analyzer '{name}'", nameof(name));
}
