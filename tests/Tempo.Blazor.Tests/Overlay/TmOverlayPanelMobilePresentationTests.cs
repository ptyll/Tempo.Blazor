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
        // A sheet that cannot be dismissed must not swipe-dismiss either, and shows no grabber.
        drawer.Instance.SwipeToDismiss.Should().BeFalse();
        drawer.Instance.ShowHandle.Should().BeFalse();
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
                b.AddAttribute(2, "MobilePresentation", PanelPresentation.Auto);
                b.AddAttribute(3, "Title", "Filters");
                b.AddAttribute(4, "ChildContent", (RenderFragment)(bb => bb.AddContent(0, "Body")));
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
        // A forced layout never imports the viewport probe — the test renders with no DOM at all.
        // The resolved layout is handed to the composed drawer, so it does not measure a second time.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Auto)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        cut.FindAll(".tm-drawer.tm-drawer--bottom").Should().HaveCount(1);
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
        cut.FindAll(".tm-viewport-probe").Should().BeEmpty();

        var drawer = cut.FindComponent<TmDrawer>();
        drawer.Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile);
        drawer.Instance.InitialMode.Should().Be(TmLayoutMode.Mobile);
    }

    [Fact]
    public void MobilePresentationSheet_RendersSingleProbe_WithoutViewportScope()
    {
        // Auto without a cascaded viewport: the PANEL's probe measures once and the resolved
        // layout is passed to the drawer — the drawer must not mount a probe of its own.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Auto)
            .Add(c => c.InitialMode, TmLayoutMode.Mobile)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        cut.FindAll(".tm-viewport-probe").Should().HaveCount(1);
    }

    // ── Sheet content wrapper: Role, Id and AdditionalAttributes land on a content div inside
    //    the drawer body, so menu/listbox ownership (axe aria-required-parent) and aria-controls
    //    targets survive the popover→sheet switch. ──────────────────────────────────────────

    [Fact]
    public void MobilePresentationSheet_WrapsContent_WithRoleIdAndAttributes()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Id, "picker-menu")
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Role, "menu")
            .Add(c => c.Title, "Filters")
            .Add(c => c.AdditionalAttributes, new Dictionary<string, object> { ["data-owner"] = "grid" })
            .AddChildContent("<button role='menuitem'>One</button>"));

        var content = cut.Find(".tm-overlay-panel-sheet__content");
        content.GetAttribute("id").Should().Be("picker-menu");
        content.GetAttribute("role").Should().Be("menu");
        content.GetAttribute("data-owner").Should().Be("grid");
        // The title names the wrapper too, so the menu dialog is labelled inside the sheet.
        var labelledBy = content.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.FindAll($"[id='{labelledBy}']").Should().ContainSingle();
        // The item sits INSIDE the role=menu wrapper.
        content.QuerySelector("[role='menuitem']").Should().NotBeNull();
    }

    [Fact]
    public void MobilePresentationSheet_DialogRole_KeepsDialogRoleOnTheDrawerRoot()
    {
        // Role="dialog" panels (date pickers) keep the dialog role on the focus-scope/drawer
        // root; the content wrapper carries no conflicting role, only the id + name.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Id, "calendar-popup")
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Role, "dialog")
            .Add(c => c.Title, "Pick a date")
            .AddChildContent("<div>Calendar</div>"));

        var content = cut.Find(".tm-overlay-panel-sheet__content");
        content.GetAttribute("role").Should().BeNull();
        content.GetAttribute("id").Should().Be("calendar-popup");
        cut.Find(".tm-focus-scope").GetAttribute("role").Should().Be("dialog");
    }

    [Fact]
    public void MobilePresentationSheet_DoneCanBeDisabled_UntilTheConsumerIsReady()
    {
        // T8: the date-range sheet keeps Done disabled until both dates are picked — a half
        // range must not close silently. The consumer drives the flag; other sheets are unaffected.
        var disabled = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Period")
            .Add(c => c.SheetDoneEnabled, false)
            .AddChildContent("<div>Body</div>"));
        disabled.Find(".tm-overlay-panel-sheet__done").HasAttribute("disabled").Should().BeTrue();

        var enabled = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Period")
            .AddChildContent("<div>Body</div>"));
        enabled.Find(".tm-overlay-panel-sheet__done").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void MobilePresentationSheet_AnchoredRoot_CarriesTopLayerPopover()
    {
        // T7: the modal sheet's root is promoted to the browser top layer (popover="manual" +
        // showPopover), so a sticky app bar (TmTopBar z1020 under TmBottomNavigation z1030) or a
        // transformed ancestor can never confine or cover the sheet. The DOM stays in place —
        // nested trap and Escape order are unchanged.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        cut.Find(".tm-focus-scope").GetAttribute("popover").Should().Be("manual");
    }

    [Fact]
    public void MobilePresentationSheet_InlineDrawer_IsNotPromoted()
    {
        // A non-modal (inline) sheet anchors to its container and must NOT join the top layer.
        var cut = Render<TmDrawer>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Position, DrawerPosition.Bottom)
            .Add(c => c.Modal, false)
            .AddChildContent("<div>Body</div>"));

        cut.Find(".tm-focus-scope").GetAttribute("popover").Should().BeNull();
    }

    [Theory]
    [InlineData("menu")]
    [InlineData("listbox")]
    public void MobilePresentationSheet_MenuOrListboxRole_InitialFocusTargetsFirstItem(string role)
    {
        // UX M1: opening a menu/listbox sheet must land focus on the first menuitem/option, not
        // on Done — the TmFocusScope resolves the selector after the sheet opened.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Role, role)
            .Add(c => c.Title, "Filters")
            .AddChildContent($"<div role='{role}'><button role='menuitem'>One</button></div>"));

        var selector = cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus-selector");
        selector.Should().NotBeNullOrEmpty();
        selector.Should().Contain("[role='menuitem']").And.Contain("[role='option']");
    }

    [Fact]
    public void MobilePresentationSheet_DialogRole_InitialFocusStaysDefault()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Role, "dialog")
            .Add(c => c.Title, "Pick a date")
            .AddChildContent("<div>Calendar</div>"));

        cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus-selector").Should().BeNull();
    }

    [Fact]
    public void MobilePresentationSheet_ShowsGrabberWhileSwipeToDismissIsOn()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.Title, "Filters")
            .AddChildContent("<div>Body</div>"));

        cut.FindComponent<TmDrawer>().Instance.ShowHandle.Should().BeTrue();
        cut.Find(".tm-sheet__handle").Should().NotBeNull();
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
