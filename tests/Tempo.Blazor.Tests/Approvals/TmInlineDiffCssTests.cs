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

    [Fact]
    public void PreWrapLivesOnTheDiffSegmentsNotOnTheContent()
    {
        // Plan 98c80bc3 (Fáze 3): `white-space: pre-wrap` used to sit on `.tm-inline-diff__content`,
        // so EVERY whitespace text node the markup emitted — between the hidden label and the
        // <del>/<ins> of a change — painted a visible gap inside the flowing text. pre-wrap belongs
        // on the SEGMENTS that carry the diff text (they keep their own newlines), while the content
        // wrapper stays at the normal value so stray inter-element whitespace collapses.
        ThemeCss.TryProperty(Stylesheet, ".tm-inline-diff__content", "white-space")
            .Should().BeNull("the content wrapper must not render markup whitespace literally");

        foreach (var segment in new[] { ".tm-inline-diff__equal", ".tm-inline-diff__del", ".tm-inline-diff__ins" })
        {
            ThemeCss.Property(Stylesheet, segment, "white-space")
                .Should().Be("pre-wrap",
                    $"{segment} carries the diff text, so it keeps its own newlines through pre-wrap");
        }
    }

    [Fact]
    public void TheStackedLayoutPresentsEachSideOfAChangeAsItsOwnRow()
    {
        // Plan 98c80bc3 (Fáze 3, review round): Layout=Stacked — the deletion and the insertion of a
        // change each become their own block row, while the content wrapper stays a plain block so
        // the UNCHANGED context keeps flowing inline (a flex column would turn every equal fragment
        // into a row of its own and shred the shared sentence).
        ThemeCss.Property(Stylesheet, ".tm-inline-diff--stacked .tm-inline-diff__content", "display")
            .Should().Be("block",
                "equal context must keep flowing — only the change hunks break onto their own rows");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff--stacked .tm-inline-diff__change", "display")
            .Should().Be("block", "the change span owns the stacked rows");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff--stacked .tm-inline-diff__del", "display")
            .Should().Be("block", "the deleted side is its own row");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff--stacked .tm-inline-diff__ins", "display")
            .Should().Be("block", "the inserted side is its own row");
    }

    [Fact]
    public void TheDecisionMarkerKeepsTrailingSpaceInTheInlineRun()
    {
        // Review round: the marker rides the insertion row (in stacked it renders INSIDE the block
        // <ins>), so it needs the trailing inline gap its start margin alone never gave it.
        ThemeCss.Property(Stylesheet, ".tm-inline-diff__marker", "margin-inline-end")
            .Should().Be("var(--tm-space-1)");
    }

    [Fact]
    public void RootSurfaceTokensFallBackToTodaysValues()
    {
        var tokens = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));
        tokens["--tm-inline-diff-padding"].Should().Be("var(--tm-space-3)",
            "the host token defaults to today's padding on :root so a missing override is a no-op");
        tokens["--tm-inline-diff-bg"].Should().Be("var(--tm-bg-surface)",
            "the host token defaults to today's surface on :root so a missing override is a no-op");
        tokens["--tm-inline-diff-border-width"].Should().Be("var(--tm-border-width)",
            "the host token defaults to today's 1px border on :root so a missing override is a no-op");
        Css().Should().NotMatchRegex(@":root\s*\{",
            "a component stylesheet must not declare :root custom properties — they belong in tokens.css");
        ThemeCss.TryProperty(Stylesheet, ".tm-inline-diff", "--tm-inline-diff-padding")
            .Should().BeNull("redeclaring the token on the component would shadow a wrapper override");
        ThemeCss.TryProperty(Stylesheet, ".tm-inline-diff", "--tm-inline-diff-bg")
            .Should().BeNull("redeclaring the token on the component would shadow a wrapper override");
        ThemeCss.TryProperty(Stylesheet, ".tm-inline-diff", "--tm-inline-diff-border-width")
            .Should().BeNull("redeclaring the token on the component would shadow a wrapper override");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff", "padding")
            .Should().Be("var(--tm-inline-diff-padding, var(--tm-space-3))",
                "a host that never sets the token must keep today's --tm-space-3 padding");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff", "background")
            .Should().Be("var(--tm-inline-diff-bg, var(--tm-bg-surface))",
                "a host that never sets the token must keep today's surface background");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff", "border")
            .Should().Be("var(--tm-inline-diff-border-width, 1px) solid var(--tm-border-color)",
                "a host that never sets the token must keep today's 1px border");
    }

    [Fact]
    public void WholeLayoutCapsEachBoxThroughLineTokens()
    {
        ThemeCss.Property(Stylesheet, ".tm-inline-diff--whole .tm-inline-diff__whole-box--new", "max-block-size")
            .Should().Be("calc(var(--tm-inline-diff-whole-new-max-lines, 4) * 1.6em)",
                "the proposed box height is a host-overridable line count with today's four-line fallback");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff--whole .tm-inline-diff__whole-box--old", "max-block-size")
            .Should().Be("calc(var(--tm-inline-diff-whole-old-max-lines, 2) * 1.6em)",
                "the current box height is a host-overridable line count with today's two-line fallback");
        var tokens = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));
        tokens["--tm-inline-diff-whole-new-max-lines"].Should().Be("4",
            "the proposed box defaults to four lines on :root — the EmusCz WholeValues cap");
        tokens["--tm-inline-diff-whole-old-max-lines"].Should().Be("2",
            "the current box defaults to two lines on :root — the EmusCz WholeValues cap");
        ThemeCss.TryProperty(Stylesheet, ".tm-inline-diff--whole", "--tm-inline-diff-whole-new-max-lines")
            .Should().BeNull("redeclaring the line-count tokens on the component would shadow a wrapper override");
        ThemeCss.TryProperty(Stylesheet, ".tm-inline-diff--whole", "--tm-inline-diff-whole-old-max-lines")
            .Should().BeNull("redeclaring the line-count tokens on the component would shadow a wrapper override");
    }

    [Fact]
    public void AHostWrapperToken_ChangesTheComputedPadding()
    {
        var css = File.ReadAllText(ThemeCss.CssPath("tokens.css"))
            + "\n.emus-ai-proposal { --tm-inline-diff-padding: 0px; }\n"
            + Css();

        InheritedCustomProperty(css, "--tm-inline-diff-padding", ":root", ".emus-ai-proposal", ".tm-inline-diff")
            .Should().Be("0px",
                "EmusCz sets the token on .emus-ai-proposal; a declaration on .tm-inline-diff would pin today's padding");
        ThemeCss.Property(Stylesheet, ".tm-inline-diff", "padding")
            .Should().Be("var(--tm-inline-diff-padding, var(--tm-space-3))",
                "computed padding follows the inherited token, so a wrapper value of 0px is what paints");
    }

    /// <summary>
    /// Walks <paramref name="selectors"/> outermost-first and returns the last declared value of
    /// <paramref name="token"/>. Custom properties inherit unless a later element redeclares them —
    /// the same shadowing a host wrapper hits when the library pins the token on the component.
    /// </summary>
    private static string? InheritedCustomProperty(string css, string token, params string[] selectors)
    {
        string? value = null;
        foreach (var selector in selectors)
        {
            var body = ThemeCss.RuleDeclarations(css, selector);
            if (string.IsNullOrEmpty(body))
            {
                continue;
            }

            foreach (var declaration in body.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                if (string.Equals(declaration[..separator].Trim(), token, StringComparison.Ordinal))
                {
                    value = ThemeCss.Normalise(declaration[(separator + 1)..]);
                }
            }
        }

        return value;
    }
}
