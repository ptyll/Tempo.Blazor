using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerTimeGrid — shared time-axis for Day/Week views.</summary>
public class TmSchedulerTimeGridTests : LocalizationTestBase
{
    private static TmScheduleEvent Evt(
        string title, DateTime start, DateTime end,
        bool allDay = false, string? color = null) =>
        new() { Title = title, Start = start, End = end, AllDay = allDay, Color = color };

    [Fact]
    public void Renders_TimeGrid_Container()
    {
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)]));

        cut.Find(".tm-scheduler-timegrid").Should().NotBeNull();
    }

    [Fact]
    public void Renders_Time_Labels()
    {
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)]));

        var labels = cut.FindAll(".tm-scheduler-timegrid-time-label");
        labels.Count.Should().BeGreaterThan(0);
        // 24 hour labels + 1 allday label = 25
        labels.Count.Should().Be(25);
    }

    [Fact]
    public void Renders_Correct_Number_Of_Columns()
    {
        var dates = new List<DateOnly>
        {
            new(2025, 6, 9),
            new(2025, 6, 10),
            new(2025, 6, 11),
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, dates));

        cut.FindAll(".tm-scheduler-timegrid-col").Count.Should().Be(3);
    }

    [Fact]
    public void Renders_Column_Headers()
    {
        var dates = new List<DateOnly>
        {
            new(2025, 6, 9),
            new(2025, 6, 10),
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, dates));

        var headers = cut.FindAll(".tm-scheduler-timegrid-col-header");
        headers.Count.Should().Be(2);
    }

    [Fact]
    public void Renders_Time_Slots()
    {
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.SlotDuration, 30));

        // 24 hours * 2 slots/hour = 48 slots per column
        var slots = cut.FindAll(".tm-scheduler-timegrid-slot");
        slots.Count.Should().Be(48);
    }

    [Fact]
    public void Renders_AllDay_Row()
    {
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)]));

        cut.Find(".tm-scheduler-timegrid-allday").Should().NotBeNull();
    }

    [Fact]
    public void AllDay_Events_Appear_In_AllDay_Row()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Conference", new(2025, 6, 10), new(2025, 6, 11), allDay: true)
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var allDayRow = cut.Find(".tm-scheduler-timegrid-allday");
        allDayRow.InnerHtml.Should().Contain("Conference");
    }

    [Fact]
    public void Timed_Events_Rendered_In_Column()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new(2025, 6, 10, 14, 0, 0), new(2025, 6, 10, 15, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEls = cut.FindAll(".tm-scheduler-timegrid-event");
        eventEls.Count.Should().Be(1);
        eventEls[0].TextContent.Should().Contain("Meeting");
    }

    [Fact]
    public void Event_Positioned_With_Top_And_Height_Style()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new(2025, 6, 10, 12, 0, 0), new(2025, 6, 10, 13, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        var style = eventEl.GetAttribute("style") ?? "";
        style.Should().Contain("top:");
        style.Should().Contain("height:");
    }

    [Fact]
    public void Slot_Click_Fires_OnSlotClick()
    {
        (DateTime Start, DateTime End)? slot = null;
        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.SlotDuration, 30)
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, s => slot = s)));

        // Click the first slot
        cut.FindAll(".tm-scheduler-timegrid-slot")[0].Click();

        slot.Should().NotBeNull();
        slot!.Value.Start.Date.Should().Be(new DateTime(2025, 6, 10));
    }

    [Fact]
    public void Event_Click_Fires_OnEventClick()
    {
        TmScheduleEvent? clicked = null;
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 9, 30, 0))
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-timegrid-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Standup");
    }

    [Fact]
    public void Overlapping_Events_Get_Lane_Positioning()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Event A", new(2025, 6, 10, 10, 0, 0), new(2025, 6, 10, 12, 0, 0)),
            Evt("Event B", new(2025, 6, 10, 11, 0, 0), new(2025, 6, 10, 13, 0, 0)),
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEls = cut.FindAll(".tm-scheduler-timegrid-event");
        eventEls.Count.Should().Be(2);

        // Overlapping events should have width < 100% and different left positions
        var styleA = eventEls[0].GetAttribute("style") ?? "";
        var styleB = eventEls[1].GetAttribute("style") ?? "";
        styleA.Should().Contain("width:");
        styleB.Should().Contain("width:");
        styleA.Should().Contain("left:");
        styleB.Should().Contain("left:");
    }

    [Fact]
    public void Event_Color_Applied_As_CSS_Variable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Colored", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0), color: "#ff5722")
        };

        var cut = RenderComponent<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        eventEl.GetAttribute("style").Should().Contain("--event-color: #ff5722");
    }
}
