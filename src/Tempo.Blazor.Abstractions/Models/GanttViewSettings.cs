namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// Display settings for the Gantt component – controls visibility of markers,
/// task name location, row density, and theme.
/// </summary>
public class GanttViewSettings
{
    /// <summary>Show assignee avatars directly on task bars. Default: true.</summary>
    public bool ShowAvatarsOnChart { get; set; } = true;

    /// <summary>Show the vertical today marker in the timeline. Default: true.</summary>
    public bool ShowTodayMarker { get; set; } = true;

    /// <summary>Shade non-working days (weekends/holidays) in the timeline. Default: true.</summary>
    public bool ShowDaysOff { get; set; } = true;

    /// <summary>Include tasks with Done/Closed status in the tree. Default: true.</summary>
    public bool ShowClosedTasks { get; set; } = true;

    /// <summary>Where the task name label is rendered relative to the bar. Default: InsideBar.</summary>
    public GanttTaskNameLocation TaskNameLocation { get; set; } = GanttTaskNameLocation.InsideBar;

    /// <summary>Row height density. Default: Comfortable (40 px).</summary>
    public GanttViewDensity ViewDensity { get; set; } = GanttViewDensity.Comfortable;

    /// <summary>Show additional items in the task context menu (Duplicate, Move, Copy link). Default: false.</summary>
    public bool ShowAdvancedContextButtons { get; set; }

    /// <summary>Show the completion percentage next to every task bar. Default: false.</summary>
    public bool ShowProgressLabel { get; set; }

    /// <summary>
    /// Color theme applied to the component. Default: Auto, which follows the ambient theme
    /// (the cascaded <c>data-theme</c> / the registered <c>ThemeService</c>); Light and Dark force
    /// <c>data-theme</c> on the Gantt root.
    /// </summary>
    public GanttTheme Theme { get; set; } = GanttTheme.Auto;
}
