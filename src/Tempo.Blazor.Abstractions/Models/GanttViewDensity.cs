namespace Tempo.Blazor.Abstractions.Models;

/// <summary>Controls row height density in the Gantt grid.</summary>
public enum GanttViewDensity
{
    Comfortable,
    Compact,

    /// <summary>Finger-sized rows (44 px) for touch devices: tree rows and timeline rows share the height.</summary>
    Touch
}
