using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for resource grouping in TimeGrid/Day/Week views.</summary>
public class TmSchedulerResourceGroupingTests : LocalizationTestBase
{
    private static readonly List<TmScheduleResource> TestResources =
    [
        new() { Id = "room-a", Name = "Room A", Color = "#3b82f6" },
        new() { Id = "room-b", Name = "Room B", Color = "#ef4444" },
    ];

    [Fact]
    public void TimeGrid_Shows_Resource_Headers_When_Resources_Provided()
    {
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Resources, TestResources));

        var resourceHeaders = cut.FindAll(".tm-scheduler-timegrid-resource-label");
        resourceHeaders.Count.Should().Be(2);
        resourceHeaders[0].TextContent.Should().Contain("Room A");
        resourceHeaders[1].TextContent.Should().Contain("Room B");
    }

    [Fact]
    public void TimeGrid_With_Resources_Shows_Columns_Per_Resource()
    {
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Resources, TestResources));

        // 1 date × 2 resources = 2 resource columns
        var resourceCols = cut.FindAll(".tm-scheduler-timegrid-resource-col");
        resourceCols.Count.Should().Be(2);
    }

    [Fact]
    public void TimeGrid_Without_Resources_Shows_Regular_Columns()
    {
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)]));

        // No resource headers when no resources
        cut.FindAll(".tm-scheduler-timegrid-resource-label").Count.Should().Be(0);
    }

    [Fact]
    public void Events_Filtered_To_Resource_Columns()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Room A Event", Start = new(2025, 6, 10, 10, 0, 0), End = new(2025, 6, 10, 11, 0, 0), ResourceId = "room-a" },
            new() { Title = "Room B Event", Start = new(2025, 6, 10, 14, 0, 0), End = new(2025, 6, 10, 15, 0, 0), ResourceId = "room-b" },
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events, events));

        var allEvents = cut.FindAll(".tm-scheduler-timegrid-event");
        allEvents.Count.Should().Be(2);
    }

    [Fact]
    public void DayView_Passes_Resources_To_TimeGrid()
    {
        var cut = RenderComponent<TmSchedulerDayView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        cut.FindAll(".tm-scheduler-timegrid-resource-label").Count.Should().Be(2);
    }

    [Fact]
    public void WeekView_Passes_Resources_To_TimeGrid()
    {
        var cut = RenderComponent<TmSchedulerWeekView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        // 7 days × 2 resources = 14 resource columns
        cut.FindAll(".tm-scheduler-timegrid-resource-col").Count.Should().Be(14);
    }
}
