using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// The CSS contract of the 2.10.0 data-display additions (plan ff043b2e, phase 1):
/// <c>TmAnimatedNumber</c> ships its own sheet and <c>TmStatCard</c> gained the opt-in KPI parts.
/// Both paint through semantic tokens only (the dark theme redefines tokens, never these files), the
/// sheets are imported and present in the committed bundle, every emitted class has a rule, the
/// whole-card target keeps a visible focus ring under forced colours and a 44 px floor on a coarse
/// pointer. Nothing here animates in CSS: the ticker is a requestAnimationFrame loop, so the sheet
/// adds no subject to the reduced-motion enumeration.
/// </summary>
public class TmDataDisplayPhase1CssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A colour literal outside a custom-property definition — the stylesheet rule.</summary>
    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Every class the two components emit with a rule of its own.</summary>
    private static readonly string[] EmittedClasses =
    [
        "tm-animated-number", "tm-animated-number__value", "tm-animated-number__reader",
        "tm-stat-card", "tm-stat-card--interactive", "tm-stat-card--has-icon",
        "tm-stat-card__icon", "tm-stat-card__trend", "tm-stat-card__trend--positive",
        "tm-stat-card__trend--negative", "tm-stat-card__trend--neutral",
        "tm-stat-card__delta", "tm-stat-card__comparison",
        "tm-stat-card__sparkline", "tm-stat-card__skeletons",
    ];

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    private static string AnimatedNumberSheet()
        => Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_animated-number.css");

    private static string StatCardSheet()
        => Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_stat-card.css");

    [Fact]
    public void BothSheets_UseSemanticTokensOnly()
    {
        foreach (var (name, css) in new (string, string)[]
                 {
                     ("_animated-number.css", AnimatedNumberSheet()),
                     ("_stat-card.css", StatCardSheet())
                 })
        {
            ColorLiteral.Matches(css).Should().BeEmpty(
                $"{name} paints through --tm-* tokens; the dark theme redefines tokens, never this file");
            ManualDarkBlock.Matches(css).Should().BeEmpty(
                $"{name} must not carry a component-local dark block: dark values live in tokens-dark.css");
        }
    }

    [Fact]
    public void TheAnimatedNumberSheet_IsImported_AndShipsInTheCommittedBundle()
    {
        Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.css")
            .Should().Contain("@import \"components/_animated-number.css\";");

        var bundle = Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css");
        bundle.Should().Contain(".tm-animated-number{", "the committed bundle is what a host loads");
        bundle.Should().Contain("tm-animated-number__value");
        bundle.Should().Contain("tm-stat-card__trend--positive");
    }

    [Fact]
    public void EveryEmittedClass_HasARule()
    {
        var css = AnimatedNumberSheet() + "\n" + StatCardSheet();

        var missing = EmittedClasses
            .Where(name => !Regex.IsMatch(css, @"\." + Regex.Escape(name) + @"(?![\w-])", RegexOptions.None, RegexTimeout))
            .ToList();

        missing.Should().BeEmpty("a class the markup emits without a rule is an unstyled contract");
    }

    /// <summary>
    /// The ticker has no CSS motion at all; if one ever appeared, the shared enumeration in
    /// animations.css would have to grow with it (ReducedMotionSelectorTests recomputes that set).
    /// </summary>
    [Fact]
    public void TheTicker_AddsNoCssAnimation()
    {
        // The prose in the sheet explains WHY there is no CSS motion, so the check runs on the
        // declarations only — a comment mentioning the word is not an animated subject.
        var css = ThemeCss.StripComments(AnimatedNumberSheet());

        css.Should().NotContain("animation:");
        css.Should().NotContain("transition");
    }

    [Fact]
    public void TheFocusRing_SurvivesForcedColours()
    {
        var forced = Block(StatCardSheet(), "@media (forced-colors: active)");

        forced.Should().Contain("outline: 2px solid Highlight",
            "High Contrast replaces every token, so the ring has to use the system Highlight colour");
        forced.Should().Contain("box-shadow: none");
        forced.Should().Contain(".tm-stat-card--interactive:focus-visible");
        forced.Should().Contain(".tm-stat-card--interactive:focus {",
            "a programmatic :focus must keep a visible ring too");
    }

    [Fact]
    public void CoarsePointers_GetTheTouchSizedFloor()
    {
        var coarse = Block(StatCardSheet(), "@media (pointer: coarse)");

        coarse.Should().Contain("a.tm-stat-card");
        coarse.Should().Contain("button.tm-stat-card");
        coarse.Should().Contain("var(--tm-touch-target)", "44 px is the library-wide coarse target");
        coarse.Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    /// <summary>
    /// The icon is absolutely positioned in the top-end corner, so the text has to RESERVE its box or a
    /// long title or a wide value runs underneath it (WCAG 1.4.10 Reflow in a narrow grid column). The
    /// gutter exists exactly when the card carries the modifier, and the modifier exists exactly when
    /// the icon renders.
    /// </summary>
    [Fact]
    public void TheHasIconModifier_ReservesTheCornerGutter_OnTheValueAndTheLabel()
    {
        var bundle = ThemeCss.BundledCss();

        foreach (var span in new[] { "tm-stat-value", "tm-stat-label" })
        {
            var winner = CssCascade.Winning(
                bundle,
                [
                    new CssCascade.Element("div", "tm-stat-card", "tm-stat-card--has-icon"),
                    new CssCascade.Element("span", span)
                ],
                "padding-inline-end");

            winner.Should().Contain("2.25rem",
                $"{span} has to keep clear of the icon box (2.25rem) plus a breath of space");
            winner.Should().Contain("var(--tm-space-2)", "the gap is a spacing token, never a literal");
        }

        CssCascade.Resolve(
                bundle,
                [
                    new CssCascade.Element("div", "tm-stat-card"),
                    new CssCascade.Element("span", "tm-stat-value")
                ],
                "padding-inline-end")
            .Value.Should().BeNull("the default card reserves nothing — it renders no icon");
    }

    /// <summary>
    /// The focus ring is the shared <c>--tm-focus-ring</c> layer (the same two-layer ring every other
    /// focusable core surface paints), not a bare <c>--tm-color-primary</c> outline: the ring token is
    /// the one the dark theme re-points, and it keeps a 3:1 indicator against the card surface in BOTH
    /// themes (WCAG 1.4.11). <c>outline</c> stays declared so <c>forced-colors</c> still paints one.
    /// </summary>
    [Fact]
    public void TheFocusRing_UsesTheSharedRingToken_AndClears3To1InBothThemes()
    {
        // The forced-colours block is a medium the cascade model cannot place, so probing the shipped
        // bundle as-is would report its (deliberately different) declarations as a MAYBE-winner and fail
        // closed. That contract is asserted on its own above (TheFocusRing_SurvivesForcedColours); here
        // the normal medium is measured, so the block comes out first.
        var bundle = WithoutForcedColours(ThemeCss.BundledCss());
        var focused = new HashSet<string>([":focus-visible"], StringComparer.Ordinal);
        var chain = new CssCascade.Element[] { new("button", "tm-stat-card", "tm-stat-card--interactive") };

        CssCascade.Winning(bundle, chain, "box-shadow", focused)
            .Should().Be("var(--tm-focus-ring)", "the library has one focus indicator, and it is the token");

        CssCascade.Winning(bundle, chain, "outline", focused)
            .Should().StartWith("2px solid transparent",
                "forced-colors forces box-shadow to none, so the rule still needs its own outline");

        foreach (var dark in new[] { false, true })
        {
            var tokens = ThemeCss.TokenGraph(dark);
            var ring = ThemeCss.LastShadowColour(tokens["--tm-focus-ring"], tokens);
            var surface = ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens);

            ThemeCss.Contrast(ring, surface).Should().BeGreaterThanOrEqualTo(
                3.0,
                $"the focus indicator must reach 3:1 against the card surface in the {(dark ? "dark" : "light")} theme");
        }
    }

    /// <summary>
    /// Removes every <c>@media (forced-colors: active)</c> block, because the cascade model cannot place
    /// that medium and would report its declarations as undecidable instead of measuring the normal one.
    /// </summary>
    private static string WithoutForcedColours(string css)
    {
        // The marker is matched loosely because the committed bundle is MINIFIED: it writes
        // "@media (forced-colors:active)" with no space around the colon.
        var marker = Regex.Match(
            css, @"@media\s*\(\s*forced-colors\s*:\s*active\s*\)", RegexOptions.None, RegexTimeout);

        while (marker.Success)
        {
            var open = css.IndexOf('{', marker.Index);
            var depth = 0;
            var end = -1;
            for (var i = open; i >= 0 && i < css.Length; i++)
            {
                if (css[i] == '{')
                {
                    depth++;
                }
                else if (css[i] == '}' && --depth == 0)
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
            {
                break;
            }

            css = css.Remove(marker.Index, end - marker.Index + 1);
            marker = Regex.Match(
                css, @"@media\s*\(\s*forced-colors\s*:\s*active\s*\)", RegexOptions.None, RegexTimeout);
        }

        return css;
    }

    /// <summary>
    /// The trend tone is a SEMANTIC token, never a literal: the OPPOSITE of the direction and the
    /// verdict have to be separable, so the sheet paints --positive with the success token and
    /// --negative with the danger token, resolved through the shipped bundle in both themes.
    /// </summary>
    [Fact]
    public void TheTrendTone_ResolvesThroughTheSemanticTokens()
    {
        var bundle = ThemeCss.BundledCss();

        foreach (var (modifier, token) in new[]
                 {
                     ("tm-stat-card__trend--positive", "--tm-color-success-text"),
                     ("tm-stat-card__trend--negative", "--tm-color-danger-text"),
                     ("tm-stat-card__trend--neutral", "--tm-text-secondary")
                 })
        {
            var winner = CssCascade.Winning(
                bundle,
                [new CssCascade.Element("div", "tm-stat-card"), new CssCascade.Element("span", "tm-stat-card__trend", modifier)],
                "color");

            foreach (var dark in new[] { false, true })
            {
                var tokens = ThemeCss.TokenGraph(dark);
                ThemeCss.ResolveColour(winner, tokens).Should().Be(
                    ThemeCss.ResolveColour($"var({token})", tokens),
                    $"{modifier} must paint the {token} token in the {(dark ? "dark" : "light")} theme");
            }
        }
    }

    /// <summary>The whole-card target is a native element, so the reset has to remove the browser chrome.</summary>
    [Fact]
    public void TheInteractiveReset_DropsTheNativeButtonChrome()
    {
        var sheet = StatCardSheet();

        sheet.Should().Contain("button.tm-stat-card");
        sheet.Should().Contain("appearance: none");
        sheet.Should().Contain("text-align: start");
        sheet.Should().Contain("a.tm-stat-card");
        sheet.Should().Contain("text-decoration: none");
    }

    private static string Block(string css, string atRule)
    {
        var start = css.IndexOf(atRule, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{atRule} block must exist");

        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return css[start..(i + 1)];
                }
            }
        }

        return css[start..];
    }
}
