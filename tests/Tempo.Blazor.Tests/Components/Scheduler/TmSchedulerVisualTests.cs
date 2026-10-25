using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Desktop visual unification (phase 3, layer A). Written RED against the pre-unification markup
/// and stylesheets: sentence-case headers, chevron navigation, tinted event cards, an Agenda
/// empty state, and the carried-forward slot-menu / ellipsis defects.
/// </summary>
public class TmSchedulerVisualTests : LocalizationTestBase
{
    private static readonly Regex Uppercase = new(@"text-transform\s*:\s*uppercase", RegexOptions.Compiled);
    private static readonly Regex HexOrWhite = new(@"#[0-9a-fA-F]{3,8}\b|:\s*white\b|rgba?\(", RegexOptions.Compiled);

    private static string Css(string file) => ThemeCss.ComponentCss(file);

    private static TmScheduleEvent Evt(string title, DateTime start, DateTime end, bool allDay = false, string? color = null) =>
        new() { Title = title, Start = start, End = end, AllDay = allDay, Color = color };

    [Fact]
    public void Toolbar_Nav_Uses_Icons_And_Keeps_The_TestIds()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.ShowPrint, true));

        var prev = cut.Find("[data-testid='scheduler-prev']");
        var next = cut.Find("[data-testid='scheduler-next']");
        prev.InnerHtml.Should().Contain("svg", "the previous control is a chevron, not a character");
        next.InnerHtml.Should().Contain("svg");
        prev.TextContent.Should().NotContain("‹");
        next.TextContent.Should().NotContain("›");

        var print = cut.Find("[data-testid='scheduler-print']");
        print.InnerHtml.Should().Contain("svg", "print is an icon button; the label stays in the accessible name");
        print.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        cut.Find(".tm-scheduler-toolbar-views [role='radiogroup']").Should().NotBeNull();
    }

    [Fact]
    public void Headers_Are_Sentence_Case()
    {
        Uppercase.IsMatch(Css("_scheduler-month.css")).Should().BeFalse("weekday headers are sentence case");
        Uppercase.IsMatch(Css("_scheduler-timegrid.css")).Should().BeFalse("column headers are sentence case");
        Uppercase.IsMatch(Css("_scheduler-agenda.css")).Should().BeFalse("the agenda weekday is sentence case");
    }

    [Fact]
    public void Event_Surface_Is_One_Rule()
    {
        var surface = Rule(Css("_scheduler.css"), ".tm-scheduler-event-surface");
        surface.Should().Contain("color-mix(in srgb, var(--event-color, var(--tm-color-primary))");
        surface.Should().Contain("var(--tm-scheduler-event-tint)");
        foreach (var file in new[] { "_scheduler-month.css", "_scheduler-timegrid.css", "_scheduler-timeline.css" })
            Count(Css(file), "color-mix(in srgb, var(--event-color").Should().BeLessThanOrEqualTo(1,
                $"{file} keeps geometry; the surface mix lives in the shared rule");
    }

    [Fact]
    public void Timed_Events_Use_The_Shared_Surface_In_Every_View()
    {
        var events = new List<TmScheduleEvent>
        {
            new() { Title = "Standup", ResourceId = "room", Start = new DateTime(2026, 5, 12, 9, 0, 0), End = new DateTime(2026, 5, 12, 10, 0, 0) }
        };
        Render<TmSchedulerMonthView>(p => p.Add(c => c.CurrentDate, new DateTime(2026, 5, 12)).Add(c => c.Events, events))
            .Find(".tm-scheduler-month-event").ClassList.Should().Contain("tm-scheduler-event-surface");
        Render<TmSchedulerTimeGrid>(p => p.Add(c => c.Dates, new[] { new DateOnly(2026, 5, 12) }).Add(c => c.Events, events))
            .Find(".tm-scheduler-timegrid-event").ClassList.Should().Contain("tm-scheduler-event-surface");
        Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 5, 12))
            .Add(c => c.Events, events)
            .Add(c => c.Resources, new List<TmScheduleResource> { new() { Id = "room", Name = "Room" } }))
            .Find(".tm-scheduler-timeline-event").ClassList.Should().Contain("tm-scheduler-event-surface");
    }

    [Fact]
    public void Month_Event_Is_A_Tinted_Card()
    {
        var month = Css("_scheduler-month.css");
        var rule = Rule(month, ".tm-scheduler-month-event");
        rule.Should().Contain("border-radius: var(--tm-radius-sm)");
        rule.Should().NotContain("color-mix");
        rule.Should().NotContain("border-left: 3px");
    }

    [Fact]
    public void Agenda_Empty_State_Uses_The_Shared_Empty_State()
    {
        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1)));

        var empty = cut.Find(".tm-scheduler-agenda-empty");
        empty.InnerHtml.Should().Contain("tm-empty-state");
        empty.TextContent.Should().Contain("Nothing scheduled");
        empty.TextContent.Should().Contain("This range has no events.");
    }

    [Fact]
    public void Agenda_Group_Keeps_The_Date_Classes_And_Reads_As_A_Row()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 9, 30, 0))
        };
        var cut = Render<TmSchedulerAgendaView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 1))
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-agenda-date").Should().NotBeNull();
        cut.Find(".tm-scheduler-agenda-date-day").Should().NotBeNull();
        cut.Find(".tm-scheduler-agenda-date-weekday").Should().NotBeNull();
        cut.Find(".tm-scheduler-agenda-date-month").Should().NotBeNull();

        var agenda = Css("_scheduler-agenda.css");
        Rule(agenda, ".tm-scheduler-agenda-group-body").Should().Contain("align-items: start");
        Rule(agenda, ".tm-scheduler-agenda-event").Should().Contain("align-items: center");
    }

    [Fact]
    public void TimeGrid_Scrolls_Inside_A_Bounded_Host_Without_Shrinking_The_Day_Track()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, new[] { new DateOnly(2026, 10, 25) })
            .Add(c => c.SlotDuration, 30));

        cut.Find(".tm-scheduler-timegrid-scroll").Should().NotBeNull();
        var body = cut.Find(".tm-scheduler-timegrid-scroll .tm-scheduler-timegrid-body");
        body.Should().NotBeNull();

        var grid = Css("_scheduler-timegrid.css");
        Rule(grid, ".tm-scheduler-timegrid-scroll").Should().Contain("overflow-y: auto");
        Rule(grid, ".tm-scheduler-timegrid-body").Should().Contain("height: 72rem");
        Rule(grid, ".tm-scheduler-timegrid-body").Should().NotContain("overflow");
    }

    [Fact]
    public void TimeGrid_Today_Column_Has_Today_Class()
    {
        var today = new DateOnly(2026, 5, 12);
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, new[] { today.AddDays(-1), today, today.AddDays(1) })
            .Add(c => c.Today, today)
            .Add(c => c.SlotDuration, 30));

        cut.FindAll(".tm-scheduler-timegrid-col.tm-scheduler-timegrid-col--today").Should().ContainSingle();
        cut.Find(".tm-scheduler-timegrid-col.tm-scheduler-timegrid-col--today").GetAttribute("aria-label")
            .Should().Contain("2026");
        var half = cut.FindAll(".tm-scheduler-timegrid-slot--half");
        half.Should().NotBeEmpty();
        half.Should().Contain(s => s.GetAttribute("data-start")!.EndsWith("T09:00"),
            "the dashed line is the slot that ends off the hour");
        cut.Find("[data-start$='T09:30']").ClassList.Should().NotContain("tm-scheduler-timegrid-slot--half");
    }

    [Fact]
    public void TimeGrid_DayHeader_Is_Single_Line_Culture_Format()
    {
        using var culture = CultureScope.Use("cs-CZ");
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, new[] { new DateOnly(2026, 5, 12) }));

        var header = cut.Find(".tm-scheduler-timegrid-col-header").TextContent.Trim();
        header.Should().Contain("12");
        header.Should().Contain("5");
        header.Replace("\n", " ").Replace("  ", " ").Should().Contain("12").And.Contain("5");
        cut.Find(".tm-scheduler-timegrid-col-header-weekday").TextContent.Should().Be("Út");
        cut.Find(".tm-scheduler-timegrid-col-header-date").TextContent.Should().Be("12.05");
    }

    [Fact]
    public void Month_Weekend_Cells_Have_Weekend_Class_When_HighlightWeekends()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2026, 5, 12))
            .Add(c => c.HighlightWeekends, true)
            .Add(c => c.FirstDayOfWeek, 1));

        cut.FindAll(".tm-scheduler-month-day--weekend").Should().NotBeEmpty();
        cut.FindAll(".tm-scheduler-month-day--weekend").Should().HaveCount(12,
            "a Monday-first month grid has two weekend columns across six rows");
    }

    [Fact]
    public void Month_OutsideMonth_Day_Has_No_Opacity_Class_On_Events()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Spill", new DateTime(2026, 4, 30, 9, 0, 0), new DateTime(2026, 4, 30, 10, 0, 0))
        };
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 5, 1))
            .Add(c => c.Events, events)
            .Add(c => c.ShowAdjacentMonthEvents, true));

        var evt = cut.Find(".tm-scheduler-month-day--other .tm-scheduler-month-event");
        evt.ClassList.Should().NotContain(c => c.Contains("opacity"));
        Css("_scheduler-month.css").Should().NotContain(".tm-scheduler-month-day--other .tm-scheduler-month-event");
    }

    [Fact]
    public void AllDayEventStyle_Tinted_Uses_Surface_Class()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Holiday", new DateTime(2026, 5, 12), new DateTime(2026, 5, 13), allDay: true, color: "#8b5cf6")
        };
        var tinted = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2026, 5, 12))
            .Add(c => c.AllDayEventStyle, TmAllDayEventStyle.Tinted)
            .Add(c => c.Events, events));
        tinted.Find(".tm-scheduler-month-event--allday").ClassList
            .Should().Contain("tm-scheduler-event--tinted");

        var solid = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2026, 5, 12))
            .Add(c => c.Events, events));
        solid.Find(".tm-scheduler-month-event--allday").ClassList
            .Should().NotContain("tm-scheduler-event--tinted");
    }

    [Fact]
    public void Timeline_Event_Has_Title_And_Time_Lines()
    {
        var events = new List<TmScheduleEvent>
        {
            new TmScheduleEvent { Title = "Workshop", ResourceId = "room", Start = new DateTime(2026, 5, 12, 9, 0, 0), End = new DateTime(2026, 5, 12, 11, 0, 0) }
        };
        var resources = new List<TmScheduleResource> { new() { Id = "room", Name = "Room" } };
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 5, 12))
            .Add(c => c.Events, events)
            .Add(c => c.Resources, resources));

        cut.Find(".tm-scheduler-timeline-event-title").TextContent.Should().Contain("Workshop");
        cut.Find(".tm-scheduler-timeline-event-time").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Agenda_Is_An_Aligned_Table()
    {
        var agenda = Css("_scheduler-agenda.css");
        Rule(agenda, ".tm-scheduler-agenda-event").Should().Contain("grid-template-columns");
        Rule(agenda, ".tm-scheduler-agenda").Should().Contain("var(--tm-bg-workspace)");
        Rule(agenda, ".tm-scheduler-agenda").Should().Contain("var(--tm-radius-card)");
    }

    [Fact]
    public void Month_Rows_Stretch_In_A_Bounded_Host()
    {
        var month = Css("_scheduler-month.css");
        Rule(month, ".tm-scheduler-month").Should().Contain("flex: 1");
        Rule(month, ".tm-scheduler-month-grid").Should().Contain("flex: 1");
        Rule(month, ".tm-scheduler-month-row").Should().Contain("flex: 1");
    }

    [Fact]
    public void Scheduler_Stylesheets_Have_No_Blue_Fallback()
    {
        foreach (var file in new[]
        {
            "_scheduler.css", "_scheduler-month.css", "_scheduler-timegrid.css",
            "_scheduler-timeline.css", "_scheduler-agenda.css"
        })
        {
            Css(file).Should().NotContain("#3b82f6", $"{file} must not fall back to a hardcoded blue");
        }
    }

    [Fact]
    public void TimeGrid_And_Timeline_Events_Are_Tinted_Cards()
    {
        var grid = Css("_scheduler-timegrid.css");
        var timed = Rule(grid, ".tm-scheduler-timegrid-event");
        timed.Should().Contain("border-radius: var(--tm-radius-sm)");
        Rule(Css("_scheduler.css"), ".tm-scheduler-event-surface")
            .Should().Contain("box-shadow: var(--tm-shadow-xs)");

        var timeline = Rule(Css("_scheduler-timeline.css"), ".tm-scheduler-timeline-event");
        timeline.Should().Contain("border-radius: var(--tm-radius-sm)");
        timeline.Should().NotContain("color-mix");
    }

    [Fact]
    public void Resize_Handle_Hover_Is_A_Token_Not_A_White_Wash()
    {
        foreach (var file in new[] { "_scheduler-timegrid.css", "_scheduler-timeline.css" })
        {
            var css = Css(file);
            var hover = string.Join('\n', css.Split('\n').Where(line => line.Contains("resize-handle") && line.Contains("hover")
                || (line.Contains("background") && css.Contains("resize-handle"))));
            css.Should().Contain(".tm-scheduler-event-resize-handle:hover");
            var block = BlockAfter(css, ".tm-scheduler-event-resize-handle:hover");
            block.Should().Contain("var(--event-color, var(--tm-color-primary))");
            block.Should().NotContain("255, 255, 255");
            block.Should().NotContain("white");
            _ = hover;
        }
    }

    [Fact]
    public void Scheduler_Stylesheets_Introduce_No_Colour_Literal()
    {
        foreach (var file in new[]
        {
            "_scheduler.css", "_scheduler-month.css", "_scheduler-timegrid.css",
            "_scheduler-timeline.css", "_scheduler-agenda.css", "_scheduler-day.css", "_scheduler-week.css"
        })
        {
            HexOrWhite.IsMatch(Css(file)).Should().BeFalse($"{file} takes every colour from a token");
        }
    }

    [Fact]
    public void Scheduler_Motion_Is_Covered_By_Reduced_Motion()
    {
        var animations = File.ReadAllText(ThemeCss.CssPath("animations.css"));
        animations.Should().Contain(".tm-scheduler-toolbar-nav-btn");
        animations.Should().Contain(".tm-scheduler-month-event");
        animations.Should().Contain(".tm-scheduler-timegrid-event");
        animations.Should().Contain(".tm-scheduler-timeline-event");
        animations.Should().Contain(".tm-scheduler-agenda-event");
    }

    [Fact]
    public void TimeGrid_Keyboard_ContextMenu_Uses_The_Slot_Element()
    {
        JSInterop.Setup<double[]>("TempoBlazor.Scheduler.slotPoint", _ => true).SetResult([120d, 340d]);
        var menus = new List<(double X, double Y)>();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.SlotDuration, 60)
            .Add(c => c.OnSlotContextMenu, EventCallback.Factory.Create<SchedulerSlotContext>(this, c => menus.Add((c.ClientX, c.ClientY)))));

        var slot = cut.Find(".tm-scheduler-timegrid-slot[tabindex='0']");
        slot.ContextMenu(new MouseEventArgs { ClientX = 0, ClientY = 0 });

        menus.Should().ContainSingle();
        menus[0].Should().Be((120d, 340d), "a keyboard menu opens on the slot, not at 0,0");
        var point = JSInterop.Invocations.Single(i => i.Identifier == "TempoBlazor.Scheduler.slotPoint");
        point.Arguments[1].Should().Be(slot.GetAttribute("data-start"),
            "the point comes from the slot the key was pressed in");
    }

    [Fact]
    public void Timeline_Keyboard_ContextMenu_Uses_The_Slot_Element()
    {
        JSInterop.Setup<double[]>("TempoBlazor.Scheduler.slotPoint", _ => true).SetResult([80d, 160d]);
        var menus = new List<(double X, double Y)>();
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.SlotDuration, 60)
            .Add(c => c.Resources, [new TmScheduleResource { Id = "r1", Name = "Room" }])
            .Add(c => c.OnSlotContextMenu, EventCallback.Factory.Create<SchedulerSlotContext>(this, c => menus.Add((c.ClientX, c.ClientY)))));

        var slot = cut.Find(".tm-scheduler-timeline-slot[tabindex='0']");
        slot.ContextMenu(new MouseEventArgs { ClientX = 0, ClientY = 0 });

        menus.Should().ContainSingle();
        menus[0].Should().Be((80d, 160d));
        var point = JSInterop.Invocations.Single(i => i.Identifier == "TempoBlazor.Scheduler.slotPoint");
        point.Arguments[1].Should().Be(slot.GetAttribute("data-start"));
        point.Arguments[2].Should().Be("r1");
    }

    [Fact]
    public void Templated_Events_Ellipsize_Inside_A_Content_Wrapper()
    {
        RenderFragment<TmScheduleEvent> template = e => builder => builder.AddContent(0, e.Title);
        var events = new List<TmScheduleEvent>
        {
            Evt("A very long templated title that must ellipsize", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var grid = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events)
            .Add(c => c.EventTemplate, template));
        var timed = grid.Find(".tm-scheduler-timegrid-event");
        timed.GetAttribute("title").Should().Be(events[0].Title);
        var wrapper = timed.QuerySelector(".tm-scheduler-event-content");
        wrapper.Should().NotBeNull();
        timed.QuerySelector("[data-resize-edge]")?.ParentElement?.ClassName.Should().NotContain("tm-scheduler-event-content");

        var timeline = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Events, events)
            .Add(c => c.EventTemplate, template)
            .Add(c => c.ReadOnly, false));
        var bar = timeline.Find(".tm-scheduler-timeline-event");
        bar.QuerySelector(".tm-scheduler-event-content").Should().NotBeNull();
        bar.QuerySelector("[data-resize-edge='left']").Should().NotBeNull();
    }

    [Fact]
    public void AllDay_Default_Stays_Solid_And_Tinted_Is_Opt_In()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("Holiday", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true, color: "#22c55e")
        };
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var chip = cut.Find(".tm-scheduler-timegrid-allday-event");
        chip.ClassList.Should().NotContain("tm-scheduler-event--tinted");
        chip.GetAttribute("style").Should().Contain("--event-color");

        var month = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 10))
            .Add(c => c.Events, events));
        month.Find(".tm-scheduler-month-event").ClassList.Should().Contain("tm-scheduler-month-event--allday");
    }

    [Fact]
    public void TimeGrid_Today_Wins_Over_Weekend()
    {
        var grid = Css("_scheduler-timegrid.css");
        var weekend = grid.IndexOf(".tm-scheduler-timegrid-col--weekend", StringComparison.Ordinal);
        var today = grid.IndexOf(".tm-scheduler-timegrid-col--today", StringComparison.Ordinal);
        weekend.Should().BeGreaterThan(-1);
        today.Should().BeGreaterThan(weekend, "the today wash must override the weekend wash");
        var header = grid.IndexOf(".tm-scheduler-timegrid-col-header--today .tm-scheduler-timegrid-col-header-weekday", StringComparison.Ordinal);
        header.Should().BeGreaterThan(-1);
        grid[header..].Should().Contain("color: var(--tm-color-primary)");
    }

    [Fact]
    public void Scheduler_Stylesheets_Never_Revert()
    {
        foreach (var file in new[] { "_scheduler.css", "_scheduler-month.css", "_scheduler-timegrid.css",
                     "_scheduler-timeline.css", "_scheduler-agenda.css" })
            Css(file).Should().NotContain("revert", $"{file} must not fall back to the user agent");
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var at = 0; (at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0; at += value.Length)
            count++;
        return count;
    }

    /// <summary>The declaration block of the first rule whose selector is exactly <paramref name="selector"/>.</summary>
    private static string Rule(string css, string selector)
    {
        var at = css.IndexOf("\n" + selector + " {", StringComparison.Ordinal);
        if (at < 0) at = css.IndexOf("\n" + selector + "{", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"{selector} must be a rule of its own");
        var open = css.IndexOf('{', at);
        var close = css.IndexOf('}', open);
        return css[open..(close + 1)];
    }

    private static string BlockAfter(string css, string selector)
    {
        var at = css.IndexOf(selector, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, selector);
        var open = css.IndexOf('{', at);
        var close = css.IndexOf('}', open);
        return css[open..(close + 1)];
    }
}
