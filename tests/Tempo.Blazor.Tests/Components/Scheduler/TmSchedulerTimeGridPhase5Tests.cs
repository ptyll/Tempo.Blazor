using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Phase 5: the time grid's hour range, the now indicator, the work week, timeline lanes and
/// resource headers.
/// </summary>
public class TmSchedulerTimeGridPhase5Tests : LocalizationTestBase
{
    private static readonly DateOnly Monday = new(2026, 6, 15); // Monday

    private static TmScheduleEvent Evt(string id, DateTime start, DateTime end, string? resourceId = null) => new()
    {
        Id = id,
        Title = id,
        Start = start,
        End = end,
        ResourceId = resourceId
    };

    private static TmScheduleResource Room(string id, string name, string? type = null) => new()
    {
        Id = id,
        Name = name,
        ResourceType = type
    };

    private void UseClock(DateTimeOffset utc)
        => Services.AddSingleton<TimeProvider>(new FakeTimeProvider(utc));

    [Fact]
    public void TimeGrid_Renders_All_24_Hours_By_Default()
    {
        using var _ = CultureScope.Use("cs-CZ"); // the gutter uses the culture's short time ("H:mm")

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.SlotDuration, 60));

        var hours = cut.FindAll(".tm-scheduler-timegrid-body .tm-scheduler-timegrid-time-label")
            .Select(l => l.TextContent.Trim()).ToList();
        hours.Should().HaveCount(25, "the closing midnight label sits under the last hour");
        hours.Should().Contain("0:00").And.Contain("23:00");
        cut.FindAll(".tm-scheduler-timegrid-slot").Should().HaveCount(24);
        cut.Find(".tm-scheduler-timegrid").GetAttribute("data-range-minutes").Should().Be("1440");
    }

    [Fact]
    public void TimeGrid_Renders_Hours_From_WorkDayStart_When_ShowFullDay_False()
    {
        using var _ = CultureScope.Use("cs-CZ"); // the gutter uses the culture's short time ("H:mm")

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.SlotDuration, 60)
            .Add(c => c.WorkDayStart, new TimeOnly(8, 0))
            .Add(c => c.WorkDayEnd, new TimeOnly(17, 0))
            .Add(c => c.ShowFullDay, false));

        var hours = cut.FindAll(".tm-scheduler-timegrid-body .tm-scheduler-timegrid-time-label")
            .Select(l => l.TextContent.Trim()).ToList();
        hours.Should().Equal("8:00", "9:00", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00", "16:00", "17:00");
        cut.Find(".tm-scheduler-timegrid-time-label--last").TextContent.Trim().Should().Be("17:00");
        cut.FindAll(".tm-scheduler-timegrid-slot").Should().HaveCount(9);
        var root = cut.Find(".tm-scheduler-timegrid");
        root.GetAttribute("data-range-start").Should().Be("480");
        root.GetAttribute("data-range-minutes").Should().Be("540");
    }

    [Fact]
    public void TimeGrid_Styles_Are_Culture_Invariant()
    {
        // cs-CZ formats a double with a comma. A style string carrying one is a broken declaration.
        using var _ = CultureScope.Use("cs-CZ");
        {
            UseClock(new DateTimeOffset(2026, 6, 15, 8, 30, 0, TimeSpan.Zero));
            var cut = Render<TmSchedulerTimeGrid>(p => p
                .Add(c => c.Dates, [Monday])
                .Add(c => c.Today, Monday)
                .Add(c => c.ShowFullDay, false)
                .Add(c => c.WorkDayStart, new TimeOnly(8, 0))
                .Add(c => c.WorkDayEnd, new TimeOnly(17, 30)));

            var styled = cut.FindAll("[style]").Select(e => e.GetAttribute("style")).Where(s => s!.Contains(',')).ToList();
            styled.Should().BeEmpty("a style value is a unitless CSS variable, never a culture-formatted double");
            cut.Find(".tm-scheduler-timegrid").GetAttribute("style").Should().Contain("--tm-scheduler-visible-hours:");
        }
    }

    [Fact]
    public void Timeline_Styles_Are_Culture_Invariant()
    {
        using var _ = CultureScope.Use("cs-CZ");
        {
            var day = new DateTime(2026, 6, 15);
            var cut = Render<TmSchedulerTimelineView>(p => p
                .Add(c => c.CurrentDate, day)
                .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room A") })
                .Add(c => c.Events, new List<TmScheduleEvent>
                {
                    Evt("a", day.AddHours(9), day.AddHours(11), "room-a"),
                    Evt("b", day.AddHours(10), day.AddHours(12), "room-a")
                }));

            cut.FindAll("[style]").Should().OnlyContain(e => !e.GetAttribute("style")!.Contains(','),
                "lane geometry is a unitless CSS variable, never a culture-formatted double");
            cut.Markup.Should().Contain("--tm-scheduler-lane:");
            cut.Markup.Should().Contain("--tm-scheduler-lanes:");
        }
    }

    [Fact]
    public void TimeGrid_Renders_NowIndicator_For_Today_Column()
    {
        UseClock(new DateTimeOffset(2026, 6, 15, 8, 30, 0, TimeSpan.Zero));
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday.AddDays(-1), Monday])
            .Add(c => c.Today, Monday)
            .Add(c => c.SlotDuration, 60));

        var indicators = cut.FindAll(".tm-scheduler-timegrid-now");
        indicators.Should().ContainSingle("only today's column carries the indicator");
        indicators[0].GetAttribute("aria-label").Should().Contain("8:30");
        indicators[0].GetAttribute("style").Should().Contain("--tm-scheduler-now: 510");
    }

    [Fact]
    public void NowIndicator_Hidden_When_ShowNowIndicator_False()
    {
        UseClock(new DateTimeOffset(2026, 6, 15, 8, 30, 0, TimeSpan.Zero));
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.Today, Monday)
            .Add(c => c.ShowNowIndicator, false));

        cut.FindAll(".tm-scheduler-timegrid-now").Should().BeEmpty();
    }

    [Fact]
    public void NowIndicator_Uses_Scheduler_TimeZone()
    {
        // 12:30 UTC is 14:30 in Prague (CEST) and 08:30 in New York (EDT).
        UseClock(new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero));
        var prague = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.Today, Monday)
            .Add(c => c.TimeZone, TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague")));
        var newYork = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.Today, Monday)
            .Add(c => c.TimeZone, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")));

        prague.Find(".tm-scheduler-timegrid-now").GetAttribute("aria-label").Should().Contain("14:30");
        newYork.Find(".tm-scheduler-timegrid-now").GetAttribute("aria-label").Should().Contain("8:30");
    }

    [Fact]
    public void NowIndicator_Rerenders_Only_Itself_On_Tick()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 8, 30, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.Today, Monday)
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                Evt("standup", new DateTime(2026, 6, 15, 9, 0, 0), new DateTime(2026, 6, 15, 10, 0, 0))
            }));

        cut.Find(".tm-scheduler-timegrid-now").GetAttribute("aria-label").Should().Contain("8:30");
        // The grid's own renders, not its children's: a tick must not ask the grid to render again.
        var gridRendersBefore = cut.Instance.RenderCount;
        clock.Advance(TimeSpan.FromMinutes(60));
        cut.WaitForAssertion(() =>
            cut.Find(".tm-scheduler-timegrid-now").GetAttribute("aria-label").Should().Contain("9:30"));
        cut.Instance.RenderCount.Should().Be(gridRendersBefore, "a tick re-renders the indicator, not the grid that hosts it");
    }

    [Fact]
    public void Week_WorkWeek_Renders_Five_Columns_And_Title_Matches()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 17))
            .Add(c => c.WeekDays, TmSchedulerWeekDays.WorkWeek));

        cut.FindAll(".tm-scheduler-timegrid-col").Should().HaveCount(5);
        var title = cut.Find(".tm-scheduler-toolbar-title-text").TextContent;
        title.Should().Contain("15").And.Contain("19");
        title.Should().NotContain("20.").And.NotContain("21");
    }

    [Fact]
    public void Month_MonthWeeks_Auto_Renders_Five_Rows_When_Enough()
    {
        // June 2026 starts on a Monday, so a Monday-first grid fits in five weeks.
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.FirstDayOfWeek, 1)
            .Add(c => c.MonthWeeks, TmSchedulerMonthWeeks.Auto));

        cut.FindAll(".tm-scheduler-month-day").Should().HaveCount(35);
    }

    [Fact]
    public void Month_MonthWeeks_Six_Is_The_Default()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15)));

        cut.FindAll(".tm-scheduler-month-day").Should().HaveCount(42);
    }

    [Fact]
    public void Timeline_Overlapping_Events_Get_Separate_Lanes()
    {
        var day = new DateTime(2026, 6, 15);
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, day)
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room A") })
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                Evt("a", day.AddHours(9), day.AddHours(11), "room-a"),
                Evt("b", day.AddHours(10), day.AddHours(12), "room-a")
            }));

        var events = cut.FindAll(".tm-scheduler-timeline-event");
        events.Should().HaveCount(2);
        events.Select(e => e.GetAttribute("style")).Should().OnlyContain(s => s!.Contains("--tm-scheduler-lane:"));
        events[0].GetAttribute("style").Should().NotBe(events[1].GetAttribute("style"));
        events[0].GetAttribute("style").Should().Contain("--tm-scheduler-lane: 0");
        events[1].GetAttribute("style").Should().Contain("--tm-scheduler-lane: 1");
        cut.Find(".tm-scheduler-timeline-row-body").GetAttribute("style").Should().Contain("--tm-scheduler-lanes: 2");
    }

    [Fact]
    public void Timeline_Renders_NowIndicator_For_Today()
    {
        UseClock(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room A"), Room("room-b", "Room B") }));

        var lines = cut.FindAll(".tm-scheduler-timeline-now");
        lines.Should().HaveCount(2, "every resource row of today carries the line");
        lines[0].GetAttribute("role").Should().Be("img");
        lines[0].GetAttribute("aria-label").Should().Contain("10:00");
        lines[0].GetAttribute("style").Should().Contain("--tm-scheduler-now: 600");
        lines[1].GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void ShowFullDay_False_Hides_Events_Outside_Range()
    {
        var day = new DateTime(2026, 6, 15);
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.ShowFullDay, false)
            .Add(c => c.WorkDayStart, new TimeOnly(8, 0))
            .Add(c => c.WorkDayEnd, new TimeOnly(17, 0))
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                Evt("before", day.AddHours(6), day.AddHours(7)),
                Evt("straddles", day.AddHours(7), day.AddHours(9)),
                Evt("inside", day.AddHours(10), day.AddHours(11)),
                Evt("after", day.AddHours(18), day.AddHours(19))
            }));

        var titles = cut.FindAll(".tm-scheduler-timegrid-event").Select(e => e.TextContent).ToList();
        titles.Should().NotContain(t => t.Contains("before") || t.Contains("after"));
        titles.Should().Contain(t => t.Contains("straddles"));
        titles.Should().Contain(t => t.Contains("inside"));
        cut.FindAll("[tabindex]").Should().NotContain(e => e.TextContent.Contains("before") || e.TextContent.Contains("after"),
            "an event outside the range is not a focus stop");
    }

    [Fact]
    public void HighlightOffHours_Is_Off_By_Default()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.WorkDayStart, new TimeOnly(8, 0))
            .Add(c => c.WorkDayEnd, new TimeOnly(17, 0)));

        cut.FindAll(".tm-scheduler-timegrid-slot--offhours").Should().BeEmpty();
    }

    [Fact]
    public void HighlightOffHours_Tints_Slots_Outside_The_Working_Day()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.HighlightOffHours, true)
            .Add(c => c.SlotDuration, 60)
            .Add(c => c.WorkDayStart, new TimeOnly(8, 0))
            .Add(c => c.WorkDayEnd, new TimeOnly(17, 0)));

        cut.FindAll(".tm-scheduler-timegrid-slot--offhours").Should().HaveCount(15, "the 15 hours outside 08:00-17:00 are tinted while the day stays full");
        cut.FindAll(".tm-scheduler-timegrid-slot").Should().HaveCount(24);
    }

    [Fact]
    public void Timeline_Resource_Column_Is_Sticky_Class()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room A") }));

        cut.Find(".tm-scheduler-timeline-resource-label").ClassList
            .Should().Contain("tm-scheduler-timeline-resource-label--sticky");
    }

    [Fact]
    public void ResourceHeaderTemplate_Is_Used_In_TimeGrid_And_Timeline()
    {
        var resources = new List<TmScheduleResource> { Room("room-a", "Room A", "room") };
        RenderFragment<TmScheduleResource> header = r => builder => builder.AddContent(0, $"HDR-{r.Id}");

        var grid = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.Resources, resources)
            .Add(c => c.ResourceHeaderTemplate, header));
        var timeline = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.Resources, resources)
            .Add(c => c.ResourceHeaderTemplate, header));

        grid.Find(".tm-scheduler-timegrid-resource-label").TextContent.Should().Contain("HDR-room-a");
        timeline.Find(".tm-scheduler-timeline-resource-label").TextContent.Should().Contain("HDR-room-a");
    }

    [Fact]
    public void Default_Resource_Header_Shows_Initials_Avatar_And_ResourceType()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room Alpha", "Meeting room") }));

        var header = cut.Find(".tm-scheduler-timegrid-resource-label");
        header.QuerySelector(".tm-avatar-fallback")!.TextContent.Should().Be("RA");
        header.TextContent.Should().Contain("Room Alpha").And.Contain("Meeting room");
    }

    [Fact]
    public void DayView_With_Resources_Hides_Date_Header()
    {
        var cut = Render<TmSchedulerDayView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.Resources, new List<TmScheduleResource>
            {
                Room("room-a", "Room A"),
                Room("room-b", "Room B")
            }));

        cut.FindAll(".tm-scheduler-timegrid-col-header").Should().BeEmpty();
        cut.FindAll(".tm-scheduler-timegrid-resource-label").Should().HaveCount(2);
    }

    [Fact]
    public void ShowEventResource_Renders_Resource_Name_Line()
    {
        var day = new DateTime(2026, 6, 15);
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.ShowEventResource, true)
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room Alpha") })
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                Evt("standup", day.AddHours(9), day.AddHours(10), "room-a")
            }));

        var line = cut.Find(".tm-scheduler-timegrid-event-resource");
        line.TextContent.Should().Contain("Room Alpha");
        line.InnerHtml.Should().Contain("tm-icon");
    }

    [Fact]
    public void TimeGrid_ScrollToTime_Scrolls_After_First_Render()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.ScrollToTime, new TimeSpan(8, 30, 0)));

        var call = JSInterop.Invocations
            .Single(i => i.Identifier == "TempoBlazor.Scheduler.scrollToTime");
        call.Arguments[1].Should().Be(510d);
    }

    [Fact]
    public void TimeGrid_Does_Not_Scroll_When_ScrollToTime_Is_Null()
    {
        Render<TmSchedulerTimeGrid>(p => p.Add(c => c.Dates, [Monday]));

        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "TempoBlazor.Scheduler.scrollToTime");
    }
}
