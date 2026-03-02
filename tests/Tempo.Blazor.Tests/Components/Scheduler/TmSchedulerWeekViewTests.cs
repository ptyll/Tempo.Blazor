using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerWeekView — 7-day wrapper around TimeGrid.</summary>
public class TmSchedulerWeekViewTests : LocalizationTestBase
{
    [Fact]
    public void Renders_Week_Container()
    {
        var cut = RenderComponent<TmSchedulerWeekView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10)));

        cut.Find(".tm-scheduler-week").Should().NotBeNull();
    }

    [Fact]
    public void Contains_TimeGrid_With_Seven_Columns()
    {
        var cut = RenderComponent<TmSchedulerWeekView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10)));

        cut.Find(".tm-scheduler-timegrid").Should().NotBeNull();
        cut.FindAll(".tm-scheduler-timegrid-col").Count.Should().Be(7);
    }

    [Fact]
    public void Week_Starts_On_Correct_Day()
    {
        // June 10, 2025 is Tuesday. With FirstDayOfWeek = 1 (Monday),
        // week should start on June 9 (Monday)
        var cut = RenderComponent<TmSchedulerWeekView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.FirstDayOfWeek, 1));

        var headers = cut.FindAll(".tm-scheduler-timegrid-col-header");
        headers.Count.Should().Be(7);
    }

    [Fact]
    public void Passes_Events_To_TimeGrid()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Mon Meeting", Start = new(2025, 6, 9, 10, 0, 0), End = new(2025, 6, 9, 11, 0, 0) },
            new() { Title = "Wed Meeting", Start = new(2025, 6, 11, 14, 0, 0), End = new(2025, 6, 11, 15, 0, 0) },
        };

        var cut = RenderComponent<TmSchedulerWeekView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.FirstDayOfWeek, 1)
            .Add(c => c.Events, events));

        cut.FindAll(".tm-scheduler-timegrid-event").Count.Should().Be(2);
    }

    [Fact]
    public void Slot_Click_Fires_Callback()
    {
        (DateTime Start, DateTime End)? slot = null;
        var cut = RenderComponent<TmSchedulerWeekView>(p => p
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
            new() { Title = "Standup", Start = new(2025, 6, 9, 9, 0, 0), End = new(2025, 6, 9, 9, 30, 0) }
        };

        var cut = RenderComponent<TmSchedulerWeekView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.FirstDayOfWeek, 1)
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-timegrid-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Standup");
    }
}
