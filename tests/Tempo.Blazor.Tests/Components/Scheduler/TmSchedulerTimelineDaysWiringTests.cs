using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The scheduler-level contract of the day-granularity timeline: the range it loads, the title it
/// prints and the step Previous/Next take are the week the Week view uses, while the hour timeline
/// keeps its single day. The "+N more" button opens that day in the Day view, exactly like month.
/// </summary>
public class TmSchedulerTimelineDaysWiringTests : LocalizationTestBase
{
    private static readonly DateTime Wednesday = new(2026, 6, 10);

    private static readonly List<TmScheduleResource> Channels =
    [
        new() { Id = "web", Name = "Web" },
        new() { Id = "mail", Name = "E-mail" }
    ];

    private static IScheduleDataProvider EmptyProvider()
    {
        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TmScheduleEvent>>([]));
        return provider;
    }

    [Fact]
    public async Task Day_Scale_Loads_The_Whole_Week()
    {
        using var _ = CultureScope.Use("en-US");
        var provider = EmptyProvider();

        Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.TimelineScale, TmSchedulerTimelineScale.Days)
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.DataProvider, provider));

        await provider.Received(1).GetEventsAsync(
            Arg.Is<TmScheduleQuery>(q =>
                q.RangeStart == new DateTime(2026, 6, 8) &&
                q.RangeEnd == new DateTime(2026, 6, 15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WorkWeek_Loads_And_Renders_Only_The_Work_Days()
    {
        using var _ = CultureScope.Use("en-US");
        var provider = EmptyProvider();

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.TimelineScale, TmSchedulerTimelineScale.Days)
            .Add(c => c.WeekDays, TmSchedulerWeekDays.WorkWeek)
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.DataProvider, provider));

        await provider.Received(1).GetEventsAsync(
            Arg.Is<TmScheduleQuery>(q =>
                q.RangeStart == new DateTime(2026, 6, 8) &&
                q.RangeEnd == new DateTime(2026, 6, 13)),
            Arg.Any<CancellationToken>());
        cut.FindAll(".tm-scheduler-timeline-day-header").Should().HaveCount(5);
    }

    [Fact]
    public async Task Day_Scale_Titles_The_Week()
    {
        using var _ = CultureScope.Use("en-US");

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.TimelineScale, TmSchedulerTimelineScale.Days)
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.Events, new List<TmScheduleEvent>()));

        cut.Find(".tm-scheduler-toolbar-title").TextContent.Should().Contain("June 8–14, 2026");
    }

    [Fact]
    public void Day_Scale_PrevNext_Step_A_Week()
    {
        using var _ = CultureScope.Use("en-US");
        DateTime date = Wednesday;
        EventCallback<DateTime> changed = EventCallback.Factory.Create<DateTime>(this, d => date = d);

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.TimelineScale, TmSchedulerTimelineScale.Days)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, changed));

        cut.Find("[data-testid='scheduler-next']").Click();
        date.Should().Be(new DateTime(2026, 6, 17));

        cut.Render(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.TimelineScale, TmSchedulerTimelineScale.Days)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, changed));

        cut.Find("[data-testid='scheduler-prev']").Click();
        date.Should().Be(new DateTime(2026, 6, 10));
    }

    [Fact]
    public void The_Hour_Scale_Still_Loads_And_Titles_One_Day()
    {
        using var _ = CultureScope.Use("en-US");

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.Events, new List<TmScheduleEvent>()));

        cut.Find(".tm-scheduler-toolbar-title").TextContent.Should().Contain("Wednesday");
        cut.FindAll(".tm-scheduler-timeline-time-header").Should().NotBeEmpty();
        cut.FindAll(".tm-scheduler-timeline-day-header").Should().BeEmpty();
    }

    [Fact]
    public void The_Board_Renders_A_Row_Per_Resource()
    {
        using var _ = CultureScope.Use("en-US");

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.TimelineScale, TmSchedulerTimelineScale.Days)
            .Add(c => c.CurrentDate, Wednesday)
            .Add(c => c.Resources, Channels)
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                new()
                {
                    Id = "e1",
                    Title = "Campaign",
                    Start = new DateTime(2026, 6, 10, 9, 0, 0),
                    End = new DateTime(2026, 6, 10, 10, 0, 0),
                    ResourceId = "mail"
                }
            }));

        cut.FindAll(".tm-scheduler-timeline-row").Should().HaveCount(2);
        cut.Find(".tm-scheduler-timeline-day-cell[data-date='2026-06-10'][data-resource='mail'] .tm-scheduler-timeline-day-event")
            .TextContent.Should().Contain("Campaign");
    }

    [Fact]
    public void The_More_Button_Opens_That_Day_In_The_Day_View()
    {
        using var _ = CultureScope.Use("en-US");
        var view = TmScheduleViewType.Timeline;
        var date = Wednesday;
        var events = Enumerable.Range(0, 4).Select(i => new TmScheduleEvent
        {
            Id = $"e{i}",
            Title = $"Slot {i}",
            Start = new DateTime(2026, 6, 10, 8 + i, 0, 0),
            End = new DateTime(2026, 6, 10, 9 + i, 0, 0),
            ResourceId = "web"
        }).ToList();

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, view)
            .Add(c => c.ViewChanged, EventCallback.Factory.Create<TmScheduleViewType>(this, v => view = v))
            .Add(c => c.TimelineScale, TmSchedulerTimelineScale.Days)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d))
            .Add(c => c.Resources, Channels)
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-timeline-day-cell[data-date='2026-06-10'][data-resource='web'] .tm-scheduler-timeline-day-more")
            .Click();

        view.Should().Be(TmScheduleViewType.Day, "the overflow opens the day, like the month grid");
        date.Should().Be(new DateTime(2026, 6, 10));
    }
}
