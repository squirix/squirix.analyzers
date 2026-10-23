using System.Text.Json.Serialization;

namespace Squirix.Analyzers.Fuzz;

[JsonConverter(typeof(JsonStringEnumConverter<FindingKind>))]
internal enum FindingKind
{
    /// <summary>An analyzer throws (AD0001).</summary>
    AnalyzerException,

    /// <summary>The process crashes, e.g. on a stack overflow.</summary>
    Crash,

    /// <summary>An analyzer does not complete.</summary>
    Timeout,

    /// <summary>
    /// Roslyn throws while it compiles the input or runs an analyzer, and no frame of the analyzers is in the stack trace.
    /// These are not reported, as they are rarely defects of the rules.
    /// </summary>
    RoslynException,

    /// <summary>The harness itself throws, outside of an analyzer.</summary>
    HarnessException,
}
