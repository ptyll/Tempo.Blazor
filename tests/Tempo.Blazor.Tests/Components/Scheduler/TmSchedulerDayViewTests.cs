using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerDayView — single-day wrapper around TimeGrid.</summary>
public class TmSchedulerDayViewTests : LocalizationTestBase
{
    [Fact]
    public void Renders_Day_Container()
    {
        var cut = RenderComponent<TmSchedulerDayView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10)));

        cut.Find(".tm-scheduler-day").Should().NotBeNull();
    }

    [Fact]
    public void Contains_TimeGrid_With_Single_Column()
    {
        var cut = RenderComponent<TmSchedulerDayView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10)));

        cut.Find(".tm-scheduler-timegrid").Should().NotBeNull();
        cut.FindAll(".tm-scheduler-timegrid-col").Count.Should().Be(1);
    }

    [Fact]
    public void Passes_Events_To_TimeGrid()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Daily Standup", Start = new(2025, 6, 10, 9, 0, 0), End = new(2025, 6, 10, 9, 30, 0) }
        };

        var cut = RenderComponent<TmSchedulerDayView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Events, events));

        cut.FindAll(".tm-scheduler-timegrid-event").Count.Should().Be(1);
    }

    [Fact]
    public void Slot_Click_Fires_Callback()
    {
        (DateTime Start, DateTime End)? slot = null;
        var cut = RenderComponent<TmSchedulerDayView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, s => slot = s)));

        cut.FindAll(".tm-scheduler-timegrid-slot")[0].Click();

        slot.Should().NotBeNull();
    }

    [Fact]
    public void Event_Click_Fires_Callback()
    {
        TmScheduleEvent? clicked = null;
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Review", Start = new(2025, 6, 10, 15, 0, 0), End = new(2025, 6, 10, 16, 0, 0) }
        };

        var cut = RenderComponent<TmSchedulerDayView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-timegrid-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Review");
    }
}
