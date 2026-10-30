using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Phase 15b carry-forward (F8 m5): the area OUTSIDE the diagram page (the
/// <c>.tm-diagram-page-backdrop</c> rect) must follow the THEME instead of pinning the
/// theme-invariant <c>--tm-paper-backdrop</c> — in dark mode a white frame around the white
/// paper was the defect. The canvas owns a local <c>--tm-diagram-backdrop</c> (light keeps the
/// historical paper-backdrop gray, a dark theme re-maps it), and the dark token registry never
/// re-declares <c>--tm-paper*</c> (they stay theme-invariant by design).
/// </summary>
public class DiagramCanvasBackdropTokenTests
{
    private const string DiagramEditorCssPath =
        "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css";

    private const string TokensCssPath =
        "src/Tempo.Blazor/wwwroot/css/tokens.css";

    private const string TokensDarkCssPath =
        "src/Tempo.Blazor/wwwroot/css/tokens-dark.css";

    private static string DiagramCss() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, DiagramEditorCssPath)));

    private static string TokensCss() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, TokensCssPath)));

    private static string TokensDarkCss() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, TokensDarkCssPath)));

    [Fact]
    public void Backdrop_uses_theme_aware_token_not_paper_backdrop()
    {
        var css = DiagramCss();

        var rule = System.Text.RegularExpressions.Regex.Match(
            css, @"\.tm-diagram-page-backdrop\s*\{([^}]*)\}");
        rule.Success.Should().BeTrue("the backdrop rule must exist");
        var body = ThemeCss.Normalise(rule.Groups[1].Value);
        body.Should().Contain("fill: var(--tm-diagram-backdrop)",
            "the backdrop paint rides the new theme-aware token, not the theme-invariant paper one");
        body.Should().NotContain("--tm-paper-backdrop",
            "the backdrop rule must not reference --tm-paper-backdrop directly");
    }

    [Fact]
    public void Backdrop_token_is_light_in_root_and_dark_on_the_theme_roots()
    {
        // Light: the historical paper-backdrop gray, aliased so a host override of
        // --tm-paper-backdrop keeps applying to the diagram backdrop.
        ThemeCss.Normalise(TokensCss())
            .Should().Contain("--tm-diagram-backdrop: var(--tm-paper-backdrop)");

        // Dark: the token re-maps to the themed workspace tone — on the theme roots
        // (a component stylesheet may not carry a dark block), never a literal.
        var dark = ThemeCss.Normalise(TokensDarkCss());
        dark.Should().Contain("--tm-diagram-backdrop: var(--tm-bg-workspace)",
            "the dark canvas rides the same workspace tone as the editor chrome");

        // The light island re-pins the :root value (ThemeLightIslandTests requires the mirror).
        var island = System.Text.RegularExpressions.Regex.Match(
            TokensDarkCss(), @"\[data-theme=""light""\]\s*\{(?<body>[^}]*)\}");
        island.Success.Should().BeTrue();
        ThemeCss.Normalise(island.Groups["body"].Value)
            .Should().Contain("--tm-diagram-backdrop: var(--tm-paper-backdrop)");
    }

    [Fact]
    public void Diagram_stylesheet_carries_no_dark_block()
    {
        var css = DiagramCss();

        css.Should().NotContain("[data-theme", "the strict audit forbids dark blocks in component stylesheets");
        css.Should().NotContain(".tm-dark");
    }

    [Fact]
    public void Paper_stays_theme_invariant()
    {
        var dark = TokensDarkCss();

        // The dark theme block must not re-declare --tm-paper* (they are theme-invariant by
        // design — a descendant-scoped dark theme would otherwise turn the paper dark).
        System.Text.RegularExpressions.Regex.Matches(dark, @"--tm-paper[a-z-]*\s*:")
            .Should().BeEmpty("tokens-dark.css must never re-declare the theme-invariant paper tokens");
    }
}
