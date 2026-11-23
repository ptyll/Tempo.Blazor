using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Fáze 10a remaining 3f487fd8: under 640 px the toast card must fill the container
/// (width 100 %, max-width none). The desktop cap <c>max-width: calc(100vw - 2rem)</c>
/// used to win at 390 px, so a full-width container with 8 px padding left the card
/// 8 px from the left and 24 px from the right.
/// </summary>
public class TmToastMobileCssTests
{
    private static IReadOnlyList<CssCascade.Element> ToastCard()
        => [new CssCascade.Element("div", "tm-toast")];

    [Fact]
    public void MobileToast_DropsTheDesktopMaxWidthCap()
    {
        var winner = CssCascade.Resolve(
            ThemeCss.BundledCss(),
            ToastCard(),
            "max-width",
            media: new CssCascade.MediaContext(WidthPx: 390));

        winner.Unmodelled.Should().BeEmpty();
        ThemeCss.Normalise(winner.Value!).Should().Be("none",
            "at 390 px the desktop calc(100vw - 2rem) cap must not shrink a 100 % card by 16 px");
    }

    [Fact]
    public void MobileToast_FillsTheContainerWidth()
    {
        var winner = CssCascade.Resolve(
            ThemeCss.BundledCss(),
            ToastCard(),
            "width",
            media: new CssCascade.MediaContext(WidthPx: 320));

        winner.Unmodelled.Should().BeEmpty();
        ThemeCss.Normalise(winner.Value!).Should().Be("100%");
    }

    [Fact]
    public void DesktopToast_KeepsTheViewportCap()
    {
        var winner = CssCascade.Resolve(
            ThemeCss.BundledCss(),
            ToastCard(),
            "max-width",
            media: new CssCascade.MediaContext(WidthPx: 1440));

        winner.Unmodelled.Should().BeEmpty();
        ThemeCss.Normalise(winner.Value!).Should().Be("calc(100vw - 2rem)",
            "the mobile override must not leak onto desktop");
    }
}
