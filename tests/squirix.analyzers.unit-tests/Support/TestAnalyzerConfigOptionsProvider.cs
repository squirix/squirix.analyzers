using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squirix.Analyzers.UnitTests.Support;

/// <summary>
/// Serves a fixed set of .editorconfig-style options to every syntax tree, so analyzer
/// tests can exercise configurable thresholds.
/// </summary>
internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly ImmutableDictionary<string, string> _options;

    public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> options)
    {
        _options = options;
    }

    public override AnalyzerConfigOptions GlobalOptions => new TestAnalyzerConfigOptions(_options);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> _options;

        public TestAnalyzerConfigOptions(ImmutableDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_options.TryGetValue(key, out var raw))
            {
                value = raw;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
