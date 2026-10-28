namespace Tempo.Blazor.Abstractions.Models;

/// <summary>How the Gantt offers its Tasks / Workload / History / Notifications / Reports panels.</summary>
public enum GanttSidebarMode
{
    /// <summary>The default: a vertical icon rail on the left (today's look).</summary>
    Rail,

    /// <summary>A horizontal tab strip with text labels above the chart (ARIA tabs); the rail is not rendered.</summary>
    Tabs,
}
