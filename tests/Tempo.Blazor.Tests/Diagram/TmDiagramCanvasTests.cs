using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class TmDiagramCanvasTests : DiagramTestBase
{
    [Fact]
    public void ComputeEdgePath_CurvedRouting_ReturnsValidSvgPath()
    {
        var doc = new DiagramDocument
        {
            Title = "Test",
            Width = 1000,
            Height = 1000,
            Nodes =
            [
                new DiagramNode { Id = "n1", StencilId = "rect", X = 0, Y = 0, W = 100, H = 100 },
                new DiagramNode { Id = "n2", StencilId = "rect", X = 200, Y = 0, W = 100, H = 100 }
            ],
            Edges =
            [
                new DiagramEdge { Id = "e1", SourceNodeId = "n1", TargetNodeId = "n2", Routing = "curved", CubicBezier = true }
            ]
        };

        var cut = Render<TmDiagramCanvas>(parameters => parameters
            .Add(p => p.Document, doc));

        var pathElement = cut.Find("path[data-edge-id=\"e1\"]");
        var d = pathElement.GetAttribute("d");
        d.Should().StartWith("M");
        d.Should().Contain("C");
    }

    [Fact]
    public void ComputeEdgePath_StraightRouting_DoesNotContainCurvedCommand()
    {
        var doc = new DiagramDocument
        {
            Title = "Test",
            Width = 1000,
            Height = 1000,
            Nodes =
            [
                new DiagramNode { Id = "n1", StencilId = "rect", X = 0, Y = 0, W = 100, H = 100 },
                new DiagramNode { Id = "n2", StencilId = "rect", X = 200, Y = 0, W = 100, H = 100 }
            ],
            Edges =
            [
                new DiagramEdge { Id = "e1", SourceNodeId = "n1", TargetNodeId = "n2", Routing = "straight" }
            ]
        };

        var cut = Render<TmDiagramCanvas>(parameters => parameters
            .Add(p => p.Document, doc));

        var pathElement = cut.Find("path[data-edge-id=\"e1\"]");
        var d = pathElement.GetAttribute("d");
        d.Should().StartWith("M");
        d.Should().NotContain("C");
    }

    [Fact]
    public void ToggleLayerVisibility_HidesNodesOnLayer()
    {
        var layer1 = new DiagramLayer { Id = "l1", Name = "Layer 1", Order = 0, IsVisible = true };
        var layer2 = new DiagramLayer { Id = "l2", Name = "Layer 2", Order = 1, IsVisible = true };
        var doc = new DiagramDocument
        {
            Title = "Test",
            Width = 1000,
            Height = 1000,
            Layers = { layer1, layer2 },
            Nodes =
            [
                new DiagramNode { Id = "n1", StencilId = "rect", X = 0, Y = 0, W = 100, H = 100, LayerId = "l1" },
                new DiagramNode { Id = "n2", StencilId = "rect", X = 200, Y = 0, W = 100, H = 100, LayerId = "l2" }
            ]
        };

        var cut = Render<TmDiagramCanvas>(parameters => parameters
            .Add(p => p.Document, doc));

        cut.FindAll("[data-node-id]").Count.Should().Be(2);

        layer1.IsVisible = false;
        cut.Render(parameters => parameters.Add(p => p.Document, doc));

        var nodes = cut.FindAll("[data-node-id]");
        nodes.Count.Should().Be(1);
        nodes[0].GetAttribute("data-node-id").Should().Be("n2");
    }

    // ── Phase 6 (tablet and touch): the resize / rotate handles ──────────────

    private static DiagramDocument CreateSelectedNodeDocument()
        => new()
        {
            Title = "Test",
            Width = 1000,
            Height = 1000,
            Nodes = { new DiagramNode { Id = "n1", StencilId = "rect", X = 100, Y = 100, W = 120, H = 80 } }
        };

    private IRenderedComponent<TmDiagramCanvas> RenderSelectedNode()
    {
        var cut = Render<TmDiagramCanvas>(parameters => parameters
            .Add(p => p.Document, CreateSelectedNodeDocument())
            .Add(p => p.ReadOnly, false));
        cut.InvokeAsync(async () => await cut.Instance.SetSelection("n1"));
        return cut;
    }

    [Fact]
    public void ResizeHandles_UsePointerEvents_AndHitAreaClass()
    {
        var cut = RenderSelectedNode();

        // bUnit does not render event-handler attributes into the DOM: a handler's presence is
        // proven by dispatching through it (an unhandled event throws). Each dispatch re-renders
        // (the mouse pointerdown arms the resize state), so the hits are re-found every round.
        for (var i = 0; i < 8; i++)
        {
            var hits = cut.FindAll(".tm-diagram-resize-handle-hit").ToList();
            hits.Should().HaveCount(8, "every resize handle carries an enlarged invisible hit area");
            hits[i].Invoking(h => h.TriggerEvent("onpointerdown",
                    new Microsoft.AspNetCore.Components.Web.PointerEventArgs { PointerType = "mouse" }))
                .Should().NotThrow("the resize gesture starts from Pointer Events so a finger can drive it");
        }

        // The visible 8x8 squares stay decorative; the hit rect carries the gesture.
        cut.FindAll(".tm-diagram-resize-handle").Should().HaveCount(8);

        var rotateHit = cut.Find(".tm-diagram-rotate-handle-hit");
        rotateHit.Invoking(h => h.TriggerEvent("onpointerdown",
                new Microsoft.AspNetCore.Components.Web.PointerEventArgs { PointerType = "touch" }))
            .Should().NotThrow("the rotate gesture starts from Pointer Events too");
    }

    [Fact]
    public void RotateHandle_TouchPress_DoesNotUseTheMousePath()
    {
        var cut = RenderSelectedNode();

        cut.Find(".tm-diagram-rotate-handle-hit").TriggerEvent("onpointerdown",
            new Microsoft.AspNetCore.Components.Web.PointerEventArgs
            {
                PointerType = "touch",
                ClientX = 160,
                ClientY = 30,
            });

        JSInterop.Invocations
            .Where(i => i.Identifier == "tmDiagramEditor.startRotate")
            .Should().BeEmpty("a finger rotation is driven by the pointer-drag session, not the mouse path");
    }

    [Fact]
    public void RotateHandle_MousePress_StartsRotationThroughJS()
    {
        var cut = RenderSelectedNode();

        cut.Find(".tm-diagram-rotate-handle-hit").TriggerEvent("onpointerdown",
            new Microsoft.AspNetCore.Components.Web.PointerEventArgs
            {
                PointerType = "mouse",
                ClientX = 160,
                ClientY = 30,
            });

        JSInterop.Invocations
            .Where(i => i.Identifier == "tmDiagramEditor.startRotate")
            .Should().ContainSingle("the mouse keeps its direct rotation path");
    }
}
