using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.Fuzz;

/// <summary>Finds the analyzer responsible for an input that stops libFuzzer: the harness process crashed, or did not complete in time.</summary>
internal static class Triage
{
    private const string UnknownComponent = "unknown";

    public static bool IsUnknown(Finding finding) => finding.Component == UnknownComponent;

    public static string ReadInput(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));

    public static async Task<int> RunCommandAsync(CommandLineArguments arguments)
    {
        var kind = arguments.GetRequiredString("kind");
        var input = Path.GetFullPath(arguments.GetRequiredString("input"));
        var log = arguments.GetString("log");
        var timeout = TimeSpan.FromSeconds(arguments.GetInt32("timeout", 60));

        var finding = await RunAsync(kind, input, log == null ? null : Path.GetFullPath(log), timeout, CancellationToken.None);
        if (finding == null)
        {
            Console.WriteLine("The input does not reproduce the issue");
            return 0;
        }

        Console.WriteLine(JsonSerializer.Serialize(finding, FindingStore.JsonOptions));
        return 1;
    }

    /// <summary>Finds the analyzer responsible for the input, or returns <see langword="null" /> when the input does not reproduce the issue.</summary>
    public static async Task<Finding?> RunAsync(string kind, string input, string? log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (kind == "timeout")
            return await FindAnalyzerThatDoesNotCompleteAsync(input, timeout, cancellationToken);

        var output = log != null && File.Exists(log) ? await File.ReadAllTextAsync(log, cancellationToken) : "";
        if (StackTraceParser.GetAnalyzerFrames(output).IsEmpty)
        {
            var result = await ChildProcess.RunHarnessAsync(["repro", input], timeout, cancellationToken);
            if (result.TimedOut)
                return await FindAnalyzerThatDoesNotCompleteAsync(input, timeout, cancellationToken);

            if (!result.IsHarnessCrash)
                return null;

            output = result.Output;
        }

        var frames = StackTraceParser.GetAnalyzerFrames(output);
        var analyzer = Components.FindByStackFrames(frames);
        return new Finding
        {
            Kind = FindingKind.Crash,
            Component = analyzer == null ? UnknownComponent : Components.GetName(analyzer),
            DiagnosticIds = analyzer == null ? [] : Components.GetDiagnosticIds(analyzer),
            Message = GetCrashReason(output),
            StackTrace = StackTraceParser.GetFrameLines(output, 100),
            Frames = frames,
        };
    }

    /// <summary>Replays the input with groups of analyzers, keeping the group that does not complete, until one analyzer remains.</summary>
    private static async Task<Finding?> FindAnalyzerThatDoesNotCompleteAsync(string input, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var allResult = await ChildProcess.RunHarnessAsync(["repro", input], timeout, cancellationToken);
        if (!allResult.TimedOut)
            return null;

        var candidates = new List<DiagnosticAnalyzer>(Components.Analyzers);
        var groupCount = Math.Max(2, Environment.ProcessorCount);
        while (candidates.Count > 1)
        {
            var groups = Split(candidates, groupCount);
            var runs = new List<Task<ChildProcessResult>>(groups.Count);
            foreach (var group in groups)
                runs.Add(ChildProcess.RunHarnessAsync(["repro", "--only", JoinNames(group), input], timeout, cancellationToken));

            var results = await Task.WhenAll(runs);
            var index = Array.FindIndex(results, static result => result.TimedOut);

            // The analyzers only hang together, or the hang is not deterministic
            candidates = index < 0 ? [] : groups[index];
        }

        var analyzer = candidates.Count == 1 ? candidates[0] : null;
        return new Finding
        {
            Kind = FindingKind.Timeout,
            Component = analyzer == null ? UnknownComponent : Components.GetName(analyzer),
            DiagnosticIds = analyzer == null ? [] : Components.GetDiagnosticIds(analyzer),
            Message = $"The input is not analyzed within {timeout.TotalSeconds:F0} seconds",
        };
    }

    private static string? GetCrashReason(string output)
    {
        if (output.Contains("Stack overflow", StringComparison.Ordinal))
            return "stack overflow";

        if (output.Contains("OutOfMemoryException", StringComparison.Ordinal))
            return "out of memory";

        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("Unhandled exception.", StringComparison.Ordinal))
                return line.Trim();
        }

        return null;
    }

    private static string JoinNames(List<DiagnosticAnalyzer> analyzers)
    {
        var names = new List<string>(analyzers.Count);
        foreach (var analyzer in analyzers)
            names.Add(Components.GetName(analyzer));

        return string.Join(',', names);
    }

    private static List<List<DiagnosticAnalyzer>> Split(List<DiagnosticAnalyzer> analyzers, int groupCount)
    {
        var groupSize = (analyzers.Count + groupCount - 1) / groupCount;
        var groups = new List<List<DiagnosticAnalyzer>>();
        for (var start = 0; start < analyzers.Count; start += groupSize)
            groups.Add(analyzers.GetRange(start, Math.Min(groupSize, analyzers.Count - start)));

        return groups;
    }
}
