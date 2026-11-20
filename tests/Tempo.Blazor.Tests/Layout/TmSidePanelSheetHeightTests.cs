using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// Phase 10d: the BOTTOM-sheet height of <see cref="TmSidePanel"/>. The sheet composes
/// <c>TmDrawer</c>; <c>SheetSnapPoints</c>, <c>SheetSnapIndex</c> and <c>SheetMaxHeight</c> ride through to
/// it, and <c>SheetHeight = Content</c> sizes the sheet to its content under the cap (the same
/// <c>tm-sheet--content</c> mode <c>TmModal</c> uses). Every default keeps the pre-10d markup.
/// </summary>
public class TmSidePanelSheetHeightTests : LocalizationTestBase
{
    private IRenderedComponent<TmSidePanel> RenderSheet(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmSidePanel>>? extra = null)
        => Render<TmSidePanel>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.Open, true);
            p.Add(x => x.Presentation, SidePanelPresentation.Sheet);
            p.AddChildContent("Panel body");
            extra?.Invoke(p);
        });

    private static string PanelStyle(IRenderedComponent<TmSidePanel> cut)
        => cut.Find(".tm-drawer__panel").GetAttribute("style") ?? string.Empty;

    // ── Defaults are unchanged ──────────────────────────────────────────────

    [Fact]
    public void Defaults_KeepTheDrawerSnapsAndCap()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile);

        var root = cut.Find(".tm-drawer");
        root.ClassList.Should().Contain("tm-drawer--bottom");
        root.ClassList.Should().NotContain("tm-sheet--content");
        root.GetAttribute("data-snap-points").Should().Be("0.5,0.85",
            "the default [0.5, 1.0] is clamped to the default 0.85 cap exactly like a bare TmDrawer");
        root.GetAttribute("data-snap-index").Should().Be("0");
        PanelStyle(cut).Should().Contain("--tm-sheet-height: 0.5").And.Contain("--tm-sheet-max: 0.85");
    }

    [Fact]
    public void SheetHeight_DefaultsToSnap()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile);

        cut.Instance.SheetHeight.Should().Be(SidePanelSheetHeight.Snap);
        cut.Instance.SheetSnapPoints.Should().BeNull();
        cut.Instance.SheetMaxHeight.Should().BeNull();
        cut.Instance.SheetSnapIndex.Should().Be(0);
    }

    // ── Snap points / index / cap ride through to TmDrawer ──────────────────

    [Fact]
    public void SheetSnapPoints_AreForwardedToTheDrawer()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetSnapPoints, new[] { 0.3, 0.7, 1.0 }));

        var drawer = cut.FindComponent<TmDrawer>().Instance;
        drawer.SnapPoints.Should().Equal(0.3, 0.7, 1.0);
        cut.Find(".tm-drawer").GetAttribute("data-snap-points").Should().Be("0.3,0.7,0.85");
    }

    [Fact]
    public void SheetSnapIndex_SelectsTheSnapTheSheetOpensAt()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetSnapPoints, new[] { 0.4, 0.7 })
            .Add(x => x.SheetSnapIndex, 1));

        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("1");
        PanelStyle(cut).Should().Contain("--tm-sheet-height: 0.7");
    }

    [Fact]
    public void SheetMaxHeight_IsForwardedAndClampsTheSnaps()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p.Add(x => x.SheetMaxHeight, 0.6));

        PanelStyle(cut).Should().Contain("--tm-sheet-max: 0.6");
        cut.Find(".tm-drawer").GetAttribute("data-snap-points").Should().Be("0.5,0.6",
            "the default full snap is clamped to the host's cap");
    }

    [Fact]
    public void SheetSnapIndexChanged_IsRaisedWhenTheKeyboardMovesTheHandle()
    {
        var seen = new List<int>();
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetSnapPoints, new[] { 0.4, 0.7 })
            .Add(x => x.SheetSnapIndexChanged, EventCallback.Factory.Create<int>(this, seen.Add)));

        cut.Find(".tm-sheet__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        seen.Should().Equal(1);
        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("1");
    }

    // ── Content height ──────────────────────────────────────────────────────

    [Fact]
    public void ContentHeight_SizesTheBottomSheetToItsContent()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content));

        var root = cut.Find(".tm-drawer");
        root.ClassList.Should().Contain("tm-sheet--content");
        root.GetAttribute("data-snap-points").Should().BeEmpty("a content sheet has no snaps");
        PanelStyle(cut).Should().NotContain("--tm-sheet-height")
            .And.Contain("--tm-sheet-max: 0.85", "the cap still applies");
    }

    [Fact]
    public void ContentHeight_KeepsTheCapFromSheetMaxHeight()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content)
            .Add(x => x.SheetMaxHeight, 0.7));

        PanelStyle(cut).Should().Contain("--tm-sheet-max: 0.7");
    }

    [Fact]
    public void ContentHeight_WinsOverSheetSnapPoints()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content)
            .Add(x => x.SheetSnapPoints, new[] { 0.3, 0.7 })
            .Add(x => x.SheetSnapIndex, 1));

        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-sheet--content");
        PanelStyle(cut).Should().NotContain("--tm-sheet-height");
    }

    [Fact]
    public void ContentHeight_RendersADecorativeGrabber_NotASlider()
    {
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content));

        var handle = cut.Find(".tm-sheet__handle");
        handle.GetAttribute("aria-hidden").Should().Be("true");
        handle.GetAttribute("role").Should().BeNull("a content sheet has nothing for the keyboard to slide between");
    }

    [Fact]
    public void ContentHeight_KeepsTheFooterStickyAndTheBodyScrollable()
    {
        RenderFragment footer = b => { b.OpenElement(0, "button"); b.AddAttribute(1, "class", "apply"); b.AddContent(2, "Apply"); b.CloseElement(); };
        var cut = RenderSheet(TmLayoutMode.Mobile, p => p
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content)
            .Add(x => x.FooterContent, footer));

        cut.Find(".tm-drawer__footer").ClassList.Should().Contain("tm-drawer__footer--sticky");
        cut.Find(".tm-drawer__footer .apply").Should().NotBeNull();
        cut.Find(".tm-drawer__body").TextContent.Should().Contain("Panel body");
    }

    [Fact]
    public void ContentHeight_FollowsTheKnobsThroughAnAutoPresentationOnAPhoneLayout()
    {
        // Auto + a Mobile layout composes the same bottom sheet (the container is not Desktop).
        var cut = Render<TmSidePanel>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Open, true)
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content)
            .AddChildContent("Panel body"));

        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-sheet--content");
    }

    // ── Where the knobs do NOT apply ────────────────────────────────────────

    [Fact]
    public void SideSheet_IgnoresTheSheetKnobs()
    {
        var cut = RenderSheet(TmLayoutMode.Tablet, p => p
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content)
            .Add(x => x.SheetSnapPoints, new[] { 0.3, 0.7 }));

        var root = cut.Find(".tm-drawer");
        root.ClassList.Should().Contain("tm-drawer--right");
        root.ClassList.Should().NotContain("tm-sheet--content").And.NotContain("tm-sheet");
        root.GetAttribute("data-snap-points").Should().Be("0.5,0.85", "the side sheet keeps the markup it always had");
        cut.FindAll(".tm-sheet__handle").Should().BeEmpty("a side sheet never renders the bottom-sheet grabber");
    }

    [Fact]
    public void Docked_IgnoresTheSheetKnobs()
    {
        var cut = Render<TmSidePanel>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
            .Add(x => x.Open, true)
            .Add(x => x.SheetHeight, SidePanelSheetHeight.Content)
            .Add(x => x.SheetSnapPoints, new[] { 0.3, 0.7 })
            .Add(x => x.SheetMaxHeight, 0.6)
            .AddChildContent("Panel body"));

        cut.FindAll(".tm-drawer").Should().BeEmpty();
        cut.Find("aside.tm-side-panel--docked").Should().NotBeNull();
        cut.Markup.Should().NotContain("tm-sheet");
    }
}
