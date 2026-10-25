using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerEventItem — shared event renderer.</summary>
public class TmSchedulerEventItemTests : LocalizationTestBase
{
    private static TmScheduleEvent Evt(
        string title, DateTime start, DateTime end,
        bool allDay = false, string? color = null, string? cssClass = null) =>
        new() { Title = title, Start = start, End = end, AllDay = allDay, Color = color, CssClass = cssClass };

    [Fact]
    public void Renders_Event_Container()
    {
        var evt = Evt("Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0));

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        cut.Find(".tm-scheduler-event").Should().NotBeNull();
    }

    [Fact]
    public void Renders_Event_Title()
    {
        var evt = Evt("Sprint Review", new DateTime(2025, 6, 10, 14, 0, 0), new DateTime(2025, 6, 10, 15, 0, 0));

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        cut.Find(".tm-scheduler-event-title").TextContent.Should().Contain("Sprint Review");
    }

    [Fact]
    public void Renders_Event_Time()
    {
        var evt = Evt("Meeting", new DateTime(2025, 6, 10, 14, 30, 0), new DateTime(2025, 6, 10, 16, 0, 0));

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        var time = cut.Find(".tm-scheduler-event-time");
        time.TextContent.Should().Contain("14:30");
    }

    [Fact]
    public void Renders_Color_Dot()
    {
        var evt = Evt("Colored", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0), color: "#e74c3c");

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        var dot = cut.Find(".tm-scheduler-event-dot");
        dot.GetAttribute("style").Should().Contain("#e74c3c");
    }

    [Fact]
    public void AllDay_Shows_AllDay_Label_Instead_Of_Time()
    {
        var evt = Evt("Holiday", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true);

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        var time = cut.Find(".tm-scheduler-event-time");
        time.TextContent.Should().Contain("All day");
    }

    [Fact]
    public void Click_Fires_OnClick()
    {
        TmScheduleEvent? clicked = null;
        var evt = Evt("Review", new DateTime(2025, 6, 10, 15, 0, 0), new DateTime(2025, 6, 10, 16, 0, 0));

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt)
            .Add(c => c.OnClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Review");
    }

    [Fact]
    public void Applies_Custom_CssClass()
    {
        var evt = Evt("Custom", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0), cssClass: "urgent");

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        cut.Find(".tm-scheduler-event").ClassList.Should().Contain("urgent");
    }

    [Fact]
    public void Uses_EventTemplate_When_Provided()
    {
        var evt = Evt("Custom Render", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0));

        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt)
            .Add(c => c.EventTemplate, (RenderFragment<TmScheduleEvent>)(e => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "custom-event-template");
                builder.AddContent(2, $"CUSTOM: {e.Title}");
                builder.CloseElement();
            })));

        cut.Find(".custom-event-template").TextContent.Should().Contain("CUSTOM: Custom Render");
    }

    /// <summary>
    /// An event is a button with an accessible name that carries the title and the time range.
    /// An all-day event names the localized "all day" wording instead of a clock range.
    /// </summary>
    [Fact]
    public void Event_Has_Role_Button_Tabindex_And_AriaLabel()
    {
        var timed = Evt("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 30, 0));
        var cut = Render<TmSchedulerEventItem>(p => p.Add(c => c.Event, timed));

        var item = cut.Find(".tm-scheduler-event");
        item.GetAttribute("role").Should().Be("button");
        item.GetAttribute("tabindex").Should().Be("0");
        item.GetAttribute("aria-label").Should().Contain("Standup").And.Contain(" to ");
        item.GetAttribute("aria-label").Should().NotContain("–");

        var allDay = Evt("Holiday", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true);
        var allDayCut = Render<TmSchedulerEventItem>(p => p.Add(c => c.Event, allDay));
        allDayCut.Find(".tm-scheduler-event").GetAttribute("aria-label").Should().Be("Holiday, all day");
    }

    /// <summary>Enter activates the event exactly once; a held Enter does not repeat the click.</summary>
    [Fact]
    public void Event_Enter_Invokes_OnEventClick_Once()
    {
        var clicks = 0;
        var evt = Evt("Review", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0));
        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt)
            .Add(c => c.OnClick, EventCallback.Factory.Create<TmScheduleEvent>(this, _ => clicks++)));

        var item = cut.Find(".tm-scheduler-event");
        item.KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        clicks.Should().Be(0);
        item.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().Be(1);
    }

    /// <summary>Space activates on keyup, so a keydown never double-fires the click.</summary>
    [Fact]
    public void Event_Space_Invokes_Once()
    {
        var clicks = 0;
        var evt = Evt("Review", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0));
        var cut = Render<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt)
            .Add(c => c.OnClick, EventCallback.Factory.Create<TmScheduleEvent>(this, _ => clicks++)));

        var item = cut.Find(".tm-scheduler-event");
        item.KeyDown(new KeyboardEventArgs { Key = " " });
        clicks.Should().Be(0);
        item.KeyUp(new KeyboardEventArgs { Key = " " });
        clicks.Should().Be(1);
    }
}
