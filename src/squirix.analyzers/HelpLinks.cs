using System.Reflection;

namespace Squirix.Analyzers;

/// <summary>
/// Builds the address of a rule's documentation page as it was at the release of this build.
/// </summary>
internal static class HelpLinks
{
    private static readonly string Root = $"https://github.com/squirix/squirix.analyzers/blob/{GetRevision()}/docs/rules/";

    internal static string For(string ruleId) => $"{Root}{ruleId}.md";

    /// <summary>
    /// Returns the release tag of this build: its version with a <c>v</c> in front. The informational version may carry the
    /// commit after a <c>+</c>, which is left out.
    /// </summary>
    private static string GetRevision()
    {
        var version = typeof(HelpLinks).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
            return "main";

        var metadata = version!.IndexOf('+');
        return "v" + (metadata < 0 ? version : version.Substring(0, metadata));
    }
}
