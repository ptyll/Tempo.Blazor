using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Layout;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

/// <summary>
/// Fáze 8 review, U2/D27 — the shell raises its layout callback BEFORE the collapse callback, so the
/// diagram editor has to tell a rail the LAYOUT imposed from a collapse the USER asked for. Before the
/// generation guard a layout flip was read as "the user closed the inspector": the inspector
/// disappeared with the node selection on a desktop → tablet → desktop trip.
/// <para>
/// These are the regressions the 1440 E2E spec measured, pinned in bUnit against the real
/// <see cref="TmLayoutObserver"/> raise the browser's measurement produces.
/// </para>
/// </summary>
public class TmDiagramEditorShellEventOrderTests : DiagramTestBase
{
    private IRenderedComponent<TmDiagramEditor> RenderEditor(out DiagramDocument document)
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            Id = "n1", StencilId = "rect", X = 120, Y = 80, W = 160, H = 80,
        });
        document = doc;

        // LayoutMode stays Auto: the shell starts at its InitialMode (desktop) and the observer's
        // measurement below is what flips it, exactly like the browser's container report.
        return Render<TmDiagramEditor>(parameters => parameters
            .Add(p => p.Document, doc));
    }

    [Fact]
    public async Task LayoutFlip_TabletThenDesktop_KeepsTheInspectorOpenAndTheSelection()
    {
        var cut = RenderEditor(out _);

        // Desktop first frame: docked inspector (Properties | Layers) and the palette next to it.
        cut.FindAll("[data-region='right'] .tm-diagram-editor__inspector").Should().ContainSingle();

        // Select a node through the canvas callback the JS layer invokes — the selection is what the
        // inspector edits and what renders the resize handles in the decorator pane.
        await cut.InvokeAsync(() => cut.FindComponent<TmDiagramCanvas>().Instance.JsOnSelectionChanged(["n1"]));
        cut.FindAll(".tm-diagram-node-handles[data-node-id='n1']").Should().ContainSingle(
            "a selected node renders its resize/rotate handles");

        var observer = cut.FindComponent<TmLayoutObserver>();

        // ── The container narrows into the tablet band ──────────────────────
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("tablet"));
        cut.Find(".tm-diagram-editor").GetAttribute("data-editor-layout").Should().Be("tablet");
        cut.FindAll("[data-region='right'] .tm-diagram-editor__inspector").Should().ContainSingle(
            "the layout's own rail (CollapsedPanels=Left) must not be adopted as a user hide");
        cut.FindAll("[data-region='rail-left']").Should().ContainSingle(
            "tablet rails the palette and keeps the inspector expanded");
        cut.FindAll(".tm-diagram-node-handles[data-node-id='n1']").Should().ContainSingle(
            "the selection survives the tablet flip");

        // ── And back to desktop ─────────────────────────────────────────────
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("desktop"));
        cut.Find(".tm-diagram-editor").GetAttribute("data-editor-layout").Should().Be("desktop");
        cut.FindAll("[data-region='right'] .tm-diagram-editor__inspector").Should().ContainSingle(
            "the inspector is still open after the trip back to desktop (U2/D27)");
        cut.FindAll(".tm-diagram-node-handles[data-node-id='n1']").Should().ContainSingle(
            "the canvas keeps the selection and its resize handles across the layout flip");
    }

    [Fact]
    public async Task DesktopShellToggle_ClosesTheInspector_UserCollapseIsAdopted()
    {
        var cut = RenderEditor(out _);
        cut.FindAll("[data-region='right'] .tm-diagram-editor__inspector").Should().ContainSingle();

        // No layout change happened in this batch: the collapse raise is the USER's, so the editor must
        // adopt it and hide the inspector (the generation guard only excuses layout-caused raises).
        cut.Find(".tm-editor-shell__panel-toggle--right").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-diagram-editor__inspector").Should().BeEmpty(
                "a user collapse on desktop hides the whole inspector");
            cut.Find("[data-testid='diagram-editor-toggle-inspector']").GetAttribute("aria-pressed")
                .Should().Be("false", "the header toggle reports the closed inspector");
        });
    }
}
