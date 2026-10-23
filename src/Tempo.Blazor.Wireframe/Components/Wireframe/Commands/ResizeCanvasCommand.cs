using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe.Commands;

/// <summary>
/// Changes the canvas width and height of one page: the page that was active when the command was created, so Undo and
/// Redo restore that page even after the user switched to another one. The canvas is told about the new size by the
/// editor (<c>TmWireframeDesignerCanvas</c> compares the document size with the one its script knows after every render).
/// </summary>
public sealed class ResizeCanvasCommand : IWireframeCommand
{
    private readonly WireframeDocument _doc;
    private readonly string? _pageId;
    private readonly double _oldW, _oldH, _newW, _newH;

    /// <summary>Creates a canvas resize command for the active page.</summary>
    /// <param name="doc">Target document.</param>
    /// <param name="oldW">Width before the change (restored by Undo).</param>
    /// <param name="oldH">Height before the change (restored by Undo).</param>
    /// <param name="newW">Width after the change.</param>
    /// <param name="newH">Height after the change.</param>
    public ResizeCanvasCommand(WireframeDocument doc, double oldW, double oldH, double newW, double newH)
    {
        _doc = doc;
        _pageId = doc.ActivePage?.Id;
        _oldW = oldW; _oldH = oldH;
        _newW = newW; _newH = newH;
    }

    /// <inheritdoc />
    public string Name => "Resize canvas";

    /// <inheritdoc />
    public void Execute() => Apply(_newW, _newH);

    /// <inheritdoc />
    public void Undo() => Apply(_oldW, _oldH);

    private void Apply(double width, double height)
    {
        var page = _pageId is null ? _doc.ActivePage : _doc.Pages.FirstOrDefault(p => p.Id == _pageId) ?? _doc.ActivePage;
        if (page is null) return;
        page.Width = width;
        page.Height = height;
    }
}
