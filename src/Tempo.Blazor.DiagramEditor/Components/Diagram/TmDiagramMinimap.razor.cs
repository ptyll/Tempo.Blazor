using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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

    protected override void OnParametersSet()
    {
        _viewportRect = Viewport is not null
            ? (Viewport.X, Viewport.Y, Viewport.Width, Viewport.Height)
            : null;
    }

    /// <summary>Returns the uniform scale factor mapping document pixels → minimap pixels.</summary>
    internal double ComputeScale() =>
        Document is null || Document.Width <= 0
            ? 1.0
            : Width / Document.Width;

    private async Task OnSvgPointerDown(PointerEventArgs e)
    {
        if (Document is null) return;

        var svgPt = ClientToSvg(e.OffsetX, e.OffsetY);

        if (_viewportRect is { } vr && IsInsideRect(svgPt.X, svgPt.Y, vr.X, vr.Y, vr.W, vr.H))
        {
            _dragging = true;
            _dragStartSvgX = svgPt.X;
            _dragStartSvgY = svgPt.Y;
            _dragStartVpX = vr.X;
            _dragStartVpY = vr.Y;
        }
        else
        {
            await NavigateRequested.InvokeAsync(new DiagramMinimapNavigateArgs(svgPt.X, svgPt.Y));
        }
    }

    private async Task OnSvgPointerMove(PointerEventArgs e)
    {
        if (!_dragging || Document is null) return;

        var svgPt = ClientToSvg(e.OffsetX, e.OffsetY);
        var dx = svgPt.X - _dragStartSvgX;
        var dy = svgPt.Y - _dragStartSvgY;

        var newX = _dragStartVpX + dx;
        var newY = _dragStartVpY + dy;

        if (_viewportRect is { } vr2)
        {
            newX = Math.Clamp(newX, 0, Math.Max(0, Document.Width - vr2.W));
            newY = Math.Clamp(newY, 0, Math.Max(0, Document.Height - vr2.H));
        }

        var vpW = _viewportRect?.W ?? 0;
        var vpH = _viewportRect?.H ?? 0;
        await NavigateRequested.InvokeAsync(new DiagramMinimapNavigateArgs(newX + vpW / 2, newY + vpH / 2));
    }

    private void OnSvgPointerUp(PointerEventArgs _) => _dragging = false;
    private void OnSvgPointerLeave(PointerEventArgs _) => _dragging = false;

    private (double X, double Y) ClientToSvg(double offsetX, double offsetY)
    {
        var scale = ComputeScale();
        if (scale <= 0) return (offsetX, offsetY);
        return (offsetX / scale, offsetY / scale);
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
