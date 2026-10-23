using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Squirix.Analyzers.Fuzz;

/// <summary>
/// A fuzzing session: starts one libFuzzer worker per core until the deadline, triages the inputs that stop a worker,
/// then merges the corpus for the next session and minimizes the inputs of the findings.
/// </summary>
/// <remarks>
/// The output folder contains:
/// <list type="bullet">
///   <item><c>seeds/</c>: the snippets of the tests, and <c>csharp.dict</c>, the libFuzzer dictionary</item>
///   <item><c>corpus/</c>: the inputs discovered by the workers, shared by all of them</item>
///   <item><c>corpus-merged/</c>: the minimal corpus that covers the same code as the corpus and the previous corpus</item>
///   <item><c>findings/</c>: one folder per finding, see <see cref="FindingStore" /></item>
///   <item><c>artifacts/</c>, <c>logs/</c>, <c>state/</c>: the crashes saved by libFuzzer, the output of each run, and the files of the workers</item>
/// </list>
/// </remarks>
internal sealed class Campaign
{
    private const int MaxConsecutiveUnexplainedStops = 5;

    private const int MaxInputLength = 16 * 1024;

    private readonly string _libFuzzer;

    private readonly string _output;

    private readonly string? _previousCorpus;

    private readonly Lock _suppressedComponentsLock = new();

    private readonly int _timeoutSeconds;

    private Campaign(string libFuzzer, string output, string? previousCorpus, int timeoutSeconds)
    {
        _libFuzzer = libFuzzer;
        _output = output;
        _previousCorpus = previousCorpus != null && Directory.Exists(previousCorpus) && Directory.EnumerateFiles(previousCorpus).GetEnumerator().MoveNext() ? previousCorpus : null;
        _timeoutSeconds = timeoutSeconds;
    }

    private string ArtifactsDirectory => Path.Combine(_output, "artifacts");

    private string CorpusDirectory => Path.Combine(_output, "corpus");

    private string DictionaryPath => Path.Combine(SeedsDirectory, "csharp.dict");

    private string FindingsDirectory => Path.Combine(_output, "findings");

    private string LogsDirectory => Path.Combine(_output, "logs");

    private string MergedCorpusDirectory => Path.Combine(_output, "corpus-merged");

    private string SeedsDirectory => Path.Combine(_output, "seeds");

    private string StateDirectory => Path.Combine(_output, "state");

    private string SuppressedComponentsFile => Path.Combine(StateDirectory, "suppressed.txt");

    public static async Task<int> RunAsync(CommandLineArguments arguments)
    {
        var previousCorpus = arguments.GetString("previous-corpus");
        var campaign = new Campaign(
            Path.GetFullPath(arguments.GetRequiredString("libfuzzer")),
            Path.GetFullPath(arguments.GetRequiredString("output")),
            previousCorpus == null ? null : Path.GetFullPath(previousCorpus),
            arguments.GetInt32("timeout", 30));

        var workerCount = arguments.GetInt32("workers", Environment.ProcessorCount);
        var duration = TimeSpan.FromMinutes(arguments.GetInt32("duration", 60));
        foreach (var directory in new[] { campaign.CorpusDirectory, campaign.FindingsDirectory, campaign.ArtifactsDirectory, campaign.LogsDirectory, campaign.StateDirectory })
            _ = Directory.CreateDirectory(directory);

        var seedCount = CorpusExtractor.Extract(Path.GetFullPath(arguments.GetRequiredString("tests")), Path.GetFullPath(arguments.GetRequiredString("rules")),
            campaign.SeedsDirectory, campaign.DictionaryPath, workerCount);
        Log($"{seedCount} seeds, {workerCount} workers, fuzzing for {duration}");

        var deadline = DateTimeOffset.UtcNow + duration;
        var workers = new List<Task>(workerCount);
        for (var index = 0; index < workerCount; index++)
            workers.Add(campaign.RunWorkerAsync(index, deadline));

        await Task.WhenAll(workers);
        await campaign.MergeCorpusAsync(TimeSpan.FromMinutes(arguments.GetInt32("merge-minutes", 30)));
        await campaign.MinimizeFindingsAsync(TimeSpan.FromMinutes(arguments.GetInt32("minimize-minutes", 30)));
        campaign.PrintSummary();
        return 0;
    }

    private static string CopyCurrentInput(string currentInputFile, string destination)
    {
        if (File.Exists(currentInputFile))
            File.Copy(currentInputFile, destination, true);

        return destination;
    }

    private static string? FindFile(string directory, string pattern)
    {
        foreach (var path in Directory.EnumerateFiles(directory, pattern))
            return path;

        return null;
    }

    private static IEnumerable<string> GetHarnessArguments()
    {
        // libfuzzer-dotnet starts the harness with "<target_path> <target_arg>", so the harness must be a single file
        var (fileName, arguments) = ChildProcess.GetHarnessCommand();
        yield return $"--target_path={fileName}";
        if (arguments.Length > 0)
            yield return $"--target_arg={arguments[0]}";
    }

    private static void Log(string message) => Console.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {message}");

    private static FindingKind? ReadFindingKind(string findingDirectory)
    {
        var path = Path.Combine(findingDirectory, FindingStore.FindingFileName);
        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return Enum.TryParse<FindingKind>(document.RootElement.GetProperty("kind").GetString(), false, out var kind) ? kind : null;
    }

    private List<string> GetCommonArguments() => [$"-timeout={_timeoutSeconds}", $"-max_len={MaxInputLength}", $"-dict={DictionaryPath}"];

    private Dictionary<string, string?> GetHarnessEnvironment(bool saveFindings) =>
        new(StringComparer.Ordinal)
        {
            [HarnessEnvironment.FindingsDirectoryVariable] = saveFindings ? FindingsDirectory : null,
            [HarnessEnvironment.SuppressedComponentsFileVariable] = SuppressedComponentsFile,

            // Turn the inputs that make an analyzer allocate without limit into OutOfMemoryException,
            // instead of letting the operating system kill the process (4 GB)
            ["DOTNET_GCHeapHardLimit"] = "0x100000000",
        };

    private async Task MergeCorpusAsync(TimeSpan duration)
    {
        _ = Directory.CreateDirectory(MergedCorpusDirectory);
        List<string> arguments = ["-merge=1", .. GetCommonArguments(), $"-artifact_prefix={Path.Combine(ArtifactsDirectory, "merge")}-", .. GetHarnessArguments(), MergedCorpusDirectory, CorpusDirectory];
        if (_previousCorpus != null)
            arguments.Add(_previousCorpus);

        Log("Merging the corpus");
        var result = await ChildProcess.RunAsync(_libFuzzer, arguments, GetHarnessEnvironment(true), Path.Combine(LogsDirectory, "merge.log"), duration, true, CancellationToken.None);
        if (result.ExitCode == 0)
            return;

        Log($"Cannot merge the corpus (exit code: {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "(timeout)"}), keeping all the inputs");
        foreach (var directory in new[] { CorpusDirectory, _previousCorpus })
        {
            if (directory == null)
                continue;

            foreach (var file in Directory.EnumerateFiles(directory))
                File.Copy(file, Path.Combine(MergedCorpusDirectory, Path.GetFileName(file)), true);
        }
    }

    /// <summary>
    /// Asks libFuzzer to minimize the smallest input of each finding: the harness throws for this finding only, so libFuzzer considers the input as a crash.
    /// The crashes and the timeouts are not minimized, as libFuzzer does not consider the death of the harness process as a crash.
    /// </summary>
    private async Task MinimizeFindingsAsync(TimeSpan duration)
    {
        var deadline = DateTimeOffset.UtcNow + duration;
        var engine = new AnalysisEngine(ComponentFilter.Create([], new HashSet<string>(StringComparer.Ordinal)));
        var findingDirectories = new List<string>(Directory.EnumerateDirectories(FindingsDirectory));
        findingDirectories.Sort(StringComparer.Ordinal);
        foreach (var findingDirectory in findingDirectories)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.FromSeconds(30))
            {
                Log("No time left to minimize the other findings");
                return;
            }

            var input = FindingStore.GetSmallestInput(findingDirectory);
            if (ReadFindingKind(findingDirectory) != FindingKind.AnalyzerException || input == null)
                continue;

            var signature = Path.GetFileName(findingDirectory);
            var minimized = Path.Combine(findingDirectory, FindingStore.MinimizedInputFileName);
            var seconds = (int)Math.Min(120, remaining.TotalSeconds);
            List<string> arguments =
            [
                "-minimize_crash=1", $"-max_total_time={seconds}", $"-exact_artifact_path={minimized}", .. GetCommonArguments(),
                $"-artifact_prefix={Path.Combine(StateDirectory, "minimize")}-", .. GetHarnessArguments(), input,
            ];
            var environment = GetHarnessEnvironment(false);
            environment[HarnessEnvironment.TargetSignatureVariable] = signature;

            Log($"Minimizing {signature}");
            _ = await ChildProcess.RunAsync(_libFuzzer, arguments, environment, Path.Combine(LogsDirectory, $"minimize-{signature}.log"), TimeSpan.FromSeconds(seconds + 120), true,
                CancellationToken.None);

            // libFuzzer keeps the inputs that crash, so make sure the minimized input reproduces the same finding
            if (File.Exists(minimized))
                await KeepMinimizedInputAsync(engine, findingDirectory, signature, minimized);
        }
    }

    private static async Task KeepMinimizedInputAsync(AnalysisEngine engine, string findingDirectory, string signature, string minimized)
    {
        foreach (var finding in await engine.RunAsync(Triage.ReadInput(minimized)))
        {
            if (finding.Signature != signature)
                continue;

            FindingStore.SaveMinimized(findingDirectory, finding);
            return;
        }

        Log($"The minimized input of {signature} does not reproduce the finding");
        File.Delete(minimized);
    }

    private void PrintSummary()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var directory in Directory.EnumerateDirectories(FindingsDirectory))
        {
            var kind = ReadFindingKind(directory)?.ToString() ?? "Unknown";
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            total++;
        }

        Log($"{total} findings");
        foreach (var (kind, count) in counts)
            Log($"  {kind}: {count}");
    }

    private async Task RunWorkerAsync(int index, DateTimeOffset deadline)
    {
        var currentInputFile = Path.Combine(StateDirectory, $"worker{index}.input");
        var consecutiveUnexplainedStops = 0;
        for (var run = 0; ; run++)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.FromMinutes(1))
                return;

            var name = $"worker{index}-run{run}";
            var artifactPrefix = $"{Path.Combine(ArtifactsDirectory, name)}-";
            List<string> arguments =
            [
                .. GetCommonArguments(), $"-seed={(index * 1000) + run + 1}", $"-max_total_time={(int)remaining.TotalSeconds}", $"-artifact_prefix={artifactPrefix}",
                "-print_final_stats=1", .. GetHarnessArguments(), CorpusDirectory, Path.Combine(SeedsDirectory, $"shard-{index}"),
            ];
            if (_previousCorpus != null)
                arguments.Add(_previousCorpus);

            var environment = GetHarnessEnvironment(true);
            environment[HarnessEnvironment.CurrentInputFileVariable] = currentInputFile;

            Log($"[{name}] Starting libFuzzer for {remaining}");
            var logFile = Path.Combine(LogsDirectory, $"{name}.log");
            var result = await ChildProcess.RunAsync(_libFuzzer, arguments, environment, logFile, remaining + TimeSpan.FromMinutes(5), true, CancellationToken.None);
            if (result.ExitCode == 0)
                return;

            // libFuzzer saves the inputs that time out or crash, but the harness is another process, so when it dies without reporting
            // the crash to libFuzzer, the input is the last one the harness wrote
            var timeoutArtifact = FindFile(ArtifactsDirectory, $"{name}-timeout-*");
            var crashArtifact = FindFile(ArtifactsDirectory, $"{name}-crash-*");
            var kind = timeoutArtifact != null ? "timeout" : "crash";
            var input = timeoutArtifact ?? crashArtifact ?? CopyCurrentInput(currentInputFile, $"{artifactPrefix}process-crash");

            Log($"[{name}] libFuzzer stopped with exit code {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "(timeout)"}, triaging the {kind} of {input}");
            var finding = File.Exists(input) ? await Triage.RunAsync(kind, input, logFile, TimeSpan.FromSeconds(_timeoutSeconds * 2), CancellationToken.None) : null;
            if (finding == null || Triage.IsUnknown(finding))
            {
                consecutiveUnexplainedStops++;
                Log($"[{name}] Cannot find the analyzer responsible for the {kind} ({consecutiveUnexplainedStops} consecutive)");
                if (consecutiveUnexplainedStops >= MaxConsecutiveUnexplainedStops)
                {
                    Log($"[{name}] Stopping the worker");
                    return;
                }
            }
            else
            {
                consecutiveUnexplainedStops = 0;
            }

            if (finding != null)
                SaveStop(name, finding, input);
        }
    }

    private void SaveStop(string name, Finding finding, string input)
    {
        Log($"[{name}] {finding.Title} ({finding.Signature})");
        FindingStore.Save(FindingsDirectory, finding, Triage.ReadInput(input));
        if (Triage.IsUnknown(finding))
            return;

        // Do not run the analyzer anymore, so the workers find other issues instead of this one again
        lock (_suppressedComponentsLock)
            File.AppendAllLines(SuppressedComponentsFile, [finding.Component]);
    }
}
