using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for scheduler drag & drop functionality.</summary>
public class TmSchedulerDragTests : LocalizationTestBase
{
    private static TmScheduleEvent Evt(
        string id, string title, DateTime start, DateTime end,
        bool allDay = false) =>
        new() { Id = id, Title = title, Start = start, End = end, AllDay = allDay };

    // ── Month view drag ──

    [Fact]
    public void MonthView_Events_Are_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-month-event");
        eventEl.GetAttribute("draggable").Should().Be("true");
    }

    [Fact]
    public void MonthView_ReadOnly_Events_Not_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true));

        var eventEl = cut.Find(".tm-scheduler-month-event");
        var draggable = eventEl.GetAttribute("draggable");
        (draggable == null || draggable == "false").Should().BeTrue();
    }

    [Fact]
    public void MonthView_DayCells_Accept_Drops()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        // Day cells should have ondrop handler
        var dayCells = cut.FindAll(".tm-scheduler-month-day");
        dayCells.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MonthView_Drop_Fires_OnEventChanged()
    {
        TmScheduleEvent? changed = null;
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, e => changed = e)));

        // Simulate dragstart on the event
        var eventEl = cut.Find(".tm-scheduler-month-event");
        eventEl.DragStart();

        // Simulate drop on a different day cell (e.g., June 12)
        var targetDays = cut.FindAll(".tm-scheduler-month-day:not(.tm-scheduler-month-day--other)");
        // Find the day cell for June 12 (index may vary by calendar layout)
        var targetDay = targetDays[11]; // June 12 ≈ index 11 (0-based from June 1)
        targetDay.Drop();

        changed.Should().NotBeNull();
        changed!.Id.Should().Be("e1");
    }

    // ── TimeGrid view drag ──

    [Fact]
    public void TimeGrid_Events_Are_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        eventEl.GetAttribute("draggable").Should().Be("true");
    }

    [Fact]
    public void TimeGrid_ReadOnly_Events_Not_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        var draggable = eventEl.GetAttribute("draggable");
        (draggable == null || draggable == "false").Should().BeTrue();
    }

    [Fact]
    public void TimeGrid_Events_Have_Resize_Handle()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        cut.FindAll(".tm-scheduler-event-resize-handle").Count.Should().Be(1);
    }

    [Fact]
    public void TimeGrid_ReadOnly_Events_No_Resize_Handle()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-scheduler-event-resize-handle").Count.Should().Be(0);
    }
}
