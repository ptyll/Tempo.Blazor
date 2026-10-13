using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the dual-thumb mechanics of <c>_range-slider.css</c> (cluster-E §2.2): the two native
/// <c>input[type=range]</c> are stacked and must let each thumb drag ITSELF — the input swallows
/// pointer events, the thumb pseudo-elements answer them — plus the start-on-top escape hatch for
/// two thumbs parked on the same value and the coarse-pointer thumb size.
/// </summary>
public sealed class RangeSliderCssGuardTests
{
    [Fact]
    public void TheInputSwallowsPointerEvents_AndTheThumbsAnswerThem()
    {
        ThemeCss.TryProperty("_range-slider.css", ".tm-range-slider__input", "pointer-events").Should().Be("none",
            "the empty track between the thumbs must not capture the pointer of the thumb below");
        ThemeCss.TryProperty("_range-slider.css", ".tm-range-slider__input::-webkit-slider-thumb", "pointer-events")
            .Should().Be("auto", "the WebKit thumb must receive the pointer or it cannot drag");
        ThemeCss.TryProperty("_range-slider.css", ".tm-range-slider__input::-moz-range-thumb", "pointer-events")
            .Should().Be("auto", "the Firefox thumb must receive the pointer or it cannot drag");
    }

    [Fact]
    public void StartOnTop_LiftsTheStartThumbOverTheEndThumb()
    {
        ThemeCss.TryProperty(
                "_range-slider.css",
                ".tm-range-slider--start-on-top .tm-range-slider__input--start",
                "z-index")
            .Should().Be("4",
                "when start == end the end thumb (z-index 3) would otherwise trap the start thumb");
    }

    [Fact]
    public void CoarsePointers_GetTouchSizedThumbs()
    {
        var css = ThemeCss.ComponentCss("_range-slider.css");

        css.Should().Contain("@media (pointer: coarse)",
            "the thumbs are 20px on a mouse but a finger needs the touch target");
        css.Should().Contain("var(--tm-touch-target)",
            "the coarse thumb size is the shared token, not a new magic number");
    }
}
