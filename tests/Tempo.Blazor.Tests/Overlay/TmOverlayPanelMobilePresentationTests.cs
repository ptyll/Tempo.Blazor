using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>
/// F3: an anchored TmOverlayPanel becomes a content-height bottom sheet on a narrow viewport —
/// the popover-vs-sheet choice resolves from the viewport scope, the sheet composes
/// TmDrawer Position=Bottom (Modal) with a title and a Done action.
/// </summary>
public class TmOverlayPanelMobilePresentationTests : LocalizationTestBase
{
    [Fact]
    public void MobilePresentationSheet_RendersInBottomDrawer_WithTitleAndDone()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div class='panel-body'>Body</div>"));

        cut.Find(".tm-drawer.tm-drawer--bottom.tm-sheet").Should().NotBeNull();
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Filters");
        cut.Find(".tm-overlay-panel-sheet__done").TextContent.Trim().Should().Be("Done");
        cut.Find(".panel-body").Should().NotBeNull();
        // The floating popover is NOT rendered in sheet mode — the sheet replaces it.
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentationSheet_DrawerIsModalContentHeightAnchoredToViewport()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        var drawer = cut.FindComponent<TmDrawer>();
        drawer.Instance.Position.Should().Be(DrawerPosition.Bottom);
        drawer.Instance.Modal.Should().BeTrue();
        // Content height: no snap points, capped by MaxHeight like every sheet.
        drawer.Instance.SnapPoints.Should().BeEmpty();
    }

    [Fact]
    public async Task MobilePresentationSheet_DoneButton_ClosesPanel()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        // Uncontrolled mode: open programmatically, then close through the Done action.
        await cut.InvokeAsync(() => cut.Instance.SetOpenAsync(true));

        cut.Find(".tm-overlay-panel-sheet__done").Click();

        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentationSheet_ForwardsCloseOnEscapeAndOutsidePointerDown()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Filters")
            .Add(c => c.CloseOnEscape, false)
            .Add(c => c.CloseOnOutsidePointerDown, false)
            .AddChildContent("<div>Body</div>"));

        var drawer = cut.FindComponent<TmDrawer>();
        drawer.Instance.CloseOnEscape.Should().BeFalse();
        drawer.Instance.CloseOnOverlayClick.Should().BeFalse();
        // A sheet that cannot be dismissed must not swipe-dismiss either.
        drawer.Instance.SwipeToDismiss.Should().BeFalse();
    }

    [Fact]
    public void MobilePresentationAuto_UsesPopoverOnDesktop()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        cut.FindAll(".tm-overlay-panel").Should().HaveCount(1);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentationAuto_UsesSheetOnMobileViewport()
    {
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);

        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmOverlayPanel>(0);
                b.AddAttribute(1, "IsOpen", true);
                b.AddAttribute(2, "Title", "Filters");
                b.AddAttribute(3, "ChildContent", (RenderFragment)(bb => bb.AddContent(0, "Body")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.FindAll(".tm-drawer.tm-drawer--bottom").Should().HaveCount(1);
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentationForcedLayoutModeMobile_UsesSheetWithoutDomMeasurement()
    {
        // A forced layout decides the sheet without any measurement of its own — the drawer it
        // composes may still measure the viewport for its own keyboard tracking.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        cut.FindAll(".tm-drawer.tm-drawer--bottom").Should().HaveCount(1);
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentationPopoverForced_UsesPopoverOnMobileViewport()
    {
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);

        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmOverlayPanel>(0);
                b.AddAttribute(1, "IsOpen", true);
                b.AddAttribute(2, "MobilePresentation", PanelPresentation.Popover);
                b.AddAttribute(3, "ChildContent", (RenderFragment)(bb => bb.AddContent(0, "Body")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.FindAll(".tm-overlay-panel").Should().HaveCount(1);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentationSheet_WithoutTitle_FallsBackToAriaLabelForAccessibleName()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.AriaLabel, "Export options")
            .AddChildContent("<div>Body</div>"));

        var drawer = cut.FindComponent<TmDrawer>();
        drawer.Instance.AriaLabel.Should().Be("Export options");
    }

    // ── TrapFocus: a dialog-role popup enforces its modality through TmFocusScope ────────────

    [Fact]
    public void TrapFocus_RendersPanelAsFocusScopeRoot_WithDialogSemantics()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.TrapFocus, true)
            .Add(c => c.Role, "dialog")
            .Add(c => c.AriaLabel, "Pick a date")
            .Add(c => c.Class, "tm-date-picker-popup")
            .AddChildContent("<div>Body</div>"));

        var scope = cut.FindComponent<TmFocusScope>();
        scope.Instance.Active.Should().BeTrue();
        scope.Instance.Modal.Should().BeTrue();
        var root = scope.Find(".tm-focus-scope");
        root.GetAttribute("role").Should().Be("dialog");
        root.GetAttribute("aria-modal").Should().Be("true");
        root.GetAttribute("aria-label").Should().Be("Pick a date");
        root.GetAttribute("popover").Should().Be("manual");
        root.ClassList.Should().Contain("tm-overlay-panel");
        root.ClassList.Should().Contain("tm-date-picker-popup");
    }

    [Fact]
    public async Task TrapFocus_EscapeDismissalStillClosesPanel()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.TrapFocus, true)
            .Add(c => c.Role, "dialog")
            .AddChildContent("<div>Body</div>"));

        // Uncontrolled mode: open programmatically…
        await cut.InvokeAsync(() => cut.Instance.SetOpenAsync(true));
        cut.FindAll(".tm-focus-scope").Should().HaveCount(1);

        // …overlay.js consumes Escape in the window capture phase in a real browser — the
        // component's dismissal path is this JSInvokable callback.
        await cut.InvokeAsync(() => cut.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-focus-scope").Should().BeEmpty();
    }
}
