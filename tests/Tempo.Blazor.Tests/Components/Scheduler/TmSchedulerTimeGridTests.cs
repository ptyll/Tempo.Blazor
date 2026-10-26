using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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

    [Theory]
    [InlineData(30, 0, 1440)]
    [InlineData(15, 0, 1440)]
    [InlineData(30, 510, 540)]
    public void Slot_Sizes_Cover_The_Whole_Range(int slot, int start, int minutes)
    {
        // Integer division used to leave the last slots short of the range, so a day summed to 96%.
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.SlotDuration, slot)
            .Add(c => c.ShowFullDay, start == 0)
            .Add(c => c.WorkDayStart, new TimeOnly(start / 60, start % 60))
            .Add(c => c.WorkDayEnd, start == 0 ? new TimeOnly(23, 59) : new TimeOnly((start + minutes) / 60, (start + minutes) % 60)));

        var sizes = cut.FindAll(".tm-scheduler-timegrid-slot")
            .Select(s => Minutes(s, "--tm-scheduler-slot-size"))
            .ToList();
        sizes.Sum().Should().Be(minutes);
    }

    [Fact]
    public void Label_Offset_Matches_The_Event_Offset_For_The_Same_Time()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                Evt("Noon", new DateTime(2025, 6, 10, 12, 0, 0), new DateTime(2025, 6, 10, 13, 0, 0))
            }));

        var label = cut.FindAll(".tm-scheduler-timegrid-time-label").Single(l => l.TextContent.Contains("12:"));
        var evt = cut.Find(".tm-scheduler-timegrid-event");
        Minutes(label, "--tm-scheduler-label-offset").Should().Be(Minutes(evt, "--tm-scheduler-event-start"));
    }

    private static int Minutes(AngleSharp.Dom.IElement element, string variable)
    {
        var style = element.GetAttribute("style") ?? "";
        var marker = variable + ": ";
        var start = style.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{variable} should be a unitless minute count, got '{style}'");
        var end = style.IndexOf(';', start);
        return int.Parse(style[(start + marker.Length)..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Renders_TimeGrid_Container()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)]));

        cut.Find(".tm-scheduler-timegrid").Should().NotBeNull();
    }

    [Fact]
    public void Renders_Time_Labels()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)]));

        var labels = cut.FindAll(".tm-scheduler-timegrid-time-label");
        labels.Count.Should().BeGreaterThan(0);
        // 24 hour labels + the closing midnight + the all-day label.
        labels.Count.Should().Be(26);
    }

    [Fact]
    public void Renders_Correct_Number_Of_Columns()
    {
        var dates = new List<DateOnly>
        {
            new DateOnly(2025, 6, 9),
            new DateOnly(2025, 6, 10),
            new DateOnly(2025, 6, 11),
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, dates));

        cut.FindAll(".tm-scheduler-timegrid-col").Count.Should().Be(3);
    }

    [Fact]
    public void Renders_Column_Headers()
    {
        var dates = new List<DateOnly>
        {
            new DateOnly(2025, 6, 9),
            new DateOnly(2025, 6, 10),
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, dates));

        var headers = cut.FindAll(".tm-scheduler-timegrid-col-header");
        headers.Count.Should().Be(2);
    }

    [Fact]
    public void Renders_Time_Slots()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.SlotDuration, 30));

        // 24 hours * 2 slots/hour = 48 slots per column
        var slots = cut.FindAll(".tm-scheduler-timegrid-slot");
        slots.Count.Should().Be(48);
    }

    [Fact]
    public void Renders_AllDay_Row()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)]));

        cut.Find(".tm-scheduler-timegrid-allday").Should().NotBeNull();
    }

    [Fact]
    public void AllDay_Events_Appear_In_AllDay_Row()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Conference", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true)
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
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
            Evt("Meeting", new DateTime(2025, 6, 10, 14, 0, 0), new DateTime(2025, 6, 10, 15, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEls = cut.FindAll(".tm-scheduler-timegrid-event");
        eventEls.Count.Should().Be(1);
        eventEls[0].TextContent.Should().Contain("Meeting");
    }

    /// <summary>
    /// A timed event suppresses the browser's context menu, the same way a slot and a month cell do.
    /// </summary>
    [Fact]
    public void Timed_Event_Prevents_The_Native_Context_Menu()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new DateTime(2025, 6, 10, 14, 0, 0), new DateTime(2025, 6, 10, 15, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-timegrid-event").OuterHtml.Should().Contain("oncontextmenu").And.Contain("preventDefault");
    }

    /// <summary>
    /// B24: EventTemplate must replace the inner content of a positioned timed event AND of an
    /// all-day chip. The positioning wrapper and the resize handles stay.
    /// </summary>
    [Fact]
    public void TimeGrid_Uses_EventTemplate()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Timed", new DateTime(2025, 6, 10, 14, 0, 0), new DateTime(2025, 6, 10, 15, 0, 0)),
            Evt("Away", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true)
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, false)
            .Add(c => c.EventTemplate, (RenderFragment<TmScheduleEvent>)(e => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "timegrid-custom");
                builder.AddContent(2, $"CUSTOM:{e.Title}");
                builder.CloseElement();
            })));

        var timed = cut.Find(".tm-scheduler-timegrid-event");
        timed.QuerySelector(".timegrid-custom")!.TextContent.Should().Contain("CUSTOM:Timed");
        timed.QuerySelectorAll(".tm-scheduler-timegrid-event-title").Should().BeEmpty();
        timed.QuerySelectorAll(".tm-scheduler-event-resize-handle").Count.Should().Be(2,
            "the positioning wrapper and resize handles stay; only the inner content is replaced");

        var allDay = cut.Find(".tm-scheduler-timegrid-allday-event");
        allDay.QuerySelector(".timegrid-custom")!.TextContent.Should().Be("CUSTOM:Away");
        allDay.TextContent.Should().NotContain("Away\n");
    }

    [Fact]
    public void Event_Positioned_With_Top_And_Height_Style()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Meeting", new DateTime(2025, 6, 10, 12, 0, 0), new DateTime(2025, 6, 10, 13, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        // Geometry is a unitless custom property. The stylesheet turns it into a percentage,
        // so a culture that formats doubles with a comma never reaches an inline style.
        var style = eventEl.GetAttribute("style") ?? "";
        style.Should().Contain("--tm-scheduler-event-start: 720");
        style.Should().Contain("--tm-scheduler-event-end: 780");
    }

    [Fact]
    public void Slot_Click_Fires_OnSlotClick()
    {
        (DateTime Start, DateTime End)? slot = null;
        var cut = Render<TmSchedulerTimeGrid>(p => p
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
            Evt("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 9, 30, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
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
            Evt("Event A", new DateTime(2025, 6, 10, 10, 0, 0), new DateTime(2025, 6, 10, 12, 0, 0)),
            Evt("Event B", new DateTime(2025, 6, 10, 11, 0, 0), new DateTime(2025, 6, 10, 13, 0, 0)),
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEls = cut.FindAll(".tm-scheduler-timegrid-event");
        eventEls.Count.Should().Be(2);

        // Overlapping events take separate lanes. The stylesheet derives width and left from them.
        var styleA = eventEls[0].GetAttribute("style") ?? "";
        var styleB = eventEls[1].GetAttribute("style") ?? "";
        styleA.Should().Contain("--tm-scheduler-lane: 0");
        styleB.Should().Contain("--tm-scheduler-lane: 1");
        styleA.Should().Contain("--tm-scheduler-lanes: 2");
    }

    [Fact]
    public void Event_Color_Applied_As_CSS_Variable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Colored", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0), color: "#ff5722")
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        eventEl.GetAttribute("style").Should().Contain("--event-color: #ff5722");
    }

    [Fact]
    public void Colourless_Event_CarriesNoInlineFallback()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Plain", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var style = cut.Find(".tm-scheduler-timegrid-event").GetAttribute("style") ?? "";
        style.Should().NotContain("--event-color",
            "an event without a colour takes the theme's event fill from CSS, not an inline fallback");
        style.Should().NotContain("#3b82f6");
    }

    /// <summary>
    /// Roving tabindex over the slots: exactly one slot is a tab stop, never one per slot (336 in a
    /// week). Up/Down move by a slot, Left/Right by a day, and Enter raises OnSlotClick once.
    /// </summary>
    [Fact]
    public void Slots_Are_Roving_Not_336_Tab_Stops()
    {
        var dates = Enumerable.Range(0, 7).Select(i => new DateOnly(2025, 6, 9).AddDays(i)).ToList();
        var clicks = new List<(DateTime Start, DateTime End)>();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, dates)
            .Add(c => c.SlotDuration, 30)
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, clicks.Add)));

        var slots = cut.FindAll(".tm-scheduler-timegrid-slot");
        slots.Count.Should().Be(7 * 48);
        slots.Count(s => s.GetAttribute("tabindex") == "0").Should().Be(1,
            "a week of 30-minute slots is one tab stop, not 336");
        slots.Count(s => s.GetAttribute("tabindex") == "-1").Should().Be(slots.Count - 1);

        var active = cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']");
        active.GetAttribute("data-start").Should().Be("2025-06-09T00:00");

        active.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']").GetAttribute("data-start").Should().Be("2025-06-09T00:30");

        cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']").GetAttribute("data-start").Should().Be("2025-06-10T00:30");

        cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        var landed = cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']");
        landed.GetAttribute("data-start").Should().Be("2025-06-10T00:00");

        landed.KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        clicks.Should().BeEmpty();
        landed.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().ContainSingle();
        clicks[0].Start.Should().Be(new DateTime(2025, 6, 10, 0, 0, 0));
    }

    /// <summary>
    /// With resources the columns are flat (day x resource) and the arrows cross them. Exactly one
    /// slot is a tab stop, and each slot names its resource.
    /// </summary>
    [Fact]
    public void Slots_With_Resources_Have_One_Tab_Stop_And_Arrows_Cross_Resource_Columns()
    {
        var resources = new List<TmScheduleResource>
        {
            new() { Id = "room-a", Name = "Room A" },
            new() { Id = "room-b", Name = "Room B" }
        };
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 9), new DateOnly(2025, 6, 10)])
            .Add(c => c.Resources, resources)
            .Add(c => c.SlotDuration, 60));

        var slots = cut.FindAll(".tm-scheduler-timegrid-slot");
        slots.Count.Should().Be(2 * 2 * 24);
        slots.Count(s => s.GetAttribute("tabindex") == "0").Should().Be(1);
        slots.Should().OnlyContain(s => s.GetAttribute("data-resource") != null);

        var active = cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']");
        active.GetAttribute("data-resource").Should().Be("room-a");

        active.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        var moved = cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']");
        moved.GetAttribute("data-resource").Should().Be("room-b");
        moved.GetAttribute("data-start").Should().Be("2025-06-09T00:00");

        moved.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        var nextDay = cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']");
        nextDay.GetAttribute("data-resource").Should().Be("room-a");
        nextDay.GetAttribute("data-start").Should().Be("2025-06-10T00:00");
    }

    /// <summary>
    /// Enter and Space on a timed event and on an all-day event each raise OnEventClick once, and a
    /// key on the event never also activates the slot behind it.
    /// </summary>
    [Fact]
    public void Events_Activate_Once_And_Do_Not_Activate_The_Slot()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Standup", Start = new DateTime(2025, 6, 9, 9, 0, 0), End = new DateTime(2025, 6, 9, 10, 0, 0) },
            new() { Title = "Holiday", Start = new DateTime(2025, 6, 9), End = new DateTime(2025, 6, 10), AllDay = true }
        };
        var clicks = new List<string>();
        var slots = new List<(DateTime, DateTime)>();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 9)])
            .Add(c => c.Events, events)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicks.Add(e.Title)))
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, slots.Add)));

        var timed = cut.Find(".tm-scheduler-timegrid-event");
        timed.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        timed.KeyUp(new KeyboardEventArgs { Key = " " });
        clicks.Should().Equal("Standup", "Standup");

        var allDay = cut.Find(".tm-scheduler-timegrid-allday-event");
        allDay.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().HaveCount(3);
        slots.Should().BeEmpty("the event's keys are fenced off the slot");
    }

    /// <summary>A day column is a labelled group, and a slot's name uses words and the culture's time.</summary>
    [Fact]
    public void Day_Columns_Are_Labelled_Groups()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 9)])
            .Add(c => c.SlotDuration, 60));

        cut.FindAll("[role='group']").Should().NotBeEmpty();
        cut.Find("[role='group']").GetAttribute("aria-label").Should().Contain("2025");

        var slot = cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']");
        slot.GetAttribute("aria-label").Should().Contain(" to ");
    }

    /// <summary>The selected column header and the selected event are both marked, and a click selects the event.</summary>
    [Fact]
    public void SelectedDate_And_SelectedEvent_Are_Marked()
    {
        var standup = Evt("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0));
        standup.Id = "standup";
        TmScheduleEvent? selected = null;

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 9), new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, new List<TmScheduleEvent> { standup })
            .Add(c => c.SelectedDate, new DateTime(2025, 6, 10))
            .Add(c => c.SelectedEvent, standup)
            .Add(c => c.SelectedEventChanged, EventCallback.Factory.Create<TmScheduleEvent?>(this, e => selected = e)));

        cut.Find(".tm-scheduler-timegrid-col--selected").TextContent.Should().Contain("10");
        var marked = cut.Find(".tm-scheduler-timegrid-event--selected");
        marked.GetAttribute("aria-current").Should().Be("true");
        marked.Click();
        selected!.Id.Should().Be("standup");
    }
}
