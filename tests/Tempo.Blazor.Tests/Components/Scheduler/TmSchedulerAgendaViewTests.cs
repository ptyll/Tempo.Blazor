using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerAgendaView.</summary>
public class TmSchedulerAgendaViewTests : LocalizationTestBase
{
    private static TmScheduleEvent Evt(
        string title, DateTime start, DateTime end,
        bool allDay = false, string? desc = null, string? color = null) =>
        new() { Title = title, Start = start, End = end, AllDay = allDay, Description = desc, Color = color };

    [Fact]
    public void Renders_Agenda_Container()
    {
        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1)));

        cut.Find(".tm-scheduler-agenda").Should().NotBeNull();
    }

    [Fact]
    public void Shows_Empty_State_When_No_Events()
    {
        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1)));

        cut.Find(".tm-scheduler-agenda-empty").Should().NotBeNull();
    }

    [Fact]
    public void Groups_Events_By_Date()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Evt A", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0)),
            Evt("Evt B", new(2025, 6, 10, 14, 0, 0), new(2025, 6, 10, 15, 0, 0)),
            Evt("Evt C", new(2025, 6, 12, 9, 0, 0), new(2025, 6, 12, 10, 0, 0)),
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        // 2 date groups (June 10 and June 12)
        cut.FindAll(".tm-scheduler-agenda-group").Count.Should().Be(2);
    }

    [Fact]
    public void Skips_Empty_Days()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Evt A", new(2025, 6, 5, 9, 0, 0), new(2025, 6, 5, 10, 0, 0)),
            Evt("Evt B", new(2025, 6, 20, 9, 0, 0), new(2025, 6, 20, 10, 0, 0)),
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        // Only 2 groups, not 15+
        cut.FindAll(".tm-scheduler-agenda-group").Count.Should().Be(2);
    }

    [Fact]
    public void Renders_Event_Title()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Team Standup", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 9, 30, 0))
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-agenda-event-title").TextContent.Should().Contain("Team Standup");
    }

    [Fact]
    public void Renders_Event_Time_Format()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new(2025, 6, 10, 14, 30, 0), new(2025, 6, 10, 16, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        var timeEl = cut.Find(".tm-scheduler-agenda-event-time");
        timeEl.TextContent.Should().Contain("14:30");
        timeEl.TextContent.Should().Contain("16:00");
    }

    [Fact]
    public void AllDay_Event_Shows_AllDay_Label()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Holiday", new(2025, 6, 10), new(2025, 6, 11), allDay: true)
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        var timeEl = cut.Find(".tm-scheduler-agenda-event-time");
        // Mock localizer returns [TmScheduler_AllDay]
        timeEl.TextContent.Should().Contain("TmScheduler_AllDay");
    }

    [Fact]
    public void Event_Click_Fires_Callback()
    {
        TmScheduleEvent? clicked = null;
        var events = new List<TmScheduleEvent>
        {
            Evt("Review", new(2025, 6, 10, 15, 0, 0), new(2025, 6, 10, 16, 0, 0))
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-agenda-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Review");
    }

    [Fact]
    public void Renders_Event_Description_When_Present()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Sprint Planning", new(2025, 6, 10, 10, 0, 0), new(2025, 6, 10, 11, 0, 0), desc: "Q3 planning session")
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-agenda-event-desc").TextContent.Should().Contain("Q3 planning session");
    }

    [Fact]
    public void Renders_Event_Color_Dot()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Colored", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0), color: "#e74c3c")
        };

        var cut = RenderComponent<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        var dot = cut.Find(".tm-scheduler-agenda-event-dot");
        dot.GetAttribute("style").Should().Contain("background-color: #e74c3c");
    }
}
