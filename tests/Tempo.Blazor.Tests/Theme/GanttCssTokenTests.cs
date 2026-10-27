using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Token gate for the TmGantt stylesheets (TmGantt redesign, phase 2). Every <c>TmGantt*.razor.css</c>
/// and the global dialog shell <c>_gantt-dialog.css</c> read the core semantic tokens, so the palette,
/// the indigo theme and dark mode propagate into the Gantt. The sheets used to invent aliases with hex
/// fallbacks (<c>--tm-bg-subtle</c>, <c>--tm-bg-hover</c>, <c>--tm-border</c>, <c>--tm-border-subtle</c>,
/// <c>--tm-surface-1/2</c>), pinning every surface to the fallback - the fallback of an undefined
/// <c>var()</c> is what renders - and kept a hard-coded light/dark palette on the root.
/// </summary>
/// <remarks>
/// Mirrors the strict audit (<c>scripts/audit-css-strict.mjs</c>) for these files but asserts the ZERO
/// state directly, so the Gantt share of <c>scripts/css-token-baseline.json</c> cannot grow back.
/// </remarks>
public sealed class GanttCssTokenTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex TokenUsage =
        new(@"var\(\s*(--tm-[a-zA-Z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TokenDefinition =
        new(@"(--tm-[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|(?<![\w-])(?:white|black)(?![\w-])",
            RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    /// <summary>The aliases the analyses kept inventing for tokens that already exist (docs/css-token-aliases.md).</summary>
    private static readonly Regex BannedAlias =
        new(@"var\(\s*--tm-(?:bg-subtle|bg-hover|border|border-subtle|surface-1|surface-2)\s*[,)]",
            RegexOptions.Compiled, RegexTimeout);

    [Fact]
    public void GanttCss_ReadsNoRetiredAlias()
    {
        var offences = GanttCss.Files()
            .SelectMany(file => BannedAlias.Matches(file.Css).Select(match => $"{file.Name}: {match.Value}"))
            .ToList();

        offences.Should().BeEmpty(
            "var(--tm-bg-subtle|--tm-bg-hover|--tm-border,|--tm-border-subtle|--tm-surface-1/2) are not tokens "
            + "(docs/css-token-aliases.md) - the hex fallback renders forever. Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void GanttCss_ReadsOnlyDefinedTokens()
    {
        var defined = GanttCss.CoreTokenDefinitions();
        defined.UnionWith(GanttCss.Files()
            .SelectMany(file => TokenDefinition.Matches(file.Css).Select(match => match.Groups[1].Value)));

        var offences = GanttCss.Files()
            .SelectMany(file => TokenUsage.Matches(file.Css)
                .Select(match => match.Groups[1].Value)
                .Where(token => !defined.Contains(token))
                .Select(token => $"{file.Name}: undefined token {token}"))
            .Distinct()
            .ToList();

        offences.Should().BeEmpty(
            "every var(--tm-*) must resolve to a core token or to a knob the Gantt declares itself. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void GanttCss_HasNoColourLiteral()
    {
        var offences = new List<string>();
        foreach (var file in GanttCss.Files())
        {
            var lines = file.Css.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (ColorLiteral.IsMatch(lines[index]))
                {
                    offences.Add($"{file.Name}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        offences.Should().BeEmpty(
            "Gantt stylesheets paint with tokens only (hex, rgb(), rgba(), hsl(), white and black are all "
            + "literals the dark mode and the indigo theme cannot reach). Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void GanttCss_HasNoManualDarkBlock()
    {
        var offences = GanttCss.Files()
            .SelectMany(file => ManualDarkBlock.Matches(file.Css).Select(match => $"{file.Name}: {match.Value}"))
            .ToList();

        offences.Should().BeEmpty(
            "dark mode is tokens-dark.css's job; a [data-theme=dark] / .tm-dark block in a component "
            + "stylesheet duplicates the theme. Offences:\n" + string.Join("\n", offences));
    }

    [Theory]
    [InlineData(".tm-gantt--theme-dark")]
    [InlineData(".tm-gantt--theme-light")]
    public void ThemeClasses_StayAsAlias_WithoutALocalPalette(string selector)
    {
        var rules = GanttCss.Rules("TmGantt.razor.css").Where(rule => rule.Selector.Contains(selector, StringComparison.Ordinal)).ToList();

        rules.Should().NotBeEmpty($"{selector} stays as an alias (and is a literal class the markup emits)");
        foreach (var rule in rules)
        {
            GanttCss.Declarations(rule.Body).Select(d => d.Property)
                .Should().OnlyContain(property => property == "color-scheme",
                    "the class is an alias of data-theme on the root; the palette comes from tokens-dark.css, never from here");
        }
    }

    [Fact]
    public void Root_IsACard_OnTheWorkspace()
    {
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt", "border-radius").Should().Be("var(--tm-radius-card)");
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt", "box-shadow").Should().Be("var(--tm-shadow-card)");
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt", "background").Should().Be("var(--tm-bg-workspace)");
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt", "color").Should().Be("var(--tm-text-primary)",
            "a forced data-theme on the root must repaint the text, not only the surface");
    }

    [Theory]
    [InlineData("open")]
    [InlineData("inprogress")]
    [InlineData("done")]
    [InlineData("closed")]
    public void StatusChip_PaintsWithTheStatusTokens(string status)
    {
        var selector = $".tm-gantt__status-badge--{status}";
        GanttCss.Property("TmGantt.razor.css", selector, "background").Should().Be($"var(--tm-status-{status}-bg)");
        GanttCss.Property("TmGantt.razor.css", selector, "color").Should().Be($"var(--tm-status-{status}-fg)");
    }

    [Theory]
    [InlineData("highest")]
    [InlineData("high")]
    [InlineData("medium")]
    [InlineData("low")]
    [InlineData("lowest")]
    public void Priority_PaintsWithThePriorityTokens(string priority)
    {
        GanttCss.Property("TmGantt.razor.css", $".tm-gantt__priority-icon--{priority}", "background")
            .Should().Be($"var(--tm-priority-{priority})");
        GanttCss.Property("TmGantt.razor.css", $".tm-gantt__bar--priority-{priority}", "border-left")
            .Should().Be($"3px solid var(--tm-priority-{priority})");
    }

    [Fact]
    public void TodayMarker_IsASolidLine_WithAPill()
    {
        var line = GanttCss.Property("TmGantt.razor.css", ".tm-gantt__today-marker", "border-left");
        line.Should().Contain("solid").And.NotContain("dashed", "the marker is a full line, not a dashed dot trail");

        GanttCss.Property("TmGantt.razor.css", ".tm-gantt__today-label", "background").Should().Be("var(--tm-color-danger)");
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt__today-label", "color").Should().Be("var(--tm-text-inverse)");
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt__today-label", "border-radius").Should().Be("var(--tm-radius-pill)");
        GanttCss.TryProperty("TmGantt.razor.css", ".tm-gantt__today-marker::before", "content")
            .Should().BeNull("the dashed marker's round dot is replaced by the pill");
    }

    [Fact]
    public void ActiveSegment_ReadsTheSegmentedConvention()
    {
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt__segment", "background").Should().Be("var(--tm-bg-muted)");
        var active = ".tm-gantt__segment-btn--active";
        GanttCss.Property("TmGantt.razor.css", active, "background").Should().Be("var(--tm-bg-surface)");
        GanttCss.Property("TmGantt.razor.css", active, "border-color").Should().Be("var(--tm-border-color-strong)",
            "the checked segment needs a 3:1 boundary (see TokenContrastTests.SegmentedCheckedBorder)");
    }

    [Fact]
    public void WorkloadCells_FollowTheRedesignedColours()
    {
        const string file = "TmGanttWorkloadView.razor.css";
        GanttCss.Property(file, ".tm-gantt__workload-cell--ok", "background").Should().Be("var(--tm-color-primary-subtle)",
            "in capacity is the accent wash, not a success green");
        GanttCss.Property(file, ".tm-gantt__workload-cell--ok", "color").Should().Be("var(--tm-color-primary-text)");
        GanttCss.Property(file, ".tm-gantt__workload-cell--over", "background").Should().Be("var(--tm-color-danger-subtle)");
        GanttCss.Property(file, ".tm-gantt__workload-cell--over", "color").Should().Be("var(--tm-color-danger-text)");
        GanttCss.Property(file, ".tm-gantt__workload-name-cell", "position").Should().Be("sticky");
        GanttCss.Property(file, ".tm-gantt__workload-name-cell", "left").Should().Be("0");
        GanttCss.Property(file, ".tm-gantt__workload-grid", "overflow").Should().Be("clip",
            "overflow:hidden would make the grid itself the scroll container and the sticky column would never stick");
        GanttCss.Property(file, ".tm-gantt__workload-grid", "width").Should().Be("max-content");
        GanttCss.Property(file, ".tm-gantt__workload-grid", "min-width").Should().Be("100%");
    }

    /// <summary>
    /// UX review finding (dark portfolio shot): the project name inherited the host page ink - dark on the
    /// dark card header. A surface that paints its own background paints its own text colour too.
    /// </summary>
    [Fact]
    public void Portfolio_Card_And_Header_Paint_Their_Own_Text_Colour()
    {
        const string file = "TmGanttPortfolio.razor.css";
        GanttCss.Property(file, ".tm-gantt-portfolio__project", "color").Should().Be("var(--tm-text-primary)");
        GanttCss.Property(file, ".tm-gantt-portfolio__project-header", "color").Should().Be("var(--tm-text-primary)");
    }

    [Fact]
    public void WorkloadView_Markup_Gives_Every_Day_At_Least_Forty_Pixels()
    {
        var razor = File.ReadAllText(Path.Combine(GanttCss.SchedulerDir, "TmGanttWorkloadView.razor"));

        razor.Should().Contain("minmax(2.5rem, 1fr)", "a day cell is never narrower than 40px");
    }

    [Fact]
    public void GanttBar_DefaultFill_And_Label_Hold_In_BothThemes()
    {
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt__bar", "background")
            .Should().Be("var(--tm-gantt-task-color, var(--tm-color-primary-solid))");
        GanttCss.Property("TmGantt.razor.css", ".tm-gantt__bar-label", "color").Should().Be("var(--tm-color-on-primary-fixed)");
    }

    [Fact]
    public void ProgressLabel_IsPositionedFromTheBarGeometry_WithoutHas()
    {
        const string label = ".tm-gantt__bar-progress-label";
        GanttCss.Property("TmGantt.razor.css", label, "position").Should().Be("absolute");
        GanttCss.Property("TmGantt.razor.css", label, "margin-left").Should().Be("var(--tm-space-2)");
        GanttCss.TryProperty("TmGantt.razor.css", $"{label}--after-deadline", "margin-left")
            .Should().NotBeNull("the C#-computed modifier steps the label past the deadline flame");

        GanttCss.Files().Single(file => file.Name == "TmGantt.razor.css").Css
            .Should().NotContain(":has(", "the deadline offset is a computed modifier class, not a :has() rule");
    }
    [Fact]
    public void Gantt_CodeBehind_HasNoHexFallback()
    {
        var code = File.ReadAllText(Path.Combine(GanttCss.SchedulerDir, "TmGantt.razor.cs"));

        code.Should().NotContain("var(--tm-color-primary, #3b82f6)", "the default bar colour is a plain token");
        File.ReadAllText(Path.Combine(GanttCss.SchedulerDir, "TmGantt.razor")).Should()
            .NotMatchRegex("\"#[0-9a-fA-F]{3,8}\"", "the Board columns take their colour from classes, not from Razor hex strings");
    }
}

/// <summary>Reads the Gantt stylesheets the way the strict audit sees them (comments stripped).</summary>
internal static class GanttCss
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TokenDefinition =
        new(@"(--tm-[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    public static string SchedulerDir => Path.Combine(Root, "src", "Tempo.Blazor", "Components", "Scheduler");

    public static IReadOnlyList<(string Name, string Css)> Files()
    {
        var files = Directory.EnumerateFiles(SchedulerDir, "TmGantt*.razor.css")
            .Append(Path.Combine(Root, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_gantt-dialog.css"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), Strip(File.ReadAllText(path))))
            .ToList();

        files.Count.Should().BeGreaterThan(8, "the Gantt owns one stylesheet per sub-component plus the dialog shell");
        return files;
    }

    public static string Strip(string css) => Comment.Replace(css, " ");

    public static IEnumerable<(string Selector, string Body)> Rules(string file)
    {
        var css = Strip(File.ReadAllText(Path.Combine(SchedulerDir, file)));
        return RuleBlock.Matches(css).Select(rule => (ThemeCss.Normalise(rule.Groups["selector"].Value), rule.Groups["body"].Value));
    }

    public static IEnumerable<(string Property, string Value)> Declarations(string body) =>
        body.Split(';')
            .Select(declaration => (Index: declaration.IndexOf(':', StringComparison.Ordinal), declaration))
            .Where(item => item.Index > 0)
            .Select(item => (item.declaration[..item.Index].Trim(), ThemeCss.Normalise(item.declaration[(item.Index + 1)..])));

    /// <summary>The value of <paramref name="property"/> in the first rule listing <paramref name="selector"/> as one of its comma-separated parts.</summary>
    public static string? TryProperty(string file, string selector, string property)
    {
        foreach (var (ruleSelector, body) in Rules(file))
        {
            if (!ruleSelector.Split(',').Select(part => part.Trim()).Contains(selector, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var (name, value) in Declarations(body))
            {
                if (name == property)
                {
                    return value;
                }
            }
        }

        return null;
    }

    public static string Property(string file, string selector, string property) =>
        TryProperty(file, selector, property)
        ?? throw new InvalidOperationException($"{file} has no rule with the exact selector '{selector}' declaring '{property}'.");

    /// <summary>Tokens the core defines: every non-bundled stylesheet under the core css root.</summary>
    public static HashSet<string> CoreTokenDefinitions()
    {
        var root = Path.Combine(Root, "src", "Tempo.Blazor", "wwwroot", "css");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".bundled.css", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in TokenDefinition.Matches(Strip(File.ReadAllText(file))))
            {
                defined.Add(match.Groups[1].Value);
            }
        }

        return defined;
    }
}
