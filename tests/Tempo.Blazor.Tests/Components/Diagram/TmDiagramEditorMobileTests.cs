using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Diagram;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// DiagramEditor redesign phase 7 (G2 — mobile viewing and light edits): with a forced
/// <see cref="TmLayoutMode.Mobile"/> the editor renders through the core shell mobile
/// presentation — a non-modal bottom sheet (peek/half/full) with the flat Shapes | Properties |
/// Layers tabs, the F5 action bar (Select | Shapes | Properties, Pan and exports in More), a
/// floating zoom overlay and the opt-in floating minimap ("⋯"), and the long-press context
/// menu as a sheet. The full editing chrome (resize/rotate handles, connect arrows, layout and
/// import) stays tablet+. Markup-level contract only: CSS cannot be asserted in bUnit.
/// </summary>
public class TmDiagramEditorMobileTests : DiagramTestBase
{
    private static DiagramDocument CreateDocument()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        return doc;
    }

    private IRenderedComponent<TmDiagramEditor> RenderMobileEditor(
        DiagramDocument? doc = null,
        Action<ComponentParameterCollectionBuilder<TmDiagramEditor>>? extra = null)
    {
        return Render<TmDiagramEditor>(p =>
        {
            p.Add(e => e.Document, doc ?? CreateDocument());
            p.Add(e => e.LayoutMode, TmLayoutMode.Mobile);
            extra?.Invoke(p);
        });
    }

    // ── X1: the mobile load state — sheet closed, minimap hidden ────────────

    [Fact]
    public void FirstMobileRender_SheetClosed_MinimapHidden()
    {
        // X1 (breaking): the canvas gets the whole stage on load; the sheet and the minimap
        // are opt-in (the action bar tiles / a selection, and the "⋯" minimap toggle).
        var cut = RenderMobileEditor();

        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("the panels sheet starts closed");
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty("the minimap starts hidden");
        cut.Find(".tm-mobile-action-bar__bar").Should().NotBeNull("the action bar still renders");
    }

    [Fact]
    public void MobileSheetInitiallyOpen_True_OpensThePeekedSheet()
    {
        // The opt-out restores the old open-on-load behaviour.
        var cut = RenderMobileEditor(extra: p => p.Add(e => e.MobileSheetInitiallyOpen, true));

        cut.Find(".tm-editor-shell__sheet").Should().NotBeNull();
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0",
            "the initially-open sheet rests at the PEEK snap");
    }

    // ── The bottom sheet: flat Shapes | Properties | Layers tabs (X2) ───────

    [Fact]
    public void BottomSheet_TabSwitch_ShowsShapesPropertiesLayers()
    {
        var cut = RenderMobileEditor();

        // The sheet starts closed (X1) — the action bar opens it.
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();

        var sheet = cut.Find(".tm-editor-shell__sheet");
        sheet.ClassList.Should().Contain("tm-drawer--inline", "the panels sheet is non-modal");
        sheet.ClassList.Should().Contain("tm-drawer--bottom");

        // X2: the sheet offers THREE flat tabs — no nested inspector TmTabs on mobile.
        var tabs = sheet.QuerySelectorAll(".tm-editor-shell__tabs [role='tab']");
        tabs.Should().HaveCount(3);
        tabs.Select(t => t.TextContent.Trim()).Should().Equal("Shapes", "Properties", "Layers");
        tabs[1].GetAttribute("aria-selected").Should().Be("true",
            "the Properties action opened the sheet on the properties tab");
        sheet.QuerySelectorAll(".tm-tabs").Should().BeEmpty(
            "the mobile sheet has flat tabs — no nested inspector tabs");

        // Shapes tab: the stencil palette (the sheet opened on Properties).
        tabs[0].Click();
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeFalse();
        cut.Find(".tm-diagram-toolbox").Should().NotBeNull();

        // Properties tab: the properties panel shows.
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[1].Click();
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeTrue();
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeFalse();
        cut.Find(".tm-diagram-properties").Should().NotBeNull();
        cut.Find("[role='tabpanel'][data-region='extra']").HasAttribute("hidden").Should().BeTrue(
            "the layers panel stays in the DOM (sheet contract) but hidden");

        // Layers tab (the shell's Extra slot): the layers panel shows.
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[2].Click();
        cut.Find("[role='tabpanel'][data-region='extra']").HasAttribute("hidden").Should().BeFalse();
        cut.Find(".tm-diagram-layers").Should().NotBeNull("the Layers surface is a flat sheet tab");
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeTrue();
    }

    [Fact]
    public void MobileSheet_HasNoDuplicatePropertiesHeading()
    {
        // U4: the sheet tab names the panel — the panel renders no own heading.
        var cut = RenderMobileEditor();
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();

        cut.FindAll(".tm-diagram-properties__title").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .Should().ContainSingle(t => t.TextContent.Trim() == "Properties");
    }

    [Fact]
    public void MobileLayersTab_HasTheEmbeddedLayersPanel()
    {
        // Carry-forward: the layers panel renders embedded (no own collapse toggle), with the
        // row menu presenting as the sheet on the mobile layout.
        var doc = new DiagramDocument();
        doc.Layers.Add(new DiagramLayer { Name = "Base", Order = 0 });
        var cut = RenderMobileEditor(doc);

        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .Single(t => t.TextContent.Trim() == "Layers").Click();

        var layers = cut.Find(".tm-editor-shell__sheet .tm-diagram-layers");
        layers.ClassList.Should().Contain("tm-diagram-layers--embedded");
        cut.FindAll(".tm-editor-shell__sheet .tm-diagram-layers__toggle").Should().BeEmpty();
    }

    [Fact]
    public void LayersRowMenu_MobileLayout_PresentsAsSheet()
    {
        // The editor forces RowMenuPresentation=Sheet on its mobile layout, so a row's "⋯"
        // menu renders the bottom sheet (no measuring needed — a forced value renders as-is).
        var doc = new DiagramDocument();
        doc.Layers.Add(new DiagramLayer { Name = "Base", Order = 0 });
        var cut = RenderMobileEditor(doc);

        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .Single(t => t.TextContent.Trim() == "Layers").Click();
        cut.Find(".tm-diagram-layers").Should().NotBeNull();

        cut.Find(".tm-diagram-layers__row-menu .tm-context-menu__trigger").Click();

        cut.Find(".tm-diagram-layers__row-menu .tm-overlay-panel-sheet").Should().NotBeNull(
            "the editor forces Sheet on its mobile layout — the row menu presents as the bottom sheet");
        cut.Find(".tm-diagram-layers__row-menu .tm-overlay-panel-sheet__title").TextContent.Trim()
            .Should().Be("Base", "the sheet is titled with the layer name");
    }

    [Fact]
    public async Task SelectingNode_OpensPropertiesTab_Peek()
    {
        var cut = RenderMobileEditor();

        // Precondition: the sheet starts closed (X1) — nothing to close.
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("precondition: the sheet is closed at load");

        // Selecting a node is an editing intent: the sheet re-opens peeked on the properties.
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnSelectionChanged(["n1"]));

        var sheet = cut.Find(".tm-editor-shell__sheet");
        sheet.GetAttribute("data-snap-index").Should().Be("0", "a selection opens the sheet at the PEEK snap");
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeFalse(
            "the sheet shows the properties behind the selection");
        cut.Find(".tm-diagram-properties").Should().NotBeNull();
        cut.FindAll("[role='tab'][aria-selected='true']")
            .Should().Contain(t => t.TextContent.Trim() == "Properties");
    }

    [Fact]
    public async Task TapStencil_InsertsNodeAtViewportCenter()
    {
        var doc = CreateDocument();
        var cut = RenderMobileEditor(doc);

        // The sheet starts closed (X1) — open the Shapes tab from the action bar.
        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']").Click();

        var tile = cut.Find(".tm-diagram-toolbox__item[data-stencil-id='general.rectangle']");
        tile.Click();

        doc.Nodes.Should().HaveCount(2, "the tap inserts a shape");
        var inserted = doc.Nodes.Last();
        inserted.StencilId.Should().Be("general.rectangle");

        // bUnit has no JS geometry: GetViewport falls back to the page centre, which is the
        // contract the browser path refines (insert at the VISIBLE viewport centre).
        var page = doc.Pages[doc.ActivePageIndex];
        var centreX = inserted.X + inserted.W / 2.0;
        var centreY = inserted.Y + inserted.H / 2.0;
        (centreX - page.Width / 2.0).Should().BeLessThanOrEqualTo(21.0, "the insert centres on the viewport (grid-snapped)");
        (centreY - page.Height / 2.0).Should().BeLessThanOrEqualTo(21.0);
    }

    [Fact]
    public async Task TapStencil_WithSheetOpen_InsertsIntoTheBandAboveTheSheet()
    {
        var doc = CreateDocument();
        // The open panels sheet (peek) covers the canvas' lower band: JS measures the band that
        // stays visible ABOVE the sheet panel and reports its centre in doc coordinates —
        // pushed ABOVE the viewport centre, which the overlap would hide.
        var page = doc.Pages[doc.ActivePageIndex];
        var bandCentre = (X: page.Width / 2.0, Y: page.Height / 2.0 - 150.0);
        JSInterop.Setup<double[]?>("tmDiagramEditor.visibleBandCentre", _ => true)
            .SetResult([bandCentre.X, bandCentre.Y]);

        var cut = RenderMobileEditor(doc);

        // X1: the sheet starts closed — open the Shapes tab from the action bar first.
        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']").Click();

        var tile = cut.Find(".tm-diagram-toolbox__item[data-stencil-id='general.rectangle']");
        tile.Click();

        // The insert went through the visible-band interop, not the plain viewport centre.
        JSInterop.VerifyInvoke("tmDiagramEditor.visibleBandCentre");

        doc.Nodes.Should().HaveCount(2, "the tap inserts a shape");
        var inserted = doc.Nodes.Last();
        var centreX = inserted.X + inserted.W / 2.0;
        var centreY = inserted.Y + inserted.H / 2.0;

        (page.Height / 2.0 - centreY).Should().BeGreaterThan(100.0,
            "the sheet overlap pushes the insert centre ABOVE the viewport centre");
        centreY.Should().BeApproximately(bandCentre.Y, 4.0,
            "the node lands on the reported band centre (grid-snapped to the default step of 8)");
        centreX.Should().BeApproximately(bandCentre.X, 4.0);
    }

    // ── U3: a selection pans the canvas above the peeked sheet ──────────────

    [Fact]
    public async Task Selection_Mobile_RevealsTheSelectionAboveTheSheet()
    {
        var cut = RenderMobileEditor();
        JSInterop.Setup<bool>("tmDiagramEditor.revealRectAboveSheet", _ => true).SetResult(true);

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnSelectionChanged(["n1"]));

        // The selected node's stored box is handed to the JS pan with a 16px margin (the
        // document grid-snaps coordinates, so read the stored box).
        var node = cut.Instance;
        var n = node.Document.Nodes.Single(x => x.Id == "n1");
        var invocation = JSInterop.Invocations.Single(i => i.Identifier == "tmDiagramEditor.revealRectAboveSheet");
        var args = invocation.Arguments.ToArray();
        args.Should().HaveCountGreaterThanOrEqualTo(6);
        args[1].Should().Be(n.X);
        args[2].Should().Be(n.Y);
        args[3].Should().Be(n.W);
        args[4].Should().Be(n.H);
        args[5].Should().Be(16);
    }

    // ── The action bar: Select | Shapes | Properties + More ─────────────────

    [Fact]
    public void ActionBar_HasSelectShapesPropertiesAndMore()
    {
        var cut = RenderMobileEditor();

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.Should().NotBeNull("the F5 action bar renders on a mobile layout");

        var tiles = bar.QuerySelectorAll(".tm-mobile-action-bar__action");
        tiles.Select(t => t.GetAttribute("data-action-id")).Should().Equal("select", "shapes", "properties");

        // X1: Pan moved into the overflow ("More") with the export commands (as a checkable
        // menuitemcheckbox — Checked is set).
        cut.Find(".tm-mobile-action-bar__more").Click();
        var labels = cut.FindAll(".tm-overlay-panel-sheet [role='menuitem'], .tm-overlay-panel-sheet [role='menuitemcheckbox']")
            .Select(i => i.TextContent.Trim()).ToList();
        labels.Should().Contain("Pan");
    }

    [Fact]
    public void ShapesTile_OpensTheSheetOnShapes()
    {
        var cut = RenderMobileEditor();

        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']").Click();

        cut.Find(".tm-editor-shell__sheet").Should().NotBeNull();
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeFalse();
        cut.Find(".tm-diagram-toolbox").Should().NotBeNull();
        cut.FindAll(".tm-diagram-toolbox__title").Should().BeEmpty("the Shapes tab names the palette (U4)");
    }

    [Fact]
    public void ViewMenu_MinimapToggle_ShowsOverlay()
    {
        // X1: the minimap starts hidden on mobile and the header "⋯" menu toggle is its
        // switch (under the frozen "panels" group semantics).
        var cut = RenderMobileEditor();

        cut.Find("[data-testid='diagram-editor-more']").Click();
        cut.Find(".tm-diagram-editor__view-menu-panel [data-tm-group='panels'] button").Click();

        var overlay = cut.Find(".tm-diagram-editor__minimap-overlay");
        overlay.Should().NotBeNull();
        overlay.QuerySelector(".tm-diagram-minimap").GetAttribute("style")
            .Should().Contain("width:112px", "the mobile minimap is 112px wide (U2/X4)");
    }

    [Fact]
    public void MoreMenu_ContainsExport_NotLayoutOrImport()
    {
        var cut = RenderMobileEditor();

        cut.Find(".tm-mobile-action-bar__more").Click();

        var sheet = cut.Find(".tm-overlay-panel-sheet");
        var labels = sheet.QuerySelectorAll("[role='menuitem']").Select(i => i.TextContent.Trim()).ToList();
        labels.Should().Contain("Export JSON");
        labels.Should().Contain("Export SVG");
        labels.Should().Contain("Export PNG");
        labels.Should().Contain("Export PDF");
        // G2: layout and import are tablet+ commands — they never appear in the mobile More.
        labels.Should().NotContain(l => l.Contains("Layout", StringComparison.OrdinalIgnoreCase));
        labels.Should().NotContain(l => l.Contains("Import", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ActionBar_More_ExportJson_TriggersExport()
    {
        var cut = RenderMobileEditor();
        cut.Find(".tm-mobile-action-bar__more").Click();
        // Selecting the JSON entry runs the same export handler as the desktop toolbar.
        var json = cut.FindAll(".tm-overlay-panel-sheet [role='menuitem']")
            .Single(i => i.TextContent.Trim() == "Export JSON");
        json.Click();
        // Loose JS: the download interop is a no-op mock; the entry must at least close the sheet.
        cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty();
    }

    // ── Full editing stays tablet+ ──────────────────────────────────────────

    [Theory]
    [InlineData(TmLayoutMode.Desktop, 8, 4)]
    [InlineData(TmLayoutMode.Tablet, 8, 4)]
    [InlineData(TmLayoutMode.Mobile, 0, 0)]
    public async Task ResizeRotateAndConnectHandles_FollowTheLayoutBand(
        TmLayoutMode mode, int resizeCount, int connectCount)
    {
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, CreateDocument())
            .Add(e => e.LayoutMode, mode));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnSelectionChanged(["n1"]));

        cut.FindAll(".tm-diagram-resize-handle").Should().HaveCount(resizeCount);
        cut.FindAll(".tm-diagram-rotate-handle").Should().HaveCount(resizeCount > 0 ? 1 : 0);
        cut.FindAll(".tm-diagram-connect-arrow").Should().HaveCount(connectCount);
    }

    // ── Long-press menu: a sheet, not a fixed menu ──────────────────────────

    [Fact]
    public async Task ContextMenu_LongPress_RendersAsSheet()
    {
        var cut = RenderMobileEditor();

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnNodeContextMenu("n1", 20, 20, "touch"));

        // The fixed viewport menu must not render on mobile…
        cut.FindAll(".tm-diagram-editor__context-menu").Should().BeEmpty(
            "the position:fixed menu is the desktop presentation only");
        // …the long-press menu is the shared F3 bottom sheet.
        var sheet = cut.Find(".tm-overlay-panel-sheet");
        sheet.GetAttribute("aria-label").Should().Be("Actions");
        var items = sheet.QuerySelectorAll("[role='menuitem']").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Delete");
        items.Should().Contain("Duplicate");
        // U5/X7: no desktop shortcuts in the touch labels.
        items.Should().Contain("Paste style");
        items.Should().NotContain(l => l.Contains("Ctrl"));
        // G2: align/distribute are tablet+ commands — not in the mobile sheet menu.
        items.Should().NotContain(l => l.Contains("Align", StringComparison.OrdinalIgnoreCase));
        items.Should().NotContain(l => l.Contains("Distribute", StringComparison.OrdinalIgnoreCase));

        // The "Done" action closes it.
        sheet.QuerySelectorAll(".tm-overlay-panel-sheet__done").Should().HaveCount(1);
    }

    [Fact]
    public async Task ContextMenu_MobileNodeMenu_SkipsTheLayoutEntries()
    {
        var cut = RenderMobileEditor();

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnCanvasContextMenu(50, 50, 20, 20, "touch"));

        var items = cut.FindAll(".tm-overlay-panel-sheet [role='menuitem']")
            .Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Select All");
        items.Should().Contain("Undo");
        // G2: the auto-layout section is tablet+.
        items.Should().NotContain(l => l.Contains("layout", StringComparison.OrdinalIgnoreCase));
    }

    // ── Viewer preset: viewing only, no edit action bar ─────────────────────

    [Fact]
    public void ViewerPreset_Mobile_HasNoEditActionBar()
    {
        var cut = RenderMobileEditor(extra: p => p.Add(e => e.ToolbarPreset, DiagramToolbarPreset.Viewer));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty(
            "a Viewer preview has no edit action bar on mobile");
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty(
            "a Viewer preview opens no panels sheet");
        cut.FindAll(".tm-editor-shell__panel-toggle--mobile").Should().BeEmpty(
            "a Viewer preview offers no panels toggle");
        cut.Find(".tm-diagram-editor__zoom-overlay").Should().NotBeNull(
            "viewing keeps the floating zoom overlay");
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty(
            "the minimap is opt-in on mobile — hidden at load even for a viewer");
    }

    [Fact]
    public void ReadOnly_Mobile_HasNoEditActionBar()
    {
        var cut = RenderMobileEditor(extra: p => p.Add(e => e.ReadOnly, true));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty(
            "a host-pinned read-only editor has no edit action bar on mobile");
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
    }

    // ── F11: the mobile surfaces follow the host-made-viewer rule ────────────

    [Fact]
    public void ShowToolbar_False_Mobile_HasNoActionBarAndNoZoomPill()
    {
        var cut = RenderMobileEditor(extra: p => p.Add(e => e.ShowToolbar, false));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty(
            "the action bar is toolbar chrome");
        cut.FindAll(".tm-diagram-editor__zoom-overlay").Should().BeEmpty(
            "the floating zoom pill is toolbar chrome");
        cut.Find(".tm-diagram-canvas, [data-testid='diagram-canvas']").Should().NotBeNull(
            "the canvas still renders");
    }

    [Fact]
    public async Task UserReadOnlyToggle_Mobile_KeepsTheSheetAndActionBar()
    {
        // A4: the user's OWN Read-only toggle must not take the surfaces away — the panels
        // render read-only and the user can always switch back.
        var cut = RenderMobileEditor();

        cut.Find("[data-testid='diagram-editor-more']").Click();
        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.Instance.IsReadOnly.Should().BeTrue("the user toggled read-only");
        cut.Find(".tm-mobile-action-bar__bar").Should().NotBeNull(
            "the user's read-only keeps the action bar");
        cut.FindAll("[data-tm-group='readonly']").Should().NotBeEmpty(
            "the toggle stays reachable so the user can switch back");
        await Task.CompletedTask;
    }

    [Fact]
    public void ViewerGroupSet_ViaToolbarGroups_Mobile_HasNoEditActionBar()
    {
        // The rule reads the EFFECTIVE group set: a ToolbarGroups selection equal to the Viewer
        // preset (zoom | view | search | export) is a viewer, preset or not.
        var cut = RenderMobileEditor(extra: p => p
            .Add(e => e.ToolbarGroups, DiagramToolbarGroups.Zoom | DiagramToolbarGroups.View
                | DiagramToolbarGroups.Search | DiagramToolbarGroups.Export));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
    }

    [Fact]
    public void ViewerPresetWithAllGroups_Mobile_KeepsTheActionBar()
    {
        // An explicit ToolbarGroups selection overrides the preset — with All the editor IS an
        // editing surface (the group set decides, not the preset value).
        var cut = RenderMobileEditor(extra: p => p
            .Add(e => e.ToolbarPreset, DiagramToolbarPreset.Viewer)
            .Add(e => e.ToolbarGroups, DiagramToolbarGroups.All));

        cut.Find(".tm-mobile-action-bar__bar").Should().NotBeNull();
    }

    [Fact]
    public void MoreMenu_Mobile_RendersForSearchAndReadOnlyWithoutViewGroups()
    {
        // On mobile Search and Read-only live ONLY in the "⋯" menu — a host that enables them
        // without the view/canvas-size groups must still get the menu trigger.
        var cut = RenderMobileEditor(extra: p => p
            .Add(e => e.ToolbarGroups, DiagramToolbarGroups.Search | DiagramToolbarGroups.ReadOnly
                | DiagramToolbarGroups.Zoom | DiagramToolbarGroups.History));

        cut.Find("[data-testid='diagram-editor-more']").Should().NotBeNull(
            "the ⋯ menu renders because it carries Search / Read-only");

        cut.Find("[data-testid='diagram-editor-more']").Click();
        var panel = cut.Find(".tm-diagram-editor__view-menu-panel");
        panel.QuerySelectorAll("[data-tm-group='search']").Should().NotBeEmpty();
        panel.QuerySelectorAll("[data-tm-group='readonly']").Should().NotBeEmpty();
    }

    [Fact]
    public void MoreMenu_Mobile_TriggerAndPanelAreNamedMoreActions()
    {
        // On mobile the "⋯" menu is the actions menu (search, read-only, view) — not "View settings".
        var cut = RenderMobileEditor();

        var trigger = cut.Find("[data-testid='diagram-editor-more']");
        trigger.GetAttribute("aria-label").Should().Be("More actions");
        trigger.GetAttribute("title").Should().Be("More actions");
        trigger.GetAttribute("aria-controls").Should().NotBeNullOrEmpty(
            "the trigger points at its panel through a per-instance id");

        trigger.Click();
        var reopenedTrigger = cut.Find("[data-testid='diagram-editor-more']");
        cut.Find(".tm-diagram-editor__view-menu-panel")
            .GetAttribute("aria-label").Should().Be("More actions");
        reopenedTrigger.GetAttribute("aria-controls").Should().Be(
            cut.Find(".tm-diagram-editor__view-menu-panel").GetAttribute("id"));
    }

    // ── Mobile chrome: compact header, floating zoom, no status bar ─────────

    [Fact]
    public void MobileChrome_FloatingZoomOverlay_ReplacesTheStatusBar()
    {
        var cut = RenderMobileEditor();

        cut.Find(".tm-diagram-editor__zoom-overlay").Should().NotBeNull();
        cut.Find(".tm-diagram-editor__zoom-overlay .tm-diagram-editor__zoom-label").Should().NotBeNull();

        cut.FindAll(".tm-diagram-editor__statusbar").Should().BeEmpty(
            "the floating zoom replaces the status bar row on mobile");

        // The header keeps Undo/Redo and the "⋯" view menu, drops the panel toggles.
        cut.FindAll(".tm-diagram-editor__panel-toggle:not(.tm-editor-shell__panel-toggle)").Should().BeEmpty(
            "the docked-panel toggles make no sense on mobile — the panels live in the sheet");
    }

    [Fact]
    public void MobileChrome_MinimapIsOptIn_OverlayNotCard()
    {
        var cut = RenderMobileEditor();

        // X1: hidden at load…
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty(
            "the minimap is opt-in on mobile");
        cut.FindAll(".tm-diagram-editor__minimap-card").Should().BeEmpty();

        // …enabled through the "⋯" toggle, hidden again on a second press.
        cut.Find("[data-testid='diagram-editor-more']").Click();
        cut.Find(".tm-diagram-editor__view-menu-panel [data-tm-group='panels'] button").Click();
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull();

        cut.Find("[data-testid='diagram-editor-more']").Click();
        cut.Find(".tm-diagram-editor__view-menu-panel [data-tm-group='panels'] button").Click();
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty();
    }

    // ── Performance: the sheet never re-renders the canvas ──────────────────

    [Fact]
    public void SheetOpenAndTabSwitch_DoNotReRenderTheCanvas()
    {
        var cut = RenderMobileEditor();
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        var before = canvas.RenderCount;

        // Switch to the inspector through the action bar entry.
        cut.Find(".tm-mobile-action-bar__action[data-action-id='properties']").Click();
        var afterProperties = canvas.RenderCount;

        // And back to the palette through the sheet tab strip (the shell's own control).
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[0].Click();

        canvas.RenderCount.Should().Be(afterProperties,
            "opening or switching the sheet must not re-render TmDiagramCanvas");
        canvas.RenderCount.Should().Be(before,
            "the sheet interactions at rest must not re-render TmDiagramCanvas");
    }
}
