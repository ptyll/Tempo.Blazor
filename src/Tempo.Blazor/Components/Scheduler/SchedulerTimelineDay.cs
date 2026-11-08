using System.Globalization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// One event placed inside a single day of the day-granularity timeline. <see cref="Lane"/> and
/// <see cref="TotalLanes"/> are the shared overlap assignment (<see cref="SchedulerLayout"/>), sized
/// per overlap cluster; <see cref="CellLanes"/> is the widest cluster in the whole cell, which is the
/// grid the cell paints. <see cref="Row"/> is the event's order inside its own lane, so two events
/// that do not overlap stack vertically instead of painting over each other.
/// </summary>
internal readonly record struct TimelineDayItem(
    TmScheduleEvent Event,
    int Lane,
    int TotalLanes,
    int CellLanes,
    int Row,
    bool IsContinuation);

/// <summary>
/// The pure part of the day-granularity timeline: which events land in one day cell, how overlaps
/// become lanes, and how a capped cell splits into the visible chips and the "+N more" rest. It is
/// separate from the component so the placement is testable without a render.
/// </summary>
internal static class SchedulerTimelineDay
{
    /// <summary>
    /// The events of one day cell, in the layout's start order. An event is in the cell when it
    /// intersects the day's half-open range, so a multi-day event paints on every day it covers and
    /// <see cref="TimelineDayItem.IsContinuation"/> marks the days after its start.
    /// </summary>
    public static IReadOnlyList<TimelineDayItem> Layout(IReadOnlyList<TmScheduleEvent>? events, DateOnly day)
    {
        if (events is null || events.Count == 0) return [];

        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var inDay = events.Where(e => e.StartLocal < end && e.EndLocal > start).ToList();
        if (inDay.Count == 0) return [];

        var positioned = SchedulerLayout.Assign(inDay);
        var cellLanes = positioned.Count == 0 ? 1 : positioned.Max(p => p.TotalLanes);
        var rows = new int[cellLanes];
        var result = new List<TimelineDayItem>(positioned.Count);
        foreach (var placed in positioned)
        {
            var lane = Math.Clamp(placed.Lane, 0, cellLanes - 1);
            var row = rows[lane];
            rows[lane] = row + 1;
            result.Add(new TimelineDayItem(
                placed.Event,
                lane,
                Math.Clamp(placed.TotalLanes, 1, cellLanes),
                cellLanes,
                row,
                placed.Event.StartLocal < start));
        }

        return result;
    }

    /// <summary>
    /// Splits a cell's items into the chips it renders and the count the "+N more" button reports.
    /// <paramref name="max"/> is clamped to at least one, because a cell that renders nothing is not
    /// a calendar cell.
    /// </summary>
    public static (IReadOnlyList<TimelineDayItem> Visible, int Remaining) Split(
        IReadOnlyList<TimelineDayItem> items, int max)
    {
        var cap = Math.Max(1, max);
        if (items.Count <= cap) return (items, 0);
        return (items.Take(cap).ToList(), items.Count - cap);
    }

    /// <summary>
    /// The grid placement of one chip inside the cell: the lane is the column, the row keeps the
    /// chronological order inside that lane, and an event from a narrower cluster spans the lanes the
    /// cell does not need. Returned as inline CSS so the stylesheet only owns the grid itself.
    /// </summary>
    public static string CellStyle(TimelineDayItem item)
    {
        var column = item.Lane + 1;
        var span = Math.Max(1, item.CellLanes - item.TotalLanes + 1);
        return string.Create(CultureInfo.InvariantCulture,
            $"--tm-scheduler-lane: {item.Lane}; --tm-scheduler-lanes: {item.CellLanes}; --tm-scheduler-lane-row: {item.Row}; grid-column: {column} / span {span}; grid-row: {item.Row + 1};");
    }
}
