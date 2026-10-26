using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        cut.Find(".tm-scheduler-timeline").Should().NotBeNull();
    }

    [Fact]
    public void Renders_Resource_Labels()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
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
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        var headers = cut.FindAll(".tm-scheduler-timeline-time-header");
        headers.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Renders_Resource_Rows()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        cut.FindAll(".tm-scheduler-timeline-row").Count.Should().Be(3);
    }

    [Fact]
    public void Events_Displayed_In_Correct_Resource_Row()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Room A Meeting", Start = new DateTime(2025, 6, 10, 10, 0, 0), End = new DateTime(2025, 6, 10, 11, 0, 0), ResourceId = "room-a" },
            new() { Title = "Room B Meeting", Start = new DateTime(2025, 6, 10, 14, 0, 0), End = new DateTime(2025, 6, 10, 15, 0, 0), ResourceId = "room-b" },
        };

        var cut = Render<TmSchedulerTimelineView>(p => p
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
            new() { Title = "Meeting", Start = new DateTime(2025, 6, 10, 10, 0, 0), End = new DateTime(2025, 6, 10, 11, 0, 0), ResourceId = "room-a" },
        };

        var cut = Render<TmSchedulerTimelineView>(p => p
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
        var cut = Render<TmSchedulerTimelineView>(p => p
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
            new() { Title = "Meeting", Start = new DateTime(2025, 6, 10, 12, 0, 0), End = new DateTime(2025, 6, 10, 14, 0, 0), ResourceId = "room-a" },
        };

        var cut = Render<TmSchedulerTimelineView>(p => p
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
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10)));

        cut.Find(".tm-scheduler-timeline").Should().NotBeNull();
        // Should show at least one default row
        cut.FindAll(".tm-scheduler-timeline-row").Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Colourless_Event_CarriesNoInlineFallback()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Plain", Start = new DateTime(2025, 6, 10, 10, 0, 0), End = new DateTime(2025, 6, 10, 11, 0, 0), ResourceId = "room-a" },
        };

        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, [new TmScheduleResource { Id = "room-a", Name = "Room A" }])
            .Add(c => c.Events, events));

        var style = cut.Find(".tm-scheduler-timeline-event").GetAttribute("style") ?? "";
        style.Should().NotContain("--event-color",
            "an event without a colour takes the theme's event fill from CSS, not an inline fallback");
        style.Should().NotContain("#3b82f6");
    }

    /// <summary>
    /// B24: EventTemplate replaces the title inside the positioned timeline bar. Resize handles stay.
    /// </summary>
    [Fact]
    public void Timeline_Uses_EventTemplate()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Standup", Start = new DateTime(2025, 6, 10, 10, 0, 0), End = new DateTime(2025, 6, 10, 11, 0, 0), ResourceId = "room-a" }
        };

        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events, events)
            .Add(c => c.EventTemplate, (RenderFragment<TmScheduleEvent>)(e => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "timeline-custom");
                builder.AddContent(2, $"CUSTOM:{e.Title}");
                builder.CloseElement();
            })));

        var bar = cut.Find(".tm-scheduler-timeline-event");
        bar.QuerySelector(".timeline-custom")!.TextContent.Should().Contain("CUSTOM:Standup");
        bar.QuerySelectorAll(".tm-scheduler-timeline-event-title").Should().BeEmpty();
        bar.QuerySelectorAll(".tm-scheduler-event-resize-handle").Count.Should().Be(2);
    }

    /// <summary>
    /// Each resource row is a row with a row header, and its slots share one roving tab stop.
    /// Right moves a slot, Down moves to the next resource's same slot, Enter raises OnSlotClick.
    /// </summary>
    [Fact]
    public void Rows_Have_Row_Header_And_Keyboard()
    {
        var clicks = new List<(DateTime Start, DateTime End)>();
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.SlotDuration, 60)
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, clicks.Add)));

        var rows = cut.FindAll("[role='row']");
        rows.Count.Should().Be(3);
        rows.Should().OnlyContain(r => r.QuerySelector("[role='rowheader']") != null);
        rows[0].QuerySelector("[role='rowheader']")!.TextContent.Should().Contain("Room A");

        var slots = cut.FindAll(".tm-scheduler-timeline-slot");
        slots.Count(s => s.GetAttribute("tabindex") == "0").Should().Be(1);
        slots.Count(s => s.GetAttribute("tabindex") == "-1").Should().Be(slots.Count - 1);

        var active = cut.Find(".tm-scheduler-timeline-slot[tabindex='0']");
        active.GetAttribute("data-start").Should().Be("2025-06-10T00:00");
        active.GetAttribute("data-resource").Should().Be("room-a");

        active.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        var moved = cut.Find(".tm-scheduler-timeline-slot[tabindex='0']");
        moved.GetAttribute("data-start").Should().Be("2025-06-10T01:00");

        moved.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        var nextRow = cut.Find(".tm-scheduler-timeline-slot[tabindex='0']");
        nextRow.GetAttribute("data-resource").Should().Be("room-b");
        nextRow.GetAttribute("data-start").Should().Be("2025-06-10T01:00");

        nextRow.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().ContainSingle();
        clicks[0].Start.Should().Be(new DateTime(2025, 6, 10, 1, 0, 0));
    }

    /// <summary>
    /// Enter and Space on a timeline event raise OnEventClick once and never the slot behind it.
    /// The event is a tab stop only inside the active resource row.
    /// </summary>
    [Fact]
    public void Timeline_Event_Activates_Once_And_Only_The_Active_Row_Is_A_Tab_Stop()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Standup", Start = new DateTime(2025, 6, 10, 9, 0, 0), End = new DateTime(2025, 6, 10, 10, 0, 0), ResourceId = "room-a" },
            new() { Title = "Review", Start = new DateTime(2025, 6, 10, 11, 0, 0), End = new DateTime(2025, 6, 10, 12, 0, 0), ResourceId = "room-b" }
        };
        var clicks = new List<string>();
        var slots = new List<(DateTime, DateTime)>();
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicks.Add(e.Title)))
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, slots.Add)));

        var rows = cut.FindAll("[role='row']");
        rows[0].QuerySelectorAll(".tm-scheduler-timeline-event[tabindex='0']").Length.Should().Be(1);
        rows[1].QuerySelectorAll(".tm-scheduler-timeline-event[tabindex='0']").Length.Should().Be(0);

        var item = cut.Find(".tm-scheduler-timeline-event[tabindex='0']");
        item.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        item.KeyUp(new KeyboardEventArgs { Key = " " });
        clicks.Should().Equal("Standup", "Standup");
        slots.Should().BeEmpty();
    }

    /// <summary>The timeline grid has its own accessible name, distinct from the calendar grid.</summary>
    [Fact]
    public void Timeline_Grid_Has_Its_Own_Label()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources));

        cut.Find("[role='grid']").GetAttribute("aria-label").Should().Be("Timeline");
    }

    /// <summary>
    /// The bar follows the display wall-clock, not the machine zone. A +14 offset is no machine
    /// zone, so the 09:00 wall-clock must land at the 09:00 mark on every machine.
    /// </summary>
    [Fact]
    public void Timeline_Bar_Follows_The_Display_Wall_Clock_Not_The_Machine_Zone()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 3, 29))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events,
            [
                new TmScheduleEvent
                {
                    Id = "dst", Title = "Standup", ResourceId = "room-a",
                    Start = new DateTimeOffset(2026, 3, 29, 9, 0, 0, TimeSpan.FromHours(14)),
                    End = new DateTimeOffset(2026, 3, 29, 10, 0, 0, TimeSpan.FromHours(14))
                }
            ]));

        var bar = cut.Find(".tm-scheduler-timeline-event");
        bar.GetAttribute("style").Should().Contain("left: 37.5000%");
        bar.GetAttribute("style").Should().Contain("width: 4.166");
    }

    /// <summary>The selected timeline bar carries aria-selected and the selected class.</summary>
    [Fact]
    public void SelectedEvent_Marks_The_Bar()
    {
        var standup = new TmScheduleEvent
        {
            Id = "standup", Title = "Standup", ResourceId = "room-a",
            Start = new DateTime(2025, 6, 10, 9, 0, 0),
            End = new DateTime(2025, 6, 10, 10, 0, 0)
        };
        TmScheduleEvent? selected = null;

        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Resources, TestResources)
            .Add(c => c.Events, new List<TmScheduleEvent> { standup })
            .Add(c => c.SelectedEvent, standup)
            .Add(c => c.SelectedEventChanged, EventCallback.Factory.Create<TmScheduleEvent?>(this, e => selected = e)));

        var bar = cut.Find(".tm-scheduler-timeline-event--selected");
        bar.GetAttribute("aria-current").Should().Be("true");
        bar.Click();
        selected!.Id.Should().Be("standup");
    }
}
