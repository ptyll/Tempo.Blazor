using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 2 — <c>AgentLanesLayout</c>, the pure C# half of <c>TmAgentLanes</c>: the time range and its
/// labelled ticks, the pixel box of every block, the sub-rows that keep overlapping work readable inside one lane,
/// open blocks drawn up to "now", the "now" position and the handoff connectors.
/// <para>
/// The tests drive the layout directly (no renderer, no clock of its own): every case pins a concrete moment, a
/// concrete pixel scale and a concrete row height, so a geometry regression is a number in the failure message
/// rather than a picture nobody diffs.
/// </para>
/// </summary>
public class AgentLanesLayoutTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = Nine.AddMinutes(30);

    private static AgentRef Agent(string id, string name, int color = 0) => new(name, id) { ColorIndex = color };

    private static AgentLanesSnapshot Snapshot(params AgentLaneBlock[] blocks) => new()
    {
        Agents = [Agent("planner", "Planner", 0), Agent("writer", "Writer", 1)],
        Blocks = blocks,
    };

    private static AgentLaneBlock Block(
        string id,
        string agentId,
        int startMinutes,
        int? endMinutes,
        AgentLaneBlockKind kind = AgentLaneBlockKind.Task,
        AgentActivityStatus status = AgentActivityStatus.Succeeded,
        AgentLaneBlockMarker? marker = null)
        => new(id, agentId, Nine.AddMinutes(startMinutes), "Block " + id)
        {
            End = endMinutes is { } end ? Nine.AddMinutes(end) : null,
            Kind = kind,
            Status = status,
            Marker = marker,
        };

    private static AgentLanesLayoutResult Build(
        AgentLanesSnapshot snapshot,
        DateTimeOffset? now = null,
        double pixelsPerHour = 120,
        double rowHeight = 48)
        => AgentLanesLayout.Build(
            snapshot,
            now ?? Now,
            new AgentLanesLayoutOptions { PixelsPerHour = pixelsPerHour, RowHeightPx = rowHeight });

    private static AgentLanesBlockPlacement Placement(AgentLanesLayoutResult layout, string blockId)
        => layout.Blocks.Single(placement => placement.Block.Id == blockId);

    [Fact]
    public void Build_WithNoBlocks_StillDrawsAnAxisAroundNow()
    {
        var layout = Build(new AgentLanesSnapshot(), Now);

        layout.IsEmpty.Should().BeTrue();
        layout.Lanes.Should().BeEmpty("an empty run has no lane to name");
        layout.Ticks.Should().HaveCountGreaterThanOrEqualTo(2, "the axis stays readable without data");
        layout.RangeStart.Should().BeOnOrBefore(Now);
        layout.RangeEnd.Should().BeOnOrAfter(Now);
        layout.HasNowLine.Should().BeTrue("'now' is inside the fallback window");
        layout.WidthPx.Should().BeApproximately(
            (layout.RangeEnd - layout.RangeStart).TotalHours * 120, 0.001);
    }

    [Fact]
    public void Build_PicksNiceTicksThatCoverTheRun()
    {
        var layout = Build(Snapshot(Block("a", "planner", 0, 90)));

        layout.Ticks.Count.Should().BeInRange(4, 9, "a tick ladder targets a readable number of labels");
        layout.TickStep.Should().BeGreaterThan(TimeSpan.Zero);
        layout.Unit.Should().Be(AgentLanesTickUnit.Minutes);

        layout.RangeStart.Should().BeOnOrBefore(Nine);
        layout.RangeEnd.Should().BeOnOrAfter(Nine.AddMinutes(90));

        foreach (var tick in layout.Ticks)
        {
            (tick.Time.ToOffset(TimeSpan.Zero).Ticks % layout.TickStep.Ticks).Should().Be(
                0, "every tick sits on the step grid, so a label reads 09:10 and never 09:07:23");
            tick.OffsetPx.Should().BeApproximately(
                (tick.Time - layout.RangeStart).TotalHours * 120, 0.001);
        }

        layout.Ticks.Select(tick => tick.Time).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Build_PlacesABlockByItsTimes()
    {
        var layout = Build(Snapshot(Block("a", "planner", 10, 40)));

        var placement = Placement(layout, "a");

        placement.LaneIndex.Should().Be(0);
        placement.RowIndex.Should().Be(0);
        placement.LeftPx.Should().BeApproximately((Nine.AddMinutes(10) - layout.RangeStart).TotalHours * 120, 0.001);
        placement.WidthPx.Should().BeApproximately(30d / 60 * 120, 0.001, "30 minutes at 120 px per hour is 60 px");
        placement.EffectiveEnd.Should().Be(Nine.AddMinutes(40));
    }

    [Fact]
    public void Build_StacksOverlappingBlocksIntoSubRows_AndReusesAFreeRow()
    {
        // A 0-30, B 15-45, C 40-70: A and B collide, C beats B's end and fits under A again.
        var layout = Build(Snapshot(
            Block("a", "planner", 0, 30),
            Block("b", "planner", 15, 45),
            Block("c", "planner", 40, 70)));

        Placement(layout, "a").RowIndex.Should().Be(0);
        Placement(layout, "b").RowIndex.Should().Be(1, "B overlaps A and cannot share its row");
        Placement(layout, "c").RowIndex.Should().Be(0, "C starts after A ended, so the first row is free again");

        var lane = layout.Lanes[0];
        lane.Rows.Should().HaveCount(2);
        lane.HeightPx.Should().Be(2 * 48);
        Placement(layout, "b").TopPx.Should().Be(48, "sub-row one starts one row height below the lane top");
    }

    [Fact]
    public void Build_DrawsAnOpenBlockUpToNow()
    {
        var layout = Build(Snapshot(Block("a", "planner", 0, null) with { Status = AgentActivityStatus.Running }));

        var placement = Placement(layout, "a");

        placement.Block.IsOpen.Should().BeTrue();
        placement.EffectiveEnd.Should().Be(Now);
        placement.WidthPx.Should().BeApproximately(60, 0.001, "0 -> 30 minutes at 120 px per hour is 60 px");
        layout.RangeEnd.Should().BeOnOrAfter(Now);
        layout.HasNowLine.Should().BeTrue();
        layout.NowPx.Should().BeApproximately((Now - layout.RangeStart).TotalHours * 120, 0.001);
    }

    [Fact]
    public void Build_ClampsAnEndBeforeItsStart()
    {
        var layout = Build(Snapshot(Block("a", "planner", 20, 10)));

        var placement = Placement(layout, "a");

        placement.EffectiveEnd.Should().Be(Nine.AddMinutes(20));
        placement.WidthPx.Should().Be(AgentLanesLayout.MinBlockWidthPx,
            "a nonsensical duration paints the minimum box instead of a negative one");
    }

    [Fact]
    public void Build_ReadsADuplicateBlockIdOnce()
    {
        var layout = Build(Snapshot(
            Block("a", "planner", 0, 10),
            Block("a", "planner", 20, 30)));

        layout.Blocks.Should().ContainSingle();
        Placement(layout, "a").Block.Start.Should().Be(Nine, "the first occurrence wins");
    }

    [Fact]
    public void Build_KeepsTheDeclaredAgentOrder_AndAppendsAnUndeclaredAgent()
    {
        var layout = Build(Snapshot(
            Block("a", "qa", 0, 10),
            Block("b", "writer", 5, 20)));

        layout.Lanes.Select(lane => lane.Agent.Id).Should().Equal("planner", "writer", "qa");
        layout.UndeclaredAgentCount.Should().Be(1);
        Placement(layout, "a").LaneIndex.Should().Be(2);
    }

    [Fact]
    public void Build_GroupsABlockWithNoAgentUnderTheUnassignedLane()
    {
        var layout = Build(new AgentLanesSnapshot { Blocks = [Block("a", "  ", 0, 10)] });

        layout.Lanes.Should().ContainSingle();
        layout.Lanes[0].Agent.Id.Should().Be(AgentLanesLayout.UnassignedAgentId);
        layout.Lanes[0].Agent.HasName.Should().BeFalse("the badge paints its localized fallback name");
    }

    [Fact]
    public void Build_StacksLanesTopToBottom()
    {
        var layout = Build(Snapshot(
            Block("a", "planner", 0, 10),
            Block("b", "planner", 20, 30),
            Block("c", "writer", 0, 10)));

        layout.Lanes[0].TopPx.Should().Be(0);
        layout.Lanes[0].HeightPx.Should().Be(48);
        layout.Lanes[1].TopPx.Should().Be(48);
        layout.HeightPx.Should().Be(96);
    }

    [Fact]
    public void Build_HandoffFromABlock_StartsAtTheBlocksEnd()
    {
        var snapshot = Snapshot(Block("a", "planner", 0, 30)) with
        {
            Handoffs = [new AgentHandoffLink("writer", Nine.AddMinutes(25)) { FromBlockId = "a", Label = "Plan ready" }],
        };

        var layout = Build(snapshot);

        var line = layout.Handoffs.Should().ContainSingle().Subject;
        var source = Placement(layout, "a");

        line.From.X.Should().BeApproximately(source.LeftPx + source.WidthPx, 0.001);
        line.From.Y.Should().BeApproximately(source.TopPx + 24, 0.001, "the arrow leaves the block's centre line");
        line.To.X.Should().BeApproximately((Nine.AddMinutes(25) - layout.RangeStart).TotalHours * 120, 0.001);
        line.To.Y.Should().BeApproximately(layout.Lanes[1].TopPx + (layout.Lanes[1].HeightPx / 2), 0.001);
        line.Points.Should().HaveCount(4, "two lanes far apart are joined by an elbow, not a diagonal");
        line.Points[0].Should().Be(line.From);
        line.Points[^1].Should().Be(line.To);
    }

    [Fact]
    public void Build_HandoffFromAnAgent_StartsOnThatLanesCentreLine()
    {
        var snapshot = Snapshot(Block("a", "planner", 0, 30)) with
        {
            Handoffs = [new AgentHandoffLink("writer", Nine.AddMinutes(20)) { FromAgentId = "planner" }],
        };

        var layout = Build(snapshot);

        var line = layout.Handoffs.Should().ContainSingle().Subject;

        line.From.X.Should().BeApproximately(line.To.X, 0.001, "both ends sit at the same instant");
        line.From.Y.Should().BeApproximately(layout.Lanes[0].TopPx + (layout.Lanes[0].HeightPx / 2), 0.001);
        line.Points.Should().HaveCount(2, "two aligned points need no elbow");
    }

    [Fact]
    public void Build_SkipsAHandoffItCannotResolve_AndCountsIt()
    {
        var snapshot = Snapshot(Block("a", "planner", 0, 30)) with
        {
            Handoffs =
            [
                new AgentHandoffLink("nobody", Nine.AddMinutes(10)) { FromAgentId = "planner" },
                new AgentHandoffLink("writer", Nine.AddMinutes(10)),
            ],
        };

        var layout = Build(snapshot);

        layout.Handoffs.Should().BeEmpty();
        layout.UnresolvedHandoffCount.Should().Be(2, "a target that names no lane and a link with no source are skipped");
    }

    [Fact]
    public void Build_FallsBackToTheSourceAgent_WhenTheBlockIsGone()
    {
        var snapshot = Snapshot(Block("a", "planner", 0, 30)) with
        {
            Handoffs =
            [
                new AgentHandoffLink("writer", Nine.AddMinutes(10))
                {
                    FromBlockId = "missing",
                    FromAgentId = "planner",
                },
            ],
        };

        var layout = Build(snapshot);

        var line = layout.Handoffs.Should().ContainSingle().Subject;
        line.From.X.Should().BeApproximately((Nine.AddMinutes(10) - layout.RangeStart).TotalHours * 120, 0.001);
    }

    [Fact]
    public void Build_UsesSecondsForAShortRun()
    {
        var layout = Build(
            Snapshot(Block("a", "planner", 0, null) with { End = Nine.AddSeconds(30) }),
            Nine.AddSeconds(30),
            pixelsPerHour: 7200);

        layout.Unit.Should().Be(AgentLanesTickUnit.Seconds);
        layout.TickStep.Should().BeLessThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Build_UsesDays_ForAMultiDayRun()
    {
        var layout = Build(
            Snapshot(Block("a", "planner", 0, null) with { End = Nine.AddDays(10) }),
            Nine.AddDays(10),
            pixelsPerHour: 2);

        layout.Unit.Should().Be(AgentLanesTickUnit.Days);
        layout.SpansMultipleDays.Should().BeTrue("a label has to carry the date once the run crosses midnight");
    }
}
