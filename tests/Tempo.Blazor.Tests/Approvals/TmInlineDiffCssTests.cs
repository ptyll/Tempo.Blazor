using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Accessibility gates for the inline diff's stylesheet. A change is carried by three channels — the
/// <c>&lt;ins&gt;</c>/<c>&lt;del&gt;</c> element semantics, the visually hidden label and the colour —
/// so this sheet must never be the only one: it uses semantic tokens (which carry a dark value) and
/// every focus stop survives a UA forced-colors mode, where the box-shadow ring is forced away.
/// </summary>
public sealed class TmInlineDiffCssTests
{
    private const string Stylesheet = "_inline-diff.css";

    private static readonly Regex ForcedColorsBlock = new(
        @"@media\s*\(\s*forced-colors\s*:\s*active\s*\)\s*\{(?<body>(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ReducedMotionBlock = new(
        @"@media\s*\(\s*prefers-reduced-motion\s*:\s*reduce\s*\)\s*\{(?<body>(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex RuleBlock = new(
        @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    private static string Css() => ThemeCss.ComponentCss(Stylesheet);

    private static IReadOnlyList<string> SelectorsWithFocusVisible()
        => RuleBlock.Matches(Css())
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups["selector"].Value))
            .Where(selector => selector.Contains(":focus-visible", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void EveryFocusStopReDeclaresTheSystemHighlightRing()
    {
        var focusStops = SelectorsWithFocusVisible();
        focusStops.Should().NotBeEmpty("the diff paints its own focus ring and must survive forced colors");

        var forced = ForcedColorsBlock.Match(Css());
        forced.Success.Should().BeTrue("the sheet ships a forced-colors block for the system palette");

        var ringed = RuleBlock.Matches(forced.Groups["body"].Value)
            .Where(match => match.Groups["body"].Value.Contains("outline", StringComparison.Ordinal)
                && match.Groups["body"].Value.Contains("Highlight", StringComparison.Ordinal))
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups["selector"].Value))
            .ToHashSet(StringComparer.Ordinal);

        focusStops.Should().OnlyContain(selector => ringed.Contains(selector),
            "a box-shadow focus ring is forced away by the UA, so every :focus-visible stop needs the "
            + "system Highlight outline — otherwise the marker and the popover buttons lose their "
            + "keyboard indicator");
    }

    [Fact]
    public void TheSheetUsesSemanticTokensOnly()
    {
        var css = Css();

        css.Should().NotMatchRegex("(?<![\\w-])#[0-9a-fA-F]{3,8}\\b",
            "a raw hex step bypasses the dark theme override");
        css.Should().NotMatchRegex("\\b(rgb|rgba|hsl|hsla)\\s*\\(",
            "a raw colour function bypasses the dark theme override");
        css.Should().NotContain("[data-theme", "the dark theme is a token concern, not a component one");
    }

    [Fact]
    public void BothSidesOfAChangeAreDistinguishableWithoutColour()
    {
        var css = Css();

        ThemeCss.Property(Stylesheet, ".tm-inline-diff__del", "text-decoration")
            .Should().Contain("line-through",
                "a strike-through survives a monochrome forced-colors mode, a red background does not");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff__ins", "text-decoration")
            .Should().Contain("underline",
                "an underline survives a monochrome forced-colors mode, a green background does not");
    }

    [Fact]
    public void TheComponentReducedMotionBlockSuppressesThePopoverAndTheChangeTransition()
    {
        var reduced = ReducedMotionBlock.Match(Css());
        reduced.Success.Should().BeTrue(
            "the popover animates in and a decided change animates its background");

        var rules = RuleBlock.Matches(reduced.Groups["body"].Value)
            .Select(match => (
                Selectors: ThemeCss.SelectorParts(match.Groups["selector"].Value),
                Body: match.Groups["body"].Value))
            .ToList();

        rules.Should().Contain(rule => rule.Selectors.Contains(".tm-inline-diff__popover")
            && rule.Body.Contains("transition: none", StringComparison.Ordinal));
        rules.Should().Contain(rule => rule.Selectors.Contains(".tm-inline-diff__change")
            && rule.Body.Contains("transition: none", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePopoverAndMarkersAreFingerSizedOnCoarsePointers()
    {
        var css = Css();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
        css.Should().Contain("min-inline-size: var(--tm-touch-target)");
    }

    [Fact]
    public void TheExpandedMarkerTakesTheInkMeantForAPrimaryFill()
    {
        // The expanded marker paints a primary fill behind its glyph. --tm-bg-surface is a SURFACE role,
        // not ink: in dark mode it is the dark surface on primary-400, which is under the 3:1 a graphical
        // object needs (SC 1.4.11) — and it pairs a background token with a foreground job. The role
        // token for ink on a primary fill is --tm-color-on-primary (what .tm-btn-primary uses), which the
        // dark theme flips for both theming APIs at once.
        ThemeCss.Property(Stylesheet, ".tm-inline-diff__marker[aria-expanded=\"true\"]", "color")
            .Should().Be("var(--tm-color-on-primary)",
                "the expanded marker's glyph sits on the primary fill, so it takes the ink token for it");

        foreach (var dark in new[] { false, true })
        {
            ThemeCss.Ratio("var(--tm-color-on-primary)", "var(--tm-color-primary)", dark)
                .Should().BeGreaterThanOrEqualTo(3.0,
                    "the expanded marker's pencil glyph is a non-text graphic and must stay visible in "
                    + "both themes (WCAG 2.2 SC 1.4.11)");
        }
    }
}
