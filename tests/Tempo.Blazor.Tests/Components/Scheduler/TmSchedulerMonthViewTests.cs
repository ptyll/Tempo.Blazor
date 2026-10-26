using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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

    /// <summary>
    /// The shared title colour is the theme's primary text, which fails on a solid all-day fill.
    /// An all-day title inherits the chip's own text colour instead.
    /// </summary>
    [Fact]
    public void AllDay_Title_Inherits_The_Event_Text_Colour()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Holiday", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true, color: "#f59e0b")
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var chip = cut.Find(".tm-scheduler-month-event--allday");
        chip.GetAttribute("style").Should().Contain("--event-text-color");
        chip.QuerySelector(".tm-scheduler-month-event-title").Should().NotBeNull();

        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_scheduler-month.css"));
        css.Should().Contain(".tm-scheduler-month-event--allday:not(.tm-scheduler-event--tinted) .tm-scheduler-month-event-title",
            "the shared title colour paints theme text on the solid fill; the all-day title must inherit the chip's own");
        css.Should().Contain("color: inherit");
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

    /// <summary>
    /// B14: the month grid and its day headers start on the configured first day of the week.
    /// June 2025 starts on a Sunday, so a Sunday-first grid opens on 1 June (no leading May cells).
    /// </summary>
    [Fact]
    public void Month_Respects_FirstDayOfWeek_Sunday()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.FirstDayOfWeek, 0));

        var headers = cut.FindAll(".tm-scheduler-month-day-header");
        headers.Count.Should().Be(7);
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        headers[0].TextContent.Trim().Should().Be(culture.DateTimeFormat.AbbreviatedDayNames[(int)DayOfWeek.Sunday]);
        headers[6].TextContent.Trim().Should().Be(culture.DateTimeFormat.AbbreviatedDayNames[(int)DayOfWeek.Saturday]);

        var days = cut.FindAll(".tm-scheduler-month-day");
        days[0].ClassList.Should().NotContain("tm-scheduler-month-day--other");
        days[0].QuerySelector(".tm-scheduler-month-day-number")!.TextContent.Should().Be("1");
    }

    /// <summary>
    /// B24: "+N more" is a real button and raises OnMoreClick with the day, without bubbling into the day click.
    /// </summary>
    [Fact]
    public void Month_More_Is_Button_And_Invokes_OnMoreClick()
    {
        DateTime? moreDay = null;
        var slotClicked = false;
        var events = new List<TmScheduleEvent>
        {
            Evt("Evt1", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0)),
            Evt("Evt2", new DateTime(2025, 6, 10, 10, 0, 0), new DateTime(2025, 6, 10, 11, 0, 0)),
            Evt("Evt3", new DateTime(2025, 6, 10, 11, 0, 0), new DateTime(2025, 6, 10, 12, 0, 0)),
            Evt("Evt4", new DateTime(2025, 6, 10, 13, 0, 0), new DateTime(2025, 6, 10, 14, 0, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.MaxVisibleEvents, 3)
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, _ => slotClicked = true))
            .Add(c => c.OnMoreClick, EventCallback.Factory.Create<DateTime>(this, d => moreDay = d)));

        var more = cut.Find(".tm-scheduler-month-more");
        more.TagName.Should().Be("BUTTON");
        more.GetAttribute("type").Should().Be("button");
        more.Click();

        moreDay.Should().Be(new DateTime(2025, 6, 10));
        slotClicked.Should().BeFalse("the more button must not bubble into the day-cell click");
    }

    /// <summary>The overflow button names the day it opens, not just the count.</summary>
    [Fact]
    public void Month_More_AriaLabel_Names_The_Day()
    {
        var events = Enumerable.Range(0, 4).Select(i =>
            Evt($"Evt{i}", new DateTime(2025, 6, 10, 9 + i, 0, 0), new DateTime(2025, 6, 10, 10 + i, 0, 0))).ToList();

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.MaxVisibleEvents, 3));

        var label = cut.Find(".tm-scheduler-month-more").GetAttribute("aria-label") ?? string.Empty;
        label.Should().Contain("2025");
        label.Should().Contain("10");
    }

    /// <summary>
    /// A11y: the month is an ARIA grid — one row of column headers and six rows of grid cells.
    /// June 2025 is a Monday-first month, so the first cell is 26 May.
    /// </summary>
    [Fact]
    public void Month_Has_Grid_Rows_And_Gridcells()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        var grid = cut.Find("[role='grid']");
        grid.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var headers = cut.FindAll("[role='columnheader']");
        headers.Count.Should().Be(7);

        var rows = cut.FindAll("[role='row']");
        rows.Count.Should().Be(7, "one header row plus the six week rows");
        rows.Skip(1).Should().OnlyContain(r => r.QuerySelectorAll("[role='gridcell']").Length == 7);

        cut.FindAll("[role='gridcell']").Count.Should().Be(42);
        cut.Find("[role='gridcell'][data-date='2025-05-26']").Should().NotBeNull();
    }

    /// <summary>Roving tabindex: exactly one day cell is a tab stop, the rest are tabindex -1.</summary>
    [Fact]
    public void Month_Roving_Tabindex_Single_Tab_Stop()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        var cells = cut.FindAll("[role='gridcell']");
        cells.Count(c => c.GetAttribute("tabindex") == "0").Should().Be(1);
        cells.Count(c => c.GetAttribute("tabindex") == "-1").Should().Be(41);
    }

    /// <summary>
    /// Arrow keys move the roving stop: Right by a day, Down by a week, Home/End across the row,
    /// PageUp/PageDown by a month (the grid rebuilds around the new month).
    /// </summary>
    [Fact]
    public void Month_ArrowRight_Moves_Focus_To_Next_Day()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        ActiveCell(cut).GetAttribute("data-date").Should().Be("2025-06-15",
            "the roving stop opens on the displayed date, not the first of the month");

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        ActiveCell(cut).GetAttribute("data-date").Should().Be("2025-06-16");

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        ActiveCell(cut).GetAttribute("data-date").Should().Be("2025-06-23");

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "Home" });
        ActiveCell(cut).GetAttribute("data-date").Should().Be("2025-06-23");

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "End" });
        ActiveCell(cut).GetAttribute("data-date").Should().Be("2025-06-29");

        // PageDown asks the host for the next month, keeping the day number.
        var requested = new List<DateTime>();
        cut.Render(p => p
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, requested.Add)));
        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "PageDown" });
        requested.Should().ContainSingle().Which.Should().Be(new DateTime(2025, 7, 29));

        cut.Render(p => p.Add(c => c.CurrentDate, new DateTime(2025, 7, 15)));
        ActiveCell(cut).GetAttribute("data-date").Should().Be("2025-07-15");
        cut.FindAll("[role='gridcell']").Count.Should().Be(42);

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        requested.Should().HaveCount(2);
        requested[1].Should().Be(new DateTime(2025, 6, 15));
    }

    /// <summary>
    /// Enter on a day cell raises OnSlotClick exactly once. A held key (Repeat) must not re-fire,
    /// and Space activates on keyup, matching the native button split.
    /// </summary>
    [Fact]
    public void Month_Enter_On_Day_Invokes_OnSlotClick_Once()
    {
        var clicks = new List<(DateTime Start, DateTime End)>();
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, clicks.Add)));

        var cell = ActiveCell(cut);
        cell.KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        clicks.Should().BeEmpty("a held Enter must not re-activate");

        cell.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        clicks.Should().ContainSingle();
        clicks[0].Start.Should().Be(new DateTime(2025, 6, 15));

        cell.KeyDown(new KeyboardEventArgs { Key = " " });
        clicks.Should().ContainSingle("Space activates on keyup, not keydown");
        cell.KeyUp(new KeyboardEventArgs { Key = " " });
        clicks.Should().HaveCount(2);
    }

    /// <summary>
    /// PageUp/PageDown keep the day number across month lengths, and a host that does not bind the
    /// callback keeps the stop inside the rendered grid.
    /// </summary>
    [Fact]
    public void Month_PageKeys_Clamp_The_Day_And_Stay_Put_Without_A_Delegate()
    {
        var requested = new List<DateTime>();
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 12, 31))
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, requested.Add)));

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "PageDown" });
        requested.Should().ContainSingle().Which.Should().Be(new DateTime(2026, 1, 31));

        cut.Render(p => p.Add(c => c.CurrentDate, new DateTime(2026, 1, 31)));
        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "PageDown" });
        requested.Should().HaveCount(2);
        requested[1].Should().Be(new DateTime(2026, 2, 28), "February 2026 has 28 days");

        var clamped = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));
        ActiveCell(clamped).KeyDown(new KeyboardEventArgs { Key = "PageDown" });
        ActiveCell(clamped).GetAttribute("data-date").Should().Be("2025-07-06",
            "without a delegate the stop stays on the nearest day the rendered grid shows");
    }

    /// <summary>
    /// Shift+F10 raises the context menu exactly once, through the native contextmenu path, and the
    /// coordinates come from the element rather than from a synthetic zero point.
    /// </summary>
    [Fact]
    public void Month_ContextMenu_Fires_Once_With_Element_Coordinates()
    {
        var menus = new List<(double X, double Y)>();
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 15, 9, 0, 0), new DateTime(2025, 6, 15, 10, 0, 0))
        };
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.OnSlotContextMenu, EventCallback.Factory.Create<SchedulerSlotContext>(this, c => menus.Add((c.ClientX, c.ClientY))))
            .Add(c => c.OnEventContextMenu, EventCallback.Factory.Create<SchedulerEventContext>(this, c => menus.Add((c.ClientX, c.ClientY)))));

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "F10", ShiftKey = true });
        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "ContextMenu" });
        menus.Should().BeEmpty("the key itself never opens the menu; the native contextmenu event does");

        ActiveCell(cut).ContextMenu(new MouseEventArgs { ClientX = 40, ClientY = 60 });
        menus.Should().ContainSingle();
        menus[0].X.Should().BeGreaterThan(0);
        menus[0].Y.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Alt, Ctrl and Meta arrows are the browser's gestures, so the roving stop ignores them.
    /// </summary>
    [Fact]
    public void Month_Ignores_Modified_Arrows()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });
        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowRight", CtrlKey = true });
        ActiveCell(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowDown", MetaKey = true });
        ActiveCell(cut).GetAttribute("data-date").Should().Be("2025-06-15");
    }

    /// <summary>
    /// The grid has one tab stop: events and the overflow button are reachable only inside the
    /// active cell, so Tab from that cell enters them and Tab elsewhere skips them.
    /// </summary>
    [Fact]
    public void Month_Events_Are_Tab_Stops_Only_Inside_The_Active_Cell()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 15, 9, 0, 0), new DateTime(2025, 6, 15, 10, 0, 0)),
            Evt("Review", new DateTime(2025, 6, 16, 14, 0, 0), new DateTime(2025, 6, 16, 15, 0, 0))
        };
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var active = ActiveCell(cut);
        active.QuerySelectorAll("[role='button'][tabindex='0']").Length.Should().Be(1);
        cut.Find("[data-date='2025-06-16']").QuerySelectorAll("[role='button'][tabindex='0']").Length.Should().Be(0);
        cut.FindAll("[tabindex='0']").Count.Should().Be(2, "the active cell plus its one event");
    }

    /// <summary>
    /// Today carries aria-current, and a cell's name is the long date plus the plural event count.
    /// </summary>
    [Fact]
    public void Month_Day_Label_Names_The_Date_And_The_Event_Count()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 15, 9, 0, 0), new DateTime(2025, 6, 15, 10, 0, 0)),
            Evt("Review", new DateTime(2025, 6, 15, 14, 0, 0), new DateTime(2025, 6, 15, 15, 0, 0))
        };
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Today, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var today = cut.Find("[data-date='2025-06-15']");
        today.GetAttribute("aria-current").Should().Be("date");
        today.GetAttribute("aria-label").Should().Contain("2025").And.Contain("2 events");

        cut.Find("[data-date='2025-06-16']").GetAttribute("aria-label").Should().Contain("no events");
        cut.Find("[role='grid']").GetAttribute("aria-describedby").Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A templated event carries no aria-label of its own; the time is a screen-reader prefix so the
    /// template's own text stays the visible name.
    /// </summary>
    [Fact]
    public void Month_Templated_Event_Has_A_Screen_Reader_Prefix_Instead_Of_An_Aria_Label()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 15, 9, 0, 0), new DateTime(2025, 6, 15, 10, 0, 0))
        };
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.EventTemplate, e => builder => builder.AddContent(0, e.Title)));

        var item = cut.Find(".tm-scheduler-month-event");
        item.GetAttribute("aria-label").Should().BeNull();
        item.QuerySelector(".tm-sr-only").Should().NotBeNull();
        item.InnerHtml.Should().Contain("Standup");
    }

    /// <summary>
    /// The selected day is a state, not the roving stop: aria-selected plus the selected class, and
    /// clicking the day raises SelectedDateChanged with that day.
    /// </summary>
    [Fact]
    public void SelectedDate_Marks_The_Day_And_A_Click_Selects_It()
    {
        DateTime? selected = null;
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.SelectedDate, new DateTime(2025, 6, 15))
            .Add(c => c.SelectedDateChanged, EventCallback.Factory.Create<DateTime?>(this, d => selected = d)));

        var day = cut.Find("[data-date='2025-06-15']");
        day.GetAttribute("aria-selected").Should().Be("true");
        day.ClassList.Should().Contain("tm-scheduler-month-day--selected");
        cut.FindAll("[aria-selected='true']").Should().ContainSingle();

        cut.Find("[data-date='2025-06-18']").Click();
        selected.Should().Be(new DateTime(2025, 6, 18));
    }

    /// <summary>The selected event carries aria-selected and the selected class; a click raises both callbacks.</summary>
    [Fact]
    public void SelectedEvent_Marks_The_Event()
    {
        var standup = Evt("Standup", new DateTime(2025, 6, 15, 9, 0, 0), new DateTime(2025, 6, 15, 10, 0, 0));
        standup.Id = "standup";
        var review = Evt("Review", new DateTime(2025, 6, 15, 11, 0, 0), new DateTime(2025, 6, 15, 12, 0, 0));
        review.Id = "review";
        TmScheduleEvent? clicked = null;
        TmScheduleEvent? selected = null;

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, new List<TmScheduleEvent> { standup, review })
            .Add(c => c.SelectedEvent, standup)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e))
            .Add(c => c.SelectedEventChanged, EventCallback.Factory.Create<TmScheduleEvent?>(this, e => selected = e)));

        var marked = cut.Find(".tm-scheduler-month-event--selected");
        marked.GetAttribute("aria-current").Should().Be("true");
        marked.TextContent.Should().Contain("Standup");
        cut.FindAll("[aria-current='true'].tm-scheduler-month-event").Should().ContainSingle();

        cut.FindAll(".tm-scheduler-month-event").Single(e => e.TextContent.Contains("Review")).Click();
        clicked!.Id.Should().Be("review");
        selected!.Id.Should().Be("review");
    }

    private static AngleSharp.Dom.IElement ActiveCell(IRenderedComponent<TmSchedulerMonthView> cut)
        => cut.Find("[role='gridcell'][tabindex='0']");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
