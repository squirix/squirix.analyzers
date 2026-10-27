using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.Fuzz;

/// <summary>
/// The .editorconfig options of an input, read from the <c>//! key = value</c> comments that start it, so libFuzzer mutates
/// the thresholds of the rules along with the code. The comments stay in the analyzed code, so the positions do not change.
/// </summary>
internal static class InputOptions
{
    public const string OptionPrefix = "//!";

    public static ImmutableDictionary<string, string> ParseOptions(string source)
    {
        var options = ImmutableDictionary.CreateBuilder<string, string>(AnalyzerConfigOptions.KeyComparer);
        var position = 0;
        while (position < source.Length)
        {
            var end = source.IndexOf('\n', position);
            if (end < 0)
                end = source.Length;

            var line = source.AsSpan(position, end - position).Trim();
            if (!line.StartsWith(OptionPrefix, StringComparison.Ordinal))
                break;

            // As in .editorconfig, the keys are case-insensitive, and the last value of a key wins
            line = line[OptionPrefix.Length..];
            var separator = line.IndexOf('=');
            if (separator > 0)
                options[line[..separator].Trim().ToString()] = line[(separator + 1)..].Trim().ToString();

            position = end + 1;
        }

        return options.ToImmutable();
    }
}
