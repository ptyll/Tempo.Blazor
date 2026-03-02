using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
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
        var evt = Evt("Meeting", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0));

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        cut.Find(".tm-scheduler-event").Should().NotBeNull();
    }

    [Fact]
    public void Renders_Event_Title()
    {
        var evt = Evt("Sprint Review", new(2025, 6, 10, 14, 0, 0), new(2025, 6, 10, 15, 0, 0));

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        cut.Find(".tm-scheduler-event-title").TextContent.Should().Contain("Sprint Review");
    }

    [Fact]
    public void Renders_Event_Time()
    {
        var evt = Evt("Meeting", new(2025, 6, 10, 14, 30, 0), new(2025, 6, 10, 16, 0, 0));

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        var time = cut.Find(".tm-scheduler-event-time");
        time.TextContent.Should().Contain("14:30");
    }

    [Fact]
    public void Renders_Color_Dot()
    {
        var evt = Evt("Colored", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0), color: "#e74c3c");

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        var dot = cut.Find(".tm-scheduler-event-dot");
        dot.GetAttribute("style").Should().Contain("#e74c3c");
    }

    [Fact]
    public void AllDay_Shows_AllDay_Label_Instead_Of_Time()
    {
        var evt = Evt("Holiday", new(2025, 6, 10), new(2025, 6, 11), allDay: true);

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        var time = cut.Find(".tm-scheduler-event-time");
        time.TextContent.Should().Contain("TmScheduler_AllDay");
    }

    [Fact]
    public void Click_Fires_OnClick()
    {
        TmScheduleEvent? clicked = null;
        var evt = Evt("Review", new(2025, 6, 10, 15, 0, 0), new(2025, 6, 10, 16, 0, 0));

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt)
            .Add(c => c.OnClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e)));

        cut.Find(".tm-scheduler-event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Review");
    }

    [Fact]
    public void Applies_Custom_CssClass()
    {
        var evt = Evt("Custom", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0), cssClass: "urgent");

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
            .Add(c => c.Event, evt));

        cut.Find(".tm-scheduler-event").ClassList.Should().Contain("urgent");
    }

    [Fact]
    public void Uses_EventTemplate_When_Provided()
    {
        var evt = Evt("Custom Render", new(2025, 6, 10, 9, 0, 0), new(2025, 6, 10, 10, 0, 0));

        var cut = RenderComponent<TmSchedulerEventItem>(p => p
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
}
