using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Squirix.Analyzers.Fuzz;

/// <summary>A defect found by the fuzzer. The findings with the same <see cref="Signature" /> are the same defect.</summary>
internal sealed class Finding
{
    private const string RoslynComponent = "Microsoft.CodeAnalysis";

    // The frames of the runtime above the code that throws, such as the helpers of a collection, are fewer than this
    private const int MaxOwnerFrameIndex = 32;

    private const int SignatureFrameCount = 5;

    /// <summary>The full name of the analyzer, or of the component that throws when it is not an analyzer.</summary>
    public required string Component { get; init; }

    /// <summary>The rules of the analyzer.</summary>
    public ImmutableArray<string> DiagnosticIds { get; init; } = [];

    public string? ExceptionType { get; init; }

    /// <summary>The frames of the analyzers, normalized so they do not depend on the compiler-generated names.</summary>
    public ImmutableArray<string> Frames { get; init; } = [];

    public required FindingKind Kind { get; init; }

    public string? Message { get; init; }

    public string Signature => field ??= ComputeSignature();

    public string? StackTrace { get; init; }

    public string Title => field ??= ComputeTitle();

    public static Finding FromException(FindingKind kind, string component, ImmutableArray<string> diagnosticIds, Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } or TargetInvocationException { InnerException: not null })
            exception = exception.InnerException!;

        var text = exception.ToString();
        var frames = StackTraceParser.GetAnalyzerFrames(text);

        // The same defect of Roslyn is reached through every analyzer that asks the semantic model about the code, so the frames of Roslyn
        // identify it, rather than the analyzer
        return kind == FindingKind.RoslynException || (kind == FindingKind.AnalyzerException && IsThrownByRoslyn(exception, frames, text))
            ? new Finding
            {
                Kind = FindingKind.RoslynException,
                Component = RoslynComponent,
                ExceptionType = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = text,
                Frames = StackTraceParser.GetTopFrames(text, SignatureFrameCount),
            }
            : new Finding
            {
                Kind = kind,
                Component = component,
                DiagnosticIds = diagnosticIds,
                ExceptionType = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = text,
                Frames = frames,
            };
    }

    /// <summary>
    /// Whether Roslyn throws by itself, rather than because of the analyzer: no frame of the analyzers is in the stack trace, or the first frame
    /// outside the runtime, the code that owns the throw, is in Roslyn. An analyzer that indexes a list out of range throws from a frame of the runtime,
    /// and owns the exception. An <see cref="ArgumentException" /> that Roslyn throws itself validates an argument that the analyzer passed to its API,
    /// whereas one of a collection used by Roslyn, such as a duplicate key, is a defect of Roslyn. The method that throws is the one of
    /// <see cref="Exception.TargetSite" />, as the stack trace hides the throw helpers of the runtime, such as the one of a duplicate key.
    /// </summary>
    private static bool IsThrownByRoslyn(Exception exception, ImmutableArray<string> analyzerFrames, string text)
    {
        if (analyzerFrames.IsEmpty)
            return true;

        var topFrames = StackTraceParser.GetTopFrames(text, MaxOwnerFrameIndex);
        var owner = 0;
        while (owner < topFrames.Length && topFrames[owner].StartsWith("System.", StringComparison.Ordinal))
            owner++;

        return owner < topFrames.Length && topFrames[owner].StartsWith(RoslynComponent + ".", StringComparison.Ordinal)
            && (exception is not ArgumentException || !IsDeclaredInRoslyn(exception.TargetSite));
    }

    private static bool IsDeclaredInRoslyn(MethodBase? method) =>
        method?.DeclaringType?.Assembly.GetName().Name is { } name && name.StartsWith(RoslynComponent, StringComparison.Ordinal);

    private static string GetShortFrame(string frame)
    {
        var index = frame.LastIndexOf('.');
        if (index > 0)
            index = frame.LastIndexOf('.', index - 1);

        return frame[(index + 1)..];
    }

    private static string GetShortName(string name) => name[(name.LastIndexOfAny(['.', '+']) + 1)..];

    private string ComputeSignature()
    {
        var parts = new StringBuilder().Append(Kind).Append('\n').Append(Component);
        if (Kind != FindingKind.Timeout)
        {
            _ = parts.Append('\n').Append(ExceptionType);
            for (var index = 0; index < Frames.Length && index < SignatureFrameCount; index++)
                _ = parts.Append('\n').Append(Frames[index]);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString()));
        return Convert.ToHexStringLower(hash)[..16];
    }

    private string ComputeTitle()
    {
        var rules = DiagnosticIds.Length switch
        {
            0 => "",
            <= 3 => string.Join(", ", DiagnosticIds) + " ",
            _ => string.Join(", ", DiagnosticIds[0], DiagnosticIds[1], DiagnosticIds[2]) + ", … ",
        };

        var location = Frames.IsEmpty ? "" : " in " + GetShortFrame(Frames[0]);
        var description = Kind switch
        {
            FindingKind.Crash => $"crashes the process{(Message == null ? "" : ": " + Message)}{location}",
            FindingKind.Timeout => "does not complete",
            _ => $"throws {GetShortName(ExceptionType ?? "an exception")}{location}",
        };

        return $"[Fuzzing] {rules}({GetShortName(Component)}) {description}";
    }
}
