namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How <see cref="TmConflictRadar"/> presents the agents' branches: the file × agent table, or a card per changed file
/// with the agents' changes stacked under it.
/// </summary>
public enum ConflictRadarPresentation
{
    /// <summary>
    /// The table while the surface's own container is at least <c>TmBreakpoints.Sm</c> (640 px) wide, and a card per
    /// file below it. Default. The container is measured, not the viewport, so the phone presentation also reaches a
    /// narrow panel inside a wide page.
    /// </summary>
    Auto,

    /// <summary>
    /// The file × agent table whatever the container measures. A table wider than its wrapper scrolls inside the
    /// wrapper (with a scroll shadow), never the page.
    /// </summary>
    Grid,

    /// <summary>
    /// A card per changed file with the agents' changes stacked under it, whatever the container measures.
    /// </summary>
    Cards,
}
