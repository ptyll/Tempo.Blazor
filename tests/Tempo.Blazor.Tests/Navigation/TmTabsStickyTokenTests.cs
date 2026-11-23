using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>
/// 2.13 TABS-STICKY-TOKENS: sticky z-index and edge-shadow colour live on <c>:root</c>;
/// the component stylesheet reads them with no fallback. Cover layers paint a full band of
/// the cover colour before fading so a non-overflowing strip has no leftover shadow.
/// </summary>
public sealed class TmTabsStickyTokenTests
{
    [Fact]
    public void StickyZIndexAndEdgeShadowColor_AreDeclaredOnRoot()
    {
        var declarations = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        declarations.Should().ContainKey("--tm-tabs-sticky-z-index");
        declarations["--tm-tabs-sticky-z-index"].Should().Be("5");
        declarations.Should().ContainKey("--tm-tabs-edge-shadow-color");
        declarations["--tm-tabs-edge-shadow-color"].Should().Be("var(--tm-border-color)");
    }

    [Fact]
    public void ComponentStylesheet_ReadsTheTokensWithoutFallback()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_tabs.css"));

        css.Should().Contain("z-index: var(--tm-tabs-sticky-z-index);");
        css.Should().NotContain("z-index: 5;");
        css.Should().Contain("var(--tm-tabs-edge-shadow-color)");
        css.Should().NotContain("linear-gradient(to right, var(--tm-border-color), transparent)");
        css.Should().NotContain("--tm-tabs-sticky-z-index:");
        css.Should().NotContain("--tm-tabs-edge-shadow-color:");
    }

    [Fact]
    public void EdgeCover_PaintsFullCoverColorAcrossTheShadowBand()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_tabs.css"));

        css.Should().Contain(
            "linear-gradient(to right, var(--tm-tabs-scroll-cover) var(--tm-tabs-edge-shadow-size), transparent var(--tm-tabs-edge-shadow-size))",
            "the cover must fully hide the shadow across --tm-tabs-edge-shadow-size before fading");
        css.Should().Contain(
            "linear-gradient(to left, var(--tm-tabs-scroll-cover) var(--tm-tabs-edge-shadow-size), transparent var(--tm-tabs-edge-shadow-size))");
    }
}
