namespace Squirix.Analyzers;

/// <summary>
/// Shared thresholds for Squirix naming analyzers (SQR0004–SQR0006).
/// SQR0007 (type namespace prefix) has no numeric threshold.
/// SQR0002 and SQR0003 read their limits from .editorconfig options instead.
/// </summary>
internal static class AnalyzerLimits
{
    /// <summary>Field name length (SQR0006).</summary>
    internal const int MaxFieldNameLength = 40;

    /// <summary>Method simple name length; property accessors subtract 4 (SQR0005).</summary>
    internal const int MaxMethodNameLength = 40;

    /// <summary>Type simple name length (SQR0004).</summary>
    internal const int MaxTypeNameLength = 40;
}
