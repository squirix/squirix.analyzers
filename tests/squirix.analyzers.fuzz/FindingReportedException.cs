using System;

namespace Squirix.Analyzers.Fuzz;

/// <summary>Reports a finding to libFuzzer as a crash, so libFuzzer can minimize its input.</summary>
internal sealed class FindingReportedException : Exception
{
    public FindingReportedException()
    {
    }

    public FindingReportedException(string message)
        : base(message)
    {
    }

    public FindingReportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FindingReportedException(Finding finding)
        : base($"{finding.Title} ({finding.Signature})\n{finding.StackTrace}")
    {
    }
}
