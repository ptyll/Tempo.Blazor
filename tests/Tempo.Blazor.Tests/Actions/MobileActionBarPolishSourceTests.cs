using FluentAssertions;

namespace Tempo.Blazor.Tests.Actions;

/// <summary>
/// Fáze 24 polish (C11 + C13): the action-bar tile height and the icon chip are stylesheet
/// contracts, so they are asserted on the sources the way <c>TmBreakpointsTests</c> reads them —
/// a bUnit render computes no layout, and the FixedViewport reserve lives in <c>tokens.css</c>
/// built from the same terms as the bar the component owns.
/// </summary>
public class MobileActionBarPolishSourceTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static string ComponentCss => File.ReadAllText(Path.Combine(
        FindRepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_mobile-action-bar.css"));

    private static string TokensCss => File.ReadAllText(Path.Combine(
        FindRepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));

    /// <summary>Strips comments so the assertions read declarations, not prose.</summary>
    private static string WithoutComments(string css) =>
        System.Text.RegularExpressions.Regex.Replace(
            css, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>The body of the first rule whose selector starts with <paramref name="selector"/>.</summary>
    private static string BlockOf(string withoutComments, string selector)
    {
        var start = withoutComments.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{selector} must exist");
        var open = withoutComments.IndexOf('{', start);
        var close = withoutComments.IndexOf('}', open);
        return withoutComments[open..close];
    }

    /// <summary>The value of a custom-property declaration, up to the terminating semicolon.</summary>
    private static string DeclarationOf(string withoutComments, string name)
    {
        var start = withoutComments.IndexOf(name, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{name} must be declared");
        var colon = withoutComments.IndexOf(':', start);
        var end = withoutComments.IndexOf(';', colon);
        return withoutComments[colon..end];
    }

    // ── C11 — one-line tiles floor only where it is needed (FixedViewport) ──────────

    [Fact]
    public void C11_InFlowBarsFloorTheirTilesAtOneLabelLine_FixedViewportKeepsTheTwoLineFloor()
    {
        var css = WithoutComments(ComponentCss);

        // The base rule keeps the two-line floor: the FixedViewport reserve (tokens.css, the
        // same terms) is pinned to the real bar box by the E2E reserve tests.
        var baseBlock = BlockOf(css, ".tm-mobile-action-bar {");
        baseBlock.Should().Contain("2 * var(--tm-font-size-xs)",
            "the base tile floor stays two-line so the FixedViewport reserve keeps matching the bar box");

        // C11: the in-flow placements (sticky/inline) re-floor the tile at ONE label line —
        // they reserve nothing, so the second line was only dead height (71px vs 61px bars).
        var overrideBlock = BlockOf(css, ".tm-mobile-action-bar:not(.tm-mobile-action-bar--fixed)");
        overrideBlock.Should().Contain("--tm-mobile-action-bar-tile-block:",
            "the override re-floors the same tile-block custom property");
        overrideBlock.Should().Contain("(var(--tm-font-size-xs) * var(--tm-line-height-tight))",
            "the override floors at exactly one label line");
        overrideBlock.Should().NotContain("2 *",
            "the one-line floor must not keep the two-line multiplier");
    }

    // ── C13 — the icon chip is a token-only opt-in, off by default ──────────────────

    [Fact]
    public void C13_TheChipTokensAreDeclared_WithTheChipOffByDefault()
    {
        var tokens = WithoutComments(TokensCss);
        tokens.Should().Contain("--tm-mobile-action-bar-icon-box: 1rem",
            "the default box is the IconSize.Sm icon itself — no visual change");
        tokens.Should().Contain("--tm-mobile-action-bar-icon-chip-bg: transparent",
            "the chip fill is OFF by default");
        tokens.Should().Contain("--tm-mobile-action-bar-icon-chip-fg: currentColor",
            "the chip ink falls back to the tile's own colour while the chip is off");
    }

    [Fact]
    public void C13_TheTileAndReserveFormulasDeriveTheIconTermFromTheChipToken()
    {
        var component = WithoutComments(ComponentCss);
        var tokens = WithoutComments(TokensCss);

        var tile = BlockOf(component, ".tm-mobile-action-bar {");
        tile.Should().Contain("var(--tm-mobile-action-bar-icon-box)",
            "a host that enlarges the icon box must grow the tile, the bar and the reserve together");
        tile.Should().NotContain("calc(1rem", "the icon term is no longer a literal");

        var reserve = DeclarationOf(tokens, "--tm-mobile-action-bar-reserve-block-size");
        reserve.Should().Contain("var(--tm-mobile-action-bar-icon-box)",
            "the FixedViewport reserve derives from the SAME term — chip on still means reserve == bar height");
        reserve.Should().NotContain("1rem", "the icon term is no longer a literal in the reserve either");
    }

    [Fact]
    public void C13_TheIconPaintsTheChipThroughTokens()
    {
        var block = BlockOf(WithoutComments(ComponentCss), ".tm-mobile-action-bar__icon {");
        block.Should().Contain("inline-size: var(--tm-mobile-action-bar-icon-box)");
        block.Should().Contain("block-size: var(--tm-mobile-action-bar-icon-box)");
        block.Should().Contain("border-radius: var(--tm-radius-full)");
        block.Should().Contain("background: var(--tm-mobile-action-bar-icon-chip-bg)");
        block.Should().Contain("color: var(--tm-mobile-action-bar-icon-chip-fg)");
    }

    [Fact]
    public void C13_TheDemoOptsTheChipIn_OnTheFixedViewportBar()
    {
        var page = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Tempo.Blazor.Demo.SharedUI", "Pages", "MobileActionBarPage.razor"));
        page.Should().Contain("Class=\"mab-icon-chip\"",
            "the demo turns the chip on for the FixedViewport tile the reserve tests measure");

        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Tempo.Blazor.Demo.SharedUI", "Pages", "MobileActionBarPage.razor.css"));
        css.Should().Contain(".mab-card ::deep .mab-icon-chip",
            "the chip class lands on the child component root, which the page scope attribute alone never matches");
        css.Should().Contain("--tm-mobile-action-bar-icon-box: 1.75rem",
            "the chip box clears the 24px coarse-pointer floor");
        css.Should().Contain("--tm-mobile-action-bar-icon-chip-bg: var(--tm-color-primary-subtle)");
        css.Should().Contain("--tm-mobile-action-bar-icon-chip-fg: var(--tm-color-primary)");
        css.Should().Contain(
            "--tm-mobile-action-bar-reserve-block-size: calc(var(--tm-mobile-action-bar-height) + env(safe-area-inset-bottom, 0px))",
            "a scoped opt-in must re-declare the reserve token — the :root reserve keeps the chip-off icon box");
    }
}
