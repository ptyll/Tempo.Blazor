using System.Globalization;
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
        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1)));

        cut.Find(".tm-scheduler-agenda").Should().NotBeNull();
    }

    [Fact]
    public void Shows_Empty_State_When_No_Events()
    {
        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1)));

        cut.Find(".tm-scheduler-agenda-empty").Should().NotBeNull();
    }

    [Fact]
    public void Groups_Events_By_Date()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Evt A", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0)),
            Evt("Evt B", new DateTime(2025, 6, 10, 14, 0, 0), new DateTime(2025, 6, 10, 15, 0, 0)),
            Evt("Evt C", new DateTime(2025, 6, 12, 9, 0, 0), new DateTime(2025, 6, 12, 10, 0, 0)),
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
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
            Evt("Evt A", new DateTime(2025, 6, 5, 9, 0, 0), new DateTime(2025, 6, 5, 10, 0, 0)),
            Evt("Evt B", new DateTime(2025, 6, 20, 9, 0, 0), new DateTime(2025, 6, 20, 10, 0, 0)),
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
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
            Evt("Team Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 9, 30, 0))
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-agenda-event-title").TextContent.Should().Contain("Team Standup");
    }

    [Fact]
    public void Renders_Event_Time_Format()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new DateTime(2025, 6, 10, 14, 30, 0), new DateTime(2025, 6, 10, 16, 0, 0))
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
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
            Evt("Holiday", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true)
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        var timeEl = cut.Find(".tm-scheduler-agenda-event-time");
        timeEl.TextContent.Should().Contain("All day");
    }

    [Fact]
    public void Event_Click_Fires_Callback()
    {
        TmScheduleEvent? clicked = null;
        var events = new List<TmScheduleEvent>
        {
            Evt("Review", new DateTime(2025, 6, 10, 15, 0, 0), new DateTime(2025, 6, 10, 16, 0, 0))
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
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
            Evt("Sprint Planning", new DateTime(2025, 6, 10, 10, 0, 0), new DateTime(2025, 6, 10, 11, 0, 0), desc: "Q3 planning session")
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-agenda-event-desc").TextContent.Should().Contain("Q3 planning session");
    }

    [Fact]
    public void Renders_Event_Color_Dot()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Colored", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0), color: "#e74c3c")
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        var dot = cut.Find(".tm-scheduler-agenda-event-dot");
        dot.GetAttribute("style").Should().Contain("background-color: #e74c3c");
    }

    /// <summary>
    /// B24: EventTemplate replaces the title/time/description inside the agenda row. The row chrome stays.
    /// </summary>
    [Fact]
    public void Agenda_Uses_EventTemplate()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 9, 30, 0), desc: "Daily")
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events)
            .Add(c => c.EventTemplate, (RenderFragment<TmScheduleEvent>)(e => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "agenda-custom");
                builder.AddContent(2, $"CUSTOM:{e.Title}");
                builder.CloseElement();
            })));

        var row = cut.Find(".tm-scheduler-agenda-event");
        row.QuerySelector(".agenda-custom")!.TextContent.Should().Contain("CUSTOM:Standup");
        row.QuerySelectorAll(".tm-scheduler-agenda-event-title").Should().BeEmpty();
        row.QuerySelector(".tm-scheduler-agenda-event-dot").Should().NotBeNull(
            "the row chrome (dot) stays; only the inner content is replaced");
    }

    /// <summary>
    /// Agenda is a list: the root is role=list, each day is a labelled group, and every event is a
    /// native button (so Enter/Space activate once without emulation).
    /// </summary>
    [Fact]
    public void Agenda_List_Semantics()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 9, 30, 0)),
            Evt("Review", new DateTime(2025, 6, 11, 14, 0, 0), new DateTime(2025, 6, 11, 15, 0, 0))
        };

        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        var list = cut.Find("[role='list']");
        list.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var groups = cut.FindAll("[role='listitem']");
        groups.Count.Should().Be(2);
        groups.Should().OnlyContain(g => g.QuerySelector("[role='group'][aria-label]") != null);

        var events_ = cut.FindAll(".tm-scheduler-agenda-event");
        events_.Should().OnlyContain(e => e.TagName == "BUTTON");
        events_.Should().OnlyContain(e => e.GetAttribute("type") == "button");

        using (CultureScope.Use("en-US"))
        {
            var american = Render<TmSchedulerAgendaView>(p => p
                .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
                .Add(c => c.Events, events));
            american.Find(".tm-scheduler-agenda-event").GetAttribute("aria-label")
                .Should().Contain("AM", "the label uses the culture's short time");
        }
    }

    /// <summary>An empty agenda has no list role, so the empty state is not a list without items.</summary>
    [Fact]
    public void Empty_Agenda_Has_No_List_Role()
    {
        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1)));

        cut.FindAll("[role='list']").Should().BeEmpty();
        cut.Find(".tm-scheduler-agenda-empty").Should().NotBeNull();
    }

    /// <summary>The selected event is aria-selected and carries the selected class.</summary>
    [Fact]
    public void SelectedEvent_Marks_The_Agenda_Row()
    {
        var standup = Evt("Standup", new DateTime(2025, 6, 15, 9, 0, 0), new DateTime(2025, 6, 15, 10, 0, 0));
        standup.Id = "standup";
        TmScheduleEvent? selected = null;

        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, new List<TmScheduleEvent> { standup })
            .Add(c => c.SelectedEvent, standup)
            .Add(c => c.SelectedEventChanged, EventCallback.Factory.Create<TmScheduleEvent?>(this, e => selected = e)));

        var row = cut.Find(".tm-scheduler-agenda-event--selected");
        row.GetAttribute("aria-current").Should().Be("true");
        row.Click();
        selected!.Id.Should().Be("standup");
    }
}
