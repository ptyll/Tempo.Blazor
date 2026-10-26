using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The lane assignment both the time grid and the timeline use. A lane is reused the moment an
/// event ends, and an event that ends when the next one starts does not overlap it.
/// </summary>
public class SchedulerLayoutTests
{
    private static TmScheduleEvent Evt(string id, int startHour, int endHour) => new()
    {
        Id = id,
        Title = id,
        Start = new DateTime(2026, 6, 15, startHour, 0, 0),
        End = new DateTime(2026, 6, 15, endHour, 0, 0)
    };

    [Fact]
    public void Assign_Gives_Overlapping_Events_Separate_Lanes()
    {
        var lanes = SchedulerLayout.Assign([Evt("a", 9, 11), Evt("b", 10, 12)]);

        lanes.Should().HaveCount(2);
        lanes[0].Lane.Should().Be(0);
        lanes[1].Lane.Should().Be(1);
        lanes.Should().OnlyContain(l => l.TotalLanes == 2);
    }

    [Fact]
    public void Assign_Reuses_A_Lane_Once_The_Earlier_Event_Ends()
    {
        // A overlaps B, B overlaps C, but A ends when C starts, so C reuses A's lane.
        var lanes = SchedulerLayout.Assign([Evt("a", 9, 11), Evt("b", 10, 13), Evt("c", 11, 14)]);

        lanes.Select(l => l.Lane).Should().Equal(0, 1, 0);
        lanes.Should().OnlyContain(l => l.TotalLanes == 2);
    }

    [Fact]
    public void Assign_Treats_An_Event_Ending_At_The_Next_Start_As_No_Overlap()
    {
        var lanes = SchedulerLayout.Assign([Evt("a", 9, 10), Evt("b", 10, 11)]);

        lanes.Should().OnlyContain(l => l.Lane == 0 && l.TotalLanes == 1);
    }

    [Fact]
    public void Assign_Keeps_Identical_Starts_In_Separate_Lanes()
    {
        var lanes = SchedulerLayout.Assign([Evt("a", 9, 10), Evt("b", 9, 11)]);

        lanes.Select(l => l.Lane).Should().Equal(0, 1);
    }

    [Fact]
    public void Assign_Sizes_Each_Overlap_Cluster_On_Its_Own()
    {
        // Two events overlap at 9, then a lone event at 14. The lone event is its own cluster,
        // so it keeps the full width instead of sharing the morning's two lanes.
        var lanes = SchedulerLayout.Assign([Evt("a", 9, 11), Evt("b", 10, 12), Evt("c", 14, 15)]);

        lanes.Single(l => l.Event.Id == "c").TotalLanes.Should().Be(1);
        lanes.Where(l => l.Event.Id != "c").Should().OnlyContain(l => l.TotalLanes == 2);
    }

    [Fact]
    public void Assign_Returns_Nothing_For_No_Events()
    {
        SchedulerLayout.Assign([]).Should().BeEmpty();
    }
}
