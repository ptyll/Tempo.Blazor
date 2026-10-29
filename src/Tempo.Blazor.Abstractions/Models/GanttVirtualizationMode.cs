namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// Whether the Gantt renders only the visible window of tree rows and timeline bars for large data sets.
/// The window is computed arithmetically from the fixed row height (the tree and the timeline share one)
/// in the browser; <c>Off</c> keeps the pre-virtualization DOM byte for byte.
/// </summary>
public enum GanttVirtualizationMode
{
    /// <summary>The default: every visible row renders (today's behaviour — the chart is meant for hundreds of rows).</summary>
    Off,

    /// <summary>Virtualizes only when the visible set reaches <c>VirtualizationThreshold</c> rows; below it the chart renders fully.</summary>
    Auto,

    /// <summary>Always virtualizes, whatever the visible count.</summary>
    On,
}
