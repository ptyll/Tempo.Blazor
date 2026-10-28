using Tempo.Blazor.Abstractions.WorkItems;
namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// Helper utilities for working with Gantt task hierarchies and timelines.
/// </summary>
public static class GanttHelper
{
    /// <summary>
    /// Builds a tree structure from flat tasks using <see cref="TmWorkItem.ParentId"/>.
    /// </summary>
    public static IReadOnlyList<GanttTaskNode> BuildTree(IReadOnlyList<TmWorkItem> tasks)
    {
        var nodes = tasks.Select(t => new GanttTaskNode(t)).ToDictionary(n => n.Task.Id);

        var roots = new List<GanttTaskNode>();
        foreach (var node in nodes.Values)
        {
            if (string.IsNullOrEmpty(node.Task.ParentId) || !nodes.TryGetValue(node.Task.ParentId, out var parent))
            {
                roots.Add(node);
            }
            else
            {
                parent.Children.Add(node);
                node.Depth = parent.Depth + 1;
            }
        }

        AssignWbs(roots, "");
        return roots;
    }

    private static void AssignWbs(IList<GanttTaskNode> nodes, string prefix)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var wbs = prefix == "" ? $"{i + 1}" : $"{prefix}.{i + 1}";
            nodes[i].WbsNumber = wbs;
            AssignWbs(nodes[i].Children, wbs);
        }
    }

    /// <summary>
    /// Sorts a tree by task start date at every level (stable, in-place of copies).
    /// </summary>
    public static List<GanttTaskNode> CascadeSort(List<GanttTaskNode> roots)
    {
        var sorted = roots.OrderBy(n => n.Task.ScheduledStart()).ToList();
        foreach (var node in sorted)
        {
            if (node.Children.Count > 0)
            {
                var sortedChildren = CascadeSort(node.Children);
                node.Children.Clear();
                node.Children.AddRange(sortedChildren);
            }
        }

        AssignWbs(sorted, "");
        return sorted;
    }

    /// <summary>
    /// Returns the pixel offset of the current time within the current day for the hours zoom view.
    /// The instant is the machine clock; callers that judge "now" in a zone of their own pass it
    /// through <see cref="GetCurrentTimeOffset(DateTime, double, DateTimeOffset)"/>.
    /// </summary>
    public static double GetCurrentTimeOffset(DateTime dayStart, double pixelPerHour)
        => GetCurrentTimeOffset(dayStart, pixelPerHour, DateTimeOffset.Now);

    /// <summary>
    /// Returns the pixel offset of <paramref name="now"/> within the current day for the hours zoom
    /// view. The Gantt passes the injected <see cref="TimeProvider"/> converted into the display
    /// zone, so the marker never reads <see cref="DateTime.Now"/>.
    /// </summary>
    public static double GetCurrentTimeOffset(DateTime dayStart, double pixelPerHour, DateTimeOffset now)
    {
        var elapsed = (now.DateTime - dayStart).TotalHours;
        return Math.Max(0, elapsed * pixelPerHour);
    }

    /// <summary>
    /// Builds upper and lower timeline header rows for the given zoom preset.
    /// </summary>
    public static (IList<TimelineHeader> Upper, IList<TimelineHeader> Lower) BuildTimelineHeaderRows(
        GanttZoomPreset preset, DateTime start, DateTime end, double pixelPerDay)
    {
        var upper = new List<TimelineHeader>();
        var lower = new List<TimelineHeader>();

        switch (preset)
        {
            case GanttZoomPreset.Hours:
                BuildHoursHeader(start, end, pixelPerDay, upper, lower);
                break;
            case GanttZoomPreset.Days:
                BuildDaysHeader(start, end, pixelPerDay, upper, lower);
                break;
            case GanttZoomPreset.Weeks:
                BuildWeeksHeader(start, end, pixelPerDay, upper, lower);
                break;
            case GanttZoomPreset.Months:
                BuildMonthsHeader(start, end, pixelPerDay, upper, lower);
                break;
            case GanttZoomPreset.Quarters:
                BuildQuartersHeader(start, end, pixelPerDay, upper, lower);
                break;
            case GanttZoomPreset.Years:
                BuildYearsHeader(start, end, pixelPerDay, upper, lower);
                break;
        }

        return (upper, lower);
    }

    private static void BuildHoursHeader(DateTime start, DateTime end, double pixelPerDay,
        List<TimelineHeader> upper, List<TimelineHeader> lower)
    {
        var pixelPerHour = pixelPerDay / 24.0;
        var current = start.Date;
        while (current < end.Date.AddDays(1))
        {
            var dayOffset = (current - start).TotalDays * pixelPerDay;
            upper.Add(new TimelineHeader(current.ToString("ddd dd MMM"), dayOffset, pixelPerDay));
            for (var h = 0; h < 24; h++)
            {
                var hOffset = dayOffset + h * pixelPerHour;
                lower.Add(new TimelineHeader($"{h:D2}", hOffset, pixelPerHour));
            }

            current = current.AddDays(1);
        }
    }

    private static void BuildDaysHeader(DateTime start, DateTime end, double pixelPerDay,
        List<TimelineHeader> upper, List<TimelineHeader> lower)
    {
        var current = new DateTime(start.Year, start.Month, 1);
        while (current <= end)
        {
            var next = current.AddMonths(1);
            var clampedStart = current < start ? start : current;
            var clampedEnd = next > end ? end : next;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            upper.Add(new TimelineHeader(current.ToString("MMMM yyyy"), offset, width));
            current = next;
        }

        var day = start.Date;
        while (day <= end.Date)
        {
            var offset = (day - start).TotalDays * pixelPerDay;
            lower.Add(new TimelineHeader(day.Day.ToString(), offset, pixelPerDay));
            day = day.AddDays(1);
        }
    }

    private static void BuildWeeksHeader(DateTime start, DateTime end, double pixelPerDay,
        List<TimelineHeader> upper, List<TimelineHeader> lower)
    {
        var current = new DateTime(start.Year, start.Month, 1);
        while (current <= end)
        {
            var next = current.AddMonths(1);
            var clampedStart = current < start ? start : current;
            var clampedEnd = next > end ? end : next;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            upper.Add(new TimelineHeader(current.ToString("MMMM yyyy"), offset, width));
            current = next;
        }

        var week = start.Date;
        while (week.DayOfWeek != DayOfWeek.Monday) week = week.AddDays(-1);
        while (week <= end.Date)
        {
            var weekEnd = week.AddDays(7);
            var clampedStart = week < start ? start : week;
            var clampedEnd = weekEnd > end ? end : weekEnd;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            var weekNum = System.Globalization.ISOWeek.GetWeekOfYear(week);
            lower.Add(new TimelineHeader($"W{weekNum}", offset, width));
            week = weekEnd;
        }
    }

    private static void BuildMonthsHeader(DateTime start, DateTime end, double pixelPerDay,
        List<TimelineHeader> upper, List<TimelineHeader> lower)
    {
        var yearStart = new DateTime(start.Year, 1, 1);
        while (yearStart.Year <= end.Year)
        {
            var yearEnd = new DateTime(yearStart.Year + 1, 1, 1);
            var clampedStart = yearStart < start ? start : yearStart;
            var clampedEnd = yearEnd > end ? end : yearEnd;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            upper.Add(new TimelineHeader(yearStart.Year.ToString(), offset, width));
            yearStart = yearEnd;
        }

        var month = new DateTime(start.Year, start.Month, 1);
        while (month <= end)
        {
            var next = month.AddMonths(1);
            var clampedStart = month < start ? start : month;
            var clampedEnd = next > end ? end : next;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            lower.Add(new TimelineHeader(month.ToString("MMM"), offset, width));
            month = next;
        }
    }

    private static void BuildQuartersHeader(DateTime start, DateTime end, double pixelPerDay,
        List<TimelineHeader> upper, List<TimelineHeader> lower)
    {
        var yearStart = new DateTime(start.Year, 1, 1);
        while (yearStart.Year <= end.Year)
        {
            var yearEnd = new DateTime(yearStart.Year + 1, 1, 1);
            var clampedStart = yearStart < start ? start : yearStart;
            var clampedEnd = yearEnd > end ? end : yearEnd;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            upper.Add(new TimelineHeader(yearStart.Year.ToString(), offset, width));
            yearStart = yearEnd;
        }

        var quarterStart = new DateTime(start.Year, ((start.Month - 1) / 3) * 3 + 1, 1);
        while (quarterStart <= end)
        {
            var next = quarterStart.AddMonths(3);
            var clampedStart = quarterStart < start ? start : quarterStart;
            var clampedEnd = next > end ? end : next;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            var q = (quarterStart.Month - 1) / 3 + 1;
            lower.Add(new TimelineHeader($"Q{q}", offset, width));
            quarterStart = next;
        }
    }

    private static void BuildYearsHeader(DateTime start, DateTime end, double pixelPerDay,
        List<TimelineHeader> upper, List<TimelineHeader> lower)
    {
        var decadeStart = new DateTime((start.Year / 10) * 10, 1, 1);
        var decadeEnd = decadeStart.AddYears(10);
        var clampedS = decadeStart < start ? start : decadeStart;
        var clampedE = decadeEnd > end ? end : decadeEnd;
        upper.Add(new TimelineHeader($"{decadeStart.Year}s", (clampedS - start).TotalDays * pixelPerDay,
            (clampedE - clampedS).TotalDays * pixelPerDay));

        var year = new DateTime(start.Year, 1, 1);
        while (year.Year <= end.Year)
        {
            var next = year.AddYears(1);
            var clampedStart = year < start ? start : year;
            var clampedEnd = next > end ? end : next;
            var offset = (clampedStart - start).TotalDays * pixelPerDay;
            var width = (clampedEnd - clampedStart).TotalDays * pixelPerDay;
            lower.Add(new TimelineHeader(year.Year.ToString(), offset, width));
            year = next;
        }
    }

    /// <summary>
    /// Flattens a tree back to a list respecting expand/collapse state.
    /// </summary>
    public static IReadOnlyList<GanttTaskNode> FlattenVisible(IReadOnlyList<GanttTaskNode> roots)
    {
        var result = new List<GanttTaskNode>();
        foreach (var root in roots)
            FlattenRecursive(root, result);
        return result;
    }

    private static void FlattenRecursive(GanttTaskNode node, List<GanttTaskNode> result)
    {
        result.Add(node);
        if (node.Task.IsExpanded)
        {
            foreach (var child in node.Children)
                FlattenRecursive(child, result);
        }
    }

    /// <summary>
    /// Computes the overall time range for a set of tasks.
    /// </summary>
    public static (DateTime Start, DateTime End) GetTimeRange(IReadOnlyList<TmWorkItem> tasks)
    {
        if (tasks.Count == 0) return (DateTime.Today, DateTime.Today.AddDays(7));
        var start = tasks.Min(t => t.ScheduledStart());
        var end = tasks.Max(t => t.ScheduledEnd());
        if (start == end) end = end.AddDays(1);
        return (start, end);
    }

    /// <summary>
    /// Computes the X offset and width for a task bar within a timeline.
    /// </summary>
    public static (double Left, double Width) CalculateBarPosition(
        DateTime taskStart, DateTime taskEnd, DateTime timelineStart, DateTime timelineEnd, double totalWidth)
    {
        var totalDuration = timelineEnd - timelineStart;
        if (totalDuration.TotalSeconds <= 0) return (0, 0);

        var left = (taskStart - timelineStart).TotalSeconds / totalDuration.TotalSeconds * totalWidth;
        var width = (taskEnd - taskStart).TotalSeconds / totalDuration.TotalSeconds * totalWidth;
        return (Math.Max(0, left), Math.Max(1, width));
    }

    /// <summary>
    /// Recursively sets <see cref="TmWorkItem.IsExpanded"/> on every node in the tree.
    /// </summary>
    public static void SetAllExpanded(IEnumerable<GanttTaskNode> roots, bool expanded)
    {
        foreach (var node in roots)
        {
            node.Task.IsExpanded = expanded;
            SetAllExpanded(node.Children, expanded);
        }
    }

    /// <summary>
    /// Returns the horizontal pixel offset of today's date within the timeline.
    /// Negative when today is before <paramref name="timelineStart"/>.
    /// </summary>
    public static double GetTodayOffset(DateTime timelineStart, double pixelPerDay)
        => GetTodayOffset(timelineStart, pixelPerDay, DateTime.Today);

    /// <summary>
    /// Returns the horizontal pixel offset of <paramref name="today"/> within the timeline.
    /// Negative when the date is before <paramref name="timelineStart"/>. Overload for callers that
    /// judge "today" in a zone of their own (the Gantt passes the user's zone).
    /// </summary>
    public static double GetTodayOffset(DateTime timelineStart, double pixelPerDay, DateTime today)
        => (today.Date - timelineStart.Date).TotalDays * pixelPerDay;

    /// <summary>
    /// Returns offset+width pairs for non-working day columns within a date range.
    /// </summary>
    public static IEnumerable<(double Offset, double Width)> GetNonWorkingDayRects(
        DateTime start, DateTime end, double pixelPerDay, WorkingSchedule schedule)
    {
        var holidaySet = new HashSet<DateTime>(schedule.Holidays.Select(h => h.Date));
        var current = start.Date;
        while (current < end.Date)
        {
            if (schedule.NonWorkingDaysOfWeek.Contains(current.DayOfWeek) || holidaySet.Contains(current))
            {
                var offset = (current - start.Date).TotalDays * pixelPerDay;
                yield return (offset, pixelPerDay);
            }

            current = current.AddDays(1);
        }
    }

    /// <summary>
    /// Returns offset+width pairs for non-working hour blocks within a single day column.
    /// </summary>
    public static IEnumerable<(double Offset, double Width)> GetNonWorkingHourRects(
        double dayOffset, double pixelPerHour, WorkingSchedule schedule)
    {
        if (schedule.WorkDayStartHour > 0)
            yield return (dayOffset, schedule.WorkDayStartHour * pixelPerHour);

        if (schedule.WorkDayEndHour < 24)
            yield return (dayOffset + schedule.WorkDayEndHour * pixelPerHour,
                (24 - schedule.WorkDayEndHour) * pixelPerHour);
    }

    /// <summary>
    /// Filters a tree of nodes by the given criteria.
    /// A parent node is kept if it or any of its descendants matches all filters.
    /// Field names: standard props ("Title", "Status", "Priority", and the dates "Start", "End", "Deadline") or "custom:{fieldId}".
    /// </summary>
    public static List<GanttTaskNode> ApplyFilters(
        IEnumerable<GanttTaskNode> roots,
        IEnumerable<GanttFilter> filters)
    {
        var filterList = filters.ToList();
        if (filterList.Count == 0)
            return roots.ToList();

        var result = new List<GanttTaskNode>();
        foreach (var root in roots)
            CollectFilteredFlat(root, filterList, result);
        return result;
    }

    private static bool CollectFilteredFlat(
        GanttTaskNode node,
        List<GanttFilter> filterList,
        List<GanttTaskNode> result)
    {
        var selfMatches = filterList.All(f => MatchesFilter(node.Task, f));

        var childVisible = new List<GanttTaskNode>();
        foreach (var child in node.Children)
            CollectFilteredFlat(child, filterList, childVisible);

        if (!selfMatches && childVisible.Count == 0)
            return false;

        result.Add(new GanttTaskNode(node.Task) { Depth = node.Depth, WbsNumber = node.WbsNumber });
        result.AddRange(childVisible);
        return true;
    }

    private static bool MatchesFilter(TmWorkItem task, GanttFilter filter)
    {
        var field = filter.Field;
        var op    = filter.Operator;
        var value = filter.Value ?? string.Empty;

        if (field.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            var fieldId = field["custom:".Length..];
            var taskVal = task.CustomFields.TryGetValue(fieldId, out var cv) ? cv ?? "" : "";
            return ApplyStringOp(taskVal, op, value);
        }

        return field switch
        {
            "Title"    => ApplyStringOp(task.Title, op, value),
            "Status"   => ApplyStringOp(task.Status.ToString(), op, value),
            "Priority" => ApplyStringOp(task.Priority.ToString(), op, value),
            "Start"    => ApplyDateOp(task.Start, op, filter.Value),
            "End"      => ApplyDateOp(task.End, op, filter.Value),
            "Deadline" => ApplyDateOp(task.DueDate, op, filter.Value),
            _          => true
        };
    }

    /// <summary>
    /// Date comparison on the calendar day. The value is an ISO <c>yyyy-MM-dd</c> date (what a native date input
    /// writes), parsed culture-invariantly; one that does not parse filters nothing, so a half-edited row cannot
    /// hide the whole chart. A task without a date (only <c>Deadline</c> can lack one) is neither equal to, before
    /// nor after any date, and is "not equal" to every one.
    /// </summary>
    private static bool ApplyDateOp(DateTime? actual, GanttFilterOperator op, string? expected)
    {
        switch (op)
        {
            case GanttFilterOperator.IsEmpty: return actual is null;
            case GanttFilterOperator.IsNotEmpty: return actual is not null;
        }

        if (!System.DateTime.TryParseExact(expected, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var wanted))
        {
            return true;
        }

        if (actual is null) return op == GanttFilterOperator.NotEquals;

        var day = actual.Value.Date;
        return op switch
        {
            GanttFilterOperator.Equals    => day == wanted.Date,
            GanttFilterOperator.NotEquals => day != wanted.Date,
            GanttFilterOperator.Before    => day < wanted.Date,
            GanttFilterOperator.After     => day > wanted.Date,
            _                             => true
        };
    }

    private static bool ApplyStringOp(string actual, GanttFilterOperator op, string expected) => op switch
    {
        GanttFilterOperator.Equals     => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
        GanttFilterOperator.NotEquals  => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
        GanttFilterOperator.Contains   => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        GanttFilterOperator.IsEmpty    => string.IsNullOrWhiteSpace(actual),
        GanttFilterOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(actual),
        _                              => true
    };

    /// <summary>
    /// Filters an activity-log sequence to only entries whose <see cref="Tempo.Blazor.Abstractions.Shared.TmActivityEntry.Action"/>
    /// is in <paramref name="changeTypes"/>. Passing an empty collection returns all entries.
    /// </summary>
    public static IReadOnlyList<Tempo.Blazor.Abstractions.Shared.TmActivityEntry> FilterHistory(
        IEnumerable<Tempo.Blazor.Abstractions.Shared.TmActivityEntry> history,
        IEnumerable<string> changeTypes)
    {
        var types = changeTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return types.Count == 0
            ? history.ToList()
            : history.Where(e => types.Contains(e.Action)).ToList();
    }

    /// <summary>
    /// Returns the min Start and max End spanning all <paramref name="children"/>.
    /// Returns null when the list is empty.
    /// </summary>
    public static (DateTime MinStart, DateTime MaxEnd)? CalculateGroupBounds(
        IReadOnlyList<TmWorkItem> children)
    {
        if (children.Count == 0) return null;
        var min = children.Min(c => c.ScheduledStart());
        var max = children.Max(c => c.ScheduledEnd());
        return (min, max);
    }

    /// <summary>
    /// Sums the durations of completed time-log entries.
    /// In-progress entries (StoppedAt is null) are counted up to UtcNow.
    /// </summary>
    public static double CalculateTotalLoggedHours(IEnumerable<GanttTimeLogEntry> log)
    {
        var now = DateTime.UtcNow;
        return log.Sum(e => ((e.StoppedAt ?? now) - e.StartedAt).TotalHours);
    }
}

/// <summary>
/// A node in the Gantt task tree.
/// </summary>
public class GanttTaskNode
{
    /// <summary>The task data.</summary>
    public TmWorkItem Task { get; }

    /// <summary>Child nodes.</summary>
    public List<GanttTaskNode> Children { get; } = [];

    /// <summary>Depth in the tree (0 for roots).</summary>
    public int Depth { get; set; }

    /// <summary>WBS number assigned by <see cref="GanttHelper.BuildTree"/>.</summary>
    public string WbsNumber { get; set; } = string.Empty;

    public GanttTaskNode(TmWorkItem task) => Task = task;
}

/// <summary>A header cell in the two-row Gantt timeline header.</summary>
public record TimelineHeader(string Label, double Offset, double Width);
