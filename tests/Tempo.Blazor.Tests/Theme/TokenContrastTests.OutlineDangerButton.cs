using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// The OutlineDanger button (2.12.0, Fáze 10c): a quiet destructive action for row-level
/// "Delete". Rest is transparent with the danger ink on the surface, hover paints the danger
/// subtle wash under the same ink. Both inks are theme-flipping tokens (#991b1b light, #fca5a5
/// dark), so no dark block is needed — but the pairs are measured, not assumed, in every theme
/// and both for the rest and the hover fill.
/// </summary>
public sealed partial class TokenContrastTests
{
    private static readonly IReadOnlyList<CssCascade.Element> OutlineDangerButton =
    [
        new("button", "tm-btn", "tm-btn-md", "tm-btn-outline-danger"),
    ];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OutlineDangerInk_HoldsAa_OnTheSurfaceAndOnTheHoverWash(bool indigo, bool dark)
    {
        var tokens = indigo ? IndigoTokens(dark) : ThemeCss.TokenGraph(dark);
        var theme = $"{(indigo ? "indigo" : "default")} {(dark ? "dark" : "light")}";
        var ink = ThemeCss.ResolveColour("var(--tm-color-danger-text)", tokens);

        ThemeCss.Contrast(ink, ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens))
            .Should().BeGreaterThanOrEqualTo(4.5, "OutlineDanger rest ink on the surface ({0})", theme);
        ThemeCss.Contrast(ink, PaintedOnSurface("var(--tm-color-danger-subtle)", tokens))
            .Should().BeGreaterThanOrEqualTo(4.5, "OutlineDanger hover ink on the danger wash ({0})", theme);
    }

    [Fact]
    public void OutlineDanger_PaintsOnlyTokens_AndWinsBySelectorNotByImportOrder()
    {
        var css = ThemeCss.BundledCss();

        var colour = CssCascade.Resolve(css, OutlineDangerButton, "color");
        colour.Unmodelled.Should().BeEmpty();
        colour.Source.Should().Be(".tm-btn.tm-btn-outline-danger",
            "the variant compounds onto .tm-btn so it wins by selector, like every other variant");
        colour.Value.Should().Be("var(--tm-color-danger-text)");

        CssCascade.Winning(css, OutlineDangerButton, "border-color")
            .Should().Be("var(--tm-color-danger-soft-border)");
        CssCascade.Winning(css, OutlineDangerButton, "background-color")
            .Should().Be("transparent");
        CssCascade.Winning(css, OutlineDangerButton, "border-width").Should().Be("1px",
            "a border painted at zero width would leave the soft border as nothing");
    }

    [Fact]
    public void OutlineDangerHover_PaintsTheDangerSubtleWash_ButNotWhenDisabled()
    {
        var hover = ThemeCss.Property("_button.css", ".tm-btn.tm-btn-outline-danger:hover:not(:disabled)", "background-color");
        hover.Should().Be("var(--tm-color-danger-subtle)");

        var css = ThemeCss.StripComments(ThemeCss.ComponentCss("_button.css"));
        css.Should().NotContain(".tm-btn.tm-btn-outline-danger:hover {",
            "an unguarded hover would repaint a disabled button");
    }

    [Fact]
    public void OutlineDanger_KeepsTheDangerFocusRing()
    {
        ThemeCss.Property("_button.css", ".tm-btn.tm-btn-outline-danger:focus-visible", "box-shadow")
            .Should().Be("var(--tm-focus-ring-danger)",
                "the destructive family keeps the danger focus ring (3:1, TokenContrastTests.StateFocusRings)");
    }
}
