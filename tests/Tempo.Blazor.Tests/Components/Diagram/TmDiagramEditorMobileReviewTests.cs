using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Diagram;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// DiagramEditor redesign phase 6/7 consolidated review fixes (plan cbb1964a, fze-6): the mobile
/// snapshot state model (sheet/minimap survive a mobile → desktop → mobile trip while the
/// desktop state in between is untouched), exit-before-entry layout transitions, the minimap
/// toggle availability rule, the overlay chrome (header strip, state labels, focus), the action
/// bar tiles (mode mirror, checked sheet tiles, tap-to-close), the "⋯" menu close-and-focus
/// contract and the zoom pill order. Markup-level contract only: CSS cannot be asserted in bUnit.
/// </summary>
public class TmDiagramEditorMobileReviewTests : DiagramTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static DiagramDocument CreateDocument()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        return doc;
    }

    private IRenderedComponent<TmDiagramEditor> RenderEditor(
        TmLayoutMode mode,
        DiagramDocument? doc = null,
        Action<ComponentParameterCollectionBuilder<TmDiagramEditor>>? extra = null)
    {
        return Render<TmDiagramEditor>(p =>
        {
            p.Add(e => e.Document, doc ?? CreateDocument());
            p.Add(e => e.LayoutMode, mode);
            extra?.Invoke(p);
        });
    }

    // ── The mobile snapshot state model ─────────────────────────────────────

    [Fact]
    public void MobileToDesktopToMobile_KeepsTheSheetClosedAndTheMinimapHidden()
    {
        // The mobile session owns its collapse state: leaving mobile saves it INTO the mobile
        // snapshot (before the pre-mobile state is restored), re-entering applies it again.
        var cut = RenderEditor(TmLayoutMode.Mobile);
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("X1: the sheet starts closed");
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty("X1: the minimap starts hidden");

        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Desktop));
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull(
            "back on desktop the pre-mobile state (inspector expanded) is restored");
        cut.Find(".tm-diagram-editor__minimap-card .tm-diagram-minimap").Should().NotBeNull(
            "back on desktop the pre-mobile state (minimap open) is restored");

        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Mobile));
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty(
            "the mobile snapshot reapplies: the sheet the user left closed stays closed");
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty(
            "the mobile snapshot reapplies: the minimap the user left hidden stays hidden");
    }

    [Fact]
    public void DesktopUserHiddenMinimap_MobileRoundTrip_StaysHidden()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        // The user hides the minimap on desktop.
        cut.Find(".tm-diagram-editor__minimap-card-toggle").Click();
        cut.FindAll(".tm-diagram-editor__minimap-card .tm-diagram-minimap").Should().BeEmpty();

        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Mobile));
        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll(".tm-diagram-editor__minimap-card .tm-diagram-minimap").Should().BeEmpty(
            "the user's own desktop choice (minimap hidden) survives the mobile trip");
    }

    [Fact]
    public void DesktopOpenInspectorAndMinimap_MobileRoundTrip_RestoresBoth()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull();
        cut.Find(".tm-diagram-editor__minimap-card .tm-diagram-minimap").Should().NotBeNull();

        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Mobile));
        cut.FindAll(".tm-diagram-editor__inspector").Should().BeEmpty("mobile hides the docks");
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty();

        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Desktop));
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull("the inspector comes back");
        cut.Find(".tm-diagram-editor__minimap-card .tm-diagram-minimap").Should().NotBeNull(
            "the minimap card comes back");
    }

    [Fact]
    public void DesktopToMobileToTablet_PaletteRailedInspectorExpanded()
    {
        // The exit branches run BEFORE the entry branches: leaving mobile restores the
        // pre-mobile (expanded, open) state first, then the tablet entry rails the palette —
        // the inspector keeps the single expanded dock slot.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Mobile));
        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Tablet));

        cut.Find(".tm-editor-shell__rail--left").Should().NotBeNull("the palette rails on tablet");
        cut.Find("aside.tm-editor-shell__panel--right").Should().NotBeNull(
            "the inspector is the expanded dock panel (not railed, not hidden)");
    }

    [Fact]
    public void DesktopToMobileToTablet_MinimapOverlayVisible()
    {
        // The desktop minimap was open; the mobile exit restores it before the tablet entry, so
        // the tablet floating overlay renders.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Mobile));
        cut.Render(p => p.Add(e => e.LayoutMode, TmLayoutMode.Tablet));

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "the pre-mobile open minimap restores on the way out of mobile and the tablet overlay shows it");
    }

    // ── The minimap toggle availability (X1 default-hide only with a reopen path) ──

    [Fact]
    public void ViewerPresetWithShowMinimap_Mobile_KeepsTheMinimapReachable()
    {
        // A Viewer (zoom | view | search | export) still carries the View group, so the "⋯"
        // minimap toggle exists — the X1 default-hide applies and the map stays reachable.
        var cut = RenderEditor(TmLayoutMode.Mobile, extra: p => p
            .Add(e => e.ToolbarPreset, DiagramToolbarPreset.Viewer));

        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty("hidden at load (X1)");

        cut.Find("[data-testid='diagram-editor-more']").Should().NotBeNull("the ⋯ menu exists");
        cut.Find("[data-testid='diagram-editor-more']").Click();
        var panelsGroup = cut.Find(".tm-diagram-editor__view-menu-panel [data-tm-group='panels']");
        panelsGroup.Should().NotBeNull("the minimap toggle stays reachable for a viewer");
        panelsGroup.QuerySelector("button").Click();

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull("the toggle reopens the map");
    }

    [Fact]
    public void ToolbarGroupsWithoutPanelsOrView_Mobile_KeepsTheOverlayVisible()
    {
        // No Panels/View group: no "⋯" minimap toggle could ever reopen the map, so the X1
        // default-hide must NOT apply — the overlay stays visible at load.
        var cut = RenderEditor(TmLayoutMode.Mobile, extra: p => p
            .Add(e => e.ToolbarGroups, DiagramToolbarGroups.Zoom | DiagramToolbarGroups.Search | DiagramToolbarGroups.Export));

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "without a reopen path the minimap never default-hides");
    }

    // ── The minimap overlay chrome (header strip, state labels, aria, focus) ──

    [Fact]
    public void TabletMinimapOverlay_CollapseLivesInAHeaderStrip_WithStateLabelsAndAria()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);

        var overlay = cut.Find(".tm-diagram-editor__minimap-overlay");
        var overlayId = overlay.GetAttribute("id");
        overlay.QuerySelector(".tm-diagram-editor__minimap-overlay-header").Should().NotBeNull(
            "the collapse affordance lives in a header strip, never over the map");

        var collapse = overlay.QuerySelector(".tm-diagram-editor__minimap-overlay-collapse")!;
        collapse.GetAttribute("aria-label").Should().Be("Hide minimap");
        collapse.GetAttribute("aria-expanded").Should().Be("true");
        collapse.GetAttribute("aria-controls").Should().Be(overlayId,
            "aria-expanded names the overlay element it controls");

        collapse.Click();

        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty();
        var reopen = cut.Find(".tm-diagram-editor__minimap-overlay-reopen");
        reopen.GetAttribute("aria-label").Should().Be("Show minimap");
        reopen.GetAttribute("aria-expanded").Should().Be("false");
        reopen.GetAttribute("aria-controls").Should().Be(overlayId,
            "the reopen button controls the same overlay id");

        reopen.Click();
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull();
    }

    [Fact]
    public void TabletMinimapOverlay_CollapseAndReopen_ManageFocus()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);

        var focusBefore = JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);
        cut.Find(".tm-diagram-editor__minimap-overlay-collapse").Click();
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation)
            .Should().BeGreaterThan(focusBefore, "collapsing moves focus to the reopen button");

        cut.Find(".tm-diagram-editor__minimap-overlay-reopen").Click();
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation)
            .Should().BeGreaterThan(focusBefore + 1, "reopening moves focus to the collapse button");
    }

    [Fact]
    public void MobileMinimapOverlay_Collapse_FocusesTheViewMenuTrigger()
    {
        // On mobile there is no reopen button — the "⋯" trigger owns the map.
        var cut = RenderEditor(TmLayoutMode.Mobile);
        cut.Find("[data-testid='diagram-editor-more']").Click();
        cut.Find(".tm-diagram-editor__view-menu-panel [data-tm-group='panels'] button").Click();
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull();

        var focusBefore = JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);
        cut.Find(".tm-diagram-editor__minimap-overlay-collapse").Click();

        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty();
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation)
            .Should().BeGreaterThan(focusBefore, "on mobile focus returns to the ⋯ trigger");
    }

    // ── The "⋯" menu: close + focus after Search / Minimap ──────────────────

    [Fact]
    public void MobileViewMenu_MinimapToggle_ClosesTheMenuAndFocusesTheTrigger()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.Find("[data-testid='diagram-editor-more']").Click();
        var focusBefore = JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);
        cut.Find(".tm-diagram-editor__view-menu-panel [data-tm-group='panels'] button").Click();

        cut.Find(".tm-diagram-editor__view-menu-panel").GetAttribute("hidden").Should().NotBeNull(
            "the menu closes after the minimap toggle");
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation)
            .Should().BeGreaterThan(focusBefore, "focus returns to the ⋯ trigger");
    }

    [Fact]
    public void MobileViewMenu_Search_ClosesTheMenuAndFocusesTheTrigger()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.Find("[data-testid='diagram-editor-more']").Click();
        var focusBefore = JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);
        cut.Find(".tm-diagram-editor__view-menu-panel [data-tm-group='search'] button").Click();

        cut.Find(".tm-diagram-editor__view-menu-panel").GetAttribute("hidden").Should().NotBeNull(
            "the menu closes after Search");
        cut.Find(".tm-diagram-search-panel").Should().NotBeNull("the search panel opened");
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation)
            .Should().BeGreaterThan(focusBefore, "focus returns to the ⋯ trigger");
    }

    // ── The action bar: mode mirror, checked sheet tiles, tap-to-close ──────

    [Fact]
    public void ActionBar_PanActive_FirstTileIsPan_TappingReturnsToSelect()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.Find(".tm-mobile-action-bar__action[data-action-id='select']").Should().NotBeNull();

        // Arm pan through "More". The overflow closes the sheet, yields once and THEN invokes
        // (F5 Y8) — wait for the tile to flip instead of asserting synchronously.
        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll(".tm-overlay-panel-sheet [role='menuitemcheckbox']")
            .Single(i => i.TextContent.Trim() == "Pan").Click();

        cut.WaitForAssertion(() =>
        {
            var tiles = cut.FindAll(".tm-mobile-action-bar__action");
            tiles.Should().HaveCount(3);
            tiles[0].GetAttribute("data-action-id").Should().Be("pan");
            tiles[0].GetAttribute("aria-pressed").Should().Be("true");
            tiles[0].TextContent.Trim().Should().Be("Pan");
        });

        // Pan left "More" while it is the first tile.
        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-overlay-panel-sheet [role='menuitemcheckbox'], .tm-overlay-panel-sheet [role='menuitem']")
                .Select(i => i.TextContent.Trim()).Should().NotContain("Pan"));

        // Close the menu again (its Done action) before the return-to-Select tap.
        cut.Find(".tm-overlay-panel-sheet .tm-overlay-panel-sheet__done").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty());

        // Tapping the Pan tile returns to Select.
        cut.Find(".tm-mobile-action-bar__action[data-action-id='pan']").Click();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-mobile-action-bar__action[data-action-id='select']")
                .GetAttribute("aria-pressed").Should().Be("true"));
    }

    [Fact]
    public void ActionBar_ShapesTile_CheckedWhileOpen_TappingActiveTileClosesTheSheet()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        var shapes = cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']");
        shapes.GetAttribute("aria-pressed").Should().Be("false", "the sheet starts closed (X1)");

        shapes.Click();
        cut.Find(".tm-editor-shell__sheet").Should().NotBeNull();
        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']")
            .GetAttribute("aria-pressed").Should().Be("true", "checked while the sheet is on Shapes");
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']")
            .GetAttribute("aria-pressed").Should().Be("false");

        // Tapping the active tile closes the sheet.
        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']").Click();
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']")
            .GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void ActionBar_PropertiesTile_CheckedWhileOpen_TappingActiveTileClosesTheSheet()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();
        cut.Find(".tm-editor-shell__sheet").Should().NotBeNull();
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']")
            .GetAttribute("aria-pressed").Should().Be("true");
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty(
            "tapping the active Properties tile closes the sheet");
    }

    // ── The floating zoom pill reads − | % | + | fit ─────────────────────────

    [Fact]
    public void MobileZoomOverlay_ReadsMinusLabelPlusFit()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        var pill = cut.Find(".tm-diagram-editor__zoom-overlay");
        var controls = pill.Children.Select(c => c.TagName == "BUTTON"
            ? c.GetAttribute("aria-label")
            : "label").ToList();
        controls.Should().Equal("Zoom out", "label", "Zoom in", "Fit to view");
    }

    // ── A selection keeps the Layers tab and the snap ───────────────────────

    [Fact]
    public async Task Selection_WithSheetOpenOnLayersTab_KeepsTabAndSnap()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        // Open the sheet on Properties (half), switch to the Layers tab, drive the sheet to half.
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .Single(t => t.TextContent.Trim() == "Layers").Click();
        var shell = cut.FindComponent<TmEditorShell>();
        await cut.InvokeAsync(() => shell.Instance.MobileSheetSnapIndexChanged.InvokeAsync(1));

        // Select a node: the sheet keeps the Layers tab AND the half snap (the user already had
        // the inspector open — the selection must not yank the tab away).
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnSelectionChanged(["n1"]));

        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("1");
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .Single(t => t.TextContent.Trim() == "Layers")
            .GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public async Task Selection_WithSheetOpenOnShapesTab_SwitchesToPropertiesAtPeek()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']").Click();
        var shell = cut.FindComponent<TmEditorShell>();
        await cut.InvokeAsync(() => shell.Instance.MobileSheetSnapIndexChanged.InvokeAsync(1));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnSelectionChanged(["n1"]));

        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0",
            "from the Shapes tab the selection re-opens peeked on the properties");
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .Single(t => t.TextContent.Trim() == "Properties")
            .GetAttribute("aria-selected").Should().Be("true");
    }

    // ── The shell panels toggle: hidden while the action bar renders ─────────

    [Fact]
    public void MobileEditor_HidesTheShellPanelsToggleWhileTheActionBarRenders()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);
        cut.FindAll(".tm-editor-shell__panel-toggle--mobile").Should().BeEmpty(
            "the action bar is the one reopen path — no second toggle");
        cut.Find(".tm-mobile-action-bar__bar").Should().NotBeNull();
    }

    [Fact]
    public void MobileWithoutActionBar_KeepsTheShellPanelsToggle()
    {
        // ShowToolbar=false: no action bar renders, but the panels exist — the shell toggle is
        // the reopen path (the editor passes ShowMobilePanelsToggle=true then).
        var cut = RenderEditor(TmLayoutMode.Mobile, extra: p => p.Add(e => e.ShowToolbar, false));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.Find(".tm-editor-shell__panel-toggle--mobile").Should().NotBeNull();
    }

    [Fact]
    public async Task MobileSheetClose_FocusesTheActionBar()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();
        cut.Find(".tm-editor-shell__sheet").Should().NotBeNull();

        cut.Find(".tm-editor-shell__sheet-close").Click();

        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmDiagramEditor.focusFirstAction",
            "closing the sheet (its close button unmounts) moves focus to the action bar");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task MobileShellLeftOpen_AlsoOpensTheInspector()
    {
        // When the shell opens the Shapes side by other means, the inspector opens too — the
        // flat sheet only shows a tab strip when BOTH panels exist.
        var cut = RenderEditor(TmLayoutMode.Mobile);
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("precondition: the sheet is closed");

        var shell = cut.FindComponent<TmEditorShell>();
        await cut.InvokeAsync(() => shell.Instance.LeftOpenChanged.InvokeAsync(true));

        cut.Find(".tm-editor-shell__sheet").Should().NotBeNull();
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .Select(t => t.TextContent.Trim()).Should().Equal("Shapes", "Properties", "Layers");
    }
}
