using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Squirix.Analyzers.Fuzz;

/// <summary>Runs the analyzers on files without libFuzzer, to reproduce a finding on any OS.</summary>
internal static class ReproCommand
{
    public static async Task<int> RunAsync(CommandLineArguments arguments)
    {
        var only = arguments.GetString("only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var engine = new AnalysisEngine(ComponentFilter.Create(only, HarnessEnvironment.GetSuppressedComponents()));
        var json = arguments.HasFlag("json");
        var findingsDirectory = HarnessEnvironment.FindingsDirectory;

        var files = new List<string>();
        foreach (var path in arguments.Positional)
            AddFiles(files, path);

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No input file");
            return 2;
        }

        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var findingCount = 0;
        var stopwatch = Stopwatch.StartNew();
        foreach (var file in files)
        {
            var source = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            foreach (var finding in await engine.RunAsync(source))
            {
                findingCount++;
                _ = signatures.Add(finding.Signature);
                if (findingsDirectory != null)
                    FindingStore.Save(findingsDirectory, finding, source);

                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { file, finding }, FindingStore.CompactJsonOptions));
                    continue;
                }

                Console.WriteLine($"{file}: {finding.Title} ({finding.Signature})");
                if (files.Count == 1)
                    PrintDetails(finding);
            }
        }

        Console.Error.WriteLine($"{files.Count} inputs, {findingCount} findings, {signatures.Count} distinct findings, {stopwatch.Elapsed.TotalMilliseconds / files.Count:F0} ms per input");
        return findingCount > 0 ? 1 : 0;
    }

    private static void AddFiles(List<string> files, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            files.Add(fullPath);
            return;
        }

        // libFuzzer names the inputs of the corpus after their hash, without extension, and a folder of findings has their details next to the inputs
        var directoryFiles = new List<string>();
        foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                directoryFiles.Add(file);
        }

        directoryFiles.Sort(StringComparer.Ordinal);
        files.AddRange(directoryFiles);
    }

    private static void PrintDetails(Finding finding)
    {
        foreach (var value in new[] { finding.Message, finding.StackTrace })
        {
            if (value == null)
                continue;

            Console.WriteLine(value);
            Console.WriteLine();
        }
    }
}
