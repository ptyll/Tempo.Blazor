using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// The Soft button (2.12.0, Fáze 4): hover and pressed states. The original rules painted the
/// hover/pressed fill from <c>--tm-color-primary-lighter</c>, which the dark theme NEVER
/// re-declares — dark kept the light primary-100 pastel under light primary-400 ink (~2:1).
/// The fix pairs the new <c>--tm-color-primary-subtle-hover</c> fill (translucent primary-500
/// tint in dark) with the <c>--tm-color-primary-hover</c> ink, and gives the pressed state an
/// inset ring so it never reads by shade alone (WCAG 1.4.1) nor looks like a hover.
/// </summary>
public sealed partial class TokenContrastTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SoftButtonHoverAndPressedInk_HoldsAa_OnTheSubtleHoverFill(bool indigo, bool dark)
    {
        var tokens = indigo ? IndigoTokens(dark) : ThemeCss.TokenGraph(dark);
        var theme = $"{(indigo ? "indigo" : "default")} {(dark ? "dark" : "light")}";

        var fill = PaintedOnSurface("var(--tm-color-primary-subtle-hover)", tokens);

        // The rules paint --tm-color-primary-hover over the fill (the plain --tm-color-primary
        // stays the REST ink — it is not used on hover/pressed and does not clear AA there).
        foreach (var ink in new[] { "var(--tm-color-primary-hover)" })
        {
            ThemeCss.Contrast(ThemeCss.ResolveColour(ink, tokens), fill)
                .Should().BeGreaterThanOrEqualTo(4.5,
                    "{0} on the Soft hover/pressed fill ({1})", ink, theme);
        }
    }

    [Fact]
    public void SubtleHoverToken_IsDeclaredByEveryThemeFile_ThatNeedsIt()
    {
        // tokens.css: the :root alias (light value).
        ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"))
            .Should().ContainKey("--tm-color-primary-subtle-hover",
                "the Soft hover/pressed fill token must exist on :root");

        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));
        dark.Should().ContainKey("--tm-color-primary-subtle-hover",
            "the dark block must re-declare it (an alias computed on :root stays light in dark)");
        dark["--tm-color-primary-subtle-hover"].Should().Contain("--tm-color-primary-500",
            "dark derives the fill from the translucent primary-500 tint, not from primary-100");

        // The light island: the dark block declares EXACTLY the property names of this block
        // (ThemeLightIslandTests pins the closure), so the island needs the name too.
        ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"))
            .Should().ContainKey("--tm-color-primary-subtle-hover");
    }

    [Fact]
    public void SoftHoverAndPressed_NeverPaintTheLighterAlias_AndPressedCarriesANonShadeCue()
    {
        var css = ThemeCss.StripComments(ThemeCss.ComponentCss("_button.css"));

        css.Should().NotContain("--tm-color-primary-lighter",
            "--tm-color-primary-lighter stays light primary-100 in dark (the dark theme never "
            + "re-declares it) — the Soft hover/pressed states must use --tm-color-primary-subtle-hover");

        var pressed = ThemeCss.Property("_button.css", ".tm-btn.tm-btn-soft.tm-btn--pressed:not(:disabled)", "box-shadow");
        pressed.Should().Contain("inset",
            "a pressed button must carry a cue that does not rely on shade (WCAG 1.4.1)");
        ThemeCss.Property("_button.css", ".tm-btn.tm-btn-soft.tm-btn--pressed:not(:disabled)", "border-color")
            .Should().Be("var(--tm-color-primary)",
                "the pressed border separates a pressed Soft from the identically filled hover");
    }
}
