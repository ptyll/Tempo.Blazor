using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Phase 15b review (MAJOR): the pressed <c>TmChip</c> toggle must be VISIBLE as pressed in
/// NORMAL rendering. The affordance rule lives in the core <c>_chip.css</c> as a
/// class+attribute selector OUTSIDE the <c>@media (forced-colors: active)</c> block — a rule
/// confined to forced colors would leave the pressed state invisible in every normal theme.
/// The UA-chrome reset rides <c>:where()</c> (zero specificity) so the variant classes
/// (<c>--soft/--filled/--outlined</c>) keep winning without !important, and the focus ring is
/// an outline so it can never erase the pressed inset ring (both used to be box-shadows).
/// </summary>
public class ChipPressedCssTests
{
    private static string ChipCss() => ThemeCss.ComponentCss("_chip.css");

    [Fact]
    public void Pressed_state_has_a_visible_rule_outside_forced_colors()
    {
        var css = ChipCss();

        var rule = Regex.Match(
            css, @"\.tm-chip--pressed\[aria-pressed=""true""\]\s*\{(?<body>[^}]*)\}");
        rule.Success.Should().BeTrue(
            "a non-forced-colors [aria-pressed] rule must exist for the pressed affordance");
        var body = ThemeCss.Normalise(rule.Groups["body"].Value);
        body.Should().Contain("box-shadow: inset 0 0 0 2px",
            "the pressed affordance paints an inset ring, visible over every variant");
        body.Should().Contain("var(--tm-chip-color, var(--tm-color-primary))",
            "the ring follows the host chip colour with the primary fallback");
    }

    [Fact]
    public void Pressed_reset_rides_where_so_variants_still_win()
    {
        var css = ChipCss();

        var reset = Regex.Match(css, @":where\(\.tm-chip--pressed\)\s*\{(?<body>[^}]*)\}");
        reset.Success.Should().BeTrue(
            "the button UA-chrome reset must ride :where() (zero specificity) so the variant classes win without !important");
        var body = ThemeCss.Normalise(reset.Groups["body"].Value);
        body.Should().Contain("font: inherit", "the button must inherit the chip typography");
        body.Should().Contain("border: 1px solid transparent",
            "the UA outset border must go, but the box model stays (the outlined variant re-styles it)");
        body.Should().Contain("appearance: none", "the UA appearance must go");
        body.Should().Contain("background: transparent", "the UA ButtonFace must go (variants re-paint it)");
    }

    [Fact]
    public void Focus_ring_is_an_outline_so_it_never_erases_the_pressed_ring()
    {
        var css = ChipCss();

        var focus = Regex.Match(css, @"\.tm-chip--pressed:focus-visible\s*\{(?<body>[^}]*)\}");
        focus.Success.Should().BeTrue("the pressed focus ring must still be styled");
        var body = ThemeCss.Normalise(focus.Groups["body"].Value);
        body.Should().Contain("outline:", "the focus ring rides outline (a box-shadow focus ring would overwrite the pressed inset ring)");
        body.Should().NotContain("box-shadow", "no box-shadow on focus — that would erase the pressed inset ring");
    }
}
