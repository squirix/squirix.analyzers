using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Squirix.Analyzers.Fuzz;

/// <summary>
/// Saves the inputs that reproduce a finding as <c>&lt;signature&gt;/input-&lt;hash&gt;.cs</c>, each one with the details of the finding
/// it produces in <c>&lt;signature&gt;/input-&lt;hash&gt;.json</c> (the stack trace depends on the input).
/// <c>&lt;signature&gt;/finding.json</c> is the finding of the first input.
/// Several workers save to the same folder, so the files are only created, never updated.
/// </summary>
internal static class FindingStore
{
    public const string FindingFileName = "finding.json";

    public const string MinimizedInputFileName = "minimized.cs";

    // The smallest inputs are kept, as they are the easiest to understand and to minimize
    private const int MaxInputsPerFinding = 5;

    private static readonly Dictionary<string, int> SmallestInputLengths = new(StringComparer.Ordinal);

    private static readonly Lock SmallestInputLengthsLock = new();

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static JsonSerializerOptions CompactJsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The file that contains the finding produced by an input.</summary>
    public static string GetFindingPath(string inputPath) => Path.ChangeExtension(inputPath, ".json");

    /// <summary>The inputs of a finding, the smallest first.</summary>
    public static List<FileInfo> GetInputs(string findingDirectory)
    {
        var inputs = new List<FileInfo>();
        foreach (var path in Directory.EnumerateFiles(findingDirectory, "input-*.cs"))
            inputs.Add(new FileInfo(path));

        inputs.Sort(static (left, right) => left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left.Name, right.Name));
        return inputs;
    }

    public static string? GetSmallestInput(string findingDirectory)
    {
        var inputs = GetInputs(findingDirectory);
        return inputs.Count == 0 ? null : inputs[0].FullName;
    }

    public static void Save(string directory, Finding finding, string input)
    {
        lock (SmallestInputLengthsLock)
        {
            if (SmallestInputLengths.TryGetValue(finding.Signature, out var length) && length <= input.Length)
                return;

            SmallestInputLengths[finding.Signature] = input.Length;
        }

        var findingDirectory = Path.Combine(directory, finding.Signature);
        _ = Directory.CreateDirectory(findingDirectory);
        var json = JsonSerializer.Serialize(finding, JsonOptions);
        var inputPath = Path.Combine(findingDirectory, $"input-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))}.cs");
        CreateFileIfMissing(Path.Combine(findingDirectory, FindingFileName), json);
        CreateFileIfMissing(GetFindingPath(inputPath), json);
        CreateFileIfMissing(inputPath, input);

        var inputs = GetInputs(findingDirectory);
        for (var index = MaxInputsPerFinding; index < inputs.Count; index++)
        {
            try
            {
                File.Delete(inputs[index].FullName);
                File.Delete(GetFindingPath(inputs[index].FullName));
            }
            catch (IOException)
            {
                // Another worker deleted it
            }
        }
    }

    /// <summary>Saves the finding produced by the minimized input of a finding.</summary>
    public static void SaveMinimized(string findingDirectory, Finding finding) =>
        File.WriteAllText(GetFindingPath(Path.Combine(findingDirectory, MinimizedInputFileName)), JsonSerializer.Serialize(finding, JsonOptions), Utf8NoBom);

    private static void CreateFileIfMissing(string path, string content)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, Utf8NoBom);
            writer.Write(content);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another worker saved the same file
        }
    }
}
