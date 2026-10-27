using System;
using System.Threading.Tasks;

namespace Squirix.Analyzers.Fuzz;

internal static class Program
{
    private const string Usage = """
                                 Usage:
                                   Squirix.Analyzers.Fuzz
                                       Run under libFuzzer (see eng/run.ps1)
                                   Squirix.Analyzers.Fuzz repro [--only <type>[,<type>...]] [--json] <file|directory>...
                                       Run the analyzers on the inputs and print the findings
                                   Squirix.Analyzers.Fuzz triage --kind <crash|timeout> --input <file> [--log <file>] [--timeout <seconds>]
                                       Find the analyzer that crashes the process or does not complete
                                   Squirix.Analyzers.Fuzz corpus --tests <directory> --rules <directory> --output <directory> --dictionary <file> [--shards <count>]
                                       Extract the code snippets of the tests to use as the seed corpus
                                   Squirix.Analyzers.Fuzz campaign --libfuzzer <path> --tests <directory> --rules <directory> --output <directory> [--duration <minutes>]
                                                                   [--workers <count>] [--timeout <seconds>] [--previous-corpus <directory>] [--merge-minutes <minutes>]
                                                                   [--minimize-minutes <minutes>]
                                       Fuzz with several libFuzzer workers, triage the crashes and minimize the findings
                                 """;

    public static async Task<int> Main(string[] args)
    {
        // libFuzzer starts the harness without arguments, and sends the inputs through shared memory
        if (args.Length == 0)
        {
            LibFuzzerTarget.RunLibFuzzer();
            return 0;
        }

        // The analyzers are instrumented when the harness runs from the output of eng/run.ps1,
        // and the instrumentation writes to a buffer that only libFuzzer allocates
        SharpFuzzTrace.EnsureInitialized();

        var arguments = CommandLineArguments.Parse(args.AsSpan(1));
        return args[0] switch
        {
            "repro" => await ReproCommand.RunAsync(arguments),
            "triage" => await Triage.RunCommandAsync(arguments),
            "corpus" => CorpusExtractor.RunCommand(arguments),
            "campaign" => await Campaign.RunAsync(arguments),
            _ => PrintUsage(),
        };
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
