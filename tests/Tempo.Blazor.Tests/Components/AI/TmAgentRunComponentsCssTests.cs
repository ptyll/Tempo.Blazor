using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for the two agent-run surfaces of plan 3e96f6d8, phase 3 — <c>_ai-agent-run-tree.css</c> and
/// <c>_ai-agent-monitor.css</c>: every class the markup emits has a rule, both surfaces declare no motion of
/// their own (they repaint every second while a run is going), the pointer targets grow to the 44 px touch
/// target on a coarse pointer, the forced-colors block keeps the system in charge, and the state tones always
/// accompany a word rather than replacing it.
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmAgentRunComponentsCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector =
        new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex MotionDeclaration =
        new(@"(^|;)\s*(transition(-[a-z-]+)?|animation(-[a-z-]+)?)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CssRule =
        new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static readonly (string Component, string Stylesheet, string Prefix)[] Surfaces =
    [
        ("TmAgentRunTree", "_ai-agent-run-tree.css", "tm-ai-agent-run-tree"),
        ("TmAgentMonitor", "_ai-agent-monitor.css", "tm-ai-agent-monitor"),
    ];

    private static string Stylesheet(string file)
        => Comment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", file)),
            " ");

    private static string Markup(string component)
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI"), component + ".razor*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return RazorComment.Replace(string.Concat(files), string.Empty);
    }

    /// <summary>The <c>tm-ai-agent-…</c> classes the markup emits, stripped of their interpolated tails.</summary>
    private static HashSet<string> Emitted(string component, string prefix)
    {
        var source = Markup(component);
        var fromAttributes = Regex.Matches(source, @"class\s*=\s*[""']([^""']*)[""']")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        var fromLiterals = Regex.Matches(source, @"""([^""]*tm-ai-agent-[^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        return fromAttributes
            .Concat(fromLiterals)
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Styled(string file)
        => CssClassSelector.Matches(Stylesheet(file))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The declaration block of the rule whose selector list contains exactly <paramref name="selector"/>.</summary>
    private static string Rule(string file, string selector)
    {
        var match = CssRule.Matches(Stylesheet(file))
            .Cast<Match>()
            .FirstOrDefault(candidate => candidate.Groups[1].Value
                .Split(',')
                .Any(token => string.Equals(token.Trim(), selector, StringComparison.Ordinal)));

        match.Should().NotBeNull($"{file} declares a rule for {selector}");

        return match!.Groups[2].Value;
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        foreach (var (component, file, prefix) in Surfaces)
        {
            var missing = Emitted(component, prefix).Except(Styled(file)).OrderBy(name => name, StringComparer.Ordinal).ToList();

            missing.Should().BeEmpty(
                $"{component} emits {string.Join(", ", missing)} without a rule in {file} — an unstyled "
                + "class ships as an unstyled surface");
        }
    }

    [Fact]
    public void NeitherSurfaceDeclaresMotion()
    {
        foreach (var (component, file, _) in Surfaces)
        {
            var declarations = MotionDeclaration.Matches(Stylesheet(file))
                .Select(match => match.Value.Trim())
                .ToList();

            declarations.Should().BeEmpty(
                $"{component} repaints every second while a run is going; a moving surface would keep "
                + "animating for the whole run, which is why both agent-run surfaces are transition-free "
                + "and the package's reduced-motion enumeration stays as it is");
        }
    }

    [Fact]
    public void BothSurfaces_GrowTheirPointerTargetsOnACoarsePointer()
    {
        foreach (var (component, file, _) in Surfaces)
        {
            var css = Stylesheet(file);

            css.Should().Contain("@media (pointer: coarse)", $"{component} has a coarse-pointer block");
        }

        Stylesheet("_ai-agent-run-tree.css").Should().Contain("--tm-touch-target",
            "a row and its disclosure reach the 44 px touch target on a phone");
        Stylesheet("_ai-agent-monitor.css").Should().Contain("--tm-touch-target",
            "the collapse toggle reaches the 44 px touch target on a phone");
    }

    [Fact]
    public void BothSurfaces_RepaintTheirFocusRingInTheSystemHighlightColour()
    {
        var ring = new Regex(@"@media \(forced-colors: active\)[\s\S]*outline-color: Highlight", RegexOptions.None, RegexTimeout);
        ring.IsMatch(Stylesheet("_ai-agent-run-tree.css")).Should()
            .BeTrue("the tree keeps the system focus ring under forced colors");
        ring.IsMatch(Stylesheet("_ai-agent-monitor.css")).Should()
            .BeTrue("the monitor's toggle keeps the system focus ring under forced colors");

        // Neither surface opts out of the system palette: an opt-out would kill the ring and the state.
        foreach (var (component, file, _) in Surfaces)
        {
            Stylesheet(file).Should().NotContain("forced-color-adjust", $"{component} keeps the system palette");
        }
    }

    [Fact]
    public void TheStateTonesAreDecorations_NotTheOnlyCarrier()
    {
        // The tone rules are scoped to a data attribute so the WORD next to the icon is what carries the
        // state; a colour-only cue would fail WCAG 1.4.1.
        var tree = Stylesheet("_ai-agent-run-tree.css");
        tree.Should().Contain(".tm-ai-agent-run-tree__status[data-status=\"done\"]");
        tree.Should().Contain(".tm-ai-agent-run-tree__status[data-status=\"failed\"]");
        tree.Should().Contain(".tm-ai-agent-run-tree__status[data-status=\"running\"]");

        var monitor = Stylesheet("_ai-agent-monitor.css");
        monitor.Should().Contain(".tm-ai-agent-monitor__state[data-status=\"done\"]");
        monitor.Should().Contain(".tm-ai-agent-monitor__state[data-status=\"failed\"]");
        monitor.Should().Contain(".tm-ai-agent-monitor__step-status[data-status=\"failed\"]");
    }

    [Fact]
    public void TheSelectedRowCarriesANonColourMarker()
    {
        // A selected row is not only a tint: it keeps an accent edge (and, under forced colors, a Highlight
        // outline), so a reader who cannot see the tint still sees the selection.
        Stylesheet("_ai-agent-run-tree.css").Should().Contain(".tm-ai-agent-run-tree__row[aria-selected=\"true\"]");
    }

    /// <summary>
    /// WCAG 1.4.10 Reflow: the timeline's step title and detail are prose a reader must be able to READ on a
    /// 320 px screen. The tree can fall back on its detail panel, the monitor cannot — so its step text wraps
    /// instead of being cut to one ellipsized line that nothing reveals.
    /// </summary>
    [Fact]
    public void TheMonitorWrapsItsStepText_InsteadOfClippingIt()
    {
        foreach (var selector in new[]
                 {
                     ".tm-ai-agent-monitor__step-title",
                     ".tm-ai-agent-monitor__step-detail",
                 })
        {
            var rule = Rule("_ai-agent-monitor.css", selector);
            rule.Should().Contain("overflow-wrap: anywhere", $"{selector} breaks a long word instead of overflowing");
            rule.Should().NotContain("white-space: nowrap", $"{selector} must be allowed to wrap at 320 px");
            rule.Should().NotContain("text-overflow: ellipsis", $"{selector} has no detail panel to reveal the cut text");
        }
    }

    /// <summary>
    /// The run title and the current step are the strip's two long, sentence-shaped values: both wrap. Only the
    /// short metric values (elapsed, cost, model) keep the single-line ellipsis, and those carry their full text
    /// in a <c>title</c>.
    /// </summary>
    [Fact]
    public void TheMonitorWrapsItsProseAndKeepsOnlyTheShortMetricsOnOneLine()
    {
        var title = Rule("_ai-agent-monitor.css", ".tm-ai-agent-monitor__title");
        title.Should().Contain("overflow-wrap: anywhere");
        title.Should().NotContain("white-space: nowrap");

        var metricValue = Rule("_ai-agent-monitor.css", ".tm-ai-agent-monitor__metric-value");
        metricValue.Should().Contain("overflow-wrap: anywhere");
        metricValue.Should().NotContain("white-space: nowrap",
            "the current step is the metric the strip exists to show and must wrap on a phone");

        var elapsed = Rule(
            "_ai-agent-monitor.css",
            ".tm-ai-agent-monitor__metric--elapsed .tm-ai-agent-monitor__metric-value");
        elapsed.Should().Contain("white-space: nowrap", "a duration is short and stays on one line");

        var model = Rule(
            "_ai-agent-monitor.css",
            ".tm-ai-agent-monitor__metric--model .tm-ai-agent-monitor__metric-value");
        model.Should().Contain("text-overflow: ellipsis");
    }
}
