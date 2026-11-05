using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The knobs of one <see cref="AgentLanesLayout.Build"/> pass: how many pixels one hour of the run occupies and how
/// tall one sub-row of a lane is. Both are PIXEL values because a lane surface paints blocks at absolute positions
/// (the component's zoom writes the first one, the touch contract the second); nothing else in the layout depends on
/// a stylesheet.
/// </summary>
internal sealed record AgentLanesLayoutOptions
{
    /// <summary>Horizontal scale: pixels one hour of the run occupies. Must be positive; anything else falls back to 120.</summary>
    public double PixelsPerHour { get; init; } = 120;

    /// <summary>The height of one sub-row of a lane in pixels. Must be positive; anything else falls back to 48.</summary>
    public double RowHeightPx { get; init; } = 48;
}

/// <summary>How a tick's label has to be formatted: seconds and minutes need the clock, days need the date.</summary>
internal enum AgentLanesTickUnit
{
    /// <summary>The step is under a minute, so a label has to carry seconds.</summary>
    Seconds = 0,

    /// <summary>The step is under an hour, so a label carries hours and minutes.</summary>
    Minutes = 1,

    /// <summary>The step is under a day, so a label carries hours and minutes (and the date when the run crosses one).</summary>
    Hours = 2,

    /// <summary>The step is a day or more, so a label has to carry the date.</summary>
    Days = 3,
}

/// <summary>One labelled tick of the time axis, positioned from the left edge of the timeline content.</summary>
internal sealed record AgentLanesTick
{
    /// <summary>The instant the tick marks, in the axis offset.</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>Distance in pixels from the start of the timeline content.</summary>
    public required double OffsetPx { get; init; }
}

/// <summary>One block as it is painted: which lane and sub-row it occupies and the box it fills.</summary>
internal sealed record AgentLanesBlockPlacement
{
    /// <summary>The block the placement paints.</summary>
    public required AgentLaneBlock Block { get; init; }

    /// <summary>Index into <see cref="AgentLanesLayoutResult.Lanes"/> of the lane that owns the block.</summary>
    public required int LaneIndex { get; init; }

    /// <summary>The sub-row inside the lane (0-based) that keeps overlapping blocks readable.</summary>
    public required int RowIndex { get; init; }

    /// <summary>Distance in pixels from the start of the timeline content to the block's left edge.</summary>
    public required double LeftPx { get; init; }

    /// <summary>The painted width in pixels; never negative, and never narrower than the minimum a label needs.</summary>
    public required double WidthPx { get; init; }

    /// <summary>Distance in pixels from the top of the lane area to the block's top edge.</summary>
    public required double TopPx { get; init; }

    /// <summary>The end the block is painted with — the recorded end, or "now" for an open block.</summary>
    public required DateTimeOffset EffectiveEnd { get; init; }
}

/// <summary>One sub-row of a lane: the blocks that fit side by side inside it.</summary>
internal sealed record AgentLaneRow
{
    /// <summary>The 0-based index of the sub-row inside its lane.</summary>
    public required int Index { get; init; }

    /// <summary>Distance in pixels from the top of the lane area to the top of this sub-row.</summary>
    public required double TopPx { get; init; }

    /// <summary>The blocks of the sub-row, ordered by start.</summary>
    public required IReadOnlyList<AgentLanesBlockPlacement> Blocks { get; init; }
}

/// <summary>One agent's lane: its header identity, its position and the sub-rows that keep overlaps readable.</summary>
internal sealed record AgentLane
{
    /// <summary>The agent the lane names. A block for an undeclared agent gets a synthesized lane, so no work is lost.</summary>
    public required AgentRef Agent { get; init; }

    /// <summary>The 0-based index of the lane; the vertical order of the timeline.</summary>
    public required int Index { get; init; }

    /// <summary>Distance in pixels from the top of the lane area to the top of this lane.</summary>
    public required double TopPx { get; init; }

    /// <summary>The lane's painted height in pixels (its sub-row count times the row height).</summary>
    public required double HeightPx { get; init; }

    /// <summary>The lane's sub-rows; at least one, so an agent with no work still has a visible lane.</summary>
    public required IReadOnlyList<AgentLaneRow> Rows { get; init; }

    /// <summary>Every block of the lane, in sub-row order.</summary>
    public IReadOnlyList<AgentLanesBlockPlacement> Blocks => Rows.SelectMany(row => row.Blocks).ToList();
}

/// <summary>One point of the timeline in layout coordinates: x from the content's left edge, y from the lane area's top.</summary>
internal sealed record AgentLanesPoint
{
    /// <summary>Horizontal position in pixels.</summary>
    public required double X { get; init; }

    /// <summary>Vertical position in pixels.</summary>
    public required double Y { get; init; }
}

/// <summary>
/// One handoff as a drawn connector: where the arrow starts, where it ends and the polyline the SVG paints between
/// them (an elbow when the two lanes are far apart, a straight line when they are aligned).
/// </summary>
internal sealed record AgentHandoffLine
{
    /// <summary>The host's link.</summary>
    public required AgentHandoffLink Link { get; init; }

    /// <summary>The arrow's start point.</summary>
    public required AgentLanesPoint From { get; init; }

    /// <summary>The arrow's end point — the target lane at the moment of the handoff.</summary>
    public required AgentLanesPoint To { get; init; }

    /// <summary>The polyline the surface paints, starting at <see cref="From"/> and ending at <see cref="To"/>.</summary>
    public required IReadOnlyList<AgentLanesPoint> Points { get; init; }
}

/// <summary>Everything one <see cref="AgentLanesLayout.Build"/> pass computed for a single render.</summary>
internal sealed record AgentLanesLayoutResult
{
    /// <summary>The moment the pass was made for; open blocks are drawn up to it.</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>The start of the drawn range, aligned to the tick step.</summary>
    public required DateTimeOffset RangeStart { get; init; }

    /// <summary>The end of the drawn range, aligned to the tick step.</summary>
    public required DateTimeOffset RangeEnd { get; init; }

    /// <summary>How a tick label has to be formatted.</summary>
    public required AgentLanesTickUnit Unit { get; init; }

    /// <summary>The selected tick step.</summary>
    public required TimeSpan TickStep { get; init; }

    /// <summary>Whether the range crosses at least one midnight, so every label has to carry the date.</summary>
    public required bool SpansMultipleDays { get; init; }

    /// <summary>The labelled ticks of the axis, left to right.</summary>
    public required IReadOnlyList<AgentLanesTick> Ticks { get; init; }

    /// <summary>The lanes, in the snapshot's agent order; undeclared agents are appended.</summary>
    public required IReadOnlyList<AgentLane> Lanes { get; init; }

    /// <summary>Every block placement, in lane and sub-row order.</summary>
    public required IReadOnlyList<AgentLanesBlockPlacement> Blocks { get; init; }

    /// <summary>The connectors to draw; an unresolvable link is counted instead of guessed.</summary>
    public required IReadOnlyList<AgentHandoffLine> Handoffs { get; init; }

    /// <summary>The width of the timeline content in pixels, including the aligned padding.</summary>
    public required double WidthPx { get; init; }

    /// <summary>The height of the lane area in pixels.</summary>
    public required double HeightPx { get; init; }

    /// <summary>Where "now" falls inside the content, in pixels.</summary>
    public required double NowPx { get; init; }

    /// <summary>Whether "now" falls inside the drawn range, i.e. whether the now line is painted at all.</summary>
    public required bool HasNowLine { get; init; }

    /// <summary>How many handoff links could not be resolved to two lanes.</summary>
    public required int UnresolvedHandoffCount { get; init; }

    /// <summary>How many lanes were synthesized because a block named an agent the snapshot did not declare.</summary>
    public required int UndeclaredAgentCount { get; init; }

    /// <summary>Whether the run has no blocks at all — the surface then renders its empty state.</summary>
    public bool IsEmpty => Blocks.Count == 0;
}

/// <summary>
/// Turns a lane snapshot into everything a lane surface paints: the time range and its labelled ticks, each agent's
/// lane with the sub-rows that keep overlapping blocks readable, the pixel box of every block, the "now" position
/// and the handoff connectors.
/// <para>
/// Pure C#: no stylesheet, no JavaScript and no clock of its own — the caller passes the moment to draw for, so the
/// whole layout is unit-testable and a component never reads the wall clock. Geometry is PIXELS, because the
/// component paints blocks at absolute positions and an SVG connector has to line up with them exactly.
/// </para>
/// <para>
/// The range is derived from the blocks (an open block reaches <c>now</c>), padded a little and then aligned to a
/// "nice" tick step chosen from a fixed ladder so the axis reads in whole minutes, hours or days instead of at
/// arbitrary fractions. Blocks that overlap in time inside one lane are stacked into sub-rows instead of covering
/// each other. A block whose agent the snapshot does not declare still gets a lane (appended, with the localized
/// fallback name), because silently dropping a host's work would make the surface lie by omission.
/// </para>
/// </summary>
internal static class AgentLanesLayout
{
    /// <summary>The agent id a block with no agent is grouped under, so unattributed work still renders somewhere.</summary>
    internal const string UnassignedAgentId = "(unassigned)";

    /// <summary>The narrowest box a block is painted with, so a one-second block is still visible and reachable.</summary>
    internal const double MinBlockWidthPx = 18;

    private const double TargetTickSpacingPx = 90;
    private const int MinTickCount = 4;
    private const int MaxTickCount = 9;
    private const double PaddingFraction = 0.03;
    private const int FallbackMinutes = 10;
    private const double MinContentWidthPx = 240;
    private const int MinSpanSeconds = 60;
    private const int MaxTicks = 400;

    /// <summary>The tick steps a range may use, in seconds — days, hours, minutes and seconds that read as "nice".</summary>
    private static readonly long[] NiceStepsInSeconds =
    [
        1, 2, 5, 10, 15, 30,
        60, 120, 300, 600, 900, 1800,
        3600, 7200, 10800, 21600, 43200,
        86400, 172800, 604800,
    ];

    /// <summary>Builds the layout of one render.</summary>
    /// <param name="snapshot">The run to draw. Null is treated as an empty run.</param>
    /// <param name="now">The moment open blocks reach and the now line marks.</param>
    /// <param name="options">The pixel scale and the row height; null uses the defaults.</param>
    /// <returns>The computed layout.</returns>
    public static AgentLanesLayoutResult Build(
        AgentLanesSnapshot? snapshot,
        DateTimeOffset now,
        AgentLanesLayoutOptions? options = null)
    {
        var settings = options ?? new AgentLanesLayoutOptions();
        var pixelsPerHour = double.IsFinite(settings.PixelsPerHour) && settings.PixelsPerHour > 0
            ? settings.PixelsPerHour
            : 120;
        var rowHeight = double.IsFinite(settings.RowHeightPx) && settings.RowHeightPx > 0
            ? settings.RowHeightPx
            : 48;

        var blocks = DistinctBlocks(snapshot?.Blocks);
        var (laneAgents, laneKeys, undeclaredAgents) = BuildLanes(snapshot?.Agents, blocks);

        var (rangeStart, rangeEnd) = ComputeRange(blocks, now);

        // A very short run (a few seconds at the default zoom) would paint a sliver of content, so the window is
        // widened symmetrically until it fills a readable minimum. The extra time does not pretend work happened:
        // it is empty axis on both sides of the run.
        var spanPx = (rangeEnd - rangeStart).TotalHours * pixelsPerHour;
        if (spanPx < MinContentWidthPx)
        {
            var extra = TimeSpan.FromHours(((MinContentWidthPx - spanPx) / pixelsPerHour) / 2);
            rangeStart -= extra;
            rangeEnd += extra;
        }

        var (step, unit) = ChooseStep(rangeStart, rangeEnd, pixelsPerHour);
        var offset = now.Offset;
        var alignedStart = Floor(rangeStart, step, offset);
        var alignedEnd = Ceiling(rangeEnd, step, offset);
        if (alignedEnd <= alignedStart)
        {
            alignedEnd = alignedStart + step;
        }

        var widthPx = Math.Max(1, (alignedEnd - alignedStart).TotalHours * pixelsPerHour);
        var ticks = BuildTicks(alignedStart, alignedEnd, step, offset, pixelsPerHour);

        var (lanes, placements, heightPx) = BuildLanesGeometry(
            laneAgents, laneKeys, blocks, alignedStart, widthPx, pixelsPerHour, rowHeight, now);

        var placementsById = new Dictionary<string, AgentLanesBlockPlacement>(StringComparer.Ordinal);
        foreach (var placement in placements)
        {
            placementsById[placement.Block.Id] = placement;
        }

        var (handoffs, unresolved) = BuildHandoffs(
            snapshot?.Handoffs, lanes, placementsById, alignedStart, widthPx, pixelsPerHour, rowHeight);

        var nowPx = (now - alignedStart).TotalHours * pixelsPerHour;
        var hasNowLine = nowPx >= 0 && nowPx <= widthPx;

        return new AgentLanesLayoutResult
        {
            Now = now,
            RangeStart = alignedStart,
            RangeEnd = alignedEnd,
            Unit = unit,
            TickStep = step,
            SpansMultipleDays = (alignedEnd.Date - alignedStart.Date).TotalDays >= 1,
            Ticks = ticks,
            Lanes = lanes,
            Blocks = placements,
            Handoffs = handoffs,
            WidthPx = widthPx,
            HeightPx = heightPx,
            NowPx = Math.Clamp(nowPx, 0, widthPx),
            HasNowLine = hasNowLine,
            UnresolvedHandoffCount = unresolved,
            UndeclaredAgentCount = undeclaredAgents,
        };
    }

    /// <summary>The blocks in render order, with a duplicate id read once (first wins) so no lane paints two boxes for one block.</summary>
    private static List<AgentLaneBlock> DistinctBlocks(IReadOnlyList<AgentLaneBlock>? blocks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AgentLaneBlock>(blocks?.Count ?? 0);
        foreach (var block in blocks ?? [])
        {
            if (block is not null && seen.Add(block.Id))
            {
                result.Add(block);
            }
        }

        result.Sort(static (left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            return byStart != 0 ? byStart : string.CompareOrdinal(left.Id, right.Id);
        });

        return result;
    }

    /// <summary>
    /// The lanes to draw: the declared agents in the host's order, then one synthesized lane per agent a block names
    /// but the snapshot forgot (sorted by first appearance, so re-renders keep the order stable).
    /// </summary>
    private static (List<AgentRef> Agents, Dictionary<string, int> IndexById, int Undeclared) BuildLanes(
        IReadOnlyList<AgentRef>? agents,
        List<AgentLaneBlock> blocks)
    {
        var lanes = new List<AgentRef>();
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        var undeclared = 0;

        foreach (var agent in agents ?? [])
        {
            if (agent is not null && indexById.TryAdd(agent.Id, lanes.Count))
            {
                lanes.Add(agent);
            }
        }

        foreach (var block in blocks)
        {
            var key = AgentKey(block.AgentId);
            if (indexById.ContainsKey(key))
            {
                continue;
            }

            var synthesized = new AgentRef(string.Empty, key) { ColorIndex = lanes.Count };
            indexById[key] = lanes.Count;
            lanes.Add(synthesized);
            undeclared++;
        }

        return (lanes, indexById, undeclared);
    }

    /// <summary>The lane key of a block's agent id; a blank id groups under <see cref="UnassignedAgentId"/>.</summary>
    private static string AgentKey(string? agentId)
        => string.IsNullOrWhiteSpace(agentId) ? UnassignedAgentId : agentId!;

    /// <summary>The raw range the blocks span (an open block reaches <paramref name="now"/>), padded a little.</summary>
    private static (DateTimeOffset Start, DateTimeOffset End) ComputeRange(
        List<AgentLaneBlock> blocks,
        DateTimeOffset now)
    {
        DateTimeOffset start;
        DateTimeOffset end;

        if (blocks.Count == 0)
        {
            start = now.AddMinutes(-FallbackMinutes);
            end = now;
        }
        else
        {
            start = blocks[0].Start;
            end = blocks[0].EndAt(now);
            foreach (var block in blocks)
            {
                if (block.Start < start)
                {
                    start = block.Start;
                }

                var blockEnd = block.EndAt(now);
                if (blockEnd > end)
                {
                    end = blockEnd;
                }
            }

            if (end < start)
            {
                end = start;
            }

            var minSpan = TimeSpan.FromSeconds(MinSpanSeconds);
            if (end - start < minSpan)
            {
                end = start + minSpan;
            }
        }

        var padding = TimeSpan.FromTicks((long)Math.Round((end - start).Ticks * PaddingFraction));
        return (start - padding, end + padding);
    }

    /// <summary>Picks the smallest "nice" step that keeps the tick count inside the readable window.</summary>
    private static (TimeSpan Step, AgentLanesTickUnit Unit) ChooseStep(
        DateTimeOffset start,
        DateTimeOffset end,
        double pixelsPerHour)
    {
        var span = end - start;
        var spanPx = Math.Max(1, span.TotalHours * pixelsPerHour);
        var target = Math.Clamp((int)Math.Round(spanPx / TargetTickSpacingPx), MinTickCount, MaxTickCount);
        var desiredSeconds = span.TotalSeconds / target;

        foreach (var candidate in NiceStepsInSeconds)
        {
            if (candidate >= desiredSeconds)
            {
                return (TimeSpan.FromSeconds(candidate), UnitOf(candidate));
            }
        }

        var weeks = Math.Max(1, (long)Math.Ceiling(desiredSeconds / 604800d));
        var seconds = weeks * 604800;
        return (TimeSpan.FromDays(weeks * 7), UnitOf(seconds));
    }

    /// <summary>The label format a step of <paramref name="stepSeconds"/> needs.</summary>
    private static AgentLanesTickUnit UnitOf(long stepSeconds)
        => stepSeconds < 60 ? AgentLanesTickUnit.Seconds
        : stepSeconds < 3600 ? AgentLanesTickUnit.Minutes
        : stepSeconds < 86400 ? AgentLanesTickUnit.Hours
        : AgentLanesTickUnit.Days;

    /// <summary>Floors an instant onto the step grid, measured in the axis offset so labels land on whole units.</summary>
    private static DateTimeOffset Floor(DateTimeOffset value, TimeSpan step, TimeSpan offset)
    {
        var local = value.ToOffset(offset);
        var remainder = local.Ticks % step.Ticks;
        return new DateTimeOffset(local.Ticks - remainder, offset);
    }

    /// <summary>Ceilings an instant onto the step grid (a value already on it stays there).</summary>
    private static DateTimeOffset Ceiling(DateTimeOffset value, TimeSpan step, TimeSpan offset)
    {
        var floored = Floor(value, step, offset);
        return floored == value.ToOffset(offset) ? floored : floored + step;
    }

    /// <summary>Every labelled tick of the aligned range, left to right.</summary>
    private static List<AgentLanesTick> BuildTicks(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan step,
        TimeSpan offset,
        double pixelsPerHour)
    {
        var ticks = new List<AgentLanesTick>();
        for (var time = start; time <= end && ticks.Count < MaxTicks; time += step)
        {
            ticks.Add(new AgentLanesTick
            {
                Time = time.ToOffset(offset),
                OffsetPx = (time - start).TotalHours * pixelsPerHour,
            });
        }

        return ticks;
    }

    /// <summary>Places every block into its lane and sub-row and computes the box it fills.</summary>
    private static (List<AgentLane> Lanes, List<AgentLanesBlockPlacement> Placements, double HeightPx) BuildLanesGeometry(
        List<AgentRef> agents,
        Dictionary<string, int> indexById,
        List<AgentLaneBlock> blocks,
        DateTimeOffset rangeStart,
        double widthPx,
        double pixelsPerHour,
        double rowHeight,
        DateTimeOffset now)
    {
        var byLane = new Dictionary<int, List<AgentLaneBlock>>();
        foreach (var block in blocks)
        {
            var laneIndex = indexById[AgentKey(block.AgentId)];
            if (!byLane.TryGetValue(laneIndex, out var list))
            {
                list = [];
                byLane[laneIndex] = list;
            }

            list.Add(block);
        }

        var lanes = new List<AgentLane>(agents.Count);
        var placements = new List<AgentLanesBlockPlacement>(blocks.Count);
        var laneTop = 0d;

        for (var laneIndex = 0; laneIndex < agents.Count; laneIndex++)
        {
            var laneBlocks = byLane.TryGetValue(laneIndex, out var list) ? list : [];
            var rowEnds = new List<DateTimeOffset>();
            var rowBlocks = new List<List<AgentLanesBlockPlacement>>();

            foreach (var block in laneBlocks)
            {
                var start = block.Start;
                var effectiveEnd = block.EndAt(now);
                var row = rowEnds.FindIndex(end => end <= start);
                if (row < 0)
                {
                    row = rowEnds.Count;
                    rowEnds.Add(effectiveEnd);
                    rowBlocks.Add([]);
                }
                else
                {
                    rowEnds[row] = effectiveEnd;
                }

                var left = Math.Clamp((start - rangeStart).TotalHours * pixelsPerHour, 0, widthPx);
                var rawWidth = (effectiveEnd - start).TotalHours * pixelsPerHour;
                var width = Math.Clamp(Math.Max(MinBlockWidthPx, rawWidth), 0, Math.Max(0, widthPx - left));

                var placement = new AgentLanesBlockPlacement
                {
                    Block = block,
                    LaneIndex = laneIndex,
                    RowIndex = row,
                    LeftPx = left,
                    WidthPx = width,
                    TopPx = laneTop + (row * rowHeight),
                    EffectiveEnd = effectiveEnd,
                };

                rowBlocks[row].Add(placement);
                placements.Add(placement);
            }

            var rows = new List<AgentLaneRow>(rowBlocks.Count);
            for (var row = 0; row < rowBlocks.Count; row++)
            {
                rows.Add(new AgentLaneRow
                {
                    Index = row,
                    TopPx = laneTop + (row * rowHeight),
                    Blocks = rowBlocks[row],
                });
            }

            var height = Math.Max(1, rows.Count) * rowHeight;
            lanes.Add(new AgentLane
            {
                Agent = agents[laneIndex],
                Index = laneIndex,
                TopPx = laneTop,
                HeightPx = height,
                Rows = rows,
            });

            laneTop += height;
        }

        return (lanes, placements, laneTop);
    }

    /// <summary>
    /// Builds one connector per resolvable handoff: from the handed-over block's right edge (or the source lane at
    /// the moment of the handoff) to the target lane at that moment, bent at the horizontal midpoint so the arrow
    /// never cuts through a lane it does not belong to.
    /// </summary>
    private static (List<AgentHandoffLine> Lines, int Unresolved) BuildHandoffs(
        IReadOnlyList<AgentHandoffLink>? links,
        List<AgentLane> lanes,
        Dictionary<string, AgentLanesBlockPlacement> placementsById,
        DateTimeOffset rangeStart,
        double widthPx,
        double pixelsPerHour,
        double rowHeight)
    {
        var lines = new List<AgentHandoffLine>();
        var unresolved = 0;
        var lanesByAgent = new Dictionary<string, AgentLane>(StringComparer.Ordinal);
        foreach (var lane in lanes)
        {
            lanesByAgent[lane.Agent.Id] = lane;
        }

        foreach (var link in links ?? [])
        {
            if (link is null || string.IsNullOrWhiteSpace(link.ToAgentId)
                || !lanesByAgent.TryGetValue(link.ToAgentId, out var targetLane))
            {
                unresolved++;
                continue;
            }

            AgentLanesPoint from;
            if (!string.IsNullOrWhiteSpace(link.FromBlockId)
                && placementsById.TryGetValue(link.FromBlockId, out var source))
            {
                from = new AgentLanesPoint { X = source.LeftPx + source.WidthPx, Y = source.TopPx + (rowHeight / 2) };
            }
            else if (!string.IsNullOrWhiteSpace(link.FromAgentId)
                     && lanesByAgent.TryGetValue(link.FromAgentId, out var sourceLane))
            {
                from = new AgentLanesPoint
                {
                    X = XOf(link.At, rangeStart, widthPx, pixelsPerHour),
                    Y = sourceLane.TopPx + (sourceLane.HeightPx / 2),
                };
            }
            else
            {
                unresolved++;
                continue;
            }

            var to = new AgentLanesPoint
            {
                X = XOf(link.At, rangeStart, widthPx, pixelsPerHour),
                Y = targetLane.TopPx + (targetLane.HeightPx / 2),
            };

            var points = new List<AgentLanesPoint> { from };
            if (Math.Abs(to.X - from.X) >= 0.5)
            {
                var midX = (from.X + to.X) / 2;
                points.Add(new AgentLanesPoint { X = midX, Y = from.Y });
                points.Add(new AgentLanesPoint { X = midX, Y = to.Y });
            }

            points.Add(to);
            lines.Add(new AgentHandoffLine { Link = link, From = from, To = to, Points = points });
        }

        return (lines, unresolved);
    }

    /// <summary>The pixel position of an instant, clamped into the drawn content.</summary>
    private static double XOf(DateTimeOffset at, DateTimeOffset rangeStart, double widthPx, double pixelsPerHour)
        => Math.Clamp((at - rangeStart).TotalHours * pixelsPerHour, 0, widthPx);
}
