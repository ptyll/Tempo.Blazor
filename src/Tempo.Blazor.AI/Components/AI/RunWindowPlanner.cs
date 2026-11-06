using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The scheduling strategy of a runs board: how each run's units are distributed over the supply windows.
/// <para>
/// The rule is first-fit with gap filling. Windows are taken in their own time order; a run starts in its PREFERRED
/// window (the first segment the host stored, or the window the reader moved it to) and then fills what is left of
/// that window, and of the windows after it, before it spills into the next one. A run that no longer fits anywhere
/// is reported as UNPLACED units instead of silently overflowing a window, and a window the host has committed beyond
/// its capacity is reported as OVERLOADED instead of being quietly treated as empty.
/// </para>
/// <para>
/// A segment's time slice is proportional to the units it takes: a window with 8 capacity offering 4 h gives each unit
/// half an hour, so the bar a reader sees is the schedule and not a decoration. Pure arithmetic over the arguments:
/// no clock, no state, no component — the whole schedule is unit-testable.
/// </para>
/// </summary>
internal static class RunWindowPlanner
{
    /// <summary>
    /// The windows a plan can use, in time order: a window that offers no time or no capacity is dropped, and two
    /// windows that start at the same instant keep the host's declaration order.
    /// </summary>
    /// <param name="windows">The host's windows, in any order.</param>
    /// <returns>A copy in plan order; the input list is never touched.</returns>
    public static IReadOnlyList<RunWindow> OrderedWindows(IReadOnlyList<RunWindow>? windows)
    {
        if (windows is null || windows.Count == 0)
        {
            return [];
        }

        var usable = new List<RunWindow>();
        foreach (var window in windows)
        {
            if (window is not null && window.HasDuration && window.EffectiveCapacity > 0)
            {
                usable.Add(window);
            }
        }

        // OrderBy is stable, so windows starting at the same instant keep the host's order.
        return [.. usable.OrderBy(window => window.Start)];
    }

    /// <summary>
    /// Places every run over the windows, in the order the host declared them.
    /// </summary>
    /// <param name="windows">The host's windows, in any order.</param>
    /// <param name="runs">The runs to place, in the host's order (which is also the placement order).</param>
    /// <param name="preferredWindowIds">
    /// Per-run window ids that win over the run's own first segment — the reader's own placement, which the host has not
    /// round-tripped yet. Null means every run uses its own preference.
    /// </param>
    /// <returns>One plan per run, in the order the runs were given. Never null.</returns>
    public static IReadOnlyList<ScheduledRunPlan> Plan(
        IReadOnlyList<RunWindow>? windows,
        IReadOnlyList<ScheduledRun>? runs,
        IReadOnlyDictionary<string, string>? preferredWindowIds = null)
    {
        var ordered = OrderedWindows(windows);
        if (runs is null || runs.Count == 0 || ordered.Count == 0)
        {
            return
            [
                .. (runs ?? []).Where(run => run is not null).Select(run => new ScheduledRunPlan(run, [], 0, run.PlannedUnits))
            ];
        }

        // The supply each window still offers, and how much of it the plan has already filled: a second run in the
        // same window starts where the first one ended, which is what "filling the gaps" means on screen.
        var remaining = new int[ordered.Count];
        var offset = new int[ordered.Count];
        for (var index = 0; index < ordered.Count; index++)
        {
            remaining[index] = ordered[index].Available;
            offset[index] = ordered[index].EffectiveUsed;
        }

        var plans = new List<ScheduledRunPlan>(runs.Count);
        foreach (var run in runs)
        {
            if (run is null)
            {
                continue;
            }

            var units = run.PlannedUnits;
            if (units == 0)
            {
                plans.Add(new ScheduledRunPlan(run, [], 0, 0));
                continue;
            }

            var startIndex = IndexOfPreferred(ordered, Preferred(run, preferredWindowIds));
            var segments = new List<ScheduledRunSegment>();
            var placed = 0;

            for (var index = startIndex; index < ordered.Count && placed < units; index++)
            {
                var take = Math.Min(remaining[index], units - placed);
                if (take <= 0)
                {
                    continue;
                }

                var window = ordered[index];
                var (start, end) = Slice(window, offset[index], take);
                segments.Add(new ScheduledRunSegment(window, start, end, take));
                remaining[index] -= take;
                offset[index] += take;
                placed += take;
            }

            plans.Add(new ScheduledRunPlan(run, segments, placed, units - placed));
        }

        return plans;
    }

    /// <summary>The plan one run gets when everything else stays as it is — what a move preview or a single re-place needs.</summary>
    /// <param name="windows">The host's windows, in any order.</param>
    /// <param name="run">The run to place.</param>
    /// <param name="preferredWindowId">The window it should start in, or null for first fit.</param>
    /// <returns>The run's plan.</returns>
    public static ScheduledRunPlan PlanOne(
        IReadOnlyList<RunWindow>? windows,
        ScheduledRun run,
        string? preferredWindowId)
    {
        ArgumentNullException.ThrowIfNull(run);
        var preferred = string.IsNullOrWhiteSpace(preferredWindowId)
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { [run.Id] = preferredWindowId! };
        var plans = Plan(windows, [run], preferred);
        return plans.Count > 0 ? plans[0] : new ScheduledRunPlan(run, [], 0, run.PlannedUnits);
    }

    /// <summary>
    /// The window a run should start in: the reader's pick when there is one, otherwise the run's own first segment.
    /// </summary>
    private static string? Preferred(ScheduledRun run, IReadOnlyDictionary<string, string>? preferredWindowIds)
    {
        if (preferredWindowIds is not null
            && preferredWindowIds.TryGetValue(run.Id, out var preferred)
            && !string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return run.PreferredWindowId;
    }

    /// <summary>The index of a preferred window, or 0 when the preference names a window the plan cannot use.</summary>
    private static int IndexOfPreferred(IReadOnlyList<RunWindow> ordered, string? preferredWindowId)
    {
        if (string.IsNullOrWhiteSpace(preferredWindowId))
        {
            return 0;
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            if (string.Equals(ordered[index].Id, preferredWindowId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        // A preference for a window that is gone (the host reshaped the week) must not hide the run: first fit is
        // still a placement, and the plan says so instead of dropping the run silently.
        return 0;
    }

    /// <summary>
    /// The time slice <paramref name="units"/> take at <paramref name="offset"/> units into a window, proportional to
    /// the window's own capacity and length.
    /// </summary>
    private static (DateTimeOffset Start, DateTimeOffset End) Slice(RunWindow window, int offset, int units)
    {
        var capacity = window.EffectiveCapacity;
        var ticks = window.Duration.Ticks;
        if (capacity <= 0 || ticks <= 0)
        {
            return (window.Start, window.Start);
        }

        var startTicks = Clamp(offset, capacity);
        var endTicks = Clamp(offset + units, capacity);
        return (window.Start.AddTicks(ticks * startTicks / capacity),
                window.Start.AddTicks(ticks * endTicks / capacity));
    }

    private static long Clamp(int value, int capacity)
        => value < 0 ? 0 : value > capacity ? capacity : value;
}

/// <summary>One slice of a run placed in one window, as the planner computes it.</summary>
/// <param name="Window">The window the slice sits in.</param>
/// <param name="Start">When the slice starts.</param>
/// <param name="End">When it ends.</param>
/// <param name="Units">How many units of the run the slice covers.</param>
internal sealed record ScheduledRunSegment(RunWindow Window, DateTimeOffset Start, DateTimeOffset End, int Units)
{
    /// <summary>How long the slice lasts, or <see cref="TimeSpan.Zero"/> when it ends before it starts.</summary>
    internal TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}

/// <summary>
/// What the planner decided for one run: the slices it got, how many units that covers and how many are left over.
/// </summary>
/// <param name="Run">The run being planned.</param>
/// <param name="Segments">The slices, in window order. Empty when nothing could be placed.</param>
/// <param name="UnitsPlaced">How many units the slices cover.</param>
/// <param name="UnitsUnplaced">How many units did not fit anywhere.</param>
internal sealed record ScheduledRunPlan(
    ScheduledRun Run,
    IReadOnlyList<ScheduledRunSegment> Segments,
    int UnitsPlaced,
    int UnitsUnplaced)
{
    /// <summary>Whether the run got any room at all.</summary>
    internal bool IsPlaced => UnitsPlaced > 0;

    /// <summary>Whether every unit of the run found a window.</summary>
    internal bool IsComplete => UnitsUnplaced == 0;

    /// <summary>Whether the run had to spill into more than one window.</summary>
    internal bool SpansWindows => Segments.Count > 1;

    /// <summary>The window the run starts in, or null when it was not placed.</summary>
    internal RunWindow? FirstWindow => Segments.Count > 0 ? Segments[0].Window : null;

    /// <summary>The window the run continues in last, or null when it was not placed.</summary>
    internal RunWindow? LastWindow => Segments.Count > 0 ? Segments[^1].Window : null;

    /// <summary>The windows the run occupies, in plan order.</summary>
    internal IReadOnlyList<RunWindow> Windows => [.. Segments.Select(segment => segment.Window)];

    /// <summary>The slice the run has in one window, or null when it is not in it.</summary>
    /// <param name="windowId">The window to look for.</param>
    /// <returns>The slice, or null.</returns>
    internal ScheduledRunSegment? SegmentIn(string? windowId)
    {
        foreach (var segment in Segments)
        {
            if (string.Equals(segment.Window.Id, windowId, StringComparison.Ordinal))
            {
                return segment;
            }
        }

        return null;
    }
}
