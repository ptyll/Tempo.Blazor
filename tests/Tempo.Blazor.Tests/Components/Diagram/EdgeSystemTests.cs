using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class EdgeSystemTests : LocalizationTestBase
{
    public EdgeSystemTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public void EdgeLabel_TextIsWrappedInPaperPill()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Label = "Test"
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Find(".tm-diagram-edge-label .tm-diagram-edge-label__text").TextContent.Trim()
            .Should().Be("Test");
    }

    [Fact]
    public void ComputeEdgePath_WithRounded_ContainsQuadraticBezier()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Routing = "orthogonal",
            Rounded = true,
            Waypoints = [new DiagramPoint(100, 25), new DiagramPoint(150, 25), new DiagramPoint(200, 25)]
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var path = cut.Find("path.tm-diagram-edge-path");
        var d = path.GetAttribute("d");
        d.Should().Contain("Q");
    }

    [Fact]
    public void ComputeEdgePath_WithEndArrowClassic_RendersStandaloneArrowhead()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            EndArrow = "classic",
            EndArrowSize = 10,
            Style = new DiagramStyle { Stroke = "#ff0000" }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var arrowhead = cut.Find("path.tm-diagram-arrowhead");
        arrowhead.Should().NotBeNull();
        arrowhead.GetAttribute("d").Should().Be("M0,0 L0,10 L9,5 z");
    }

    [Fact]
    public void ComputeEdgePath_WithDashPattern_DashArrayIsRendered()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Style = new DiagramStyle { StrokeDashPattern = "dashed" }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var path = cut.Find("path.tm-diagram-edge-path");
        path.GetAttribute("stroke-dasharray").Should().Be("5,5");
    }

    [Fact]
    public void ComputeEdgePath_WithJumpStyleArc_ContainsArcCommand()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 100, W = 100, H = 50 };
        var n3 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 200, W = 100, H = 50 };
        var n4 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 300, W = 100, H = 50 };
        doc.Nodes.AddRange([n1, n2, n3, n4]);

        // Horizontal edge crossing vertical edge
        var edgeH = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Routing = "orthogonal",
            JumpStyle = "arc",
            Waypoints = [new DiagramPoint(100, 50), new DiagramPoint(200, 50), new DiagramPoint(200, 100)]
        };
        var edgeV = new DiagramEdge
        {
            SourceNodeId = n3.Id,
            TargetNodeId = n4.Id,
            Routing = "orthogonal",
            JumpStyle = "arc",
            Waypoints = [new DiagramPoint(100, 200), new DiagramPoint(100, 250), new DiagramPoint(200, 250)]
        };
        // Adjust so they cross at (100,250) and (100,50)? Let's make simpler crossing:
        // Edge H: (50,150) -> (250,150) horizontal
        // Edge V: (150,50) -> (150,250) vertical
        edgeH.Waypoints = [new DiagramPoint(50, 150), new DiagramPoint(250, 150)];
        edgeV.Waypoints = [new DiagramPoint(150, 50), new DiagramPoint(150, 250)];
        doc.Edges.Add(edgeH);
        doc.Edges.Add(edgeV);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var paths = cut.FindAll("path.tm-diagram-edge-path");
        var d = paths[0].GetAttribute("d");
        d.Should().Contain("A");
    }

    [Fact]
    public void GetEdgePoints_WithSourceAndTargetSpacing_AppliesSpacing()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 0, Y = 0, W = 100, H = 50,
            Ports = [new DiagramPort { Side = PortSide.Right, Offset = 0.5 }]
        };
        var n2 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 200, Y = 0, W = 100, H = 50,
            Ports = [new DiagramPort { Side = PortSide.Left, Offset = 0.5 }]
        };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            SourceSpacing = 20,
            TargetSpacing = 15,
            EndArrow = "none"
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var path = cut.Find("path.tm-diagram-edge-path");
        var d = path.GetAttribute("d");
        // Source port is at (100,25), spacing 20 to right => (120,25)
        // Target port is at (200,25), spacing 15 to left => (185,25)
        d.Should().Contain("M 120 25");
        d.Should().Contain("L 185 25"); // line ends exactly at target spacing (no automatic inset)
    }

    [Fact]
    public void InsertEdgeWaypointCommand_AddsAndRemovesWaypoint()
    {
        var doc = new DiagramDocument();
        var stack = new DiagramCommandStack();
        var edge = new DiagramEdge { SourceNodeId = "a", TargetNodeId = "b" };
        edge.Waypoints.Add(new DiagramPoint(10, 10));
        doc.Edges.Add(edge);

        var cmd = new InsertEdgeWaypointCommand(doc, edge.Id, 1, new DiagramPoint(20, 20));
        cmd.Execute();
        edge.Waypoints.Count.Should().Be(2);
        edge.Waypoints[1].X.Should().Be(20);

        cmd.Undo();
        edge.Waypoints.Count.Should().Be(1);
    }

    [Fact]
    public void DeleteEdgeWaypointCommand_RemovesAndRestoresWaypoint()
    {
        var doc = new DiagramDocument();
        var edge = new DiagramEdge { SourceNodeId = "a", TargetNodeId = "b" };
        edge.Waypoints.Add(new DiagramPoint(10, 10));
        edge.Waypoints.Add(new DiagramPoint(20, 20));
        doc.Edges.Add(edge);

        var cmd = new DeleteEdgeWaypointCommand(doc, edge.Id, 1, new DiagramPoint(20, 20));
        cmd.Execute();
        edge.Waypoints.Count.Should().Be(1);

        cmd.Undo();
        edge.Waypoints.Count.Should().Be(2);
        edge.Waypoints[1].X.Should().Be(20);
    }

    [Fact]
    public void UpdateEdgeStyleCommand_ApplyAndUndo()
    {
        var doc = new DiagramDocument();
        var edge = new DiagramEdge
        {
            SourceNodeId = "a",
            TargetNodeId = "b",
            StartArrow = "none",
            EndArrow = "classic",
            Style = new DiagramStyle { Stroke = "#000000", StrokeWidth = 1 }
        };
        doc.Edges.Add(edge);

        var before = DiagramEdgeStyleSnapshot.FromEdge(edge);
        edge.EndArrow = "block";
        edge.Style.Stroke = "#ff0000";
        edge.Style.StrokeWidth = 2;
        var after = DiagramEdgeStyleSnapshot.FromEdge(edge);

        // reset to before for command test
        before.ApplyTo(edge);

        var cmd = new UpdateEdgeStyleCommand(doc, edge.Id, before, after);
        cmd.Execute();
        edge.EndArrow.Should().Be("block");
        edge.Style.Stroke.Should().Be("#ff0000");
        edge.Style.StrokeWidth.Should().Be(2);

        cmd.Undo();
        edge.EndArrow.Should().Be("classic");
        edge.Style.Stroke.Should().Be("#000000");
        edge.Style.StrokeWidth.Should().Be(1);
    }

    [Fact]
    public void UpdateEdgeRoutingCommand_ChangesRoutingAndWaypoints()
    {
        var doc = new DiagramDocument();
        var edge = new DiagramEdge
        {
            SourceNodeId = "a",
            TargetNodeId = "b",
            Routing = "straight",
            Waypoints = [new DiagramPoint(10, 10)]
        };
        doc.Edges.Add(edge);

        var oldWps = edge.Waypoints.Select(p => new DiagramPoint(p.X, p.Y)).ToList();
        var newWps = new List<DiagramPoint> { new(20, 20), new(30, 30) };

        var cmd = new UpdateEdgeRoutingCommand(doc, edge.Id, "straight", "orthogonal", oldWps, newWps);
        cmd.Execute();
        edge.Routing.Should().Be("orthogonal");
        edge.Waypoints.Count.Should().Be(2);

        cmd.Undo();
        edge.Routing.Should().Be("straight");
        edge.Waypoints.Count.Should().Be(1);
        edge.Waypoints[0].X.Should().Be(10);
    }

    [Fact]
    public async Task ComputeOrthogonalWaypointsAsync_PassesObstaclesExcludingSourceAndTarget()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 0, W = 100, H = 50 };
        var n3 = new DiagramNode { StencilId = "general.rectangle", X = 150, Y = 0, W = 50, H = 50 }; // obstacle
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);
        doc.Nodes.Add(n3);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Routing = "orthogonal"
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        var _ = await cut.Instance.ComputeOrthogonalWaypointsAsync(edge);

        var invocation = JSInterop.Invocations
            .Where(i => i.Identifier == "tmDiagramEditor.computeOrthogonalWaypoints")
            .LastOrDefault();

        invocation.Should().NotBeNull();
        // Arguments: x1, y1, side1, x2, y2, side2, routing, sourceSpacing, targetSpacing, srcBounds, tgtBounds, obstacles, elbowOrientation
        var obstacles = invocation.Arguments[11] as System.Collections.Generic.IEnumerable<object>;
        obstacles.Should().NotBeNull();
        var obstacleList = obstacles!.ToList();
        obstacleList.Count.Should().Be(1);
    }

    [Fact]
    public void StencilShape_GetSectionContent_WithMathJaxEnabled_WrapsMathInSpan()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 0, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { EnableMathJax = true },
            Data = { ["label"] = "$$x^2$$" }
        };
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var mathSpan = cut.Find(".tm-diagram-math");
        mathSpan.Should().NotBeNull();
        mathSpan.TextContent.Should().Contain("x^2");
    }

    [Fact]
    public void StencilShape_GetSectionContent_WithMathJaxDisabled_ReturnsPlainText()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 0, Y = 0, W = 100, H = 50,
            Style = new DiagramStyle { EnableMathJax = false },
            Data = { ["label"] = "$$x^2$$" }
        };
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var nodeText = cut.Find(".tm-diagram-node__text");
        nodeText.InnerHtml.Should().NotContain("tm-diagram-math");
        nodeText.TextContent.Should().Contain("$$x^2$$");
    }

    [Fact]
    public void EdgeLabel_WithMathJaxEnabled_RendersForeignObject()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Label = "$$x^2$$",
            Style = new DiagramStyle { EnableMathJax = true }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        // After F3 per-node <foreignObject>s exist too; pick the one that
        // actually holds the MathJax label (class="tm-diagram-math") rather
        // than the first foreignObject in document order.
        var mathFo = cut.FindAll("foreignObject")
            .FirstOrDefault(fo => fo.InnerHtml.Contains("tm-diagram-math", StringComparison.Ordinal));
        mathFo.Should().NotBeNull();
    }

    [Fact]
    public void OnMathSvgCached_StoresSvgInNodeData()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Instance.JsOnMathSvgCached(n1.Id, "<svg></svg>");
        n1.Data["__mathSvg"].Should().Be("<svg></svg>");
    }

    [Fact]
    public void ComputeEdgePath_WithEndArrowFillFalse_RendersUnfilledArrowhead()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            EndArrow = "classic",
            EndArrowFill = false,
            Style = new DiagramStyle { Stroke = "#000000" }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var arrowhead = cut.Find("path.tm-diagram-arrowhead");
        arrowhead.Should().NotBeNull();
        arrowhead.GetAttribute("fill").Should().Be("none");
    }

    [Fact]
    public void ComputeEdgePath_WithEndArrowFillTrue_RendersFilledArrowhead()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            EndArrow = "classic",
            EndArrowFill = true,
            Style = new DiagramStyle { Stroke = "#000000" }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var arrowhead = cut.Find("path.tm-diagram-arrowhead");
        arrowhead.Should().NotBeNull();
        arrowhead.GetAttribute("fill").Should().Be("#000000");
    }

    [Fact]
    public void GetEffectiveFill_WithUserOverride_ReturnsUserValue()
    {
        DiagramArrowheadRegistry.GetEffectiveFill("classic", false).Should().BeFalse();
        DiagramArrowheadRegistry.GetEffectiveFill("classic", true).Should().BeTrue();
        DiagramArrowheadRegistry.GetEffectiveFill("classic", null).Should().BeTrue(); // default for filled
        DiagramArrowheadRegistry.GetEffectiveFill("open", null).Should().BeFalse(); // default for empty
    }

    [Fact]
    public void CanToggleFill_ForLineArrowheads_ReturnsFalse()
    {
        DiagramArrowheadRegistry.CanToggleFill("async").Should().BeFalse();
        DiagramArrowheadRegistry.CanToggleFill("cross").Should().BeFalse();
        DiagramArrowheadRegistry.CanToggleFill("none").Should().BeFalse();
        DiagramArrowheadRegistry.CanToggleFill("classic").Should().BeTrue();
        DiagramArrowheadRegistry.CanToggleFill("open").Should().BeTrue();
    }

    [Fact]
    public void UpdateEdgeStyleCommand_SnapshotIncludesArrowFill()
    {
        var doc = new DiagramDocument();
        var edge = new DiagramEdge
        {
            SourceNodeId = "a",
            TargetNodeId = "b",
            StartArrow = "classic",
            StartArrowFill = false,
            EndArrow = "block",
            EndArrowFill = true,
        };
        doc.Edges.Add(edge);

        var snapshot = DiagramEdgeStyleSnapshot.FromEdge(edge);
        snapshot.StartArrowFill.Should().BeFalse();
        snapshot.EndArrowFill.Should().BeTrue();

        // Modify edge and apply snapshot back
        edge.StartArrowFill = true;
        edge.EndArrowFill = false;
        snapshot.ApplyTo(edge);
        edge.StartArrowFill.Should().BeFalse();
        edge.EndArrowFill.Should().BeTrue();
    }

    [Fact]
    public void LinkShape_RendersTwoPaths()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Shape = "link",
            Style = new DiagramStyle { Stroke = "#000000" }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var paths = cut.FindAll("path.tm-diagram-edge-path");
        paths.Count.Should().Be(2, "link shape should render two parallel paths");
    }

    [Fact]
    public void FilledEdgeShape_RendersThickPath()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Shape = "filledEdge",
            Style = new DiagramStyle { Stroke = "#000000" }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var path = cut.Find("path.tm-diagram-edge-path");
        var strokeWidth = path.GetAttribute("stroke-width");
        strokeWidth.Should().Be("8");
    }

    [Fact]
    public void PipeShape_RendersHollowThickPath()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Shape = "pipe",
            Style = new DiagramStyle { Stroke = "#000000", StrokeWidth = 10 }
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var paths = cut.FindAll("path");
        // Find the white inner path (hollow center) among all paths in the edge group
        var whitePath = paths.FirstOrDefault(p => p.GetAttribute("stroke") == "white");
        whitePath.Should().NotBeNull("pipe shape should render a white inner path for hollow effect");
    }

    [Fact]
    public void WireShape_ForcesRedColorAndDashPattern()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Shape = "wire",
            Style = new DiagramStyle { Stroke = "#000000" } // should be overridden
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var path = cut.Find("path.tm-diagram-edge-path");
        path.GetAttribute("stroke").Should().Be("#ff0000");
        path.GetAttribute("stroke-dasharray").Should().Be("8,8");
    }

    [Fact]
    public void EdgeZIndex_HigherValueRendersLaterInDOM()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edgeLow = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            ZIndex = 1,
            Shape = "connector",
            Style = new DiagramStyle { Stroke = "#000000" }
        };
        var edgeHigh = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            ZIndex = 5,
            Shape = "link",
            Style = new DiagramStyle { Stroke = "#ff0000" }
        };
        doc.Edges.Add(edgeLow);
        doc.Edges.Add(edgeHigh);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var paths = cut.FindAll("path.tm-diagram-edge-path");
        // The higher Z-index edge should render after the lower one in DOM order
        // With link shape (2 paths) + connector (1 path) = 3 paths total
        paths.Count.Should().Be(3);
        // Last path should belong to the high Z-index edge (red color)
        paths[^1].GetAttribute("stroke").Should().Be("#ff0000");
    }

    [Fact]
    public void DoubleLine_WithJumpStyleArc_RendersJumps()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 50, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 50, W = 100, H = 50 };
        var n3 = new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 0, W = 50, H = 150 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);
        doc.Nodes.Add(n3);

        // Horizontal edge crossing vertical edge area
        var edgeH = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Shape = "link",
            Routing = "orthogonal",
            JumpStyle = "arc",
            Waypoints = [new DiagramPoint(50, 75), new DiagramPoint(150, 75)]
        };
        var edgeV = new DiagramEdge
        {
            SourceNodeId = n3.Id,
            TargetNodeId = n3.Id, // self-reference, just for crossing test
            Routing = "orthogonal",
            Waypoints = [new DiagramPoint(125, 0), new DiagramPoint(125, 150)]
        };
        doc.Edges.Add(edgeH);
        doc.Edges.Add(edgeV);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        // link shape should still render 2 paths even with jump style
        var paths = cut.FindAll("path.tm-diagram-edge-path");
        paths.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ArrowSelect_RendersButtonTriggerWithSvg()
    {
        var cut = Render<TmDiagramArrowSelect>(p => p
            .Add(c => c.Value, "classic")
            .Add(c => c.Options, new List<SelectOption<string>>
            {
                new() { Value = "none", Label = "None" },
                new() { Value = "classic", Label = "Classic" }
            }));

        cut.Render();
        var trigger = cut.Find("button.tm-diagram-arrow-select__trigger");
        trigger.Should().NotBeNull();
        trigger.InnerHtml.Should().Contain("svg");
    }

    [Fact]
    public void GetEdgePoints_WithTargetEdgeId_ResolvesToPointOnTargetEdge()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var targetEdge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Routing = "orthogonal",
            Waypoints = [new DiagramPoint(120, 25), new DiagramPoint(180, 25)]
        };
        doc.Edges.Add(targetEdge);

        var sourceEdge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetEdgeId = targetEdge.Id,
            TargetEdgeT = 0.5,
            Routing = "orthogonal",
            Waypoints = []
        };
        doc.Edges.Add(sourceEdge);

        var pts = DiagramGeometryHelper.GetEdgePoints(doc, sourceEdge);
        pts.Should().HaveCount(2);
        pts[1].X.Should().BeApproximately(150, 1);
        pts[1].Y.Should().BeApproximately(25, 1);
    }

    [Fact]
    public void GetEdgePoints_WithSourceEdgeId_ResolvesToPointOnSourceEdge()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var sourceEdge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Routing = "orthogonal",
            Waypoints = [new DiagramPoint(120, 25), new DiagramPoint(180, 25)]
        };
        doc.Edges.Add(sourceEdge);

        var childEdge = new DiagramEdge
        {
            SourceEdgeId = sourceEdge.Id,
            SourceEdgeT = 0.5,
            TargetNodeId = n2.Id,
            Routing = "orthogonal",
            Waypoints = []
        };
        doc.Edges.Add(childEdge);

        var pts = DiagramGeometryHelper.GetEdgePoints(doc, childEdge);
        pts.Should().HaveCount(2);
        pts[0].X.Should().BeApproximately(150, 1);
        pts[0].Y.Should().BeApproximately(25, 1);
    }

    [Fact]
    public void EdgeToEdge_RendersPathInCanvas()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var targetEdge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Routing = "orthogonal",
            Waypoints = [new DiagramPoint(120, 25), new DiagramPoint(180, 25)]
        };
        doc.Edges.Add(targetEdge);

        var sourceEdge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetEdgeId = targetEdge.Id,
            TargetEdgeT = 0.5,
            Routing = "orthogonal",
            Waypoints = []
        };
        doc.Edges.Add(sourceEdge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var paths = cut.FindAll("path.tm-diagram-edge-path");
        paths.Should().HaveCount(2);
    }

    [Fact]
    public void EdgeLabelOffset_RendersTransformInCanvas()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 0, Y = 0, W = 100, H = 50 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 0, W = 100, H = 50 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var edge = new DiagramEdge
        {
            SourceNodeId = n1.Id,
            TargetNodeId = n2.Id,
            Label = "Test",
            LabelPositionT = 0.5,
            LabelOffsetX = 10,
            LabelOffsetY = -5,
            Routing = "straight",
            EndArrow = "none"
        };
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        cut.Render();
        var labelGroup = cut.Find("g.tm-diagram-edge-label-group");
        labelGroup.Should().NotBeNull();
        var transform = labelGroup.GetAttribute("transform");
        transform.Should().Contain("translate(");
        transform.Should().Contain("10");   // offsetX
        transform.Should().Contain("-5");   // offsetY
    }

    [Fact]
    public void UpdateEdgeLabelPositionCommand_ExecuteAndUndo()
    {
        var edge = new DiagramEdge
        {
            SourceNodeId = "a",
            TargetNodeId = "b",
            LabelPositionT = 0.5,
            LabelOffsetX = 0,
            LabelOffsetY = 0
        };

        var cmd = new UpdateEdgeLabelPositionCommand(edge, 0.7, 12, -8);
        cmd.Execute();
        edge.LabelPositionT.Should().Be(0.7);
        edge.LabelOffsetX.Should().Be(12);
        edge.LabelOffsetY.Should().Be(-8);

        cmd.Undo();
        edge.LabelPositionT.Should().Be(0.5);
        edge.LabelOffsetX.Should().Be(0);
        edge.LabelOffsetY.Should().Be(0);
    }
}
