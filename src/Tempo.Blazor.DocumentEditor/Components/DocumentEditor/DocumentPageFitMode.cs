namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>
/// How the canvas engine fits the page to the width of the document column. The fit is a mode of the engine itself (a resize observer on the
/// scroll viewport re-applies it), so it follows the splitter, the inspector docking, a rotation and a window resize without a Blazor round trip.
/// An explicit zoom of the reader (the status bar, the View tab, ctrl+wheel, pinch) always wins until "Fit width" is chosen again.
/// </summary>
public enum DocumentPageFitMode
{
    /// <summary>The page is fitted only when it is wider than the column (and never enlarged); a column that holds the page shows it at 100 percent. The default in every layout.</summary>
    Auto,

    /// <summary>The engine never fits the page: the zoom is the reader's or the host's, and a narrow column scrolls sideways.</summary>
    Off,

    /// <summary>The page always follows the width of the column, enlarging included.</summary>
    FitWidth
}
