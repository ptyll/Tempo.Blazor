namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// How the wireframe canvas lays out its view when it first renders, when the document is replaced and when the active page
/// changes (decision <c>WIREFRAME-INITIAL-ZOOM</c>). It never overrides a zoom the user chose afterwards (buttons, pinch,
/// Ctrl+wheel), not even when the container is resized.
/// </summary>
public enum WireframeInitialZoom
{
    /// <summary>
    /// The whole page is fitted into the canvas with a 40 px padding, but only ever SHRUNK: a page that fits at 100 % stays at
    /// exactly 100 % and is centred, it is never enlarged. This is the default.
    /// </summary>
    Fit = 0,

    /// <summary>
    /// The page is shown at 100 %. A page larger than the canvas starts at its top-left corner (padded) and is panned or zoomed
    /// from there; a smaller page is centred.
    /// </summary>
    ActualSize = 1,
}
