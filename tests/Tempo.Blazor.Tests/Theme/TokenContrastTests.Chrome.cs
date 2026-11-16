using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// The later additions of <see cref="TokenContrastTests"/> (phase-11 chrome, the NotionEditor option
/// palette and the on-primary-fixed ink), split into their own partial file so the original stays
/// under the repository's 1200-line source ceiling (N270/F13). Nothing moves semantically — same
/// class, same measured token graph.
/// </summary>
public sealed partial class TokenContrastTests
{
    /// <summary>
    /// The active page number, the idle sort glyph and the checkbox accent, measured in both themes.
    /// The dark primary fill is primary-400 and the on-primary token aliases the page ink there, so
    /// the page number cannot use it — primary-900 is the ink that clears 4.5:1 against both fills.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Phase11Chrome_KeepsAaContrast(bool dark)
    {
        var tokens = ThemeCss.TokenGraph(dark);
        var surface = ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens);

        ThemeCss.Contrast(
                ThemeCss.ResolveColour("var(--tm-color-on-primary-fixed)", tokens),
                ThemeCss.ResolveColour("var(--tm-color-primary-solid)", tokens))
            .Should().BeGreaterThanOrEqualTo(4.5, "active page number, {0}", dark ? "dark" : "light");

        ThemeCss.Contrast(
                ThemeCss.ResolveColour("var(--tm-text-secondary)", tokens), surface)
            .Should().BeGreaterThanOrEqualTo(4.5, "idle sort glyph, {0}", dark ? "dark" : "light");

        ThemeCss.Contrast(
                ThemeCss.ResolveColour("var(--tm-color-primary)", tokens), surface)
            .Should().BeGreaterThanOrEqualTo(3, "checkbox accent, {0}", dark ? "dark" : "light");
    }

    /// <summary>
    /// NotionEditor option palette (Fáze 2). Pill text sits on the option wash and white text sits
    /// on the solid swatch; both must hold AA in light and dark, default and indigo. The values are
    /// read from the package palette files — a number copied here would not catch a drifted swatch.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NotionOptionPalette_HoldsAa(bool indigo, bool dark)
    {
        var tokens = ThemeCss.TokenGraph(dark);
        if (indigo)
        {
            foreach (var (name, value) in ThemeCss.Declarations(ThemeCss.CssPath("theme-indigo.css")))
            {
                tokens[name] = value;
            }
        }

        var root = ThemeCss.RepositoryRoot().FullName;
        var paletteDir = Path.Combine(root, "src", "Tempo.Blazor.NotionEditor", "wwwroot", "css", "components");
        foreach (var (name, value) in ThemeCss.Declarations(Path.Combine(paletteDir, "_notion-variables.css")))
        {
            tokens[name] = value;
        }

        if (dark)
        {
            foreach (var (name, value) in ThemeCss.Declarations(Path.Combine(paletteDir, "_notion-dark.css")))
            {
                tokens[name] = value;
            }
        }

        foreach (var name in new[] { "gray", "red", "orange", "yellow", "green", "blue", "purple", "pink", "brown" })
        {
            var ink = ThemeCss.ResolveColour($"var(--tm-notion-option-{name}-ink)", tokens);
            var wash = ThemeCss.ResolveColour($"var(--tm-notion-option-{name}-wash)", tokens);
            ThemeCss.Contrast(ink, wash).Should().BeGreaterThanOrEqualTo(
                4.5, "option pill text {0}, {1} {2}", name, indigo ? "indigo" : "default", dark ? "dark" : "light");

            var solid = ThemeCss.ResolveColour($"var(--tm-notion-option-{name}-swatch)", tokens);
            ThemeCss.Contrast("#ffffff", solid).Should().BeGreaterThanOrEqualTo(
                4.5, "white text on the solid swatch {0}, {1} {2}", name, indigo ? "indigo" : "default", dark ? "dark" : "light");
        }
    }

    /// <summary>
    /// White ink on a primary fill must use the solid step. Dark <c>--tm-color-primary</c> is
    /// primary-400, which fails AA against fixed white; primary-solid stays primary-600.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnPrimaryFixed_HoldsAaAgainstPrimarySolid(bool indigo, bool dark)
    {
        var tokens = ThemeCss.TokenGraph(dark);
        if (indigo)
        {
            foreach (var (name, value) in ThemeCss.Declarations(ThemeCss.CssPath("theme-indigo.css")))
            {
                tokens[name] = value;
            }
        }

        ThemeCss.Contrast(
                ThemeCss.ResolveColour("var(--tm-color-on-primary-fixed)", tokens),
                ThemeCss.ResolveColour("var(--tm-color-primary-solid)", tokens))
            .Should().BeGreaterThanOrEqualTo(
                4.5,
                "on-primary-fixed vs primary-solid, {0} {1}",
                indigo ? "indigo" : "default",
                dark ? "dark" : "light");
    }
}
