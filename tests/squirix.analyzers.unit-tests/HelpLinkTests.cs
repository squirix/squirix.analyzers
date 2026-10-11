using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Squirix.Analyzers.UnitTests;

public sealed class HelpLinkTests
{
    /// <summary>The link names the release tag, so a reader lands on the page that describes the installed version.</summary>
    [Test]
    public async Task EveryRuleLinksToItsPageAtTheReleaseTag()
    {
        var assembly = typeof(TooManyMethodsAnalyzer).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        var pages = FindRulePages();
        var rules = new List<string>();
        var problems = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                continue;

            foreach (var rule in ((DiagnosticAnalyzer)Activator.CreateInstance(type)!).SupportedDiagnostics)
            {
                rules.Add(rule.Id);
                if (rule.HelpLinkUri != $"https://github.com/squirix/squirix.analyzers/blob/v{version}/docs/rules/{rule.Id}.md")
                    problems.Add($"{rule.Id}: unexpected link '{rule.HelpLinkUri}'");

                if (!File.Exists(Path.Combine(pages, rule.Id + ".md")))
                    problems.Add($"{rule.Id}: no page in docs/rules");
            }
        }

        _ = await Assert.That(Regex.IsMatch(version, @"^\d+\.\d+\.\d+")).IsTrue();
        _ = await Assert.That(rules).IsNotEmpty();
        _ = await Assert.That(problems).IsEmpty();
    }

    private static string FindRulePages()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var pages = Path.Combine(directory.FullName, "docs", "rules");
            if (Directory.Exists(pages))
                return pages;
        }

        throw new DirectoryNotFoundException("docs/rules was not found above the test output directory.");
    }
}
