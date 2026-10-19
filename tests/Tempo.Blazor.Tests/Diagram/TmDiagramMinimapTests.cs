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
    public void ViewBox_FramesTheContent_NotThePage()
    {
        // X4: the minimap frames the content (nodes ∪ viewport, padded), never the whole
        // page — a 3000×2000 page with two small nodes must not render them as 16 % slivers.
        var doc = new DiagramDocument { Width = 3000, Height = 2000 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 200, Y = 150, W = 180, H = 140 });
        doc.Nodes.Add(new DiagramNode { Id = "n2", X = 500, Y = 150, W = 180, H = 140 });

        var cut = Render<TmDiagramMinimap>(
            parameters => parameters.Add(p => p.Document, doc));

        var viewBox = cut.Find("svg").GetAttribute("viewBox")!;
        var parts = viewBox.Split(' ').Select(double.Parse).ToArray();
        parts.Should().HaveCount(4);
        parts[2].Should().BeLessThan(1500, "the framed width is a fraction of the 3000px page");
        parts[0].Should().BeGreaterThan(0, "the frame starts at the content, not the page origin");
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
    public void ViewportRect_IsUnderTheNodes_AndTransparent()
    {
        // X4: the viewport rectangle paints UNDER the node boxes and never fills over them.
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 10, Y = 20, W = 100, H = 80 });
        var vp = new DiagramMinimapViewport(100, 50, 400, 300);

        var cut = Render<TmDiagramMinimap>(
            parameters =>
            {
                parameters.Add(p => p.Document, doc);
                parameters.Add(p => p.Viewport, vp);
            });

        var svg = cut.Find("svg");
        var children = svg.Children.ToArray();
        var viewportIndex = Array.FindIndex(children,
            c => c.ClassList.Contains("tm-diagram-minimap__viewport"));
        var firstNodeIndex = Array.FindIndex(children,
            c => c.ClassList.Contains("tm-diagram-minimap__node"));
        viewportIndex.Should().BeGreaterThanOrEqualTo(0);
        firstNodeIndex.Should().BeGreaterThanOrEqualTo(0);
        viewportIndex.Should().BeLessThan(firstNodeIndex, "the viewport outline paints under the nodes");

        var vprect = cut.Find("rect.tm-diagram-minimap__viewport");
        vprect.GetAttribute("fill").Should().Be("none", "the viewport is a transparent outline");
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
        // The tap maps through the fitted origin: doc point = bounds origin + offset / scale.
        var b = cut.Instance.ComputeBounds();
        var s = cut.Instance.ComputeScale();
        navigated!.CentreX.Should().BeApproximately(b.X + 10 / s, 0.1);
        navigated!.CentreY.Should().BeApproximately(b.Y + 10 / s, 0.1);
    }

    [Fact]
    public void Navigate_At112px_MapsThroughTheFittedOrigin()
    {
        // U2/X4: the width parameter drives BOTH the render and the tap mapping — the 112px
        // mobile map must navigate through the same fitted origin as it displays.
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        var vp = new DiagramMinimapViewport(100, 50, 400, 300);
        DiagramMinimapNavigateArgs? navigated = null;

        var cut = Render<TmDiagramMinimap>(
            parameters =>
            {
                parameters.Add(p => p.Document, doc);
                parameters.Add(p => p.Viewport, vp);
                parameters.Add(p => p.Width, 112);
                parameters.Add(p => p.NavigateRequested, new Microsoft.AspNetCore.Components.EventCallback<DiagramMinimapNavigateArgs>(null, (System.Action<DiagramMinimapNavigateArgs>)(args => navigated = args)));
            });

        var b = cut.Instance.ComputeBounds();
        var s = cut.Instance.ComputeScale();
        var mapW = (int)Math.Round(b.W * s);
        var mapH = (int)Math.Round(b.H * s);
        var svg = cut.Find("svg");
        // Press BELOW the viewport rect (the map centre is inside it) — the horizontal centre
        // of the map must still navigate through the fitted origin.
        svg.PointerDown(new PointerEventArgs { PointerType = "touch", OffsetX = mapW / 2.0, OffsetY = mapH - 2.0 });

        navigated.Should().NotBeNull();
        navigated!.CentreX.Should().BeApproximately(b.X + b.W / 2, 1.0,
            "the tap at the horizontal centre navigates to the framed region's centre");
        navigated!.CentreY.Should().BeGreaterThan(50 + 300,
            "the press below the viewport navigates (no drag starts outside the rect)");
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

    // ── UX review: the viewport drag freezes the mapping, captures the pointer ──

    [Fact]
    public void ViewportDrag_EqualMovesAroundAViewportUpdate_ProduceEqualDeltas()
    {
        // The drag freezes the pointerdown-time bounds/scale: two equal +10px moves must yield
        // EQUAL navigate deltas even when the parent feeds the panned viewport back (a re-render)
        // BETWEEN the moves — without the freeze the re-fitted scale skews the second delta.
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 100, Y = 100, W = 100, H = 100 });
        var navigations = new List<DiagramMinimapNavigateArgs>();
        var currentVp = new DiagramMinimapViewport(100, 50, 400, 300);

        var cut = Render<TmDiagramMinimap>(parameters =>
        {
            parameters.Add(p => p.Document, doc);
            parameters.Add(p => p.Viewport, currentVp);
            parameters.Add(p => p.NavigateRequested,
                new Microsoft.AspNetCore.Components.EventCallback<DiagramMinimapNavigateArgs>(null,
                    (Action<DiagramMinimapNavigateArgs>)(args =>
                    {
                        navigations.Add(args);
                        // Pan feedback: the canvas pans and the viewport follows the navigation.
                        currentVp = new DiagramMinimapViewport(args.CentreX - 200, args.CentreY - 150, 400, 300);
                    })));
        });

        var b = cut.Instance.ComputeBounds();
        var s = cut.Instance.ComputeScale();
        // Press INSIDE the viewport rect (near its top-left corner).
        var downX = (100 + 40 - b.X) * s;
        var downY = (50 + 40 - b.Y) * s;

        var svg = cut.Find("svg");
        svg.PointerDown(new PointerEventArgs { PointerType = "touch", PointerId = 7, OffsetX = downX, OffsetY = downY });
        svg.PointerMove(new PointerEventArgs { PointerType = "touch", PointerId = 7, OffsetX = downX + 5, OffsetY = downY });
        navigations.Should().HaveCount(1);
        var firstDelta = navigations[0].CentreX - 300; // the original viewport centre was (300, 200)

        // The parent re-renders with the panned viewport — the fitted bounds/scale change now.
        cut.Render(parameters =>
        {
            parameters.Add(p => p.Document, doc);
            parameters.Add(p => p.Viewport, currentVp);
        });

        svg.PointerMove(new PointerEventArgs { PointerType = "touch", PointerId = 7, OffsetX = downX + 10, OffsetY = downY });
        navigations.Should().HaveCount(2);
        var secondDelta = navigations[1].CentreX - navigations[0].CentreX;

        secondDelta.Should().Be(firstDelta,
            "the frozen mapping makes the two equal +5px moves produce exactly equal deltas");
    }

    [Fact]
    public void ViewportDrag_CapturesThePointer_AndEndsOnlyOnPointerUp()
    {
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 100, Y = 100, W = 100, H = 100 });
        var navigations = new List<DiagramMinimapNavigateArgs>();
        var vp = new DiagramMinimapViewport(100, 50, 400, 300);

        var cut = Render<TmDiagramMinimap>(parameters =>
        {
            parameters.Add(p => p.Document, doc);
            parameters.Add(p => p.Viewport, vp);
            parameters.Add(p => p.NavigateRequested,
                new Microsoft.AspNetCore.Components.EventCallback<DiagramMinimapNavigateArgs>(null,
                    (Action<DiagramMinimapNavigateArgs>)(args => navigations.Add(args))));
        });

        var b = cut.Instance.ComputeBounds();
        var s = cut.Instance.ComputeScale();
        var downX = (100 + 40 - b.X) * s;
        var downY = (50 + 40 - b.Y) * s;

        var svg = cut.Find("svg");
        svg.PointerDown(new PointerEventArgs { PointerType = "touch", PointerId = 9, OffsetX = downX, OffsetY = downY });

        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmDiagramMinimap.capturePointer",
            "the drag captures the pointer on pointerdown (the gesture owns the pointer until the lift — a slip off the svg no longer ends it)");

        svg.PointerMove(new PointerEventArgs { PointerType = "touch", PointerId = 9, OffsetX = downX + 5, OffsetY = downY });
        navigations.Should().HaveCount(1);

        svg.PointerUp(new PointerEventArgs { PointerType = "touch", PointerId = 9, OffsetX = downX + 5, OffsetY = downY });
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmDiagramMinimap.releasePointer",
            "the lift releases the capture");

        // The drag is over: further moves navigate nothing.
        svg.PointerMove(new PointerEventArgs { PointerType = "touch", PointerId = 9, OffsetX = downX + 10, OffsetY = downY });
        navigations.Should().HaveCount(1, "after pointerup the drag no longer navigates");
    }

    [Fact]
    public void ViewportDrag_MouseButtonReleasedWithoutPointerUp_EndsTheDragWithoutNavigating()
    {
        // A mouse drag whose button the browser released without a pointerup (browser chrome, an
        // OS gesture seizure) reports Buttons == 0 on the next move: the drag ends exactly like a
        // lift and must NOT navigate to wherever the cursor happens to be.
        var doc = new DiagramDocument { Width = 1000, Height = 800 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 100, Y = 100, W = 100, H = 100 });
        var navigations = new List<DiagramMinimapNavigateArgs>();
        var vp = new DiagramMinimapViewport(100, 50, 400, 300);

        var cut = Render<TmDiagramMinimap>(parameters =>
        {
            parameters.Add(p => p.Document, doc);
            parameters.Add(p => p.Viewport, vp);
            parameters.Add(p => p.NavigateRequested,
                new Microsoft.AspNetCore.Components.EventCallback<DiagramMinimapNavigateArgs>(null,
                    (Action<DiagramMinimapNavigateArgs>)(args => navigations.Add(args))));
        });

        var b = cut.Instance.ComputeBounds();
        var s = cut.Instance.ComputeScale();
        var downX = (100 + 40 - b.X) * s;
        var downY = (50 + 40 - b.Y) * s;

        var svg = cut.Find("svg");
        svg.PointerDown(new PointerEventArgs { PointerType = "mouse", PointerId = 5, Buttons = 1, OffsetX = downX, OffsetY = downY });
        navigations.Should().BeEmpty("a press inside the viewport starts a drag — it does not navigate");

        svg.PointerMove(new PointerEventArgs { PointerType = "mouse", PointerId = 5, Buttons = 0, OffsetX = downX + 60, OffsetY = downY + 40 });
        navigations.Should().BeEmpty("a button-less mouse move ends the drag without navigating");

        // The drag really ended: even a buttons-pressed move afterwards navigates nothing.
        svg.PointerMove(new PointerEventArgs { PointerType = "mouse", PointerId = 5, Buttons = 1, OffsetX = downX + 80, OffsetY = downY + 60 });
        navigations.Should().BeEmpty();

        JSInterop.Invocations.Should().Contain(i => i.Identifier == "tmDiagramMinimap.releasePointer",
            "the button-less move runs the same cleanup as a lift (the capture is released)");
    }

    // ── UX review: hidden layers and edge geometry frame the map ─────────────

    [Fact]
    public void HiddenLayerNodesAndEdges_AreExcludedFromTheMap()
    {
        var doc = new DiagramDocument { Width = 3000, Height = 2000 };
        var hidden = new DiagramLayer { Name = "Hidden", Order = 1, IsVisible = false };
        doc.Layers.Add(hidden);
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 100, Y = 100, W = 100, H = 100 });
        doc.Nodes.Add(new DiagramNode { Id = "n2", X = 2500, Y = 1500, W = 100, H = 100, LayerId = hidden.Id });

        var cut = Render<TmDiagramMinimap>(parameters => parameters.Add(p => p.Document, doc));

        cut.FindAll("rect.tm-diagram-minimap__node").Should().HaveCount(1,
            "a node on a hidden layer does not render on the map");
        var b = cut.Instance.ComputeBounds();
        (b.X + b.W).Should().BeLessThan(1000, "the far hidden node is not framed either");
    }

    [Fact]
    public void EdgeWaypoints_AreIncludedInTheFramedRegion()
    {
        var doc = new DiagramDocument { Width = 4000, Height = 3000 };
        doc.Nodes.Add(new DiagramNode { Id = "n1", X = 100, Y = 100, W = 100, H = 100 });
        doc.Nodes.Add(new DiagramNode { Id = "n2", X = 300, Y = 100, W = 100, H = 100 });
        doc.Edges.Add(new DiagramEdge
        {
            SourceNodeId = "n1",
            TargetNodeId = "n2",
            Label = "long way around",
            Waypoints = [new DiagramPoint { X = 2200, Y = 1700 }],
        });

        var cut = Render<TmDiagramMinimap>(parameters => parameters.Add(p => p.Document, doc));

        var b = cut.Instance.ComputeBounds();
        (b.X + b.W).Should().BeGreaterThan(2000, "the waypoint frames the region (the edge geometry counts)");
        (b.Y + b.H).Should().BeGreaterThan(1700, "the waypoint's Y frames the region too");
    }
}
