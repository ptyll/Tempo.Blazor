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
/// Review round 1 of phase 5. The now indicator is one internal component fed by one ticker per
/// grid, a default CurrentDate is never written, and the timeline shares the time-grid header.
/// </summary>
public class SchedulerReviewRound1Tests : LocalizationTestBase
{
    private static readonly DateOnly Monday = new(2026, 6, 15);

    private static TmScheduleResource Room(string id, string name, string? type = null) => new()
    {
        Id = id,
        Name = name,
        ResourceType = type
    };

    [Fact]
    public void NowIndicator_Is_Internal_And_The_Public_Line_Is_Gone()
    {
        var indicator = typeof(TmScheduler).Assembly.GetType("Tempo.Blazor.Components.Scheduler.SchedulerNowIndicator");
        indicator.Should().NotBeNull("the indicator lives in one internal class");
        indicator!.IsPublic.Should().BeFalse();
        typeof(TmScheduler).Assembly.GetType("Tempo.Blazor.Components.Scheduler.TmSchedulerNowIndicator")
            .Should().BeNull("the public indicator was the line's host, and the review removed it");
        typeof(TmScheduler).Assembly.GetType("Tempo.Blazor.Components.Scheduler.SchedulerNowIndicatorLine")
            .Should().BeNull("a tick notifies the indicator itself, not a second line component");
    }

    [Fact]
    public void NowIndicator_Tick_Crosses_Midnight_In_The_Display_Zone()
    {
        // 21:59 UTC is 23:59 in Prague. One minute later the column is yesterday.
        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        var clock = new ZonedClock(new DateTimeOffset(2026, 6, 15, 21, 59, 0, TimeSpan.Zero), prague);
        Services.AddSingleton<TimeProvider>(clock);
        // Only today's column is rendered, so crossing midnight has nothing left to draw.
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.TimeZone, prague));

        cut.FindAll(".tm-scheduler-timegrid-now").Should().ContainSingle();
        cut.Find(".tm-scheduler-timegrid-now").GetAttribute("aria-label").Should().Contain("23:59");
        var renders = cut.Instance.RenderCount;

        clock.Advance(TimeSpan.FromMinutes(1));
        cut.WaitForAssertion(() => cut.FindAll(".tm-scheduler-timegrid-now").Should().BeEmpty());
        cut.Instance.RenderCount.Should().Be(renders, "the tick re-renders the indicator, not the grid");
    }

    [Fact]
    public void One_Grid_Owns_One_Ticker()
    {
        var clock = new ZonedClock(new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        Services.AddSingleton<TimeProvider>(clock);
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday, Monday.AddDays(1), Monday.AddDays(2)])
            .Add(c => c.Resources, new List<TmScheduleResource>
            {
                Room("a", "Room A"),
                Room("b", "Room B")
            }));

        // One line per resource column of today, one timer for all of them.
        var lines = cut.FindAll(".tm-scheduler-timegrid-now");
        lines.Should().HaveCount(2);
        clock.Advance(TimeSpan.FromMinutes(1));
        cut.WaitForAssertion(() =>
            cut.Find(".tm-scheduler-timegrid-now").GetAttribute("aria-label").Should().Contain("8:01"));
    }

    [Fact]
    public void Timeline_NowIndicator_Comes_From_The_Same_Component()
    {
        var clock = new ZonedClock(new DateTimeOffset(2026, 6, 15, 10, 15, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        Services.AddSingleton<TimeProvider>(clock);
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("a", "Room A"), Room("b", "Room B") }));

        var lines = cut.FindAll(".tm-scheduler-timeline-now");
        lines.Should().HaveCount(2);
        lines[0].GetAttribute("role").Should().Be("img");
        lines[0].GetAttribute("aria-label").Should().Contain("10:15");
        lines[1].GetAttribute("aria-hidden").Should().Be("true");
        lines[0].GetAttribute("style").Should().Contain("--tm-scheduler-now: 615");

        cut.Instance.RenderCount.Should().BeGreaterThan(0);
        var renders = cut.Instance.RenderCount;
        clock.Advance(TimeSpan.FromMinutes(1));
        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-scheduler-timeline-now")[0].GetAttribute("aria-label").Should().Contain("10:16"));
        cut.Instance.RenderCount.Should().Be(renders);
    }

    [Fact]
    public void Default_CurrentDate_Is_Read_As_Today_And_Never_Written()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        DateTime bound = default;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Bind(c => c.CurrentDate, bound, v => bound = v));

        bound.Should().Be(default, "a default binding is today for display, never a write back");
        cut.Find(".tm-scheduler-toolbar-title-text").TextContent.Should().Contain("15");
        cut.Render();
        bound.Should().Be(default, "a later render still does not write the parameter");
    }

    [Fact]
    public void Timeline_Default_Header_Shows_The_Avatar_And_The_Type()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room Alpha", "Meeting room") }));

        var header = cut.Find(".tm-scheduler-timeline-resource-label .tm-scheduler-resource-header");
        header.QuerySelector(".tm-avatar-sm").Should().NotBeNull("the timeline header uses AvatarSize.Sm so the name fits the column");
        header.QuerySelector(".tm-scheduler-resource-header-avatar")!.GetAttribute("aria-hidden").Should().Be("true");
        header.TextContent.Should().Contain("Room Alpha").And.Contain("Meeting room");
    }

    [Fact]
    public void Timeline_Uses_The_Shared_Working_Range()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.ShowFullDay, false)
            .Add(c => c.WorkDayStart, new TimeOnly(8, 30))
            .Add(c => c.WorkDayEnd, new TimeOnly(17, 0))
            .Add(c => c.SlotDuration, 60));

        var root = cut.Find(".tm-scheduler-timeline");
        root.GetAttribute("data-range-start").Should().Be("480", "08:30 floors to the 60 minute slot");
        root.GetAttribute("data-range-minutes").Should().Be("540");
        root.GetAttribute("data-tm-scroll").Should().Be("timeline");
    }

    [Fact]
    public void ShowEventResource_Names_The_Resource_In_The_Accessible_Name()
    {
        var day = new DateTime(2026, 6, 15);
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [Monday])
            .Add(c => c.ShowEventResource, true)
            .Add(c => c.Resources, new List<TmScheduleResource> { Room("room-a", "Room Alpha") })
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                new()
                {
                    Id = "standup",
                    Title = "Standup",
                    Start = day.AddHours(9),
                    End = day.AddHours(10),
                    ResourceId = "room-a"
                }
            }));

        cut.Find(".tm-scheduler-timegrid-event").GetAttribute("aria-label").Should().Contain("Room Alpha");
        cut.Find(".tm-scheduler-timegrid-event-resource .tm-icon").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void HighlightOffHours_Reaches_The_Timeline_Slots()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.HighlightOffHours, true)
            .Add(c => c.SlotDuration, 60)
            .Add(c => c.WorkDayStart, new TimeOnly(8, 0))
            .Add(c => c.WorkDayEnd, new TimeOnly(17, 0)));

        cut.FindAll(".tm-scheduler-timeline-slot--offhours").Should().HaveCount(15);
    }

    [Fact]
    public void HighlightOffHours_Is_Off_On_The_Timeline_By_Default()
    {
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 6, 15))
            .Add(c => c.WorkDayStart, new TimeOnly(8, 0))
            .Add(c => c.WorkDayEnd, new TimeOnly(17, 0)));

        cut.FindAll(".tm-scheduler-timeline-slot--offhours").Should().BeEmpty();
    }

    [Fact]
    public void Scheduler_Declares_The_Hour_And_OffHours_Tokens()
    {
        var css = File.ReadAllText(Path.Combine(FindRepo(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_scheduler.css"));
        css.Should().Contain(".tm-scheduler {");
        css.Should().Contain("--tm-scheduler-hour-height: 3rem;");
        css.Should().Contain("--tm-scheduler-offhours-bg:");
        css.Should().Contain("--tm-scheduler-lane-height: 3.5rem;");
        css.Should().Contain("--tm-scheduler-timeline-hour-width: 5rem;");
    }

    [Fact]
    public void Corner_Header_Has_Its_Own_Sticky_Modifier()
    {
        var css = File.ReadAllText(Path.Combine(FindRepo(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_scheduler-timeline.css"));
        css.Should().Contain(".tm-scheduler-timeline-resource-header--sticky");
        css.Should().Contain("isolation: isolate");
    }

    /// <summary>A fake clock whose local zone is the one the test pins.</summary>
    private sealed class ZonedClock(DateTimeOffset start, TimeZoneInfo zone) : FakeTimeProvider(start)
    {
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
