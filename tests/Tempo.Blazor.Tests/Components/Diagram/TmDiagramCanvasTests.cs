using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramCanvasTests : LocalizationTestBase
{
    private const string DiagramEditorCssPath =
        "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css";

    public TmDiagramCanvasTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public void PageRects_CarryPaperClasses_AndKeepExportFallbackAttributes()
    {
        var doc = new DiagramDocument();
        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ShowPageView, true));

        // The paper colour itself is a token (U14): on screen the CSS class wins, while the
        // literal presentation attributes stay as the standalone-export fallback.
        cut.Find("rect.tm-diagram-page").GetAttribute("fill").Should().Be("white");
        cut.Find("rect.tm-diagram-page").GetAttribute("stroke").Should().Be("#d1d5db");
        cut.Find("rect.tm-diagram-page-backdrop").Should().NotBeNull();
    }

    [Fact]
    public void DiagramCanvas_DeclaresPaperInkFromPaperToken()
    {
        var css = ThemeCss.StripComments(File.ReadAllText(
            Path.Combine(ThemeCss.RepositoryRoot().FullName, DiagramEditorCssPath)));

        // The node ink is declared once, on the canvas, from the theme-invariant paper token —
        // never again on the node body (that was the --tm-color-secondary-hover hack).
        var canvas = System.Text.RegularExpressions.Regex.Match(
            css, @"\.tm-diagram-canvas\s*\{([^}]*)\}");
        canvas.Success.Should().BeTrue("the package must keep a plain .tm-diagram-canvas rule");
        var canvasBody = ThemeCss.Normalise(canvas.Groups[1].Value);
        canvasBody.Should().Contain("--tm-diagram-node-ink: var(--tm-paper-ink-muted)");
        canvasBody.Should().Contain("--tm-diagram-paper: var(--tm-paper)");

        var nodeBody = System.Text.RegularExpressions.Regex.Match(
            css, @"\.tm-diagram-node__body\s*\{([^}]*)\}");
        nodeBody.Success.Should().BeTrue();
        ThemeCss.Normalise(nodeBody.Groups[1].Value).Should().NotContain("--tm-diagram-node-ink:",
            "the node body must not redeclare the ink; it inherits the canvas declaration");
    }

    [Fact]
    public async Task SelectedNode_RendersRotateHandle()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();

        cut.Find(".tm-diagram-rotate-handle").Should().NotBeNull();
    }

    [Fact]
    public async Task SelectedNode_RendersConnectArrows()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();

        cut.FindAll(".tm-diagram-connect-arrow").Count.Should().Be(4);
        cut.Find(".tm-diagram-connect-arrow--n").Should().NotBeNull();
        cut.Find(".tm-diagram-connect-arrow--e").Should().NotBeNull();
        cut.Find(".tm-diagram-connect-arrow--s").Should().NotBeNull();
        cut.Find(".tm-diagram-connect-arrow--w").Should().NotBeNull();
    }

    [Fact]
    public async Task SelectedNodeWithPorts_RendersPortHitCirclesWithCssOwnedAttributes()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        };
        node.Ports.Add(new DiagramPort { Side = PortSide.Right });
        node.Ports.Add(new DiagramPort { Side = PortSide.Left });
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(node.Id);
        cut.Render();

        var hits = cut.FindAll("circle.tm-diagram-port-hit");
        hits.Should().NotBeEmpty("the selected node's ports render the finger-sized hit circles");
        foreach (var hit in hits)
        {
            hit.HasAttribute("style").Should().BeFalse("pointer-events is owned by CSS (none by default, all only on a coarse pointer)");
            hit.HasAttribute("r").Should().BeFalse("the hit radius is owned by CSS (--tm-diagram-handle-hit)");
            hit.HasAttribute("fill").Should().BeFalse("the fill is owned by CSS");
        }
    }

    [Fact]
    public async Task SelectedNodeWithPorts_MarksHandlesGroupWithDataHasPorts()
    {
        var doc = new DiagramDocument();
        var withPorts = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        };
        withPorts.Ports.Add(new DiagramPort { Side = PortSide.Right });
        var withoutPorts = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 300,
            Y = 100,
            W = 120,
            H = 60
        };
        doc.Nodes.Add(withPorts);
        doc.Nodes.Add(withoutPorts);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection([withPorts.Id, withoutPorts.Id]);
        cut.Render();

        cut.Find($"g.tm-diagram-node-handles[data-node-id='{withPorts.Id}']")
            .GetAttribute("data-has-ports").Should().Be("true");
        cut.Find($"g.tm-diagram-node-handles[data-node-id='{withoutPorts.Id}']")
            .HasAttribute("data-has-ports").Should().BeFalse(
                "a port-less node keeps every resize handle — the coarse-pointer hiding scopes to data-has-ports");
    }

    [Fact]
    public async Task ConnectArrowClick_RaisesEventCallback()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        };
        doc.Nodes.Add(node);

        (string NodeId, string Direction)? captured = null;

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false)
            .Add(c => c.OnConnectArrowClicked, async args => { captured = args; }));

        await cut.Instance.SetSelection(node.Id);
        cut.Render();

        cut.Find(".tm-diagram-connect-arrow--e").Click();

        captured.Should().NotBeNull();
        captured.Value.NodeId.Should().Be(node.Id);
        captured.Value.Direction.Should().Be("e");
    }

    [Fact]
    public void ShowPageView_RendersDropShadowFilterAndGrayBackground()
    {
        var doc = new DiagramDocument { Width = 800, Height = 600 };

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ShowPageView, true));

        var svg = cut.Find("svg");
        var viewBox = svg.GetAttribute("viewBox");
        viewBox.Should().StartWith("-200 -200 ");

        var defs = cut.Find("defs");
        defs.InnerHtml.ToLowerInvariant().Should().Contain("fedropshadow");
    }

    [Fact]
    public void HidePageView_UsesExactDocumentDimensions()
    {
        var doc = new DiagramDocument { Width = 800, Height = 600 };

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ShowPageView, false));

        var svg = cut.Find("svg");
        svg.GetAttribute("viewBox").Should().Be("0 0 800 600");
    }

    [Fact]
    public async Task OnDeleteSelected_RemovesSelectedEdge()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 40, H = 40 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 100, W = 40, H = 40 };
        var edge = new DiagramEdge { SourceNodeId = n1.Id, TargetNodeId = n2.Id };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.OnDeleteSelected([edge.Id]);

        doc.Edges.Should().BeEmpty();
        doc.Nodes.Should().HaveCount(2);
    }

    [Fact]
    public async Task EdgeToolbarFlipButton_SwapsSourceAndTarget()
    {
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 40, H = 40 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 100, W = 40, H = 40 };
        var edge = new DiagramEdge { SourceNodeId = n1.Id, TargetNodeId = n2.Id };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);
        doc.Edges.Add(edge);

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        // Select the edge by clicking the invisible hit path
        var hitPath = cut.Find(".tm-diagram-edge-hit-path");
        hitPath.Click();
        cut.Render();

        // The edge toolbar should be rendered
        cut.Find(".tm-diagram-edge-toolbar").Should().NotBeNull();

        // Click the flip button
        var flipButton = cut.Find(".tm-diagram-edge-toolbar button[data-action=\"flip\"]");
        flipButton.Should().NotBeNull();
        flipButton.Click();

        // Verify source and target were swapped
        edge.SourceNodeId.Should().Be(n2.Id);
        edge.TargetNodeId.Should().Be(n1.Id);
    }
}
