using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Tempo publishes TWO dark-theme switches — <c>[data-theme="dark"]</c> and the <c>.tm-dark</c> class
/// (<c>src/Tempo.Blazor/AGENT.md</c>: "Dark mode is activated by <c>[data-theme="dark"]</c> or
/// <c>.tm-dark</c> on a parent element"). A rule written for only one of them ships a fix that half the
/// consumers never receive, and nothing tells them: the component simply keeps its light colours while
/// everything around it turns dark.
/// <para>
/// This drift is invisible to every other kind of test — it is not a missing token, not a wrong ratio and
/// not a DOM difference, it is one absent selector. It also grows silently: the two theming APIs are
/// written by hand in every dark block, so each new dark rule is another chance to forget one.
/// </para>
/// <para>
/// The guard is deliberately repository-wide (every shipped package, scoped <c>.razor.css</c> included) and
/// not limited to the handful of files that were broken when it was written. A guard scoped to today's
/// offenders would freeze the rest of the repo's drift as acceptable.
/// </para>
/// </summary>
public class DarkThemeSelectorParityTests
{
    private const string AttributeSwitch = "[data-theme=\"dark\"]";
    private const string ClassSwitch = ".tm-dark";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex RuleHeader =
        new(@"(?<selector>[^{}]+)\{", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    /// <summary>
    /// Every stylesheet Tempo SHIPS. The demo application is excluded because it is not a package: it owns
    /// its own theming class (<c>.dark</c>) and is free to use whichever switch it likes. Generated bundles
    /// and build output are excluded because they are copies of these same sources.
    /// </summary>
    private static IEnumerable<FileInfo> ShippedStylesheets() =>
        new DirectoryInfo(Path.Combine(ThemeCss.RepositoryRoot().FullName, "src"))
            .EnumerateFiles("*.css", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains(".Demo", StringComparison.Ordinal))
            .Where(file => !file.Name.Contains(".bundled.", StringComparison.Ordinal))
            .OrderBy(file => file.FullName, StringComparer.Ordinal);

    /// <summary>
    /// Selector parts written with <paramref name="present"/> that have no <paramref name="expected"/> twin
    /// in the SAME rule, and how many parts carried <paramref name="present"/> at all.
    /// </summary>
    private static List<string> PartsWithoutTwin(string present, string expected, out int scanned)
    {
        var offenders = new List<string>();
        var total = 0;
        var root = ThemeCss.RepositoryRoot().FullName;

        foreach (var stylesheet in ShippedStylesheets())
        {
            var css = Comment.Replace(File.ReadAllText(stylesheet.FullName), " ");
            foreach (Match rule in RuleHeader.Matches(css))
            {
                var parts = ThemeCss.SelectorParts(rule.Groups["selector"].Value);
                foreach (var part in parts.Where(p => p.Contains(present, StringComparison.Ordinal)
                                                      && !p.Contains(expected, StringComparison.Ordinal)))
                {
                    total++;
                    var twin = part.Replace(present, expected, StringComparison.Ordinal);
                    if (!parts.Contains(twin, StringComparer.Ordinal))
                    {
                        offenders.Add($"{Path.GetRelativePath(root, stylesheet.FullName)}: {part}");
                    }
                }
            }
        }

        scanned = total;
        return offenders;
    }

    [Fact]
    public void EveryDarkAttributeSelector_HasATmDarkClassTwin()
    {
        var offenders = PartsWithoutTwin(AttributeSwitch, ClassSwitch, out var scanned);

        // Non-vacuous: the sweep has to have found dark rules at all. A refactor that renames the switch,
        // or a glob that stops matching, would otherwise leave this permanently and silently green.
        scanned.Should().BeGreaterThan(140,
            "the shipped packages carry a large body of dark rules — a near-empty sweep means the scan broke, "
            + "not that the drift is gone");

        offenders.Should().BeEmpty(
            "both dark switches are public API, so a consumer that toggles the theme with the CLASS must get "
            + "every dark rule the attribute consumer gets");
    }

    /// <summary>
    /// The same obligation in the other direction, and it was NOT hypothetical: the six NotionEditor
    /// database-view stylesheets were written class-first, so 34 dark rules never reached a consumer using
    /// the attribute. A one-directional guard would have declared that drift acceptable forever.
    /// </summary>
    [Fact]
    public void EveryTmDarkClassSelector_HasADataThemeAttributeTwin()
    {
        var offenders = PartsWithoutTwin(ClassSwitch, AttributeSwitch, out var scanned);

        scanned.Should().BeGreaterThan(50,
            "a near-empty sweep means the scan broke, not that every dark rule stopped using the class");

        offenders.Should().BeEmpty(
            "a consumer that toggles the theme with the ATTRIBUTE must get every dark rule the class "
            + "consumer gets");
    }

    /// <summary>
    /// The class twin has to be written into the SAME rule. Splitting it into a second rule elsewhere in the
    /// file would satisfy a naive sweep while letting the two copies drift apart declaration by declaration —
    /// which is the whole failure mode. Checking the count of attribute parts against class parts per file is
    /// the cheap invariant that catches a split.
    /// </summary>
    [Fact]
    public void TheTwoSwitches_AreDeclaredTogether_NotAsParallelRules()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var mismatches = new List<string>();

        foreach (var stylesheet in ShippedStylesheets())
        {
            var css = Comment.Replace(File.ReadAllText(stylesheet.FullName), " ");
            var attribute = 0;
            var klass = 0;

            foreach (Match rule in RuleHeader.Matches(css))
            {
                foreach (var part in ThemeCss.SelectorParts(rule.Groups["selector"].Value))
                {
                    if (part.Contains(AttributeSwitch, StringComparison.Ordinal))
                    {
                        attribute++;
                    }
                    else if (part.Contains(ClassSwitch, StringComparison.Ordinal))
                    {
                        klass++;
                    }
                }
            }

            if (attribute != klass)
            {
                mismatches.Add(
                    $"{Path.GetRelativePath(root, stylesheet.FullName)}: {attribute} attribute vs {klass} class selectors");
            }
        }

        mismatches.Should().BeEmpty(
            "every dark rule must list both switches side by side, so the two theming APIs cannot drift apart");
    }
}
