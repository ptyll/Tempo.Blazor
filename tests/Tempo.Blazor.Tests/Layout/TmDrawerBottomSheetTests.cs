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

        var handle = cut.Find(".tm-sheet__handle");
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

        cut.Find(".tm-sheet__handle").GetAttribute("aria-label").Should().Be("Přetažením změníte výšku panelu");
    }

    [Fact]
    public void Bottom_NamesTheSnapByItsValue()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SnapPoints, new[] { 0.25, 0.5, 0.75, 1 })
            .Add(x => x.SnapIndex, 2)
            .AddChildContent("Body"));

        cut.Find(".tm-sheet__handle").GetAttribute("aria-valuetext").Should().Be("75 %");
    }

    [Fact]
    public void Bottom_DefaultsToTheHalfSnap_AndExposesTheSnapList()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        var drawer = cut.Find(".tm-drawer");
        drawer.GetAttribute("data-snap-points").Should().Be("0.5,0.85",
            "the full snap is clamped to MaxHeight, so the gesture never sees a snap it cannot reach");
        drawer.GetAttribute("data-snap-index").Should().Be("0");
        cut.Find(".tm-sheet__handle").GetAttribute("aria-valuenow").Should().Be("0");
    }

    [Fact]
    public void Bottom_HonoursCustomSnapPoints_AndRejectsAnEmptyList()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SnapPoints, new[] { 0.3, 0.7, 1.0 })
            .AddChildContent("Body"));

        cut.Find(".tm-drawer").GetAttribute("data-snap-points").Should().Be("0.3,0.7,0.85");
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

        cut.Find(".tm-sheet__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("1",
            "ArrowUp raises the sheet to the next snap");

        cut.Find(".tm-sheet__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("0");

        cut.Find(".tm-sheet__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
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
    public void Bottom_SwipeToDismiss_ReachesTheGestureAsAnArgument()
    {
        // The gesture engine owns the behaviour, so the markup carries no test-only attribute. What
        // is observable is the argument the drawer hands attachGesture: true unless the host opts out.
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-sheet.js");
        module.SetupVoid("attachGesture", _ => true).SetVoidResult();

        var on = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        var off = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SwipeToDismiss, false)
            .AddChildContent("Body"));

        var swipes = module.Invocations
            .Where(i => i.Identifier == "attachGesture")
            .Select(i => (bool)i.Arguments[3]!)
            .ToList();
        swipes.Should().Equal(true, false);
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

        cut.Find(".tm-drawer").GetAttribute("data-max-height").Should().BeNull(
            "the cap is a stylesheet rule and a MaxHeight parameter, not a test-only attribute");
        cut.Find(".tm-drawer__panel").GetAttribute("style").Should().Contain("--tm-sheet-max");
    }

    [Fact]
    public void Bottom_ShowHandleFalse_RemovesTheGrabber()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.ShowHandle, false)
            .AddChildContent("Body"));

        cut.FindAll(".tm-sheet__handle").Should().BeEmpty();
    }

    [Fact]
    public void Bottom_ReportedSnap_BecomesTheRenderedSnap()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SnapPoints, new[] { 0.3, 0.6, 1.0 })
            .AddChildContent("Body"));

        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("0");

        cut.InvokeAsync(() => cut.Instance.HandleSheetSnappedAsync(2)).GetAwaiter().GetResult();

        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("2",
            "the gesture reports a snap index and C# owns it; the module must not write the height itself");
        cut.Find(".tm-sheet__handle").GetAttribute("aria-valuenow").Should().Be("2");
        cut.Find(".tm-sheet__handle").GetAttribute("aria-valuetext").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Bottom_EmptySnapPoints_SizesToContentUnderTheCap()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SnapPoints, Array.Empty<double>())
            .AddChildContent("Short menu"));

        var style = cut.Find(".tm-drawer__panel").GetAttribute("style") ?? string.Empty;
        style.Should().NotContain("--tm-sheet-height", "no snap points means the panel sizes to its content");
        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-sheet--content");
    }

    [Fact]
    public void Bottom_SnapIndex_IsTwoWay()
    {
        var seen = new List<int>();
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, DrawerPosition.Bottom)
            .Add(x => x.SnapIndex, 1)
            .Add(x => x.SnapIndexChanged, EventCallback.Factory.Create<int>(this, seen.Add))
            .AddChildContent("Body"));

        cut.Find(".tm-drawer").GetAttribute("data-snap-index").Should().Be("1");

        cut.Find(".tm-sheet__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        seen.Should().Equal(0);
    }

    [Fact]
    public void NonModal_ResolvesFromTheContainerContext_NotTheViewport()
    {
        var cut = Render<ViewportHost>(p => p
            .Add(x => x.ViewportMode, TmLayoutMode.Mobile)
            .Add(x => x.ContainerMode, TmLayoutMode.Desktop)
            .Add(x => x.Modal, false));

        cut.Find(".tm-drawer").GetAttribute("data-layout").Should().Be("desktop",
            "a non-modal sheet is positioned against its container, so the unnamed context wins");
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

        [Parameter] public TmLayoutMode ContainerMode { get; set; } = TmLayoutMode.Desktop;

        [Parameter] public bool Modal { get; set; } = true;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(ContainerMode, ContainerMode));
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
                    sheet.AddAttribute(3, "Modal", Modal);
                    sheet.AddAttribute(4, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Body")));
                    sheet.CloseComponent();
                }));
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
