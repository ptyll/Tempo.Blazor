using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The bottom drawer is a sheet: a drag handle, snap points, a sticky footer, and a presentation
/// that follows the viewport rather than the container the trigger sits in. These tests render the
/// mode without a DOM, so they pin the markup contract the stylesheet and the E2E suite build on.
/// </summary>
public class TmDrawerBottomSheetTests : LocalizationTestBase
{
    [Fact]
    public void Bottom_RendersTheSheetWithAHandleAndAnchorsItToTheBottom()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.Title, "Filters")
            .AddChildContent("Body"));

        var drawer = cut.Find(".tm-drawer");
        drawer.ClassList.Should().Contain("tm-drawer--bottom");
        drawer.GetAttribute("data-layout").Should().Be("desktop",
            "a sheet with no viewport scope renders its InitialMode, which is desktop");

        var handle = cut.Find(".tm-drawer__handle");
        handle.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        handle.GetAttribute("role").Should().Be("slider");
        handle.GetAttribute("aria-valuemin").Should().Be("0");
        handle.GetAttribute("aria-valuemax").Should().NotBeNullOrWhiteSpace();
        handle.GetAttribute("aria-valuenow").Should().NotBeNullOrWhiteSpace();

        cut.Find(".tm-drawer__panel").GetAttribute("style").Should().Contain("--tm-sheet-height");
    }

    [Fact]
    public void Bottom_UsesTheLocalizedHandleLabel()
    {
        UseCzechLocalization();

        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        cut.Find(".tm-drawer__handle").GetAttribute("aria-label").Should().Be("Posunout panel");
    }

    [Fact]
    public void Bottom_DefaultsToTheHalfSnap_AndExposesTheSnapList()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        var drawer = cut.Find(".tm-drawer");
        drawer.GetAttribute("data-snap-points").Should().Be("0.5,1");
        drawer.GetAttribute("data-snap-index").Should().Be("0");
        cut.Find(".tm-drawer__handle").GetAttribute("aria-valuenow").Should().Be("0");
    }

    [Fact]
    public void Bottom_HonoursCustomSnapPoints_AndRejectsAnEmptyList()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SnapPoints, new[] { 0.3, 0.7, 1.0 })
            .AddChildContent("Body"));

        cut.Find(".tm-drawer").GetAttribute("data-snap-points").Should().Be("0.3,0.7,1");

        var act = () => Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SnapPoints, Array.Empty<double>())
            .AddChildContent("Body"));

        act.Should().Throw<ArgumentException>("a sheet needs at least one snap point");
    }

    [Fact]
    public void Bottom_ArrowDownStepsToTheNextLowerSnap_AndEscapeStillCloses()
    {
        var closed = false;
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, open => closed = !open))
            .AddChildContent("Body"));

        cut.Find(".tm-drawer__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("1",
            "ArrowUp raises the sheet to the next snap");

        cut.Find(".tm-drawer__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("0");

        cut.Find(".tm-drawer__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        closed.Should().BeTrue("ArrowDown past the lowest snap dismisses the sheet");
    }

    [Fact]
    public void Bottom_FooterStaysASiblingOfTheScrollingBody()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.FooterContent, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Apply</button>")))
            .AddChildContent("<p>Long body</p>"));

        var panel = cut.Find(".tm-drawer__panel");
        panel.InnerHtml.Should().Contain("tm-drawer__body");
        panel.InnerHtml.Should().Contain("tm-drawer__footer");
        cut.Find(".tm-drawer__footer").ClassList.Should().Contain("tm-drawer__footer--sticky");
    }

    [Fact]
    public void Bottom_SwipeToDismiss_IsOnByDefault_AndCanBeTurnedOff()
    {
        var on = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        on.Find(".tm-drawer").GetAttribute("data-swipe-to-dismiss").Should().Be("true");

        var off = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SwipeToDismiss, false)
            .AddChildContent("Body"));

        off.Find(".tm-drawer").GetAttribute("data-swipe-to-dismiss").Should().Be("false");
    }

    [Fact]
    public void NonModal_RendersInsideItsContainer_WithoutAnOverlay()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Modal, false)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        cut.FindAll(".tm-drawer__overlay").Should().BeEmpty();
        var drawer = cut.Find(".tm-drawer");
        drawer.ClassList.Should().Contain("tm-drawer--inline");
        drawer.GetAttribute("aria-modal").Should().Be("false");
    }

    [Fact]
    public void Bottom_FollowsTheViewportScope_NotTheContainerItSitsIn()
    {
        var cut = Render<ViewportHost>(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        var drawer = cut.Find(".tm-drawer");
        drawer.GetAttribute("data-layout").Should().Be("mobile",
            "a sheet is positioned against the viewport, so a mobile viewport scope wins over the desktop container");
    }

    [Fact]
    public void Bottom_ForcedLayoutMode_RendersThatModeWithoutADom()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.LayoutMode, TmLayoutMode.Tablet)
            .AddChildContent("Body"));

        cut.Find(".tm-drawer").GetAttribute("data-layout").Should().Be("tablet");
    }

    [Fact]
    public void Bottom_InitialMode_RendersBeforeAnyMeasurement()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.InitialMode, TmLayoutMode.Mobile)
            .AddChildContent("Body"));

        cut.Find(".tm-drawer").GetAttribute("data-layout").Should().Be("mobile",
            "with no viewport scope the first frame renders InitialMode, so a phone host does not flash the desktop sheet");
    }

    [Fact]
    public void Bottom_CapsItsHeightAt85Percent_SoTheContentBehindItStaysVisible()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        cut.Find(".tm-drawer").GetAttribute("data-max-height").Should().Be("85",
            "a sheet never covers the whole viewport; the stylesheet caps it at 85dvh");
    }

    [Fact]
    public void Bottom_ShowHandleFalse_RemovesTheGrabber()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.ShowHandle, false)
            .AddChildContent("Body"));

        cut.FindAll(".tm-drawer__handle").Should().BeEmpty();
    }

    [Fact]
    public void SideDrawer_DoesNotGrowAHandle()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Right)
            .AddChildContent("Body"));

        cut.FindAll(".tm-drawer__handle").Should().BeEmpty();
        cut.Find(".tm-drawer").ClassList.Should().NotContain("tm-drawer--bottom");
    }

    /// <summary>
    /// Cascades a desktop container context and a separately named viewport context, the two
    /// cascades a sheet must tell apart.
    /// </summary>
    private sealed class ViewportHost : ComponentBase
    {
        [Parameter] public TmLayoutMode ViewportMode { get; set; } = TmLayoutMode.Mobile;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Desktop, TmLayoutMode.Desktop));
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<CascadingValue<TmLayoutContext>>(0);
                inner.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Auto, ViewportMode));
                inner.AddAttribute(2, "Name", TmLayoutScopes.Viewport);
                inner.AddAttribute(3, "ChildContent", (RenderFragment)(sheet =>
                {
                    sheet.OpenComponent<TmDrawer>(0);
                    sheet.AddAttribute(1, "IsOpen", true);
                    sheet.AddAttribute(2, "Position", DrawerPosition.Bottom);
                    sheet.AddAttribute(3, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Body")));
                    sheet.CloseComponent();
                }));
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
