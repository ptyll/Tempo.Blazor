using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
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
}
