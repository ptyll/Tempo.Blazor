using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class TmDiagramMinimapTests : DiagramTestBase
{
    [Fact]
    public void RendersSvgWithDocumentDimensions()
    {
        var doc = new DiagramDocument { Width = 800, Height = 600 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 10, Y = 20, W = 100, H = 80 });

        var cut = Render<TmDiagramMinimap>(
            parameters => parameters.Add(p => p.Document, doc));

        var svg = cut.Find("svg");
        svg.GetAttribute("viewBox").Should().Be("0 0 800 600");
    }

    [Fact]
    public void RendersNodeRects()
    {
        var doc = new DiagramDocument { Width = 500, Height = 400 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 10, Y = 20, W = 100, H = 80 });
        doc.Nodes.Add(new DiagramNode { Id = "n2", X = 200, Y = 50, W = 60, H = 40 });

        var cut = Render<TmDiagramMinimap>(
            parameters => parameters.Add(p => p.Document, doc));

        var rects = cut.FindAll("rect.tm-diagram-minimap__node");
        rects.Count.Should().Be(2);
    }

    [Fact]
    public void SelectedNodeUsesSelectedFill()
    {
        var doc = new DiagramDocument { Width = 500, Height = 400 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 10, Y = 20, W = 100, H = 80 });

        var cut = Render<TmDiagramMinimap>(
            parameters =>
            {
                parameters.Add(p => p.Document, doc);
                parameters.Add(p => p.SelectedIds, ["n1"]);
            });

        var rect = cut.Find("rect.tm-diagram-minimap__node");
        rect.GetAttribute("fill").Should().Be("var(--tm-color-primary-subtle, #dbeafe)");
    }

    [Fact]
    public void ViewportRectRenderedWhenViewportProvided()
    {
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        var vp = new DiagramMinimapViewport(100, 50, 400, 300);

        var cut = Render<TmDiagramMinimap>(
            parameters =>
            {
                parameters.Add(p => p.Document, doc);
                parameters.Add(p => p.Viewport, vp);
            });

        var vprect = cut.Find("rect.tm-diagram-minimap__viewport");
        vprect.GetAttribute("x").Should().Be("100");
        vprect.GetAttribute("y").Should().Be("50");
        vprect.GetAttribute("width").Should().Be("400");
        vprect.GetAttribute("height").Should().Be("300");
    }

    [Fact]
    public void ClickingOutsideViewportNavigates()
    {
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        var vp = new DiagramMinimapViewport(100, 50, 400, 300);
        DiagramMinimapNavigateArgs? navigated = null;

        var cut = Render<TmDiagramMinimap>(
            parameters =>
            {
                parameters.Add(p => p.Document, doc);
                parameters.Add(p => p.Viewport, vp);
                parameters.Add(p => p.NavigateRequested, new Microsoft.AspNetCore.Components.EventCallback<DiagramMinimapNavigateArgs>(null, (System.Action<DiagramMinimapNavigateArgs>)(args => navigated = args)));
            });

        // Phase 6: the minimap is driven by Pointer Events (a finger drags the viewport rect),
        // so the navigation press is a pointerdown.
        var svg = cut.Find("svg");
        svg.PointerDown(new PointerEventArgs { PointerType = "touch", OffsetX = 10, OffsetY = 10 });

        navigated.Should().NotBeNull();
        navigated!.CentreX.Should().BeApproximately(50, 0.1);
        navigated!.CentreY.Should().BeApproximately(50, 0.1);
    }

    [Fact]
    public void UpdateViewport_UpdatesRectCoordinates()
    {
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        var vp = new DiagramMinimapViewport(100, 50, 400, 300);

        var cut = Render<TmDiagramMinimap>(
            parameters =>
            {
                parameters.Add(p => p.Document, doc);
                parameters.Add(p => p.Viewport, vp);
            });

        cut.InvokeAsync(() => cut.Instance.UpdateViewport(200, 150, 500, 400)).Wait();

        var vprect = cut.Find("rect.tm-diagram-minimap__viewport");
        vprect.GetAttribute("x").Should().Be("200");
        vprect.GetAttribute("y").Should().Be("150");
    }
}
