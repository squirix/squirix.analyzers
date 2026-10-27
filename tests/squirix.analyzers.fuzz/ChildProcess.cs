using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squirix.Analyzers.Fuzz;

internal static class ChildProcess
{
    // Only the beginning and the end of the output of libFuzzer matter, so the output in memory is bounded
    private const int MaxOutputLength = 1_000_000;

    /// <summary>The command that starts the harness: the dotnet host and the harness assembly, or the apphost.</summary>
    public static (string FileName, string[] Arguments) GetHarnessCommand()
    {
        var processPath = Environment.ProcessPath!;
        return string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? (processPath, [typeof(ChildProcess).Assembly.Location])
            : (processPath, []);
    }

    /// <summary>Runs a process until it exits or the timeout expires, then kills what remains of it.</summary>
    /// <param name="fileName">The program to run.</param>
    /// <param name="arguments">The arguments of the program.</param>
    /// <param name="environment">The environment variables to set, or to remove when their value is null.</param>
    /// <param name="outputFile">The file to copy the output to.</param>
    /// <param name="timeout">How long the process may run.</param>
    /// <param name="isolateProcessGroup">
    /// Start the process in a new process group (Linux only), and kill the whole group when the process exits. libFuzzer does not stop the harness
    /// when an input times out, so the harness keeps running the input forever, and keeps the redirected output open, after libFuzzer exits.
    /// </param>
    /// <param name="cancellationToken">Stops waiting for the process.</param>
    public static async Task<ChildProcessResult> RunAsync(string fileName, List<string> arguments, Dictionary<string, string?>? environment, string? outputFile,
        TimeSpan timeout, bool isolateProcessGroup, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(isolateProcessGroup ? "setsid" : fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // setsid does not fork, as the processes started by .NET are not process group leaders, so the id of the process is the id of the new process group
        if (isolateProcessGroup)
            startInfo.ArgumentList.Add(fileName);

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (environment != null)
        {
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;
        }

        using var process = new Process();
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;
        var output = new StringBuilder();
        var outputLock = new Lock();
        await using var writer = outputFile == null ? null : new StreamWriter(outputFile, false, Encoding.UTF8) { AutoFlush = true };

        void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;

            lock (outputLock)
            {
                if (output.Length < MaxOutputLength)
                    _ = output.AppendLine(e.Data);

                writer?.WriteLine(e.Data);
            }
        }

        // Unlike WaitForExitAsync, the Exited event does not wait for the end of the redirected output
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        process.OutputDataReceived += OnData;
        process.ErrorDataReceived += OnData;
        _ = process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        try
        {
            await exited.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }
        finally
        {
            if (isolateProcessGroup)
                KillProcessGroup(process.Id);
            else
                KillProcessTree(process);
        }

        // The redirected output ends when all the processes that inherited it exit
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // Some process of the tree keeps the output open, the output read so far is enough
        }

        lock (outputLock)
            return new ChildProcessResult(timedOut ? null : process.ExitCode, timedOut, output.ToString());
    }

    /// <summary>Runs the harness in a new process, so a stack overflow or a hang does not stop the current process.</summary>
    public static Task<ChildProcessResult> RunHarnessAsync(List<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var (fileName, prefix) = GetHarnessCommand();
        var allArguments = new List<string>(prefix);
        allArguments.AddRange(arguments);
        return RunAsync(fileName, allArguments, null, null, timeout, false, cancellationToken);
    }

    private static void KillProcessGroup(int processGroupId)
    {
        // A negative id sends the signal to all the processes of the group
        var startInfo = new ProcessStartInfo("kill") { UseShellExecute = false };
        startInfo.ArgumentList.Add("-KILL");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add((-processGroupId).ToString(CultureInfo.InvariantCulture));
        using var kill = Process.Start(startInfo)!;
        kill.WaitForExit();
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // The process already exited
        }
    }
}
