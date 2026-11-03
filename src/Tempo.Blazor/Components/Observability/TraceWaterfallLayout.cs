using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Observability;

/// <summary>
/// The axis unit a waterfall tick is expressed in. The unit follows the visible range: a range under
/// one second is measured in milliseconds, under a minute in seconds, and anything longer in
/// minutes. The unit's LABEL is localized by the component, never by this pure helper.
/// </summary>
internal enum TraceAxisUnit
{
    /// <summary>Milliseconds.</summary>
    Ms,

    /// <summary>Seconds.</summary>
    Seconds,

    /// <summary>Minutes.</summary>
    Minutes
}

/// <summary>
/// A visible slice of a trace, as a fraction of the full axis: <c>Start</c> + <c>Width</c> must stay
/// inside <c>[0, 1]</c>. The full trace is <see cref="Full"/>; zooming narrows the window around its
/// centre.
/// </summary>
internal readonly record struct TraceWaterfallWindow(double Start, double Width)
{
    /// <summary>The whole trace.</summary>
    public static TraceWaterfallWindow Full => new(0, 1);

    /// <summary>
    /// The window clamped into a usable range: the start into <c>[0, 1]</c>, the width into
    /// <c>[0.01, 1 - start]</c> (a zero-width window would divide by zero and render nothing, so a
    /// zoom-in bottoms out at 1 %). A NaN/negative input is treated as <see cref="Full"/>.
    /// </summary>
    public TraceWaterfallWindow Normalized()
    {
        if (double.IsNaN(Start) || double.IsNaN(Width) || Width <= 0)
        {
            return Full;
        }

        var start = Math.Clamp(Start, 0, 1);
        var width = Math.Clamp(Width, 0.01, 1 - start);
        return new TraceWaterfallWindow(start, width);
    }
}

/// <summary>One tick of the waterfall time axis.</summary>
internal sealed record TraceWaterfallAxisTick
{
    /// <summary>The tick's offset from the visible axis start.</summary>
    public TimeSpan Offset { get; init; }

    /// <summary>The tick's position along the axis, in percent (0–100).</summary>
    public double Percent { get; init; }
}

/// <summary>
/// The time axis of the current render: where it starts, how long it is, which unit its ticks are in
/// and the ticks themselves.
/// </summary>
internal sealed record TraceWaterfallAxis
{
    /// <summary>The absolute start of the visible axis.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>The visible span of time. Never zero (a degenerate trace yields 1 ms).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The unit the ticks are expressed in, chosen from <see cref="Duration"/>.</summary>
    public TraceAxisUnit Unit { get; init; }

    /// <summary>The ticks, ascending, starting at the axis start.</summary>
    public IReadOnlyList<TraceWaterfallAxisTick> Ticks { get; init; } = [];

    /// <summary>Formats one tick offset in <see cref="Unit"/> with the culture's number format.</summary>
    /// <param name="tick">The tick to format.</param>
    /// <param name="culture">The culture to format with.</param>
    /// <returns>The numeric part only — the component appends the localized unit label.</returns>
    public string FormatTick(TraceWaterfallAxisTick tick, IFormatProvider? culture)
        => TraceWaterfallFormat.FormatAxisValue(tick.Offset, Unit, culture);
}

/// <summary>
/// One visible row of the waterfall: the span, its place in the tree and its bar measured against
/// the current axis (plus the sums of its subtree's metrics).
/// </summary>
internal sealed record TraceWaterfallRow
{
    /// <summary>The span this row renders.</summary>
    public required TraceSpan Span { get; init; }

    /// <summary>The row's position in the FULL pre-order tree. Used as the roving-tabindex slot and as a stable key.</summary>
    public required int Index { get; init; }

    /// <summary>The tree depth; a root is 0.</summary>
    public required int Depth { get; init; }

    /// <summary>Whether the span has children, so the row renders a disclosure.</summary>
    public required bool HasChildren { get; init; }

    /// <summary>
    /// Whether the span hangs under the root because its parent could not be resolved (missing,
    /// rejected or duplicated) rather than because it is a real root.
    /// </summary>
    public required bool IsOrphan { get; init; }

    /// <summary>The bar's start, as a percentage of the axis.</summary>
    public required double OffsetPercent { get; init; }

    /// <summary>The bar's width, as a percentage of the axis.</summary>
    public required double WidthPercent { get; init; }

    /// <summary>Whether any part of the span falls inside the visible window.</summary>
    public required bool IsVisible { get; init; }

    /// <summary>The span's duration on this render (a running span ends at "now"). Never negative.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Whether the span is the slowest row of the trace.</summary>
    public required bool IsSlowest { get; init; }

    /// <summary>
    /// The sum of every metric over this span's subtree, INCLUDING the span's own metrics. A key no
    /// descendant reports is absent (the same "not reported" contract as the span's own map).
    /// </summary>
    public required IReadOnlyDictionary<string, double> SubtreeMetrics { get; init; }
}

/// <summary>The result of one <see cref="TraceWaterfallLayout.Build"/> pass.</summary>
internal sealed record TraceWaterfallLayoutResult
{
    /// <summary>The rows in pre-order (every parent before its children), rejected spans excluded.</summary>
    public IReadOnlyList<TraceWaterfallRow> Rows { get; init; } = [];

    /// <summary>The visible time axis.</summary>
    public required TraceWaterfallAxis Axis { get; init; }

    /// <summary>The id of the slowest row, or null when there are no rows.</summary>
    public string? SlowestSpanId { get; init; }

    /// <summary>
    /// The ids of the spans rejected because they take part in a parent cycle (including a span
    /// parented to itself). They render no row; their children become orphans under the root.
    /// </summary>
    public IReadOnlyList<string> RejectedSpanIds { get; init; } = [];

    /// <summary>How many rendered spans are still running.</summary>
    public int RunningCount { get; init; }

    /// <summary>How many rendered spans failed.</summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// The FULL trace duration, before any zoom window — what the toolbar summary reports, so zooming
    /// never changes what the trace is.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>An empty result for a host that supplied nothing.</summary>
    /// <param name="now">The time the empty axis is anchored to.</param>
    public static TraceWaterfallLayoutResult Empty(DateTimeOffset now) => new()
    {
        Axis = new TraceWaterfallAxis
        {
            Start = now,
            Duration = TimeSpan.FromMilliseconds(1),
            Unit = TraceAxisUnit.Ms,
            Ticks = []
        }
    };
}

/// <summary>
/// The pure layout pass of <c>TmTraceWaterfall</c>: it builds the tree from the flat span list,
/// rejects cycles, measures every bar against the visible axis and sums each metric over each
/// subtree. No rendering, no services, no clock of its own — "now" is always a parameter, which is
/// what makes the live mode testable with a fake clock.
/// </summary>
internal static class TraceWaterfallLayout
{
    /// <summary>
    /// How many tick intervals an axis should aim for. Three: the timeline column shares the row
    /// width with the name column, the duration and every metric column, and a detail panel can take
    /// a third of the card — five labels ("0", "0.25 s", "0.5 s", "0.75 s", "1 s") collide in the
    /// ~200 px that are left, while three stay apart and still read the extent.
    /// </summary>
    private const double TargetIntervals = 3;

    /// <summary>The candidate tick steps, in milliseconds, that read as "nice" numbers.</summary>
    private static readonly double[] CandidateStepsMs =
    [
        0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 25, 50,
        100, 200, 250, 500, 1_000, 2_000, 5_000, 10_000, 15_000, 30_000,
        60_000, 120_000, 300_000, 600_000, 900_000, 1_800_000, 3_600_000
    ];

    /// <summary>
    /// Builds the layout for one render.
    /// </summary>
    /// <param name="spans">
    /// The flat span list. A null or empty list yields <see cref="TraceWaterfallLayoutResult.Empty"/>.
    /// A span with a blank id is skipped; a repeated id keeps its FIRST occurrence (a later duplicate
    /// cannot be addressed by parent links anyway).
    /// </param>
    /// <param name="now">
    /// The clock the layout treats as "now": every span without an end (running, or a host that has
    /// not filled it in yet) extends to this instant.
    /// </param>
    /// <param name="window">The visible slice of the trace; null means the whole trace.</param>
    public static TraceWaterfallLayoutResult Build(
        IReadOnlyList<TraceSpan>? spans,
        DateTimeOffset now,
        TraceWaterfallWindow? window = null)
    {
        var ordered = UniqueSpans(spans);
        if (ordered.Count == 0)
        {
            return TraceWaterfallLayoutResult.Empty(now);
        }

        var rejected = FindCycleMembers(ordered);
        var roots = new List<(TraceSpan Span, bool IsOrphan)>();
        var children = new Dictionary<string, List<TraceSpan>>(StringComparer.Ordinal);
        var byId = ordered.ToDictionary(span => span.Id, StringComparer.Ordinal);

        foreach (var span in ordered)
        {
            if (rejected.Contains(span.Id))
            {
                continue; // a cycle member renders no row; its children resolve as orphans
            }

            var parent = ResolveParent(span, byId, rejected);
            if (parent is null)
            {
                // A root with a parent id is an ORPHAN: the value named a span that does not exist
                // (or that the cycle pass rejected). A genuine root has no parent id at all.
                roots.Add((span, IsOrphan(span)));
            }
            else
            {
                if (!children.TryGetValue(parent.Id, out var siblings))
                {
                    children[parent.Id] = siblings = [];
                }

                siblings.Add(span);
            }
        }

        var entries = Flatten(roots, children);
        if (entries.Count == 0)
        {
            return TraceWaterfallLayoutResult.Empty(now);
        }

        var subtreeMetrics = SumSubtrees(entries, children);
        var axis = BuildAxis(entries, now, window, out var totalDuration);
        var slowest = FindSlowest(entries, children, now);

        var rows = new List<TraceWaterfallRow>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var (offset, width, visible) = Measure(entry.Span, axis, now);
            rows.Add(new TraceWaterfallRow
            {
                Span = entry.Span,
                Index = index,
                Depth = entry.Depth,
                HasChildren = children.TryGetValue(entry.Span.Id, out var kids) && kids.Count > 0,
                IsOrphan = entry.IsOrphan,
                OffsetPercent = offset,
                WidthPercent = width,
                IsVisible = visible,
                Duration = Duration(entry.Span, now),
                IsSlowest = string.Equals(slowest, entry.Span.Id, StringComparison.Ordinal),
                SubtreeMetrics = subtreeMetrics[entry.Span.Id]
            });
        }

        return new TraceWaterfallLayoutResult
        {
            Rows = rows,
            Axis = axis,
            SlowestSpanId = slowest,
            RejectedSpanIds = rejected.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            TotalDuration = totalDuration,
            RunningCount = rows.Count(row => row.Span.Status == TraceSpanStatus.Running),
            ErrorCount = rows.Count(row => row.Span.Status == TraceSpanStatus.Error)
        };
    }

    /// <summary>
    /// The spans that can participate in the tree, in input order: a blank id is unusable and is
    /// skipped, and a repeated id keeps its first occurrence (a child naming that id resolves to the
    /// first span, so dropping the duplicate is the only deterministic reading).
    /// </summary>
    private static List<TraceSpan> UniqueSpans(IReadOnlyList<TraceSpan>? spans)
    {
        var ordered = new List<TraceSpan>();
        if (spans is null)
        {
            return ordered;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var span in spans)
        {
            if (span is null || string.IsNullOrWhiteSpace(span.Id))
            {
                continue;
            }

            if (seen.Add(span.Id))
            {
                ordered.Add(span);
            }
        }

        return ordered;
    }

    /// <summary>
    /// The ids of every span that takes part in a parent cycle. The walk follows parent links with a
    /// three-colour state map, so a chain is visited once: entering a node that the CURRENT walk has
    /// already entered means the stack from that node onward is the cycle. Nothing recurses, so a
    /// deep trace cannot overflow the stack and a cycle cannot loop.
    /// </summary>
    private static HashSet<string> FindCycleMembers(IReadOnlyList<TraceSpan> spans)
    {
        var byId = spans.ToDictionary(span => span.Id, StringComparer.Ordinal);
        const int unvisited = 0;
        const int inProgress = 1;
        const int done = 2;
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var cycles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in spans)
        {
            if (state.ContainsKey(start.Id))
            {
                continue;
            }

            var path = new List<string>();
            var current = start.Id;
            while (current is not null)
            {
                if (!byId.TryGetValue(current, out var span))
                {
                    break; // a parent that does not exist terminates the walk
                }

                if (state.TryGetValue(current, out var colour))
                {
                    if (colour == inProgress)
                    {
                        var from = path.IndexOf(current);
                        for (var index = from; index >= 0 && index < path.Count; index++)
                        {
                            cycles.Add(path[index]);
                        }
                    }

                    break;
                }

                state[current] = inProgress;
                path.Add(current);
                current = span.ParentId;
            }

            foreach (var id in path)
            {
                state[id] = done;
            }
        }

        return cycles;
    }

    /// <summary>
    /// The parent a span hangs under, or null when it belongs to the root: no parent id, a parent id
    /// naming no supplied span, a parent that is a cycle member, or a span parented to itself.
    /// </summary>
    private static TraceSpan? ResolveParent(
        TraceSpan span,
        IReadOnlyDictionary<string, TraceSpan> byId,
        IReadOnlySet<string> rejected)
    {
        if (string.IsNullOrWhiteSpace(span.ParentId)
            || string.Equals(span.ParentId, span.Id, StringComparison.Ordinal))
        {
            return null;
        }

        return byId.TryGetValue(span.ParentId, out var parent) && !rejected.Contains(parent.Id)
            ? parent
            : null;
    }

    /// <summary>Flattens the forest into pre-order entries without recursing (a deep trace is legal).</summary>
    private static List<(TraceSpan Span, int Depth, bool IsOrphan)> Flatten(
        List<(TraceSpan Span, bool IsOrphan)> roots,
        IReadOnlyDictionary<string, List<TraceSpan>> children)
    {
        var entries = new List<(TraceSpan Span, int Depth, bool IsOrphan)>();
        var stack = new Stack<(TraceSpan Span, int Depth, bool IsOrphan)>();
        for (var index = roots.Count - 1; index >= 0; index--)
        {
            stack.Push((roots[index].Span, 0, roots[index].IsOrphan));
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (!visited.Add(entry.Span.Id))
            {
                continue;
            }

            entries.Add(entry);
            if (children.TryGetValue(entry.Span.Id, out var kids))
            {
                for (var index = kids.Count - 1; index >= 0; index--)
                {
                    // A child reaching a node already in the tree would be a second parent — the
                    // model gives each span one, so this only guards the flattening against a
                    // duplicate id that slipped past UniqueSpans. A CHILD always has a resolved
                    // parent, so it is never an orphan even when that parent is one.
                    if (!visited.Contains(kids[index].Id))
                    {
                        stack.Push((kids[index], entry.Depth + 1, false));
                    }
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Whether a ROOT span hangs under the root because its parent id could not be resolved, rather
    /// than because it genuinely is a root. Called only for spans that resolved no parent, so a
    /// non-blank, non-self parent id is exactly the orphan case.
    /// </summary>
    private static bool IsOrphan(TraceSpan span)
        => !string.IsNullOrWhiteSpace(span.ParentId)
           && !string.Equals(span.ParentId, span.Id, StringComparison.Ordinal);

    /// <summary>
    /// Sums every metric over every subtree. A pre-order list visits a parent before its children, so
    /// walking it BACKWARDS means a child's sums are complete before the parent reads them. Each
    /// descendant is counted exactly once (the subtree map already includes the child's own value).
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, double>> SumSubtrees(
        List<(TraceSpan Span, int Depth, bool IsOrphan)> entries,
        IReadOnlyDictionary<string, List<TraceSpan>> children)
    {
        var sums = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal);
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var span = entries[index].Span;
            var total = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (key, value) in span.Metrics)
            {
                total[key] = value;
            }

            if (children.TryGetValue(span.Id, out var kids))
            {
                foreach (var child in kids)
                {
                    if (!sums.TryGetValue(child.Id, out var childSums))
                    {
                        continue;
                    }

                    foreach (var (key, value) in childSums)
                    {
                        total[key] = total.TryGetValue(key, out var existing) ? existing + value : value;
                    }
                }
            }

            sums[span.Id] = total;
        }

        return sums;
    }

    /// <summary>
    /// Builds the visible axis: the trace's own range narrowed by <paramref name="window"/>, with
    /// "nice" ticks in the unit the range reads in.
    /// </summary>
    private static TraceWaterfallAxis BuildAxis(
        List<(TraceSpan Span, int Depth, bool IsOrphan)> entries,
        DateTimeOffset now,
        TraceWaterfallWindow? window,
        out TimeSpan totalDuration)
    {
        var rangeStart = entries.Min(entry => entry.Span.StartedAt);
        var rangeEnd = entries.Max(entry => EndOf(entry.Span, now));
        var rangeDuration = rangeEnd - rangeStart;
        if (rangeDuration <= TimeSpan.Zero)
        {
            rangeDuration = TimeSpan.FromMilliseconds(1);
        }

        totalDuration = rangeDuration;
        var visible = (window ?? TraceWaterfallWindow.Full).Normalized();
        var start = rangeStart + TimeSpan.FromTicks((long)(rangeDuration.Ticks * visible.Start));
        var duration = TimeSpan.FromTicks(
            Math.Max(1, (long)(rangeDuration.Ticks * visible.Width)));

        var unit = TraceWaterfallFormat.AxisUnit(duration);
        return new TraceWaterfallAxis
        {
            Start = start,
            Duration = duration,
            Unit = unit,
            Ticks = BuildTicks(duration)
        };
    }

    /// <summary>The ticks of an axis of the given length: 0, step, 2 × step … while inside the range.</summary>
    private static List<TraceWaterfallAxisTick> BuildTicks(TimeSpan duration)
    {
        var ticks = new List<TraceWaterfallAxisTick>();
        var totalMs = duration.TotalMilliseconds;
        var stepMs = CandidateStepsMs.FirstOrDefault(step => step >= totalMs / TargetIntervals);
        if (stepMs <= 0)
        {
            stepMs = CandidateStepsMs[^1];
        }

        var step = TimeSpan.FromMilliseconds(stepMs);
        for (var multiple = 0; ; multiple++)
        {
            var offset = TimeSpan.FromTicks(step.Ticks * multiple);
            if (offset > duration)
            {
                break;
            }

            ticks.Add(new TraceWaterfallAxisTick
            {
                Offset = offset,
                Percent = totalMs <= 0 ? 0 : 100.0 * offset.TotalMilliseconds / totalMs
            });

            if (ticks.Count > 64)
            {
                break; // a hard stop: a pathological range never renders thousands of ticks
            }
        }

        return ticks;
    }

    /// <summary>
    /// Measures one span against the axis: its start offset and width in percent, clamped into the
    /// window, plus whether any part of it is inside the window at all.
    /// </summary>
    private static (double OffsetPercent, double WidthPercent, bool IsVisible) Measure(
        TraceSpan span,
        TraceWaterfallAxis axis,
        DateTimeOffset now)
    {
        var end = EndOf(span, now);
        var axisEnd = axis.Start + axis.Duration;
        var durationTicks = (double)axis.Duration.Ticks;
        var startTicks = Math.Clamp((double)(span.StartedAt - axis.Start).Ticks, 0, durationTicks);
        var endTicks = Math.Clamp((double)(end - axis.Start).Ticks, 0, durationTicks);
        var widthTicks = Math.Max(0, endTicks - startTicks);

        return (
            100.0 * startTicks / durationTicks,
            100.0 * widthTicks / durationTicks,
            end > axis.Start && span.StartedAt < axisEnd);
    }

    /// <summary>
    /// The id of the slowest span, or null when there are no rows. The search runs over LEAF spans:
    /// an intermediate span's duration is its children's envelope, and the root's is the whole
    /// request, so highlighting either would just point at the trace itself. Only a trace where every
    /// span has a child (a single path) falls back to the longest row. Ties keep the pre-order first.
    /// </summary>
    private static string? FindSlowest(
        List<(TraceSpan Span, int Depth, bool IsOrphan)> entries,
        IReadOnlyDictionary<string, List<TraceSpan>> children,
        DateTimeOffset now)
    {
        var slowest = Longest(
            entries.Where(entry => !children.TryGetValue(entry.Span.Id, out var kids) || kids.Count == 0),
            now);
        return slowest ?? Longest(entries, now);
    }

    /// <summary>The id of the longest span of a sequence, or null when the sequence is empty.</summary>
    private static string? Longest(
        IEnumerable<(TraceSpan Span, int Depth, bool IsOrphan)> entries,
        DateTimeOffset now)
    {
        string? slowest = null;
        var longest = TimeSpan.MinValue;
        foreach (var entry in entries)
        {
            var duration = Duration(entry.Span, now);
            if (duration > longest)
            {
                longest = duration;
                slowest = entry.Span.Id;
            }
        }

        return slowest;
    }

    /// <summary>The span's duration on this render: a span without an end (or ending early) is measured to "now"/zero.</summary>
    private static TimeSpan Duration(TraceSpan span, DateTimeOffset now)
    {
        var end = EndOf(span, now);
        return end > span.StartedAt ? end - span.StartedAt : TimeSpan.Zero;
    }

    /// <summary>The effective end of a span: its own end, or "now" while it is unfinished, never before its start.</summary>
    private static DateTimeOffset EndOf(TraceSpan span, DateTimeOffset now)
    {
        var end = span.EndedAt ?? now;
        return end < span.StartedAt ? span.StartedAt : end;
    }
}
