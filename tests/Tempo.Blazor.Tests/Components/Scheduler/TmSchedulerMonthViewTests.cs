using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerMonthView.</summary>
public class TmSchedulerMonthViewTests : LocalizationTestBase
{
    private static TmScheduleEvent Evt(string title, DateTime start, DateTime end, bool allDay = false, string? color = null) =>
        new() { Title = title, Start = start, End = end, AllDay = allDay, Color = color };

    [Fact]
    public void Renders_Month_Container()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        cut.Find(".tm-scheduler-month").Should().NotBeNull();
        cut.Find(".tm-scheduler-month-grid").Should().NotBeNull();
    }

    [Fact]
    public void Renders_42_Day_Cells()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        cut.FindAll(".tm-scheduler-month-day").Count.Should().Be(42);
    }

    [Fact]
    public void Renders_7_Day_Headers()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        cut.FindAll(".tm-scheduler-month-day-header").Count.Should().Be(7);
    }

    [Fact]
    public void Today_Has_Today_Class()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, DateTime.Today));

        cut.FindAll(".tm-scheduler-month-day--today").Count.Should().Be(1);
    }

    [Fact]
    public void Today_Parameter_Drives_The_Highlighted_Cell()
    {
        // Fáze 25b: the Scheduler passes today in the display zone; the month view must highlight
        // THAT date, not the machine's DateTime.Today (they differ when the zones disagree).
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Today, new DateTime(2025, 6, 20)));

        cut.FindAll(".tm-scheduler-month-day--today").Count.Should().Be(1);
        cut.Find(".tm-scheduler-month-day--today .tm-scheduler-month-day-number").TextContent
            .Should().Be("20");
    }

    [Fact]
    public void Today_Parameter_Defaults_To_Machine_Today()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, DateTime.Today));

        cut.Instance.Today.Should().Be(DateTime.Today);
        cut.FindAll(".tm-scheduler-month-day--today").Count.Should().Be(1);
    }

    [Fact]
    public void Other_Month_Days_Have_Other_Class()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        cut.FindAll(".tm-scheduler-month-day--other").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Renders_Events_On_Correct_Day()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new DateTime(2025, 6, 10, 14, 0, 0), new DateTime(2025, 6, 10, 15, 0, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var eventElements = cut.FindAll(".tm-scheduler-month-event");
        eventElements.Count.Should().Be(1);
        eventElements[0].TextContent.Should().Contain("Meeting");
    }

    [Fact]
    public void AllDay_Event_Has_AllDay_Class()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Holiday", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true)
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        cut.FindAll(".tm-scheduler-month-event--allday").Count.Should().Be(1);
    }

    [Fact]
    public void Shows_Event_Time_For_Non_AllDay_Events()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new DateTime(2025, 6, 10, 14, 30, 0), new DateTime(2025, 6, 10, 15, 0, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var timeEl = cut.Find(".tm-scheduler-month-event-time");
        timeEl.TextContent.Should().Contain("14:30");
    }

    [Fact]
    public void Day_Click_Fires_OnSlotClick()
    {
        (DateTime Start, DateTime End)? slot = null;
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, s => slot = s)));

        // Click the first non-other-month day cell
        var dayCells = cut.FindAll(".tm-scheduler-month-day:not(.tm-scheduler-month-day--other)");
        dayCells[0].Click();

        slot.Should().NotBeNull();
        slot!.Value.Start.Month.Should().Be(6);
    }

    [Fact]
    public void Event_Click_Fires_OnEventClick()
    {
        TmScheduleEvent? clicked = null;
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 9, 30, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-month-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Standup");
    }

    [Fact]
    public void Shows_More_Link_When_Events_Exceed_Max()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Evt1", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0)),
            Evt("Evt2", new DateTime(2025, 6, 10, 10, 0, 0), new DateTime(2025, 6, 10, 11, 0, 0)),
            Evt("Evt3", new DateTime(2025, 6, 10, 11, 0, 0), new DateTime(2025, 6, 10, 12, 0, 0)),
            Evt("Evt4", new DateTime(2025, 6, 10, 13, 0, 0), new DateTime(2025, 6, 10, 14, 0, 0)),
            Evt("Evt5", new DateTime(2025, 6, 10, 14, 0, 0), new DateTime(2025, 6, 10, 15, 0, 0)),
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.MaxVisibleEvents, 3));

        var more = cut.Find(".tm-scheduler-month-more");
        more.Should().NotBeNull();
        // 3 visible, 2 remaining → "+N more" shown
        cut.FindAll(".tm-scheduler-month-event").Count.Should().Be(3);
        more.TextContent.Should().NotBeEmpty();
    }

    [Fact]
    public void Event_Color_Applied_As_CSS_Variable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Colored", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0), color: "#ff5722")
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-month-event");
        eventEl.GetAttribute("style").Should().Contain("--event-color: #ff5722");
    }

    [Fact]
    public void Uses_EventTemplate_When_Provided()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Maintenance", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0), color: "#dc2626")
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.EventTemplate, (RenderFragment<TmScheduleEvent>)(e => builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "custom-month-event");
                builder.AddContent(2, $"CUSTOM: {e.Title}");
                builder.CloseElement();
            })));

        var eventEl = cut.Find(".tm-scheduler-month-event");
        eventEl.GetAttribute("style").Should().Contain("--event-color: #dc2626");
        cut.Find(".custom-month-event").TextContent.Should().Contain("CUSTOM: Maintenance");
        cut.FindAll(".tm-scheduler-month-event-title").Should().BeEmpty();
    }
}
