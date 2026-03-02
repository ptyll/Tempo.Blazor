using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerTimelineView — horizontal timeline with resources.</summary>
public class TmSchedulerTimelineViewTests : LocalizationTestBase
{
    private static readonly List<TmScheduleResource> TestResources =
    [
        new() { Id = "room-a", Name = "Room A", Color = "#3b82f6" },
        new() { Id = "room-b", Name = "Room B", Color = "#ef4444" },
        new() { Id = "room-c", Name = "Room C", Color = "#10b981" },
    ];

    [Fact]
    public void Renders_Timeline_Container()
    {
        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        cut.Find(".tm-scheduler-timeline").Should().NotBeNull();
    }

    [Fact]
    public void Renders_Resource_Labels()
    {
        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        var labels = cut.FindAll(".tm-scheduler-timeline-resource-label");
        labels.Count.Should().Be(3);
        labels[0].TextContent.Should().Contain("Room A");
        labels[1].TextContent.Should().Contain("Room B");
        labels[2].TextContent.Should().Contain("Room C");
    }

    [Fact]
    public void Renders_Time_Headers()
    {
        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        var headers = cut.FindAll(".tm-scheduler-timeline-time-header");
        headers.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Renders_Resource_Rows()
    {
        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        cut.FindAll(".tm-scheduler-timeline-row").Count.Should().Be(3);
    }

    [Fact]
    public void Events_Displayed_In_Correct_Resource_Row()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Room A Meeting", Start = new(2025, 6, 10, 10, 0, 0), End = new(2025, 6, 10, 11, 0, 0), ResourceId = "room-a" },
            new() { Title = "Room B Meeting", Start = new(2025, 6, 10, 14, 0, 0), End = new(2025, 6, 10, 15, 0, 0), ResourceId = "room-b" },
        };

        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events, events));

        var eventEls = cut.FindAll(".tm-scheduler-timeline-event");
        eventEls.Count.Should().Be(2);
    }

    [Fact]
    public void Event_Click_Fires_Callback()
    {
        TmScheduleEvent? clicked = null;
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Meeting", Start = new(2025, 6, 10, 10, 0, 0), End = new(2025, 6, 10, 11, 0, 0), ResourceId = "room-a" },
        };

        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-timeline-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Meeting");
    }

    [Fact]
    public void Slot_Click_Fires_Callback()
    {
        (DateTime Start, DateTime End)? slot = null;
        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, s => slot = s)));

        cut.FindAll(".tm-scheduler-timeline-slot")[0].Click();

        slot.Should().NotBeNull();
    }

    [Fact]
    public void Event_Has_Horizontal_Position()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Meeting", Start = new(2025, 6, 10, 12, 0, 0), End = new(2025, 6, 10, 14, 0, 0), ResourceId = "room-a" },
        };

        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-timeline-event");
        var style = eventEl.GetAttribute("style") ?? "";
        style.Should().Contain("left:");
        style.Should().Contain("width:");
    }

    [Fact]
    public void Renders_Without_Resources()
    {
        var cut = RenderComponent<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10)));

        cut.Find(".tm-scheduler-timeline").Should().NotBeNull();
        // Should show at least one default row
        cut.FindAll(".tm-scheduler-timeline-row").Count.Should().BeGreaterThanOrEqualTo(1);
    }
}
