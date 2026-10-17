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
    public void TabletMode_MinimapIsOverlay()
    {
        var cut = RenderTabletEditor(CreateDocument());

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "DI3: on a tablet layout the minimap floats over the canvas corner");
        cut.FindAll(".tm-diagram-editor__minimap-card").Should().BeEmpty(
            "the right-column minimap card must not render on a tablet layout");
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
}
