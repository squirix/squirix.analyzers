using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Squirix.Analyzers.Fuzz;

/// <summary>
/// Creates the seed corpus from the code snippets of the tests, which are raw string literals, and the libFuzzer dictionary from the C# tokens.
/// Every other snippet starts with the <c>//!</c> options of the rules, found in the string literals of the rules, with edge values.
/// </summary>
internal static partial class CorpusExtractor
{
    private const int MinSnippetLength = 20;

    // Common identifiers of the APIs the rules know about, so the mutations can produce code that the rules analyze
    private static readonly string[] DictionaryIdentifiers =
    [
        "Task", "Task<int>", "ValueTask", "ValueTask<bool>", "ContinueWith", "Unwrap", "Run", "Func<Task>", "Action", "CancellationToken",
        "OperationCanceledException", "TaskCanceledException", "IsCancellationRequested", "IDisposable", "IAsyncDisposable", "Dispose", "DisposeAsync",
        "disposing", "_disposed", "Interlocked", "Exchange", "CompareExchange", "Volatile", "Read", "Write", "ArgumentNullException", "ThrowIfNull",
        "ArgumentException", "ThrowIfNullOrEmpty", "ArgumentOutOfRangeException", "ThrowIfNegative", "TimeSpan", "Zero", "TimeProvider", "System",
        "ILogger", "ILogger<T>", "NullLogger", "Instance", "GrpcChannel", "GrpcChannelOptions", "HttpHandler", "DisposeHttpClient", "HttpClientHandler",
        "TestContext", "Current", "CancellationTokenSource", "Assert", "Throws", "ThrowsAsync", "IsTrue", "Equals", "ReferenceEquals", "TryParse",
        "TryGetValue", "out", "readonly struct", "record struct", "ref struct", "in", "ref readonly", "~C()", "partial", "global using", "using static",
    ];

    private static readonly string[] OptionValues = ["1", "2", "0", "-1", "100", "2147483647", "2147483648", "x"];

    public static int Extract(string testsDirectory, string rulesDirectory, string outputDirectory, string dictionaryPath, int shardCount)
    {
        var snippets = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(testsDirectory, "*.cs", SearchOption.AllDirectories))
            foreach (var token in ParseTokens(file))
            {
                if (!token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) && !token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
                    continue;

                var snippet = token.ValueText;
                if (snippet.Length >= MinSnippetLength && snippet.AsSpan().ContainsAny('{', ';'))
                    _ = snippets.TryAdd(Hash(snippet), snippet);
            }

        var optionKeys = FindOptionKeys(rulesDirectory);
        var seeds = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var snippet in snippets.Values)
        {
            var seed = index % 2 == 1 && optionKeys.Count > 0 ? GetOptionsHeader(optionKeys, index / 2) + snippet : snippet;
            _ = seeds.TryAdd(Hash(seed), seed);
            index++;
        }

        index = 0;
        foreach (var (hash, seed) in seeds)
        {
            var shardDirectory = Path.Combine(outputDirectory, $"shard-{(index % shardCount).ToString(CultureInfo.InvariantCulture)}");
            _ = Directory.CreateDirectory(shardDirectory);
            File.WriteAllText(Path.Combine(shardDirectory, hash), seed, new UTF8Encoding(false));
            index++;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(dictionaryPath)!);
        File.WriteAllLines(dictionaryPath, GetDictionaryEntries(optionKeys));
        return seeds.Count;
    }

    public static int RunCommand(CommandLineArguments arguments)
    {
        var count = Extract(
            Path.GetFullPath(arguments.GetRequiredString("tests")),
            Path.GetFullPath(arguments.GetRequiredString("rules")),
            Path.GetFullPath(arguments.GetRequiredString("output")),
            Path.GetFullPath(arguments.GetRequiredString("dictionary")),
            arguments.GetInt32("shards", 1));

        Console.WriteLine($"{count} seeds extracted");
        return 0;
    }

    /// <summary>The keys of the .editorconfig options of the rules, such as <c>SQR0002.max_methods_per_type</c>.</summary>
    private static SortedSet<string> FindOptionKeys(string rulesDirectory)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(rulesDirectory, "*.cs", SearchOption.AllDirectories))
            foreach (var token in ParseTokens(file))
            {
                if (token.IsKind(SyntaxKind.StringLiteralToken) && OptionKeyRegex().IsMatch(token.ValueText))
                    _ = keys.Add(token.ValueText);
            }

        return keys;
    }

    // https://llvm.org/docs/LibFuzzer.html#dictionaries
    private static string FormatDictionaryEntry(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b is (byte)'"' or (byte)'\\')
                _ = builder.Append('\\').Append((char)b);
            else if (b is >= 0x20 and < 0x7F)
                _ = builder.Append((char)b);
            else
                _ = builder.Append(CultureInfo.InvariantCulture, $"\\x{b:X2}");
        }

        return builder.Append('"').ToString();
    }

    private static List<string> GetDictionaryEntries(SortedSet<string> optionKeys)
    {
        var entries = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kinds in new[] { SyntaxFacts.GetKeywordKinds(), SyntaxFacts.GetContextualKeywordKinds(), SyntaxFacts.GetPunctuationKinds() })
            foreach (var kind in kinds)
            {
                var text = SyntaxFacts.GetText(kind);
                if (text.Length > 0)
                    _ = entries.Add(text);
            }

        foreach (var text in new[] { "\"\"\"", "$\"", "@\"", "#nullable enable", "#pragma warning disable", "#if DEBUG", "#else", "#endif", "///", InputOptions.OptionPrefix + " " })
            _ = entries.Add(text);

        foreach (var identifier in DictionaryIdentifiers)
            _ = entries.Add(identifier);

        foreach (var key in optionKeys)
            _ = entries.Add(key);

        var lines = new List<string>(entries.Count);
        foreach (var entry in entries)
            lines.Add(FormatDictionaryEntry(entry));

        return lines;
    }

    private static string GetOptionsHeader(SortedSet<string> optionKeys, int variant)
    {
        var builder = new StringBuilder();
        var offset = 0;
        foreach (var key in optionKeys)
        {
            _ = builder.Append(InputOptions.OptionPrefix).Append(' ').Append(key).Append(" = ").Append(OptionValues[(variant + offset) % OptionValues.Length]).Append('\n');
            offset++;
        }

        return builder.ToString();
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(@"^SQR[0-9]{4}\.[a-z_]+$", RegexOptions.None, 10_000)]
    private static partial Regex OptionKeyRegex();

    private static IEnumerable<SyntaxToken> ParseTokens(string file) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(file), Components.ParseOptions).GetRoot().DescendantTokens();
}
