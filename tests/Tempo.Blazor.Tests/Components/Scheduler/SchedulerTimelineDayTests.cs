using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The pure placement of the day-granularity timeline: which events land in one day cell, how an
/// overlap becomes lanes, and how the cell cap splits into chips plus the "+N more" rest.
/// </summary>
public class SchedulerTimelineDayTests
{
    private static readonly DateOnly Day = new(2026, 6, 10);

    private static TmScheduleEvent Evt(
        string id, int startHour, int endHour, string? resourceId = "a", DateOnly? day = null, bool allDay = false)
    {
        var date = (day ?? Day).ToDateTime(TimeOnly.MinValue);
        return new TmScheduleEvent
        {
            Id = id,
            Title = id,
            Start = date.AddHours(startHour),
            End = date.AddHours(endHour),
            ResourceId = resourceId,
            AllDay = allDay
        };
    }

    [Fact]
    public void Layout_Keeps_Only_The_Events_That_Intersect_The_Day()
    {
        var items = SchedulerTimelineDay.Layout(
        [
            Evt("inside", 9, 10),
            Evt("before", 9, 10, day: Day.AddDays(-1)),
            Evt("after", 9, 10, day: Day.AddDays(1))
        ], Day);

        items.Select(i => i.Event.Id).Should().Equal("inside");
    }

    [Fact]
    public void Layout_Paints_A_Multi_Day_Event_On_Every_Day_It_Covers()
    {
        var spanning = Evt("span", 22, 26);
        var items = SchedulerTimelineDay.Layout([spanning], Day.AddDays(1));

        items.Should().HaveCount(1);
        items[0].IsContinuation.Should().BeTrue("the event started the day before");
    }

    [Fact]
    public void Layout_Stacks_Overlapping_Events_Into_Lanes_And_Sequential_Ones_Into_Rows()
    {
        var items = SchedulerTimelineDay.Layout([Evt("a", 9, 11), Evt("b", 10, 12), Evt("c", 13, 14)], Day);

        // a and b overlap, so they take the cell's two lanes; c comes later and stacks in lane 0.
        items.Should().HaveCount(3);
        items.Single(i => i.Event.Id == "a").Lane.Should().Be(0);
        items.Single(i => i.Event.Id == "b").Lane.Should().Be(1);
        items.Single(i => i.Event.Id == "c").Lane.Should().Be(0);
        items.Should().OnlyContain(i => i.CellLanes == 2, "the widest cluster sizes the whole cell");
        items.Single(i => i.Event.Id == "c").Row.Should().Be(1, "it sits below a in the same lane");
        items.Single(i => i.Event.Id == "a").Row.Should().Be(0);
    }

    [Fact]
    public void Layout_Sizes_An_Uncrowded_Cell_To_One_Lane()
    {
        var items = SchedulerTimelineDay.Layout([Evt("a", 9, 10), Evt("b", 11, 12)], Day);

        items.Should().OnlyContain(i => i.CellLanes == 1 && i.Lane == 0);
        items.Single(i => i.Event.Id == "b").Row.Should().Be(1);
    }

    [Fact]
    public void Layout_Returns_Nothing_For_An_Empty_Cell()
        => SchedulerTimelineDay.Layout([], Day).Should().BeEmpty();

    [Fact]
    public void Split_Caps_The_Chips_And_Reports_The_Rest()
    {
        var items = SchedulerTimelineDay.Layout(
            [Evt("a", 8, 9), Evt("b", 9, 10), Evt("c", 10, 11), Evt("d", 11, 12)], Day);

        var (visible, remaining) = SchedulerTimelineDay.Split(items, 3);

        visible.Should().HaveCount(3);
        remaining.Should().Be(1);
        SchedulerTimelineDay.Split(items, 10).Remaining.Should().Be(0);
        SchedulerTimelineDay.Split(items, 0).Visible.Should().HaveCount(1, "a cell always renders one chip");
    }

    [Fact]
    public void CellStyle_Places_The_Chip_In_Its_Lane_And_Row()
    {
        var items = SchedulerTimelineDay.Layout([Evt("a", 9, 11), Evt("b", 10, 12)], Day);
        var first = items.Single(i => i.Event.Id == "a");

        var style = SchedulerTimelineDay.CellStyle(first);

        style.Should().Contain("grid-column: 1 / span 1").And.Contain("grid-row: 1")
            .And.Contain("--tm-scheduler-lane: 0").And.Contain("--tm-scheduler-lanes: 2");

        var second = SchedulerTimelineDay.CellStyle(items.Single(i => i.Event.Id == "b"));
        second.Should().Contain("grid-column: 2 / span 1");
    }

    [Fact]
    public void CellStyle_Gives_A_Lone_Event_The_Whole_Cell()
    {
        var items = SchedulerTimelineDay.Layout([Evt("a", 9, 10)], Day);
        SchedulerTimelineDay.CellStyle(items[0]).Should().Contain("grid-column: 1 / span 1");
    }
}
