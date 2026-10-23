using System;
using System.Collections.Generic;
using System.IO;

namespace Squirix.Analyzers.Fuzz;

/// <summary>
/// The environment variables that configure the harness. libFuzzer starts the harness with a single argument,
/// so the settings of the fuzzing session cannot be passed on the command line.
/// </summary>
internal static class HarnessEnvironment
{
    /// <summary>The file where the harness writes each input before running it, as libFuzzer does not save the input that kills the harness process.</summary>
    public const string CurrentInputFileVariable = "FUZZ_CURRENT_INPUT_FILE";

    /// <summary>The folder where the findings are saved. Nothing is saved when it is not set.</summary>
    public const string FindingsDirectoryVariable = "FUZZ_FINDINGS_DIR";

    /// <summary>A file listing the full names of the analyzers not to run, one per line, such as the ones that already crashed the process.</summary>
    public const string SuppressedComponentsFileVariable = "FUZZ_SUPPRESSED_FILE";

    /// <summary>When set, the harness throws for the finding with this signature, so libFuzzer can minimize its input, and saves nothing.</summary>
    public const string TargetSignatureVariable = "FUZZ_TARGET_SIGNATURE";

    public static string? CurrentInputFile => GetPath(CurrentInputFileVariable);

    public static string? FindingsDirectory => GetPath(FindingsDirectoryVariable);

    public static string? TargetSignature => GetString(TargetSignatureVariable);

    public static HashSet<string> GetSuppressedComponents()
    {
        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        var path = GetPath(SuppressedComponentsFileVariable);
        if (path == null || !File.Exists(path))
            return suppressed;

        foreach (var line in File.ReadAllLines(path))
        {
            var name = line.Trim();
            if (name.Length > 0)
                _ = suppressed.Add(name);
        }

        return suppressed;
    }

    private static string? GetPath(string name)
    {
        var value = GetString(name);
        return value == null ? null : Path.GetFullPath(value);
    }

    private static string? GetString(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
