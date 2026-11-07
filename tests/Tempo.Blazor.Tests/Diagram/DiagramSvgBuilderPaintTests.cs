using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Diagram.Stencils;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

/// <summary>
/// Phase 4 review (A7): the server-side SVG export of a BPMN node. The shipped stencils paint with
/// the canvas tokens (<c>var(--tm-diagram-paper, #ffffff)</c> / <c>var(--tm-diagram-node-stroke)</c>)
/// — a standalone serialized SVG has NO stylesheet, so a <c>var()</c> paint would render black. The
/// builder therefore resolves a token paint to the palette and lets the NODE's own style win over the
/// stencil layout.
/// </summary>
public class DiagramSvgBuilderPaintTests
{
    private static DiagramSvgRenderer BuildRenderer()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
        return new DiagramSvgRenderer(registry);
    }

    private static DiagramDocument BpmnTaskDocument(string? fill = null, string? stroke = null)
    {
        var document = new DiagramDocument();
        document.EnsurePages();
        var node = new DiagramNode
        {
            Id = "task",
            StencilId = "bpmn2.task.user",
            X = 40,
            Y = 40,
            W = 150,
            H = 92
        };
        node.Data["label"] = "Approve";
        if (fill is not null)
        {
            node.Style.Fill = fill;
        }

        if (stroke is not null)
        {
            node.Style.Stroke = stroke;
        }

        document.Pages[0].Nodes.Add(node);
        return document;
    }

    [Fact]
    public void The_server_svg_of_a_bpmn_task_never_carries_a_css_var()
    {
        var svg = BuildRenderer().RenderSvg(BpmnTaskDocument());

        svg.Should().NotContain("var(", "a var() paint cannot resolve in a standalone SVG");
        svg.Should().Contain(DiagramSvgPalette.Light.DefaultNodeFill);
        svg.Should().Contain(DiagramSvgPalette.Light.DefaultNodeStroke);
    }

    [Fact]
    public void The_nodes_own_style_wins_over_the_stencil_layout()
    {
        var svg = BuildRenderer().RenderSvg(BpmnTaskDocument(fill: "#ffcc00", stroke: "#003366"));

        svg.Should().Contain("#ffcc00", "the authored node colour must reach the export");
        svg.Should().Contain("#003366");
        svg.Should().NotContain("var(");
    }

    [Fact]
    public void A_plain_stencil_fill_still_reaches_the_export_unchanged()
    {
        // A non-token stencil fill (a host's own stencil) is emitted verbatim — the palette only
        // replaces the unresolvable var() references.
        var svg = BuildRenderer().RenderSvg(BpmnTaskDocument(fill: "rebeccapurple"));

        svg.Should().Contain("rebeccapurple");
    }
}
