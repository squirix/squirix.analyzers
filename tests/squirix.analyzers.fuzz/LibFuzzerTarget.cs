using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SharpFuzz;

namespace Squirix.Analyzers.Fuzz;

/// <summary>
/// The function libFuzzer calls for each input. The findings are saved instead of being reported to libFuzzer as crashes,
/// as libFuzzer stops on the first crash. Only the inputs that kill the process or that do not complete stop libFuzzer.
/// </summary>
internal sealed class LibFuzzerTarget
{
    private const int MaxInputLength = 64 * 1024;

    private readonly string? _currentInputFile = HarnessEnvironment.CurrentInputFile;

    private readonly AnalysisEngine _engine;

    private readonly string? _findingsDirectory = HarnessEnvironment.FindingsDirectory;

    private readonly string? _targetSignature = HarnessEnvironment.TargetSignature;

    private LibFuzzerTarget()
    {
        _engine = new AnalysisEngine(ComponentFilter.Create([], HarnessEnvironment.GetSuppressedComponents()));
    }

    public static void RunLibFuzzer()
    {
        // The instrumented analyzers must not run before libFuzzer.Run allocates the coverage buffer, which includes their static constructors
        LibFuzzerTarget? target = null;
        Fuzzer.LibFuzzer.Run(data => (target ??= new LibFuzzerTarget()).Execute(data));
    }

    private void Execute(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxInputLength)
            return;

        if (_currentInputFile != null)
            File.WriteAllBytes(_currentInputFile, data);

        var source = Encoding.UTF8.GetString(data);
        var findings = _engine.RunAsync(source).GetAwaiter().GetResult();
        if (findings.Count == 0)
            return;

        // Under load, Roslyn rarely throws on an input that it analyzes the next time, so a finding is kept only when the input reproduces it
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in _engine.RunAsync(source).GetAwaiter().GetResult())
            _ = signatures.Add(finding.Signature);

        foreach (var finding in findings)
        {
            if (!signatures.Contains(finding.Signature))
                continue;

            if (_targetSignature != null)
            {
                // Report the finding to libFuzzer, so it can minimize the input
                if (finding.Signature == _targetSignature)
                    throw new FindingReportedException(finding);
            }
            else if (_findingsDirectory != null)
            {
                FindingStore.Save(_findingsDirectory, finding, source);
            }
        }
    }
}
