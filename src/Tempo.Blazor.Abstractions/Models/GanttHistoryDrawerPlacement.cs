namespace Tempo.Blazor.Abstractions.Models;

/// <summary>What the Gantt history drawer is positioned against.</summary>
public enum GanttHistoryDrawerPlacement
{
    /// <summary>The default: the drawer sits on the right edge of the Gantt component and is clipped by it.</summary>
    Component,

    /// <summary>The drawer is fixed to the right edge of the viewport, spanning its full height (the pre-redesign behaviour).</summary>
    Viewport,
}