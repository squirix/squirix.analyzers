using System;
using System.Collections.Generic;
using System.Globalization;

namespace Squirix.Analyzers.Fuzz;

/// <summary>The arguments of a subcommand: <c>--name value</c> options, <c>--name</c> flags, and positional values.</summary>
internal sealed class CommandLineArguments
{
    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "json" };

    private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);

    public List<string> Positional { get; } = [];

    public static CommandLineArguments Parse(ReadOnlySpan<string> args)
    {
        var result = new CommandLineArguments();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                result.Positional.Add(arg);
                continue;
            }

            var name = arg[2..];
            if (Flags.Contains(name) || index + 1 >= args.Length)
            {
                result._options[name] = null;
            }
            else
            {
                result._options[name] = args[index + 1];
                index++;
            }
        }

        return result;
    }

    public int GetInt32(string name, int defaultValue)
    {
        var value = GetString(name);
        return value == null ? defaultValue : int.Parse(value, CultureInfo.InvariantCulture);
    }

    public string GetRequiredString(string name) => GetString(name) ?? ThrowOptionMissing(name);

    public string? GetString(string name) => _options.GetValueOrDefault(name);

    public bool HasFlag(string name) => _options.ContainsKey(name);

    private static string ThrowOptionMissing(string name) => throw new ArgumentException($"The option '--{name}' is required", nameof(name));
}
