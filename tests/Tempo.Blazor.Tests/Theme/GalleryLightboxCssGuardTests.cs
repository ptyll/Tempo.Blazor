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

    [Fact]
    public void LightboxCss_Rtl_MirrorsTheChevrons()
    {
        var css = ThemeCss.ComponentCss("_lightbox.css");

        // m4 (review): the mirror keys off the element's OWN resolved direction (:dir), not a
        // hardcoded ancestor [dir] attribute — the same live probe the tm-rtl.js helper uses, so
        // a host that flips direction through CSS instead of the dir attribute mirrors too.
        css.Should().Contain(".tm-lightbox-prev:dir(rtl) svg",
            "in RTL the next image lies to the LEFT, so the chevron glyphs flip " +
            "(the logical insets already swap the buttons and the code swaps the arrow keys)");
        css.Should().Contain(".tm-lightbox-next:dir(rtl) svg");
        css.Should().Contain("transform: scaleX(-1)");
        css.Should().NotContain("[dir=\"rtl\"] .tm-lightbox",
            "the ancestor-attribute form missed CSS-driven direction and is replaced by :dir(rtl)");
    }

    [Fact]
    public void LightboxCss_MobileRow_KeepsATouchTargetOfHeight_Panorama()
    {
        var css = ThemeCss.ComponentCss("_lightbox.css");

        // A panorama's row is shorter than the 44px arrows; centred on the row their tops reached
        // into the close/delete band (-3rem) and overlapped the delete. The row floors at one
        // touch target so the arrows never leave the image band.
        css.Should().Contain("min-height: var(--tm-touch-target)");
    }

    [Fact]
    public void LightboxCss_MobileDelete_SitsInTheFooterRow_ClearOfTheArrows()
    {
        // M2 (review): below 640px the delete leaves the top-end chrome band (-3rem, the corner
        // the arrows' upper ends can reach on a short row) for the footer flex row; the footer
        // drops by one --tm-space-2 so the row keeps that much real clearance from the arrows.
        var css = ThemeCss.ComponentCss("_lightbox.css");
        var mobile = css.Substring(css.IndexOf("@media (width < 640px)", StringComparison.Ordinal));

        mobile.Should().Contain(".tm-lightbox-footer .tm-lightbox-delete {",
            "the delete is re-targeted inside the media query");
        mobile.Should().Contain("position: static;",
            "the delete sits in the footer flex row, not the arrows' chrome band");
        mobile.Should().Contain(".tm-lightbox-footer { bottom: calc(-2.5rem - var(--tm-space-2)); }",
            "the footer keeps ≥8px (--tm-space-2) of clearance below the arrow row");
    }
}
