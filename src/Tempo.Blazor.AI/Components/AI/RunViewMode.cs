namespace Tempo.Blazor.Components.AI;

/// <summary>
/// What a runs board shows: a WEEK of windows grouped by day, a single DAY of windows, or the same schedule as a table
/// — the text alternative a reader who cannot use the two visual views can still read and act on.
/// <para>
/// The table is not a lesser view: it carries every run, every window slice, the time each slice occupies, its units and
/// its status, and the same move menu. What changes between the views is only the shape of the markup.
/// </para>
/// </summary>
public enum RunViewMode
{
    /// <summary>Every supplied window, grouped by day. The DEFAULT, because a schedule is read as a week.</summary>
    Week = 0,

    /// <summary>One day's windows, in full.</summary>
    Day = 1,

    /// <summary>The same schedule as one table (the text alternative, WCAG 1.3.1).</summary>
    Table = 2,
}
