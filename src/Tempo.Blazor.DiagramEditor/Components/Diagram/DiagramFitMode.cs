namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// How <see cref="TmDiagramEditor.FitToViewAsync"/> (and the canvas fit) computes the fitted
/// scale. <see cref="Page"/> is today's fit-to-view: the whole content box (both axes) inside the
/// editor's box. <see cref="Width"/> scales by the CONTAINER WIDTH only — a taller-than-box
/// diagram stays scrollable instead of shrinking to the height (the embedded mobile preview's
/// fit-on-load, where a tall flow must stay readable).
/// </summary>
public enum DiagramFitMode
{
    /// <summary>Fit the whole content (both axes) into the editor's box — the default, today's behavior.</summary>
    Page = 0,

    /// <summary>Scale so the content width matches the container width; the height may overflow and scroll.</summary>
    Width = 1,
}
