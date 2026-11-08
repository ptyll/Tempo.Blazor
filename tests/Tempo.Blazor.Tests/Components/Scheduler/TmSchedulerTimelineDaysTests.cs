using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The day-granularity timeline: resource rows × the days of the range, one chip per event with its
/// time, lanes for an overlap inside one day, the "+N more" cap, and the same move contract the hour
/// timeline offers (HTML5 drop, the touch commit and the Alt+Arrow alternative).
/// </summary>
public class TmSchedulerTimelineDaysTests : LocalizationTestBase
{
    private static readonly DateTime Wednesday = new(2026, 6, 10);
    private const string MondayIso = "2026-06-08";
    private const string TuesdayIso = "2026-06-09";
    private const string WednesdayIso = "2026-06-10";

    private static readonly List<TmScheduleResource> Channels =
    [
        new() { Id = "web", Name = "Web" },
        new() { Id = "mail", Name = "E-mail" }
    ];

    private static TmScheduleEvent Evt(
        string id, string title, int day, int startHour, int endHour, string resourceId = "web", bool allDay = false)
    {
        var start = new DateTime(2026, 6, day, startHour, 0, 0);
        return new TmScheduleEvent
        {
            Id = id,
            Title = title,
            Start = start,
            // An all-day event covers the whole day, so its end is the next midnight.
            End = allDay ? start.AddDays(1) : new DateTime(2026, 6, day, endHour, 0, 0),
            ResourceId = resourceId,
            AllDay = allDay
        };
    }

    private IRenderedComponent<TmSchedulerTimelineView> RenderDays(
        Action<ComponentParameterCollectionBuilder<TmSchedulerTimelineView>>? configure = null)
    {
        return Render<TmSchedulerTimelineView>(p =>
        {
            p.Add(c => c.CurrentDate, Wednesday)
                .Add(c => c.Scale, TmSchedulerTimelineScale.Days)
                .Add(c => c.Resources, Channels);
            configure?.Invoke(p);
        });
    }

    // ── Columns ──

    [Fact]
    public void Day_Scale_Renders_One_Column_Per_Week_Day()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays();

        cut.Find(".tm-scheduler-timeline").GetAttribute("data-scale").Should().Be("days");
        cut.FindAll(".tm-scheduler-timeline-day-header").Should().HaveCount(7);
        cut.FindAll(".tm-scheduler-timeline-day-header").Select(h => h.GetAttribute("data-date"))
            .Should().Equal("2026-06-08", "2026-06-09", "2026-06-10", "2026-06-11", "2026-06-12", "2026-06-13", "2026-06-14");
        cut.Find(".tm-scheduler-timeline-day-header").TextContent.Should()
            .Contain("Mon").And.Contain("6/8");
    }

    [Fact]
    public void Day_Columns_Start_On_FirstDayOfWeek()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p.Add(c => c.FirstDayOfWeek, 0));

        cut.FindAll(".tm-scheduler-timeline-day-header")[0].GetAttribute("data-date")
            .Should().Be("2026-06-07", "Sunday starts the week");
        cut.FindAll(".tm-scheduler-timeline-day-header").Should().HaveCount(7);
    }

    [Fact]
    public void WorkWeek_Hides_The_Weekend()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p.Add(c => c.WeekDays, TmSchedulerWeekDays.WorkWeek));

        cut.FindAll(".tm-scheduler-timeline-day-header").Should().HaveCount(5);
        cut.FindAll(".tm-scheduler-timeline-day-header").Select(h => h.GetAttribute("data-date"))
            .Should().NotContain("2026-06-13").And.NotContain("2026-06-14");
        cut.FindAll(".tm-scheduler-timeline-row").Should().HaveCount(2);
    }

    [Fact]
    public void Rows_Are_Resources_With_A_Sticky_Label()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays();

        var rows = cut.FindAll(".tm-scheduler-timeline-row");
        rows.Should().HaveCount(2);
        rows[0].GetAttribute("data-resource").Should().Be("web");
        rows[0].QuerySelector(".tm-scheduler-timeline-resource-label").TextContent.Should().Contain("Web");
        rows[0].QuerySelector(".tm-scheduler-timeline-resource-label")
            .ClassList.Should().Contain("tm-scheduler-timeline-resource-label--sticky");
        cut.Find(".tm-scheduler-timeline-resource-header")
            .ClassList.Should().Contain("tm-scheduler-timeline-resource-header--sticky");
    }

    [Fact]
    public void Day_Cells_Are_Grid_Cells_With_A_Roving_Stop_And_A_Day_Name()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays();

        var grid = cut.Find(".tm-scheduler-timeline-body");
        grid.GetAttribute("role").Should().Be("grid");
        grid.GetAttribute("aria-label").Should().NotBeNullOrEmpty();

        var cells = cut.FindAll(".tm-scheduler-timeline-day-cell");
        cells.Should().HaveCount(14, "two resource rows of seven days");
        cells.Should().OnlyContain(c => c.GetAttribute("role") == "gridcell");
        cells.Count(c => c.GetAttribute("tabindex") == "0").Should().Be(1, "one roving tab stop");
        cells[0].GetAttribute("aria-label").Should().Contain("Monday").And.Contain("no events");
    }

    // ── Events in a cell ──

    [Fact]
    public void An_Event_Paints_In_Its_Own_Day_Cell_With_Its_Time()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { Evt("e1", "Launch", 10, 9, 10) }));

        var cell = cut.Find($".tm-scheduler-timeline-day-cell[data-date='{WednesdayIso}'][data-resource='web']");
        var chips = cell.QuerySelectorAll(".tm-scheduler-timeline-day-event");
        chips.Should().HaveCount(1);
        chips[0].TextContent.Should().Contain("9:00").And.Contain("Launch");
        chips[0].GetAttribute("data-event-id").Should().Be("e1");
        chips[0].GetAttribute("role").Should().Be("button");

        cut.FindAll(".tm-scheduler-timeline-day-event").Should().HaveCount(1, "the event paints on one day only");
    }

    [Fact]
    public void All_Day_Event_Prints_The_All_Day_Word()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { Evt("e1", "Campaign", 10, 0, 0, allDay: true) }));

        cut.Find(".tm-scheduler-timeline-day-event").TextContent.Should()
            .Contain("All day", "an all-day event has no time of day").And.Contain("Campaign");
    }

    [Fact]
    public void A_Multi_Day_Event_Paints_On_Every_Day_It_Covers()
    {
        using var _ = CultureScope.Use("en-US");
        var spanning = new TmScheduleEvent
        {
            Id = "e1",
            Title = "Sale",
            Start = new DateTime(2026, 6, 9, 22, 0, 0),
            End = new DateTime(2026, 6, 11, 2, 0, 0),
            ResourceId = "web"
        };

        var cut = RenderDays(p => p.Add(c => c.Events, new List<TmScheduleEvent> { spanning }));

        cut.FindAll(".tm-scheduler-timeline-day-event").Should().HaveCount(3);
        cut.Find($".tm-scheduler-timeline-day-cell[data-date='{TuesdayIso}'] .tm-scheduler-timeline-day-event")
            .ClassList.Should().NotContain("tm-scheduler-timeline-day-event--continuation");
        cut.Find($".tm-scheduler-timeline-day-cell[data-date='{WednesdayIso}'] .tm-scheduler-timeline-day-event")
            .ClassList.Should().Contain("tm-scheduler-timeline-day-event--continuation");
    }

    [Fact]
    public void EventTemplate_Replaces_The_Chip_Content()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { Evt("e1", "Launch", 10, 9, 10) })
            .Add(c => c.EventTemplate, (RenderFragment<TmScheduleEvent>)(e => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "host-chip");
                builder.AddAttribute(2, "data-testid", "host-chip");
                builder.AddContent(3, e.Title.ToUpperInvariant());
                builder.CloseElement();
            })));

        var chip = cut.Find(".tm-scheduler-timeline-day-event");
        chip.QuerySelector(".host-chip").Should().NotBeNull();
        chip.QuerySelector(".tm-scheduler-timeline-event-title").Should().BeNull();
        chip.QuerySelector(".tm-sr-only").TextContent.Should().Contain("Launch", "the accessible name survives the template");
    }

    [Fact]
    public void The_Time_Offers_A_Full_And_A_Compact_Reading_And_A_Title()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { Evt("e1", "Launch", 10, 9, 10) }));

        // "9:00 AM" is one unbreakable piece (U+00A0 between the time and its meridiem); only the
        // plain spaces around the en dash are break opportunities.
        const string nbsp = "\u00A0";
        var time = cut.Find(".tm-scheduler-timeline-day-event .tm-scheduler-timeline-event-time");
        time.GetAttribute("title").Should().Be($"9:00{nbsp}AM – 10:00{nbsp}AM");
        time.QuerySelector(".tm-scheduler-event-time-full").TextContent.Should()
            .Be($"9:00{nbsp}AM – 10:00{nbsp}AM");
        time.QuerySelector(".tm-scheduler-event-time-compact").TextContent.Should()
            .Be($"9:00 – 10:00{nbsp}AM");
    }

    [Fact]
    public void Overlapping_Events_In_One_Day_Take_Lanes()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                Evt("e1", "One", 10, 9, 11),
                Evt("e2", "Two", 10, 10, 12)
            }));

        var cell = cut.Find($".tm-scheduler-timeline-day-cell[data-date='{WednesdayIso}']");
        cell.QuerySelector(".tm-scheduler-timeline-day-cell-events")
            .GetAttribute("style").Should().Contain("--tm-scheduler-lanes: 2");

        var chips = cell.QuerySelectorAll(".tm-scheduler-timeline-day-event");
        chips.Should().HaveCount(2);
        chips.Select(c => c.GetAttribute("data-lane")).Should().Equal("0", "1");
        chips[0].GetAttribute("style").Should().Contain("grid-column: 1 / span 1").And.Contain("grid-row: 1");
        chips[1].GetAttribute("style").Should().Contain("grid-column: 2 / span 1");
    }

    [Fact]
    public void A_Capped_Cell_Offers_The_Plural_More_Button()
    {
        using var _ = CultureScope.Use("en-US");
        DateTime? opened = null;
        var events = Enumerable.Range(0, 5)
            .Select(i => Evt($"e{i}", $"Slot {i}", 10, 8 + i, 9 + i))
            .ToList();

        var cut = RenderDays(p => p
            .Add(c => c.Events, events)
            .Add(c => c.MaxVisibleEvents, 3)
            .Add(c => c.OnMoreClick, EventCallback.Factory.Create<DateTime>(this, d => opened = d)));

        var cell = cut.Find($".tm-scheduler-timeline-day-cell[data-date='{WednesdayIso}']");
        cell.QuerySelectorAll(".tm-scheduler-timeline-day-event").Should().HaveCount(3);

        var more = cell.QuerySelector(".tm-scheduler-timeline-day-more");
        more.Should().NotBeNull();
        more!.TextContent.Should().Be("+2 more");
        more.GetAttribute("aria-label").Should().Contain("2 more events");

        more.Click();
        opened.Should().Be(new DateTime(2026, 6, 10));
    }

    [Fact]
    public void The_More_Button_Fences_Its_Keys_Off_The_Cell()
    {
        using var _ = CultureScope.Use("en-US");
        var events = Enumerable.Range(0, 4)
            .Select(i => Evt($"e{i}", $"Slot {i}", 10, 8 + i, 9 + i))
            .ToList();

        var cut = RenderDays(p => p
            .Add(c => c.Events, events)
            .Add(c => c.MaxVisibleEvents, 2));

        // The button is a native control inside the emulating cell: Enter on it must open the day
        // once, not also activate the cell behind it.
        cut.Find(".tm-scheduler-timeline-day-more").OuterHtml.Should()
            .Contain("blazor:onkeydown:stoppropagation")
            .And.Contain("blazor:onkeyup:stoppropagation");
    }

    // ── Move contract ──

    [Fact]
    public void Chips_Advertise_The_Keyboard_Move_Shortcuts()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { Evt("e1", "Launch", 10, 9, 10) }));

        var chip = cut.Find(".tm-scheduler-timeline-day-event");
        chip.GetAttribute("draggable").Should().Be("true");
        chip.GetAttribute("aria-keyshortcuts").Should()
            .Contain("Alt+ArrowRight").And.Contain("Alt+ArrowDown").And.Contain("Alt+Shift+ArrowRight");
        chip.GetAttribute("aria-describedby").Should().NotBeNullOrEmpty();
        cut.Find(".tm-sr-only").TextContent.Should().Contain("Alt+Left", "the day-scale hint words its own chords");
    }

    [Fact]
    public void ReadOnly_Chip_Is_Not_A_Move_Target()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.ReadOnly, true)
            .Add(c => c.Events, new List<TmScheduleEvent> { Evt("e1", "Launch", 10, 9, 10) }));

        var chip = cut.Find(".tm-scheduler-timeline-day-event");
        chip.GetAttribute("draggable").Should().Be("false");
        chip.GetAttribute("aria-keyshortcuts").Should().BeNull();
    }

    [Fact]
    public void Drop_On_Another_Day_And_Row_Keeps_The_Time_Of_Day()
    {
        using var _ = CultureScope.Use("en-US");
        TmScheduleEvent? changed = null;
        var evt = Evt("e1", "Launch", 10, 9, 10);
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { evt })
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, e => changed = e)));

        cut.Find(".tm-scheduler-timeline-day-event").DragStart();
        cut.Find($".tm-scheduler-timeline-day-cell[data-date='{TuesdayIso}'][data-resource='mail']").Drop();

        changed.Should().NotBeNull();
        changed!.StartLocal.Should().Be(new DateTime(2026, 6, 9, 9, 0, 0), "the hour stays, the day moves");
        changed.EndLocal.Should().Be(new DateTime(2026, 6, 9, 10, 0, 0));
        changed.ResourceId.Should().Be("mail");
    }

    [Fact]
    public async Task Touch_Commit_With_No_Minute_Axis_Keeps_The_Time_Of_Day()
    {
        using var _ = CultureScope.Use("en-US");
        TmScheduleEvent? changed = null;
        var evt = Evt("e1", "Launch", 10, 9, 10);
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { evt })
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, e => changed = e)));

        await cut.InvokeAsync(() => cut.Instance.NotifyMoveComplete("e1", "2026-06-12", -1, "mail"));

        changed.Should().NotBeNull();
        changed!.StartLocal.Should().Be(new DateTime(2026, 6, 12, 9, 0, 0));
        changed.ResourceId.Should().Be("mail");
    }

    [Fact]
    public async Task Alt_Arrow_Moves_A_Day_And_Alt_Up_Moves_A_Resource()
    {
        using var _ = CultureScope.Use("en-US");
        string? announced = null;
        var evt = Evt("e1", "Launch", 10, 9, 10);
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { evt })
            .Add(c => c.OnAnnounced, EventCallback.Factory.Create<string>(this, s => announced = s)));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 1, 0, false));
        evt.StartLocal.Should().Be(new DateTime(2026, 6, 11, 9, 0, 0), "Alt+Right moves one day");
        announced.Should().Contain("Launch");

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, 1, false));
        evt.ResourceId.Should().Be("mail", "Alt+Down moves to the next resource");
        evt.StartLocal.Should().Be(new DateTime(2026, 6, 11, 9, 0, 0), "the day does not move");
    }

    [Fact]
    public async Task Alt_Shift_Arrow_Resizes_The_End_By_A_Day()
    {
        using var _ = CultureScope.Use("en-US");
        var evt = Evt("e1", "Launch", 10, 9, 10);
        var cut = RenderDays(p => p.Add(c => c.Events, new List<TmScheduleEvent> { evt }));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 1, 0, true));

        evt.StartLocal.Should().Be(new DateTime(2026, 6, 10, 9, 0, 0), "the start is not what a resize moves");
        evt.EndLocal.Should().Be(new DateTime(2026, 6, 11, 10, 0, 0), "the end grows by a day");
    }

    [Fact]
    public void A_Day_Cell_Click_Raises_The_Slot_Callback_For_The_Whole_Day()
    {
        using var _ = CultureScope.Use("en-US");
        (DateTime Start, DateTime End)? slot = null;
        var cut = RenderDays(p => p
            .Add(c => c.OnSlotClick, EventCallback.Factory.Create<(DateTime, DateTime)>(this, s => slot = s)));

        cut.Find($".tm-scheduler-timeline-day-cell[data-date='{WednesdayIso}']").Click();

        slot.Should().NotBeNull();
        slot!.Value.Start.Should().Be(new DateTime(2026, 6, 10));
        slot.Value.End.Should().Be(new DateTime(2026, 6, 10, 23, 59, 59));
    }

    // ── The hour scale is untouched ──

    [Fact]
    public void The_Day_Board_Scrolls_Inside_A_Frame_That_Owns_The_Edge_Shadows()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays();

        // The shadows are painted by the frame, never by the scroller itself (an inset shadow would
        // sit under the rows). scheduler.js sets data-scroll-shadow only while the board overflows.
        var frame = cut.Find(".tm-scheduler-timeline-day-frame");
        frame.GetAttribute("data-tm-scroll-frame").Should().Be("true");
        frame.QuerySelector("[data-tm-scroll='timeline-days']").Should().NotBeNull();
        frame.QuerySelector(".tm-scheduler-timeline-day-frame").Should().BeNull("the frame is not nested");

        // The hour timeline has no frame: it has no scroll-edge contract.
        var hour = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.Resources, Channels));
        hour.FindAll(".tm-scheduler-timeline-day-frame").Should().BeEmpty();
    }

    [Fact]
    public void Hour_Timeline_Does_Not_Re_Scroll_When_The_Layout_Changes()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.Resources, Channels)
            .Add(c => c.ScrollToTime, new TimeSpan(8, 0, 0))
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        int Scrolls() => JSInterop.Invocations.Count(i => i.Identifier == "TempoBlazor.Scheduler.scrollToTime");
        Scrolls().Should().Be(1, "the hour timeline scrolls once, exactly as it did before the day scale");

        cut.Render(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));
        Scrolls().Should().Be(1, "a resolved layout must not re-scroll the HOUR timeline");

        cut.Render(p => p.Add(c => c.ScrollToTime, new TimeSpan(9, 0, 0)));
        Scrolls().Should().Be(2, "a new ScrollToTime is still a new scroll");
    }

    [Fact]
    public void Day_Board_Re_Applies_The_Scroll_When_The_Layout_Resolves()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p
            .Add(c => c.ScrollToTime, new TimeSpan(8, 0, 0))
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        int Scrolls() => JSInterop.Invocations.Count(i => i.Identifier == "TempoBlazor.Scheduler.scrollToTime");
        Scrolls().Should().Be(1);

        // The first render can precede the container measurement, and a board that did not overflow
        // then clamped the scroll: the day board re-applies it once the layout is known.
        cut.Render(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));
        Scrolls().Should().Be(2);
    }

    [Fact]
    public void Hour_Scale_Stays_The_Hour_Timeline()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.Resources, Channels));

        cut.FindAll(".tm-scheduler-timeline-day-header").Should().BeEmpty();
        cut.FindAll(".tm-scheduler-timeline-time-header").Should().NotBeEmpty();
        cut.Find(".tm-scheduler-timeline").GetAttribute("data-scale").Should().BeNull();
    }

    // ── One tab stop per grid ──

    [Fact]
    public void The_Day_Grid_Has_Exactly_One_Tab_Stop()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p.Add(c => c.Events, new List<TmScheduleEvent>
        {
            Evt("e1", "Launch", 10, 9, 10),
            Evt("e2", "Follow-up", 10, 10, 11),
            Evt("e3", "Mail", 10, 9, 10, "mail")
        }));

        // Two resources × seven days of cells, three chips, and still one stop: the active cell.
        cut.FindAll(".tm-scheduler-timeline--days [tabindex='0']").Should().HaveCount(1);
        cut.Find(".tm-scheduler-timeline-day-cell[tabindex='0']").Should().NotBeNull();
        cut.FindAll(".tm-scheduler-timeline-day-event").Select(c => c.GetAttribute("tabindex"))
            .Should().OnlyContain(t => t == "-1");
    }

    [Fact]
    public void Enter_Or_F2_On_A_Cell_Steps_Onto_Its_First_Chip()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p.Add(c => c.Events, new List<TmScheduleEvent>
        {
            Evt("e1", "Launch", 10, 9, 10)
        }));

        var cell = cut.Find(".tm-scheduler-timeline-day-cell[data-date='2026-06-10'][data-resource='web']");
        cell.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        // The chip is the grid's stop now and the cell gave its own up; the focus moved there.
        cut.Find(".tm-scheduler-timeline-day-event").GetAttribute("tabindex").Should().Be("0");
        cut.FindAll(".tm-scheduler-timeline--days [tabindex='0']").Should().HaveCount(1);
        JSInterop.Invocations.Should().Contain(i =>
            i.Identifier == "TempoBlazor.Scheduler.focusRovingStop"
            && i.Arguments[1] as string == "[data-event-id=\"e1\"]");

        // Escape hands the stop back to the cell.
        cut.Find(".tm-scheduler-timeline-day-event").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.Find(".tm-scheduler-timeline-day-cell[data-date='2026-06-10'][data-resource='web']")
            .GetAttribute("tabindex").Should().Be("0");
        cut.FindAll(".tm-scheduler-timeline--days [tabindex='0']").Should().HaveCount(1);
    }

    [Fact]
    public void F2_Enters_A_Cell_The_Same_Way_Enter_Does()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = RenderDays(p => p.Add(c => c.Events, new List<TmScheduleEvent>
        {
            Evt("e1", "Launch", 10, 9, 10)
        }));

        cut.Find(".tm-scheduler-timeline-day-cell[data-date='2026-06-10']")
            .KeyDown(new KeyboardEventArgs { Key = "F2" });

        cut.Find(".tm-scheduler-timeline-day-event").GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void ArrowDown_From_The_Last_Chip_Reaches_The_More_Button()
    {
        using var _ = CultureScope.Use("en-US");
        var events = Enumerable.Range(0, 4)
            .Select(i => Evt($"e{i}", $"Slot {i}", 10, 8 + i, 9 + i))
            .ToList();
        var cut = RenderDays(p => p
            .Add(c => c.Events, events)
            .Add(c => c.MaxVisibleEvents, 2));

        var cell = cut.Find(".tm-scheduler-timeline-day-cell[data-date='2026-06-10'][data-resource='web']");
        cell.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find($"[data-event-id='e0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find($"[data-event-id='e1']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var more = cut.Find(".tm-scheduler-timeline-day-more");
        more.GetAttribute("tabindex").Should().Be("0", "the capped cell's only other widget is its More button");
        cut.FindAll(".tm-scheduler-timeline--days [tabindex='0']").Should().HaveCount(1);
        JSInterop.Invocations.Should().Contain(i =>
            i.Identifier == "TempoBlazor.Scheduler.focusRovingStop"
            && i.Arguments[1] as string == ".tm-scheduler-timeline-day-more[data-cell=\"web|2026-06-10\"]");
    }

    [Fact]
    public void The_More_Button_Is_Not_A_Tab_Stop_Of_Its_Own()
    {
        using var _ = CultureScope.Use("en-US");
        var events = Enumerable.Range(0, 4)
            .Select(i => Evt($"e{i}", $"Slot {i}", 10, 8 + i, 9 + i))
            .ToList();
        var cut = RenderDays(p => p
            .Add(c => c.Events, events)
            .Add(c => c.MaxVisibleEvents, 2));

        cut.Find(".tm-scheduler-timeline-day-more").GetAttribute("tabindex").Should().Be("-1");
        cut.FindAll(".tm-scheduler-timeline--days [tabindex='0']").Should().HaveCount(1);
    }

    // ── Multi-day continuation ──

    [Fact]
    public void Dragging_A_Continuation_Chip_Keeps_The_Multi_Day_Offset()
    {
        using var _ = CultureScope.Use("en-US");
        TmScheduleEvent? changed = null;
        var campaign = new TmScheduleEvent
        {
            Id = "c4",
            Title = "Always-on campaign",
            Start = new DateTime(2026, 6, 9, 9, 0, 0),
            End = new DateTime(2026, 6, 11, 10, 0, 0),
            ResourceId = "web"
        };
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { campaign })
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, e => changed = e)));

        // The Wednesday chip is a continuation: the event started on Tuesday. Dropping it on Thursday
        // moves the WHOLE span by one day; it must not snap the start onto the dropped day.
        var grabbed = cut.Find(
            ".tm-scheduler-timeline-day-cell[data-date='2026-06-10'] .tm-scheduler-timeline-day-event");
        grabbed.ClassList.Should().Contain("tm-scheduler-timeline-day-event--continuation");
        grabbed.DragStart();
        cut.Find(".tm-scheduler-timeline-day-cell[data-date='2026-06-11']").Drop();

        changed.Should().NotBeNull();
        changed!.StartLocal.Should().Be(new DateTime(2026, 6, 10, 9, 0, 0), "the span moved by the day the pointer crossed");
        changed.EndLocal.Should().Be(new DateTime(2026, 6, 12, 10, 0, 0), "and kept its length");
    }

    [Fact]
    public async Task Touch_Commit_Carries_The_Grabbed_Day_For_A_Continuation_Chip()
    {
        using var _ = CultureScope.Use("en-US");
        TmScheduleEvent? changed = null;
        var campaign = new TmScheduleEvent
        {
            Id = "c4",
            Title = "Always-on campaign",
            Start = new DateTime(2026, 6, 9, 9, 0, 0),
            End = new DateTime(2026, 6, 11, 10, 0, 0),
            ResourceId = "web"
        };
        var cut = RenderDays(p => p
            .Add(c => c.Events, new List<TmScheduleEvent> { campaign })
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, e => changed = e)));

        // scheduler.js sends the day the finger grabbed the chip in as the fifth argument.
        await cut.InvokeAsync(() => cut.Instance.NotifyMoveComplete(
            "c4", "2026-06-11", -1, "mail", "2026-06-10"));

        changed.Should().NotBeNull();
        changed!.StartLocal.Should().Be(new DateTime(2026, 6, 10, 9, 0, 0));
        changed.EndLocal.Should().Be(new DateTime(2026, 6, 12, 10, 0, 0));
        changed.ResourceId.Should().Be("mail");
    }

    [Fact]
    public void Today_Marks_Its_Column_When_The_Week_Contains_It()
    {
        using var _ = CultureScope.Use("en-US");
        var today = DateOnly.FromDateTime(Wednesday);
        var cut = RenderDays(p => p.Add(c => c.Today, today));

        var cell = cut.Find($".tm-scheduler-timeline-day-cell[data-date='{WednesdayIso}']");
        cell.GetAttribute("aria-current").Should().Be("date");
        cell.ClassList.Should().Contain("tm-scheduler-timeline-day-cell--today");
    }
}
