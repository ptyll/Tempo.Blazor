using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the gallery/lightbox stylesheets for the native-button gallery item and the mobile
/// lightbox layout: the <c>&lt;button&gt;</c> reset in <c>_image-gallery.css</c>, the removal of
/// the never-rendered legacy lightbox section (cluster-D §4) and the below-640px rule that lays
/// the arrows over the image edges inside the viewport.
/// </summary>
public sealed class GalleryLightboxCssGuardTests
{
    [Fact]
    public void GalleryItemCss_ResetsTheNativeButton()
    {
        ThemeCss.TryProperty("_image-gallery.css", ".tm-gallery-item", "padding").Should().Be("0",
            "a UA button's default padding must not reshape the card");
        ThemeCss.TryProperty("_image-gallery.css", ".tm-gallery-item", "font").Should().Be("inherit",
            "a button renders with UA type, not the gallery's");
        ThemeCss.TryProperty("_image-gallery.css", ".tm-gallery-item", "text-align")
            .Should().Be("start", "the UA centers button text; the title bar must read start-aligned");
        ThemeCss.TryProperty("_image-gallery.css", ".tm-gallery-item", "background")
            .Should().Be("var(--tm-bg-surface)", "no UA button face under the thumbnail");
        ThemeCss.TryProperty("_image-gallery.css", ".tm-gallery-item:focus-visible", "box-shadow")
            .Should().Be("var(--tm-focus-ring)",
            "the focus indicator is the two-layer ring, not the removed glow");
        ThemeCss.TryProperty("_image-gallery.css", ".tm-gallery-item:focus-visible", "outline")
            .Should().Be("2px solid transparent",
            "forced-colors mode forces box-shadow:none; the transparent outline is the indicator there");
    }

    [Fact]
    public void LightboxCss_HasNoLegacySection()
    {
        var css = ThemeCss.ComponentCss("_lightbox.css");

        css.Should().NotContain(".tm-lightbox-overlay");
        css.Should().NotContain(".tm-lightbox-container");
        css.Should().NotContain(".tm-lightbox-nav");
        css.Should().NotContain(".tm-lightbox-image-wrapper");
        css.Should().NotContain(".tm-lightbox-error");
    }

    [Fact]
    public void LightboxCss_CounterPill_LivesInTheLiveSectionWithATokenFill()
    {
        ThemeCss.TryProperty("_lightbox.css", ".tm-lightbox-counter", "background")
            .Should().Be("color-mix(in srgb, var(--tm-color-white) 15%, transparent)",
            "the legacy absolutely-positioned pill moved into the live section with a token fill");
        ThemeCss.TryProperty("_lightbox.css", ".tm-lightbox-counter", "color")
            .Should().Be("var(--tm-color-white)");
    }

    [Fact]
    public void LightboxCss_MobileArrows_LayOverTheImageEdges()
    {
        var css = ThemeCss.ComponentCss("_lightbox.css");

        css.Should().Contain("@media (width < 640px)",
            "range syntax with the TmBreakpoints.Sm width — not media debt");
        css.Should().Contain("inset-inline-start: var(--tm-space-2)");
        css.Should().Contain("inset-inline-end: var(--tm-space-2)");
        css.Should().Contain("width: var(--tm-touch-target)",
            "the arrows are touch targets on the phones where they overlay the image");
        css.Should().Contain("height: var(--tm-touch-target)");
    }
}
