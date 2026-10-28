namespace Tempo.Blazor.Abstractions.Models;

/// <summary>Where the Gantt shows its workload: as an alternate view, or as a panel docked under the timeline.</summary>
public enum GanttWorkloadPlacement
{
    /// <summary>The default: the workload is an alternate <see cref="TmGantt"/> view (<c>GanttView.Workload</c>), as before.</summary>
    View,

    /// <summary>A panel under the timeline that shares the Gantt's date range and scrolls horizontally with it.</summary>
    Docked,
}