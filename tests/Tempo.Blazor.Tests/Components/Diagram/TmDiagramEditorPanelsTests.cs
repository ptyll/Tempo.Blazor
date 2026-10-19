using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Tests.Diagram;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Phase 6 (tablet and touch, DI3): the editor renders through the core <c>TmEditorShell</c>
/// tablet presentation — docked panels with at most one expanded side (the palette rails to a
/// 44px strip), the minimap floats over the canvas as an overlay instead of a right-column
/// card, and selecting a node raises the inspector back up when another panel took the dock.
/// </summary>
public class TmDiagramEditorPanelsTests : DiagramTestBase
{
    private static DiagramDocument CreateDocument()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        return doc;
    }

    private IRenderedComponent<TmDiagramEditor> RenderTabletEditor(
        DiagramDocument doc,
        Action<ComponentParameterCollectionBuilder<TmDiagramEditor>>? extra = null)
    {
        return Render<TmDiagramEditor>(p =>
        {
            p.Add(e => e.Document, doc);
            p.Add(e => e.LayoutMode, TmLayoutMode.Tablet);
            extra?.Invoke(p);
        });
    }

    [Fact]
    public void TabletMode_ExpandingInspector_RailsTheToolbox()
    {
        // Tablet default: the inspector (right) is expanded, the palette (left) renders as the rail.
        var cut = RenderTabletEditor(CreateDocument());

        cut.FindAll(".tm-editor-shell__rail--left").Should().NotBeEmpty(
            "on a tablet layout the palette renders as the shell rail");
        cut.FindAll(".tm-diagram-toolbox").Should().BeEmpty(
            "the railed palette is not rendered as a panel");
        cut.FindAll(".tm-editor-shell__rail--right").Should().BeEmpty(
            "the inspector stays docked while it owns the single expanded slot");
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull();
    }

    [Fact]
    public void TabletMode_ExpandingToolbox_RailsTheInspector()
    {
        var cut = RenderTabletEditor(CreateDocument());

        // The rail's expand toggle swaps the single expanded slot to the palette.
        cut.Find(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle").Click();

        cut.Find(".tm-diagram-toolbox").Should().NotBeNull("the expanded palette renders as a panel again");
        cut.FindAll(".tm-editor-shell__rail--right").Should().NotBeEmpty(
            "the inspector took the rail so the canvas keeps its width");
        cut.FindAll(".tm-diagram-editor__inspector").Should().BeEmpty();
    }

    [Fact]
    public async Task SelectingNode_ExpandsDockedInspector_WhenTablet()
    {
        var doc = CreateDocument();
        var cut = RenderTabletEditor(doc);

        // Give the palette the expanded slot: the inspector rails.
        cut.Find(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle").Click();
        cut.FindAll(".tm-editor-shell__rail--right").Should().NotBeEmpty("precondition: inspector railed");

        // Selecting a node on the canvas raises the inspector back up (rails the palette).
        // The canvas raises the editor's OnSelectionChanged through its JS entry point.
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnSelectionChanged(["n1"]));

        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull(
            "a node selection must surface the inspector on a tablet layout");
        cut.FindAll(".tm-editor-shell__rail--right").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__rail--left").Should().NotBeEmpty(
            "the palette returned to the rail — only one panel stays expanded");
    }

    [Fact]
    public void TabletMode_InspectorToggle_RailsAndExpandsTheInspector()
    {
        var cut = RenderTabletEditor(CreateDocument());

        // Default tablet state: the inspector is expanded (the palette takes the rail).
        var toggle = cut.Find("[data-testid='diagram-editor-toggle-inspector']");
        toggle.GetAttribute("aria-pressed").Should().Be("true");
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull();

        toggle.Click();
        cut.FindAll(".tm-editor-shell__rail--right").Should().NotBeEmpty(
            "on tablet the collapsed inspector is the rail, never hidden");
        cut.Find("[data-testid='diagram-editor-toggle-inspector']")
            .GetAttribute("aria-pressed").Should().Be("false");

        cut.Find("[data-testid='diagram-editor-toggle-inspector']").Click();
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull("the toggle expands the inspector again");
        cut.Find("[data-testid='diagram-editor-toggle-inspector']")
            .GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public async Task TabletMode_Selection_RaisesTheRailedInspectorOnThePropertiesTab()
    {
        // Pin behaviour: the Layers tab choice does not survive a selection raise — the
        // selection re-opens the inspector on the Properties tab (the editing context).
        var cut = RenderTabletEditor(CreateDocument());

        // Choose the Layers tab…
        cut.FindAll(".tm-diagram-editor__inspector [role='tab']")
            .Single(t => t.TextContent.Trim() == "Layers").Click();
        // …rail the inspector through the header toggle…
        cut.Find("[data-testid='diagram-editor-toggle-inspector']").Click();
        cut.FindAll(".tm-editor-shell__rail--right").Should().NotBeEmpty("precondition: inspector railed");

        // …and select a node: the inspector expands again, with the Properties tab active.
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnSelectionChanged(["n1"]));

        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull("the selection raises the inspector");
        cut.FindAll(".tm-editor-shell__rail--right").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__inspector [role='tab']")
            .Single(t => t.GetAttribute("aria-selected") == "true")
            .TextContent.Trim().Should().Be("Properties", "the selection pins the tab to Properties");
    }

    [Fact]
    public void TabletMode_MinimapIsOverlay()
    {
        var cut = RenderTabletEditor(CreateDocument());

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "DI3: on a tablet layout the minimap floats over the canvas corner");
        cut.FindAll(".tm-diagram-editor__minimap-card").Should().BeEmpty(
            "the right-column minimap card must not render on a tablet layout");
    }

    [Fact]
    public void TabletMode_Minimap_Is160pxWide()
    {
        // U2/X4: the width comes from the resolved layout (parameter, not CSS), so the tap
        // mapping can never drift from the render.
        var cut = RenderTabletEditor(CreateDocument());

        cut.Find(".tm-diagram-editor__minimap-overlay .tm-diagram-minimap").GetAttribute("style")
            .Should().Contain("width:160px");
    }

    [Fact]
    public void TabletMode_MinimapOverlayCollapse_ShowsReopenButton()
    {
        // X4: the tablet overlay collapses in place and reopens from the floating button.
        var cut = RenderTabletEditor(CreateDocument());

        cut.Find(".tm-diagram-editor__minimap-overlay-collapse").Click();
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty(
            "the in-place collapse hides the overlay");
        cut.Find("[data-testid='diagram-editor-minimap-reopen']").Should().NotBeNull(
            "the floating reopen button takes over");

        cut.Find("[data-testid='diagram-editor-minimap-reopen']").Click();
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull();
    }

    [Fact]
    public void DesktopMode_KeepsMinimapInPanelCard()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-editor__minimap-card").Should().NotBeNull(
            "desktop keeps the minimap card in the right column");
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty();
    }

    [Fact]
    public void MinimapPlacement_Overlay_ForcesTheOverlayOnDesktop()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.LayoutMode, TmLayoutMode.Desktop)
            .Add(e => e.MinimapPlacement, DiagramMinimapPlacement.Overlay));

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull();
        cut.FindAll(".tm-diagram-editor__minimap-card").Should().BeEmpty();
    }

    [Fact]
    public void MinimapPlacement_Panel_ForcesTheCardOnTablet()
    {
        var cut = RenderTabletEditor(CreateDocument(), p => p
            .Add(e => e.MinimapPlacement, DiagramMinimapPlacement.Panel));

        cut.Find(".tm-diagram-editor__minimap-card").Should().NotBeNull();
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty();
    }

    // ── F7: the palette toggle and the rail button drive the same state ──────

    [Fact]
    public void TabletMode_RailButton_ExpandsThePalette()
    {
        // Tablet default: the palette is railed. The rail's labelled button must expand it
        // (before the fix it wrote _toolboxCollapsed=false, which does nothing while railed).
        var cut = RenderTabletEditor(CreateDocument());

        cut.FindAll(".tm-editor-shell__rail--left .tm-diagram-editor__rail-btn").Should().NotBeEmpty(
            "precondition: the palette renders as a rail with its labelled button");
        cut.Find(".tm-editor-shell__rail--left .tm-diagram-editor__rail-btn").Click();

        cut.Find(".tm-diagram-toolbox").Should().NotBeNull("the rail button expands the palette");
        cut.FindAll(".tm-editor-shell__rail--right").Should().NotBeEmpty(
            "the inspector took the rail — only one panel stays expanded");
    }

    [Fact]
    public void TabletMode_HeaderToolboxToggle_RailsAndExpandsThePalette()
    {
        var cut = RenderTabletEditor(CreateDocument());

        // Default tablet state: the palette is railed, so the header toggle reports NOT pressed.
        var toggle = cut.Find("[data-testid='diagram-editor-toggle-toolbox']");
        toggle.GetAttribute("aria-pressed").Should().Be("false",
            "the railed palette is the collapsed form (the inverted report was F7)");

        toggle.Click();
        cut.Find(".tm-diagram-toolbox").Should().NotBeNull("the toggle expands the palette");
        cut.Find("[data-testid='diagram-editor-toggle-toolbox']")
            .GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("[data-testid='diagram-editor-toggle-toolbox']").Click();
        cut.FindAll(".tm-editor-shell__rail--left").Should().NotBeEmpty(
            "on tablet the collapsed palette is the rail, never hidden");
        cut.FindAll(".tm-diagram-toolbox").Should().BeEmpty();
    }

    [Fact]
    public void DesktopMode_HeaderToolboxToggle_AfterShellCollapse_ReopensDockedNotRailed()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.LayoutMode, TmLayoutMode.Desktop));

        // Collapse the palette through the shell's own arrow.
        cut.Find(".tm-editor-shell__panel-toggle--left[aria-expanded='true']").Click();
        cut.FindAll(".tm-diagram-toolbox").Should().BeEmpty("precondition: the palette is collapsed");

        // The header toggle must bring it back DOCKED — on desktop no stale rail may appear.
        cut.Find("[data-testid='diagram-editor-toggle-toolbox']").Click();

        cut.Find(".tm-diagram-toolbox").Should().NotBeNull();
        cut.FindAll(".tm-editor-shell__rail--left").Should().BeEmpty(
            "a desktop panel reopens docked, not as a rail");
    }

    [Fact]
    public void HeaderToolboxToggle_InvokesCanvasRelayout()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.LayoutMode, TmLayoutMode.Desktop));

        var before = JSInterop.Invocations.Count(i => i.Identifier == "tmDiagramEditor.relayout");
        cut.Find("[data-testid='diagram-editor-toggle-toolbox']").Click();
        var after = JSInterop.Invocations.Count(i => i.Identifier == "tmDiagramEditor.relayout");

        after.Should().BeGreaterThan(before,
            "expanding/collapsing a docked panel must re-measure the canvas viewport");
    }

    // ── F10: the minimap overlay respects its toggle; no empty right dock ────

    [Fact]
    public void TabletMode_HeaderMinimapToggle_HidesTheOverlay()
    {
        var cut = RenderTabletEditor(CreateDocument());

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "precondition: the floating minimap renders on tablet");
        cut.Find("[data-testid='diagram-editor-toggle-minimap']").Click();
        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty(
            "the header toggle hides the overlay (it used to flip only aria-pressed)");

        cut.Find("[data-testid='diagram-editor-toggle-minimap']").Click();
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull();
    }

    [Fact]
    public void TabletMode_InspectorClosed_HasNoEmptyRightDock()
    {
        // On tablet the minimap is an overlay over the canvas — it must not keep an empty
        // right dock open once the inspector is gone.
        var cut = RenderTabletEditor(CreateDocument(), p => p
            .Add(e => e.ShowPropertiesPanel, false)
            .Add(e => e.ShowLayersPanel, false));

        cut.FindAll("aside.tm-editor-shell__panel--right").Should().BeEmpty(
            "no empty right dock without the inspector");
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "the minimap still floats over the canvas");
    }
}
