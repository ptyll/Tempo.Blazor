using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Components.Gallery;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>
/// F3 review round 2 (U1, BLOCKER): the promoted bottom sheet (popover=manual) covered every
/// z-index overlay opened from inside it — a nested TmDialog painted INVISIBLE under the sheet
/// at 390 and 1440 (arch probe4). The fix promotes EVERY modal viewport-anchored overlay root
/// to the browser top layer at activation through the shared promote helper, so the top-layer
/// order equals the open order: TmModal/TmDialog overlay roots, every modal TmDrawer position
/// (not only Bottom) and the TmOverlayPanel sheet (via the drawer it composes). TmToastContainer
/// is promoted too and re-raises itself on each push so toasts stay above open sheets/dialogs.
/// These tests pin the popover="manual" contract each root must carry; the E2E lane
/// (OverlayRound2FixE2ETests) pins the painted order.
/// </summary>
public class TopLayerPromotionTests : LocalizationTestBase
{
    public TopLayerPromotionTests()
    {
        Services.AddScoped<ToastService>();
        Services.AddSingleton<Tempo.Blazor.Abstractions.Shared.ITmNotificationService, NoOpNotificationService>();
    }

    // ── TmModal / TmDialog: the overlay root is the promoted element ──────────

    [Fact]
    public void TmModal_OverlayRoot_CarriesPopoverManual_WhenShown()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .AddChildContent("<p>content</p>"));

        cut.Find(".tm-modal-overlay").GetAttribute("popover").Should().Be("manual",
            "a modal is a modal viewport-anchored overlay: its overlay root must join the top layer " +
            "so it paints above a promoted sheet a host opened it from");
    }

    [Fact]
    public void TmModal_OverlayRoot_IsAbsent_WhenHidden()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.Title, "Test"));

        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
    }

    [Fact]
    public void TmDialog_OverlayRoot_CarriesPopoverManual_WhenShown()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Test")
            .Add(d => d.Message, "Sure?"));

        cut.Find(".tm-modal-overlay").GetAttribute("popover").Should().Be("manual",
            "a dialog opened from inside a promoted sheet must itself be top-layer or it paints invisible");
    }

    // ── TmDrawer: EVERY modal position is promoted, inline drawers never ──────

    [Theory]
    [InlineData(DrawerPosition.Bottom)]
    [InlineData(DrawerPosition.Right)]
    [InlineData(DrawerPosition.Left)]
    public void TmDrawer_ModalRoot_CarriesPopoverManual_AtEveryPosition(DrawerPosition position)
    {
        var cut = Render<TmDrawer>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Position, position)
            .Add(d => d.Title, "Test")
            .AddChildContent("<p>content</p>"));

        cut.Find(".tm-drawer").GetAttribute("popover").Should().Be("manual",
            $"a modal {position} drawer anchors to the viewport like a sheet — only the top layer " +
            "guarantees nothing above it can cover it");
    }

    [Fact]
    public void TmDrawer_InlineRoot_NeverCarriesPopover()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Position, DrawerPosition.Bottom)
            .Add(d => d.Modal, false)
            .AddChildContent("<p>content</p>"));

        cut.Find(".tm-drawer").GetAttribute("popover").Should().BeNull(
            "an inline (Modal=false) sheet anchors to its container and is never promoted");
    }

    // ── TmToastContainer: promoted and re-raised above open modal surfaces ────

    [Fact]
    public void TmToastContainer_Root_CarriesPopoverManual()
    {
        var cut = Render<TmToastContainer>();

        cut.Find(".tm-toast-container").GetAttribute("popover").Should().Be("manual",
            "toasts ride the top layer and re-raise on each push — a toast pushed while a sheet is " +
            "open must paint above it");
    }

    // ── F3 review round 3 (V2): every other modal viewport-anchored root ──────
    // The round-2 fix promoted TmModal/TmDialog/TmDrawer/toast roots. The architect probe found the
    // remaining fixed viewport-anchored roots still z-index painted: opened while a promoted surface
    // is up they stay FOCUSABLE (the trap reaches into them) but paint UNDER it — the same
    // focused-but-invisible class as the round-2 blocker.

    [Fact]
    public void TmCommandPalette_Backdrop_CarriesPopoverManual_WhenOpen()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new List<ICommandPaletteAction>()));

        cut.Find(".tm-command-palette-backdrop").GetAttribute("popover").Should().Be("manual",
            "the palette traps focus like a modal — opened from inside a promoted drawer it must " +
            "paint above it, not invisible under it");
    }

    [Fact]
    public void TmCommandPalette_Backdrop_IsAbsent_WhenClosed()
    {
        var cut = Render<TmCommandPalette>(p => p.Add(c => c.IsOpen, false));

        cut.FindAll(".tm-command-palette-backdrop").Should().BeEmpty();
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Overlay_CarriesPopoverManual_WhenVisible()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p.Add(h => h.IsVisible, true));

        cut.Find(".tm-keyboard-shortcuts-overlay").GetAttribute("popover").Should().Be("manual",
            "a viewport-fixed overlay with a focus trap must join the top layer like TmModal");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Overlay_IsAbsent_WhenHidden()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p.Add(h => h.IsVisible, false));

        cut.FindAll(".tm-keyboard-shortcuts-overlay").Should().BeEmpty();
    }

    [Fact]
    public void TmLightbox_ScopeRoot_CarriesPopoverManual_WhenOpen()
    {
        var cut = Render<TmLightbox>(p => p.Add(l => l.IsOpen, true));

        cut.Find(".tm-lightbox").GetAttribute("popover").Should().Be("manual",
            "the lightbox is a modal viewport-anchored surface (the TmFocusScope root is its " +
            "promoted element, mirroring TmDrawer)");
    }

    [Fact]
    public void TmLightbox_ScopeRoot_IsAbsent_WhenClosed()
    {
        var cut = Render<TmLightbox>(p => p.Add(l => l.IsOpen, false));

        cut.FindAll(".tm-lightbox").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttImportDialog_Overlay_CarriesPopoverManual_WhenOpen()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(d => d.IsOpen, true));

        cut.Find(".tm-gantt__dialog-overlay").GetAttribute("popover").Should().Be("manual",
            "the import dialog traps focus and anchors to the viewport — it must promote like TmModal");
    }

    [Fact]
    public void TmGanttImportDialog_Overlay_IsAbsent_WhenClosed()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(d => d.IsOpen, false));

        cut.FindAll(".tm-gantt__dialog-overlay").Should().BeEmpty();
    }

    [Fact]
    public void TmNotificationToastContainer_Root_CarriesPopoverManual()
    {
        var cut = Render<TmNotificationToastContainer>();

        cut.Find(".tm-notification-toast-container").GetAttribute("popover").Should().Be("manual",
            "notification toasts ride the top layer like TmToastContainer: pinned on top and " +
            "re-raised on each push");
    }
}
