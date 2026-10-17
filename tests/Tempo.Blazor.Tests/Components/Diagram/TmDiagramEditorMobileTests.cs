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
/// presentation — a non-modal bottom sheet (peek/half/full) with the Shapes | Properties |
/// Layers surfaces, the F5 action bar (Select | Pan | Properties | More), a floating zoom
/// overlay and the floating minimap, and the long-press context menu as a sheet. The full
/// editing chrome (resize/rotate handles, connect arrows, layout and import) stays tablet+.
/// Markup-level contract only: CSS cannot be asserted in bUnit.
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

    // ── The bottom sheet: Shapes | Properties | Layers ──────────────────────

    [Fact]
    public void BottomSheet_TabSwitch_ShowsShapesPropertiesLayers()
    {
        var cut = RenderMobileEditor();

        // Both panels open: the non-modal sheet renders inline over the canvas stage.
        var sheet = cut.Find(".tm-editor-shell__sheet");
        sheet.ClassList.Should().Contain("tm-drawer--inline", "the panels sheet is non-modal");
        sheet.ClassList.Should().Contain("tm-drawer--bottom");

        // The sheet header offers the Shapes | Inspector tab strip (scoped to the shell's own
        // tablist: the inspector inside carries its own Properties | Layers strip).
        var tabs = sheet.QuerySelectorAll(".tm-editor-shell__tabs [role='tab']");
        tabs.Should().HaveCount(2);
        tabs[0].TextContent.Trim().Should().Be("Shapes");
        tabs[0].GetAttribute("aria-selected").Should().Be("true", "the palette tab is the default");
        tabs[1].GetAttribute("aria-selected").Should().Be("false");

        // Shapes tab: the stencil palette.
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeFalse();
        cut.Find(".tm-diagram-toolbox").Should().NotBeNull();

        // Switch to the inspector tab: the properties panel shows.
        tabs[1].Click();
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeTrue();
        var inspectorPanel = cut.Find("[role='tabpanel'][data-region='right']");
        inspectorPanel.HasAttribute("hidden").Should().BeFalse();
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull();
        cut.Find(".tm-diagram-properties").Should().NotBeNull("the Properties surface is the inspector's first tab");
        cut.FindAll(".tm-diagram-layers").Should().BeEmpty();

        // The inspector keeps its Properties | Layers tab strip (DI2): Layers is reachable.
        var layerTab = cut.FindAll(".tm-diagram-editor__inspector [role='tab']")
            .Single(t => t.TextContent.Trim() == "Layers");
        layerTab.Click();
        cut.Find(".tm-diagram-layers").Should().NotBeNull("the Layers surface is reachable in the sheet");
        cut.FindAll(".tm-diagram-properties").Should().BeEmpty();
    }

    [Fact]
    public async Task SelectingNode_OpensPropertiesTab_Peek()
    {
        var cut = RenderMobileEditor();

        // Precondition: the sheet is closed (the user collapsed it through the shell close).
        cut.Find(".tm-editor-shell__sheet-close").Click();
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("precondition: the sheet is closed");

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

    // ── The action bar: Select | Pan | Properties | More ────────────────────

    [Fact]
    public void ActionBar_HasSelectPanPropertiesMore()
    {
        var cut = RenderMobileEditor();

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.Should().NotBeNull("the F5 action bar renders on a mobile layout");

        var tiles = bar.QuerySelectorAll(".tm-mobile-action-bar__action");
        tiles.Select(t => t.GetAttribute("data-action-id")).Should().Equal("select", "pan", "properties");

        // The More trigger closes the bar (the export entries live in its menu).
        bar.QuerySelectorAll(".tm-mobile-action-bar__more").Should().HaveCount(1);
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
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnNodeContextMenu("n1", 20, 20));

        // The fixed viewport menu must not render on mobile…
        cut.FindAll(".tm-diagram-editor__context-menu").Should().BeEmpty(
            "the position:fixed menu is the desktop presentation only");
        // …the long-press menu is the shared F3 bottom sheet.
        var sheet = cut.Find(".tm-overlay-panel-sheet");
        sheet.GetAttribute("aria-label").Should().Be("Actions");
        var items = sheet.QuerySelectorAll("[role='menuitem']").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Delete");
        items.Should().Contain("Duplicate");
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
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnCanvasContextMenu(50, 50, 20, 20));

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
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "viewing keeps the floating minimap");
    }

    [Fact]
    public void ReadOnly_Mobile_HasNoEditActionBar()
    {
        var cut = RenderMobileEditor(extra: p => p.Add(e => e.ReadOnly, true));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty(
            "a host-pinned read-only editor has no edit action bar on mobile");
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
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
    public void MobileChrome_MinimapIsOverlayNotCard()
    {
        var cut = RenderMobileEditor();

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull();
        cut.FindAll(".tm-diagram-editor__minimap-card").Should().BeEmpty();
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
