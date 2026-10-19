using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Minimap — a scaled-down SVG overview of the diagram canvas.
/// Shows all node bounding boxes and simplified edge lines.
/// The current viewport is shown as a dashed rectangle that the user can drag to pan the main canvas.
/// Clicking outside the viewport navigates to that point.
/// </summary>
public partial class TmDiagramMinimap : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Document to render.</summary>
    [Parameter] public DiagramDocument? Document { get; set; }

    /// <summary>IDs of currently selected elements (highlighted in blue).</summary>
    [Parameter] public string[] SelectedIds { get; set; } = [];

    /// <summary>Current viewport state in document coordinates. When null the viewport rect is not shown.</summary>
    [Parameter] public DiagramMinimapViewport? Viewport { get; set; }

    /// <summary>Fixed pixel width of the minimap panel. Height is derived from canvas aspect ratio.</summary>
    [Parameter] public int Width { get; set; } = 200;

    /// <summary>Raised when the user clicks or drags the minimap to navigate. Arguments are the document-space centre point.</summary>
    [Parameter] public EventCallback<DiagramMinimapNavigateArgs> NavigateRequested { get; set; }

    /// <summary>Additional CSS class on the wrapper div.</summary>
    [Parameter] public string? Class { get; set; }

    private ElementReference _container;
    private ElementReference _svg;

    private (double X, double Y, double W, double H)? _viewportRect;

    private bool _dragging;
    private double _dragStartSvgX, _dragStartSvgY;
    private double _dragStartVpX, _dragStartVpY;

    /// <summary>The frozen pointerdown-time bounds/scale: the drag's client→svg mapping and the
    /// clamp never see the re-rendered (panned) values, so the deltas stay exact.</summary>
    private (double X, double Y, double W, double H)? _dragBounds;
    private double _dragScale;

    /// <summary>The bounds/scale cached for the CURRENT render (computed once per parameter set —
    /// the razor reads these, never recomputes).</summary>
    private (double X, double Y, double W, double H)? _renderBounds;
    private double _renderScale;

    protected override void OnParametersSet()
    {
        _viewportRect = Viewport is not null
            ? (Viewport.X, Viewport.Y, Viewport.Width, Viewport.Height)
            : null;
        // One computation per render; the razor and the drag share these values.
        _renderBounds = ComputeBoundsCore();
        _renderScale = ComputeScaleCore(_renderBounds.Value);
    }

    /// <summary>Max rendered height as a multiple of Width (keeps a tall document from making a tall card).</summary>
    private const double MaxAspect = 0.75;

    /// <summary>Document-space region the minimap shows (X4, draw.io-like): the union of all VISIBLE
    /// node boxes, the edge geometry (waypoints, free-floating ends, labels) and the current
    /// viewport, padded by 10 %, never smaller than a fifth of the page. Nodes and edges on hidden
    /// layers are excluded (they do not render on the canvas, so the map must not frame them).</summary>
    internal (double X, double Y, double W, double H) ComputeBounds()
        => _renderBounds ?? ComputeBoundsCore();

    private (double X, double Y, double W, double H) ComputeBoundsCore()
    {
        if (Document is null) return (0, 0, 1, 1);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Add(double x, double y, double w, double h)
        {
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w); maxY = Math.Max(maxY, y + h);
        }
        foreach (var n in Document.Nodes)
        {
            if (!IsLayerVisible(n)) continue;
            Add(n.X, n.Y, n.W, n.H);
        }
        foreach (var e in Document.Edges)
        {
            if (!IsLayerVisible(e)) continue;
            foreach (var wp in e.Waypoints) Add(wp.X, wp.Y, 0, 0);
            if (e.SourcePoint is { } sp) Add(sp.X, sp.Y, 0, 0);
            if (e.TargetPoint is { } tp) Add(tp.X, tp.Y, 0, 0);
            if (!string.IsNullOrEmpty(e.Label))
            {
                // The label rides the path at t with an offset — map its anchor so a lone label
                // does not sit outside the framed region.
                var (lx, ly) = EdgeLabelPoint(e);
                Add(lx, ly, 0, 0);
            }
        }
        if (_viewportRect is { } vp) Add(vp.X, vp.Y, vp.W, vp.H);
        if (minX == double.MaxValue) return (0, 0, Math.Max(1, Document.Width), Math.Max(1, Document.Height));
        var pad = Math.Max(maxX - minX, maxY - minY) * 0.1;
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;
        var minW = Document.Width / 5; var minH = Document.Height / 5;
        if (maxX - minX < minW) { var c = (minX + maxX) / 2; minX = c - minW / 2; maxX = c + minW / 2; }
        if (maxY - minY < minH) { var c = (minY + maxY) / 2; minY = c - minH / 2; maxY = c + minH / 2; }
        return (minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>The label anchor: the point at <c>LabelPositionT</c> along the line between the
    /// connected ends (or the explicit free-floating points) with the label offset applied.</summary>
    private (double X, double Y) EdgeLabelPoint(DiagramEdge edge)
    {
        var src = EdgeEndPoint(edge.SourceNodeId, edge.SourcePoint);
        var tgt = EdgeEndPoint(edge.TargetNodeId, edge.TargetPoint);
        if (src is null || tgt is null) return (0, 0);
        var x = src.Value.X + (tgt.Value.X - src.Value.X) * edge.LabelPositionT + edge.LabelOffsetX;
        var y = src.Value.Y + (tgt.Value.Y - src.Value.Y) * edge.LabelPositionT + edge.LabelOffsetY;
        return (x, y);
    }

    /// <summary>The connected node's centre, or the free-floating point when the end is detached.</summary>
    private (double X, double Y)? EdgeEndPoint(string? nodeId, DiagramPoint? freePoint)
    {
        if (nodeId is not null && Document?.Nodes.FirstOrDefault(n => n.Id == nodeId) is { } node)
        {
            return (node.X + node.W / 2, node.Y + node.H / 2);
        }
        return freePoint is { } p ? (p.X, p.Y) : null;
    }

    /// <summary>A node on a hidden layer does not render on the canvas — the map ignores it too.</summary>
    private bool IsLayerVisible(DiagramNode node)
    {
        if (node.LayerId is null || Document is null) return true;
        var layer = Document.Layers.FirstOrDefault(l => l.Id == node.LayerId);
        return layer?.IsVisible ?? true;
    }

    /// <summary>An edge on a hidden layer does not render on the canvas — the map ignores it too.</summary>
    private bool IsLayerVisible(DiagramEdge edge)
    {
        if (edge.LayerId is null || Document is null) return true;
        var layer = Document.Layers.FirstOrDefault(l => l.Id == edge.LayerId);
        return layer?.IsVisible ?? true;
    }

    /// <summary>Uniform scale mapping document units to minimap pixels for <see cref="ComputeBounds"/>.</summary>
    internal double ComputeScale()
        => _renderScale != 0 ? _renderScale : ComputeScaleCore(ComputeBounds());

    private double ComputeScaleCore((double X, double Y, double W, double H) b)
    {
        if (b.W <= 0 || b.H <= 0) return 1.0;
        return Math.Min(Width / b.W, Width * MaxAspect / b.H);
    }

    /// <summary>The bounds the CURRENT render uses: the frozen drag values while dragging, the
    /// cached per-render values otherwise.</summary>
    private (double X, double Y, double W, double H) RenderBounds => _dragging && _dragBounds is { } db ? db : ComputeBounds();

    /// <summary>The scale paired with <see cref="RenderBounds"/>.</summary>
    private double RenderScale => _dragging && _dragBounds is not null ? _dragScale : ComputeScale();

    private async Task OnSvgPointerDown(PointerEventArgs e)
    {
        if (Document is null) return;

        var svgPt = ClientToSvg(e.OffsetX, e.OffsetY);

        if (_viewportRect is { } vr && IsInsideRect(svgPt.X, svgPt.Y, vr.X, vr.Y, vr.W, vr.H))
        {
            _dragging = true;
            // Freeze the mapping for the whole drag: the render AND the client→svg conversion
            // keep these values, so a NavigateRequested-induced re-render (new viewport) cannot
            // rescale the map under the finger.
            _dragBounds = ComputeBounds();
            _dragScale = ComputeScale();
            _dragStartSvgX = svgPt.X;
            _dragStartSvgY = svgPt.Y;
            _dragStartVpX = vr.X;
            _dragStartVpY = vr.Y;
            // The pointer belongs to the drag until it lifts: a slip off the svg must not end it
            // (the old pointerleave bail-out lost drags on the overlay's rounded corner).
            try { await JS.InvokeVoidAsync("tmDiagramMinimap.capturePointer", _svg, e.PointerId); }
            catch (JSException) { /* capture is an enhancement; the drag still works */ }
        }
        else
        {
            await NavigateRequested.InvokeAsync(new DiagramMinimapNavigateArgs(svgPt.X, svgPt.Y));
        }
    }

    private async Task OnSvgPointerMove(PointerEventArgs e)
    {
        if (!_dragging || Document is null) return;

        // A mouse drag whose button was released without a pointerup (browser chrome, an OS-level
        // gesture seizure) reports Buttons == 0: end the drag exactly like a lift — navigating for
        // a button-less move would fling the canvas to wherever the cursor happens to be.
        if (e.PointerType == "mouse" && e.Buttons == 0)
        {
            await OnSvgPointerUp(e);
            return;
        }

        var svgPt = ClientToSvg(e.OffsetX, e.OffsetY);
        var dx = svgPt.X - _dragStartSvgX;
        var dy = svgPt.Y - _dragStartSvgY;

        var newX = _dragStartVpX + dx;
        var newY = _dragStartVpY + dy;

        if (_viewportRect is { } vr2)
        {
            // Clamp against the FROZEN bounds (the page is not the frame of reference while the
            // map itself is being dragged).
            var b = RenderBounds;
            newX = Math.Clamp(newX, b.X, Math.Max(b.X, b.X + b.W - vr2.W));
            newY = Math.Clamp(newY, b.Y, Math.Max(b.Y, b.Y + b.H - vr2.H));
        }

        var vpW = _viewportRect?.W ?? 0;
        var vpH = _viewportRect?.H ?? 0;
        await NavigateRequested.InvokeAsync(new DiagramMinimapNavigateArgs(newX + vpW / 2, newY + vpH / 2));
    }

    private async Task OnSvgPointerUp(PointerEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _dragBounds = null;
        try { await JS.InvokeVoidAsync("tmDiagramMinimap.releasePointer", _svg, e.PointerId); }
        catch (JSException) { /* already released */ }
    }

    private (double X, double Y) ClientToSvg(double offsetX, double offsetY)
    {
        var scale = RenderScale;
        if (scale <= 0) return (offsetX, offsetY);
        var b = RenderBounds;
        return (b.X + offsetX / scale, b.Y + offsetY / scale);
    }

    private static bool IsInsideRect(double px, double py, double rx, double ry, double rw, double rh)
        => px >= rx && px <= rx + rw && py >= ry && py <= ry + rh;

    /// <summary>Updates the viewport rectangle from the main canvas pan/zoom state.</summary>
    public void UpdateViewport(double x, double y, double width, double height)
    {
        _viewportRect = (x, y, width, height);
        InvokeAsync(StateHasChanged);
    }
}

/// <summary>Current viewport region in document coordinates.</summary>
public sealed class DiagramMinimapViewport
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    public DiagramMinimapViewport(double x, double y, double width, double height)
    { X = x; Y = y; Width = width; Height = height; }
}

/// <summary>Navigate-to arguments: document-space centre point.</summary>
public sealed class DiagramMinimapNavigateArgs
{
    public double CentreX { get; init; }
    public double CentreY { get; init; }

    public DiagramMinimapNavigateArgs(double cx, double cy) { CentreX = cx; CentreY = cy; }
}
