using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.Fuzz;

/// <summary>Serves the options of an input to every syntax tree, as an .editorconfig section that matches all the files.</summary>
internal sealed class InputOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly ConfigOptions _options;

    public InputOptionsProvider(ImmutableDictionary<string, string> options)
    {
        _options = new ConfigOptions(options);
    }

    public override AnalyzerConfigOptions GlobalOptions => _options;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;

    private sealed class ConfigOptions : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> _options;

        public ConfigOptions(ImmutableDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => _options.TryGetValue(key, out value);
    }
}
