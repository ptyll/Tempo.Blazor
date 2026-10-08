using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// N325: a class passed via <c>Class="…"</c> to <see cref="Components.Overlay.TmOverlayPanel"/>
/// lands on an element rendered by THAT component's template — it carries TmOverlayPanel's scope
/// attribute, never the consumer's. A scoped <c>.razor.css</c> rule targeting it therefore never
/// applies (the <c>.tm-notion-notifications__panel</c> width silently fell back to the global
/// <c>.tm-notification-bell__dropdown</c>). The generic ownership sweep cannot see this because
/// the class string sits in the owner's own markup; the panel-class set has to come from the
/// <c>&lt;TmOverlayPanel&gt;</c> tags themselves. Such rules must use <c>::deep</c> or move to a
/// global stylesheet (the N197 pattern).
/// </summary>
public sealed class OverlayPanelScopedCssTests
{
    [Fact]
    public void Repository_Sweep_ReportsNoUndeepOverlayPanelRule()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var findings = OverlayPanelScopedCss.Scan(root);

        // Fail-closed: an empty population means the extractor stopped seeing TmOverlayPanel
        // Class attributes at all — not that the tree is clean.
        OverlayPanelScopedCss.DiscoveredPanelClassCount(root).Should().BeGreaterThan(0,
            "the sweep's denominator comes from <TmOverlayPanel Class=\"…\"> tags in src/; " +
            "zero means the extractor broke, not that nothing needs checking");

        findings.Should().BeEmpty(
            "a scoped rule on a TmOverlayPanel Class never applies — use ::deep or move it to a " +
            "global stylesheet (N197/N325 pattern). Findings: " +
            string.Join("; ", findings.Select(finding => $"{finding.File} :: {finding.Selector}")));
    }

    [Fact]
    public void Detector_UndeepPanelClass_IsReported()
    {
        const string razor = """
            <TmOverlayPanel IsOpen="_open" Class="tm-bell__dropdown tm-fake-panel">
                <p>content</p>
            </TmOverlayPanel>
            """;
        const string css = ".tm-fake-panel { width: 24rem; }";

        var findings = OverlayPanelScopedCss.FindUndeepRules(razor, css, "Fake.razor.css");

        findings.Should().ContainSingle(finding =>
            finding.File == "Fake.razor.css" && finding.Selector.Contains("tm-fake-panel"));
    }

    [Fact]
    public void Detector_DeepRule_IsAccepted()
    {
        const string razor = """
            <TmOverlayPanel IsOpen="_open" Class="tm-fake-panel">
                <p>content</p>
            </TmOverlayPanel>
            """;
        const string css = """
            .tm-owner::deep .tm-fake-panel { width: 24rem; }
            .tm-unrelated { color: red; }
            """;

        var findings = OverlayPanelScopedCss.FindUndeepRules(razor, css, "Fake.razor.css");

        findings.Should().BeEmpty();
    }
}

/// <summary>The N325 detector: scoped rules whose key class is passed to TmOverlayPanel.</summary>
internal static class OverlayPanelScopedCss
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex OverlayPanelTag =
        new(@"<TmOverlayPanel\b[^>]*?>", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex ClassAttribute =
        new(@"\bClass\s*=\s*""([^""]*)""", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ClassName =
        new(@"\.(-?[_a-zA-Z][_a-zA-Z0-9-]*)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex DeepMarker =
        new(@"::deep|:deep\(", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Every selector item whose key class is passed to TmOverlayPanel without ::deep.</summary>
    public static IReadOnlyList<OverlayPanelScopedFinding> FindUndeepRules(
        string razor, string scopedCss, string file)
    {
        var panelClasses = PanelClasses(razor);
        var findings = new List<OverlayPanelScopedFinding>();
        if (panelClasses.Count == 0)
        {
            return findings;
        }

        var stripped = Comment.Replace(scopedCss, " ");
        foreach (Match rule in RuleBlock.Matches(stripped))
        {
            var selector = rule.Groups["selector"].Value;
            if (DeepMarker.IsMatch(selector))
            {
                continue;
            }

            if (ClassName.Matches(selector).Any(match => panelClasses.Contains(match.Groups[1].Value)))
            {
                findings.Add(new OverlayPanelScopedFinding(file, selector.Trim()));
            }
        }

        return findings;
    }

    /// <summary>Number of class tokens passed to TmOverlayPanel anywhere under src/ — the sweep's
    /// denominator, independent of which files are flagged.</summary>
    public static int DiscoveredPanelClassCount(string repositoryRoot) =>
        RazorFiles(repositoryRoot).SelectMany(path => PanelClasses(File.ReadAllText(path))).Count();

    public static IReadOnlyList<OverlayPanelScopedFinding> Scan(string repositoryRoot)
    {
        var findings = new List<OverlayPanelScopedFinding>();
        foreach (var razorPath in RazorFiles(repositoryRoot))
        {
            var cssPath = razorPath + ".css";
            if (!File.Exists(cssPath))
            {
                continue;
            }

            findings.AddRange(FindUndeepRules(
                File.ReadAllText(razorPath),
                File.ReadAllText(cssPath),
                Path.GetRelativePath(repositoryRoot, cssPath).Replace('\\', '/')));
        }

        return findings;
    }

    private static HashSet<string> PanelClasses(string razor)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match tag in OverlayPanelTag.Matches(razor))
        {
            foreach (Match attr in ClassAttribute.Matches(tag.Value))
            {
                foreach (var token in attr.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    classes.Add(token);
                }
            }
        }

        return classes;
    }

    private static IEnumerable<string> RazorFiles(string repositoryRoot) =>
        Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct OverlayPanelScopedFinding(string File, string Selector);
