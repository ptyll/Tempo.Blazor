using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmScheduler main container.</summary>
public class TmSchedulerTests : LocalizationTestBase
{
    [Fact]
    public void Renders_Container()
    {
        var cut = Render<TmScheduler>();

        cut.Find(".tm-scheduler").Should().NotBeNull();
    }

    [Fact]
    public void Renders_Toolbar()
    {
        var cut = Render<TmScheduler>();

        cut.Find(".tm-scheduler-toolbar").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_Shows_Navigation_Buttons()
    {
        var cut = Render<TmScheduler>();

        cut.Find("[data-testid='scheduler-prev']").Should().NotBeNull();
        cut.Find("[data-testid='scheduler-today']").Should().NotBeNull();
        cut.Find("[data-testid='scheduler-next']").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_Shows_View_Switcher()
    {
        var cut = Render<TmScheduler>();

        cut.FindAll("[role='tab']").Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void View_Switcher_Changes_View()
    {
        TmScheduleViewType view = TmScheduleViewType.Month;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.ViewChanged, EventCallback.Factory.Create<TmScheduleViewType>(this, v => view = v)));

        // Click "Agenda" button
        var agendaBtn = cut.FindAll("[role='tab']")
            .First(b => b.GetAttribute("data-view") == "Agenda");
        agendaBtn.Click();

        view.Should().Be(TmScheduleViewType.Agenda);
    }

    [Fact]
    public void Active_View_Button_Has_Active_Class()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month));

        var monthBtn = cut.FindAll("[role='tab']")
            .First(b => b.GetAttribute("data-view") == "Month");
        monthBtn.GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Next_Navigation_Changes_Date()
    {
        DateTime date = new DateTime(2025, 6, 15);
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find("[data-testid='scheduler-next']").Click();

        date.Month.Should().Be(7);
    }

    [Fact]
    public void Prev_Navigation_Changes_Date()
    {
        DateTime date = new DateTime(2025, 6, 15);
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find("[data-testid='scheduler-prev']").Click();

        date.Month.Should().Be(5);
    }

    [Fact]
    public void Default_Date_Renders_An_Event_Scheduled_For_Today()
    {
        // A default CurrentDate means today, not year 1. An event for today must be in range.
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                new() { Id = "today", Title = "Today's standup", Start = DateTime.Today.AddHours(9), End = DateTime.Today.AddHours(9.5) }
            }));

        cut.FindAll(".tm-scheduler-timegrid-event").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Today's standup");
    }

    [Fact]
    public void Next_And_Prev_Step_From_Today_When_The_Date_Is_Default()
    {
        DateTime date = default;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find("[data-testid='scheduler-next']").Click();
        date.Date.Should().Be(DateTime.Today.AddDays(1));

        date = default;
        cut.Find("[data-testid='scheduler-prev']").Click();
        date.Date.Should().Be(DateTime.Today.AddDays(-1));
    }

    [Fact]
    public void Today_Navigation_Returns_To_Today()
    {
        DateTime date = new DateTime(2025, 1, 1);
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find("[data-testid='scheduler-today']").Click();

        date.Date.Should().Be(DateTime.Today);
    }

    [Fact]
    public void Applies_Custom_Class()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.Class, "my-scheduler"));

        cut.Find(".tm-scheduler").ClassList.Should().Contain("my-scheduler");
    }

    [Fact]
    public void Default_View_Is_Month()
    {
        var cut = Render<TmScheduler>();

        cut.FindAll(".tm-scheduler-month").Count.Should().Be(1);
    }

    [Fact]
    public void Displays_Month_View_When_View_Is_Month()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month));

        cut.FindAll(".tm-scheduler-month").Count.Should().Be(1);
        cut.FindAll(".tm-scheduler-agenda").Count.Should().Be(0);
    }

    [Fact]
    public void Displays_Agenda_View_When_View_Is_Agenda()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Agenda));

        cut.FindAll(".tm-scheduler-agenda").Count.Should().Be(1);
        cut.FindAll(".tm-scheduler-month").Count.Should().Be(0);
    }

    [Fact]
    public void Displays_Day_View_When_View_Is_Day()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day));

        cut.FindAll(".tm-scheduler-day").Count.Should().Be(1);
        cut.FindAll(".tm-scheduler-month").Count.Should().Be(0);
    }

    [Fact]
    public void Displays_Week_View_When_View_Is_Week()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week));

        cut.FindAll(".tm-scheduler-week").Count.Should().Be(1);
        cut.FindAll(".tm-scheduler-month").Count.Should().Be(0);
    }

    [Fact]
    public void Day_Navigation_Changes_By_One_Day()
    {
        DateTime date = new DateTime(2025, 6, 15);
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find("[data-testid='scheduler-next']").Click();

        date.Day.Should().Be(16);
    }

    [Fact]
    public void Week_Navigation_Changes_By_Seven_Days()
    {
        DateTime date = new DateTime(2025, 6, 15);
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find("[data-testid='scheduler-next']").Click();

        date.Day.Should().Be(22);
    }

    [Fact]
    public void Displays_Timeline_View_When_View_Is_Timeline()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline));

        cut.FindAll(".tm-scheduler-timeline").Count.Should().Be(1);
        cut.FindAll(".tm-scheduler-month").Count.Should().Be(0);
    }

    [Fact]
    public void Month_View_Uses_EventTemplate_When_Provided()
    {
        var events = new List<TmScheduleEvent>
        {
            new()
            {
                Title = "Custom Month",
                Start = new DateTime(2025, 6, 10, 9, 0, 0),
                End = new DateTime(2025, 6, 10, 10, 0, 0),
                Color = "#16a34a"
            }
        };

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.EventTemplate, (RenderFragment<TmScheduleEvent>)(e => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "scheduler-month-template");
                builder.AddContent(2, e.Title);
                builder.CloseElement();
            })));

        var eventEl = cut.Find(".tm-scheduler-month-event");
        eventEl.GetAttribute("style").Should().Contain("--event-color: #16a34a");
        cut.Find(".scheduler-month-template").TextContent.Should().Contain("Custom Month");
    }

    /// <summary>
    /// B14: Timeline loads a single day. The data provider receives [day, day+1), not the week.
    /// </summary>
    [Fact]
    public async Task Timeline_Range_Is_Single_Day()
    {
        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TmScheduleEvent>>([]));

        Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15, 14, 30, 0))
            .Add(c => c.DataProvider, provider));

        await provider.Received(1).GetEventsAsync(
            Arg.Is<TmScheduleQuery>(q =>
                q.RangeStart == new DateTime(2025, 6, 15) &&
                q.RangeEnd == new DateTime(2025, 6, 16)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>B14: Timeline prev/next moves one day, not a week.</summary>
    [Fact]
    public void Timeline_PrevNext_Moves_One_Day()
    {
        DateTime date = new DateTime(2025, 6, 15);
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find("[data-testid='scheduler-next']").Click();
        date.Should().Be(new DateTime(2025, 6, 16));

        cut.Render(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));
        cut.Find("[data-testid='scheduler-prev']").Click();
        date.Should().Be(new DateTime(2025, 6, 15));
    }

    /// <summary>
    /// B14: Agenda loads and navigates by AgendaDays (default 30), not by a calendar month.
    /// </summary>
    [Fact]
    public async Task Agenda_Range_And_Navigation_Follow_AgendaDays()
    {
        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TmScheduleEvent>>([]));

        DateTime date = new DateTime(2025, 5, 15);
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.AgendaDays, 30)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        await provider.Received().GetEventsAsync(
            Arg.Is<TmScheduleQuery>(q =>
                q.RangeStart == new DateTime(2025, 5, 15) &&
                q.RangeEnd == new DateTime(2025, 6, 14)),
            Arg.Any<CancellationToken>());

        cut.Find("[data-testid='scheduler-next']").Click();
        date.Should().Be(new DateTime(2025, 6, 14));

        cut.Render(p => p
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.AgendaDays, 30)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));
        cut.Find("[data-testid='scheduler-prev']").Click();
        date.Should().Be(new DateTime(2025, 5, 15));

        cut.FindComponent<TmSchedulerAgendaView>().Instance.AgendaDays.Should().Be(30);
    }

    /// <summary>
    /// A non-positive AgendaDays is normalized once: the provider, the view and the title all see one day.
    /// </summary>
    [Fact]
    public async Task AgendaDays_NonPositive_Is_Normalized_Consistently()
    {
        using var _ = CultureScope.Use("cs-CZ");

        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TmScheduleEvent>>([]));

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.AgendaDays, 0)
            .Add(c => c.DataProvider, provider));

        await provider.Received(1).GetEventsAsync(
            Arg.Is<TmScheduleQuery>(q =>
                q.RangeStart == new DateTime(2025, 6, 15) && q.RangeEnd == new DateTime(2025, 6, 16)),
            Arg.Any<CancellationToken>());
        cut.FindComponent<TmSchedulerAgendaView>().Instance.AgendaDays.Should().Be(1);
        cut.Find(".tm-scheduler-toolbar-title [aria-live]").TextContent.Trim().Should().Be("15. června 2025");
    }

    /// <summary>
    /// B24: "+N more" without an OnMoreClick delegate switches to Day view on that day.
    /// </summary>
    [Fact]
    public void Month_More_Without_Delegate_Switches_To_Day()
    {
        TmScheduleViewType? view = null;
        DateTime? date = null;
        var events = new List<TmScheduleEvent>();
        for (var i = 0; i < 4; i++)
        {
            events.Add(new TmScheduleEvent
            {
                Title = $"Evt{i}",
                Start = new DateTime(2025, 6, 10, 9 + i, 0, 0),
                End = new DateTime(2025, 6, 10, 10 + i, 0, 0)
            });
        }

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.ViewChanged, EventCallback.Factory.Create<TmScheduleViewType>(this, v => view = v))
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find(".tm-scheduler-month-more").Click();

        view.Should().Be(TmScheduleViewType.Day);
        date.Should().Be(new DateTime(2025, 6, 10));
    }

    /// <summary>
    /// The Day fallback runs only when the host binds both callbacks, so it can follow the date and
    /// the view together. With only the date bound the click is inert: the callback must not fire.
    /// </summary>
    [Fact]
    public void Month_More_With_Only_The_Date_Bound_Is_Inert()
    {
        DateTime? date = null;
        var events = new List<TmScheduleEvent>();
        for (var i = 0; i < 4; i++)
        {
            events.Add(new TmScheduleEvent
            {
                Title = $"Evt{i}",
                Start = new DateTime(2025, 6, 10, 9 + i, 0, 0),
                End = new DateTime(2025, 6, 10, 10 + i, 0, 0)
            });
        }

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => date = d)));

        cut.Find(".tm-scheduler-month-more").Click();

        date.Should().BeNull("the fallback needs both bindings, so a lone date binding stays silent");
        cut.FindAll(".tm-scheduler-month-day").Should().NotBeEmpty();
    }

    /// <summary>
    /// The month query covers exactly the rendered grid: June 2026 ends on 2026-07-13, and February
    /// 2026 with a Sunday-first week starts on the 1st and ends on 2026-03-15.
    /// </summary>
    [Theory]
    [InlineData(2026, 6, 1, "2026-07-13")]
    [InlineData(2026, 2, 0, "2026-03-15")]
    public async Task Month_Range_Covers_Rendered_Grid(int year, int month, int firstDay, string end)
    {
        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TmScheduleEvent>>([]));

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(year, month, 15))
            .Add(c => c.FirstDayOfWeek, firstDay)
            .Add(c => c.DataProvider, provider));

        var expectedStart = month == 2
            ? new DateTime(2026, 2, 1)
            : new DateTime(2026, 6, 1);
        await provider.Received(1).GetEventsAsync(
            Arg.Is<TmScheduleQuery>(q => q.RangeStart == expectedStart && q.RangeEnd == DateTime.Parse(end)),
            Arg.Any<CancellationToken>());

        // The query is exactly the grid the month view rendered, not a separately computed range.
        var rendered = cut.FindAll(".tm-scheduler-month-day").ToList();
        rendered.Should().NotBeEmpty();
        DateTime.Parse(rendered[0].GetAttribute("data-date")!).Should().Be(expectedStart);
        DateTime.Parse(rendered[^1].GetAttribute("data-date")!).AddDays(1).Should().Be(DateTime.Parse(end));
    }

    /// <summary>A Sunday-first week containing Sunday 15 June 2025 is [15 June, 22 June).</summary>
    [Fact]
    public async Task Week_Range_Respects_FirstDayOfWeek()
    {
        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TmScheduleEvent>>([]));

        Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.FirstDayOfWeek, 0)
            .Add(c => c.DataProvider, provider));

        await provider.Received(1).GetEventsAsync(
            Arg.Is<TmScheduleQuery>(q =>
                q.RangeStart == new DateTime(2025, 6, 15) && q.RangeEnd == new DateTime(2025, 6, 22)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The fallback raises the date before the view, so the reload never queries the old date in the
    /// new view: the last provider query is the opened day.
    /// </summary>
    [Fact]
    public async Task Month_More_Fallback_Loads_The_Opened_Day_Last()
    {
        var events = Enumerable.Range(0, 4).Select(i => new TmScheduleEvent
        {
            Title = $"Evt{i}",
            Start = new DateTime(2025, 6, 10, 9 + i, 0, 0),
            End = new DateTime(2025, 6, 10, 10 + i, 0, 0)
        }).ToList();

        var queries = new List<TmScheduleQuery>();
        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                queries.Add(ci.Arg<TmScheduleQuery>());
                return Task.FromResult<IReadOnlyList<TmScheduleEvent>>(events);
            });

        TmScheduleViewType view = TmScheduleViewType.Month;
        DateTime date = new DateTime(2025, 6, 15);
        IRenderedComponent<TmScheduler> cut = null!;
        void Rerender() => cut.Render(p => p
            .Add(c => c.View, view)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.Events, events)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.ViewChanged, EventCallback.Factory.Create<TmScheduleViewType>(this, v => { view = v; Rerender(); }))
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => { date = d; Rerender(); })));

        cut = Render<TmScheduler>(p => p
            .Add(c => c.View, view)
            .Add(c => c.CurrentDate, date)
            .Add(c => c.Events, events)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.ViewChanged, EventCallback.Factory.Create<TmScheduleViewType>(this, v => { view = v; Rerender(); }))
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => { date = d; Rerender(); })));

        cut.Find(".tm-scheduler-month-more").Click();

        queries.Last().RangeStart.Should().Be(new DateTime(2025, 6, 10));
        queries.Last().RangeEnd.Should().Be(new DateTime(2025, 6, 11));
    }

    /// <summary>
    /// "Today" is today in the scheduler timezone, taken from the injected TimeProvider — never DateTime.Today.
    /// </summary>
    [Fact]
    public void Today_Uses_Scheduler_TimeZone()
    {
        var prague = TimeZoneInfo.FindSystemTimeZoneById(
            TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Prague", out _)
                ? "Europe/Prague"
                : "Central Europe Standard Time");
        // 2025-06-15 22:30 UTC is already 2025-06-16 in Prague (CEST, UTC+2).
        var clock = new FakeTimeProvider(new DateTimeOffset(2025, 6, 15, 22, 30, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);

        DateTime? received = null;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.TimeZoneId, prague.Id)
            .Add(c => c.CurrentDate, new DateTime(2025, 1, 1))
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => received = d)));

        cut.Find("[data-testid='scheduler-today']").Click();

        received.Should().Be(new DateTime(2025, 6, 16));
    }

    /// <summary>MaxVisibleEvents is forwarded to the month view (default 3).</summary>
    [Fact]
    public void MaxVisibleEvents_Is_Forwarded_To_MonthView()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.MaxVisibleEvents, 5));

        cut.FindComponent<TmSchedulerMonthView>().Instance.MaxVisibleEvents.Should().Be(5);
    }

    /// <summary>
    /// Selecting a day is two-way and independent of navigation: the click raises SelectedDateChanged
    /// and the cell is marked, while CurrentDate stays put.
    /// </summary>
    [Fact]
    public void Selecting_A_Day_Is_Two_Way()
    {
        DateTime? selected = null;
        var navigated = false;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.SelectedDate, new DateTime(2025, 6, 15))
            .Add(c => c.SelectedDateChanged, EventCallback.Factory.Create<DateTime?>(this, d => selected = d))
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, _ => navigated = true)));

        cut.Find("[data-date='2025-06-15']").GetAttribute("aria-selected").Should().Be("true");
        cut.Find("[data-date='2025-06-18']").Click();

        selected.Should().Be(new DateTime(2025, 6, 18));
        navigated.Should().BeFalse("selecting a day does not navigate");
    }

    /// <summary>The selected day is forwarded to the week and day column headers.</summary>
    [Fact]
    public void SelectedDate_Is_Forwarded_To_Views()
    {
        foreach (var view in new[] { TmScheduleViewType.Week, TmScheduleViewType.Day })
        {
            var cut = Render<TmScheduler>(p => p
                .Add(c => c.View, view)
                .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
                .Add(c => c.SelectedDate, new DateTime(2025, 6, 15)));

            cut.Find(".tm-scheduler-timegrid-col--selected").TextContent.Should().Contain("15");
        }
    }

    /// <summary>A selected week or day column announces the selected day in its accessible name.</summary>
    [Fact]
    public void Week_Day_Header_Reflects_SelectedDate()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.SelectedDate, new DateTime(2025, 6, 15)));

        cut.Find(".tm-scheduler-timegrid-col--selected").GetAttribute("aria-label")
            .Should().Contain("Selected day");
    }

    /// <summary>A day click selects the day and does not clear the selected event.</summary>
    [Fact]
    public void Day_Click_Does_Not_Clear_SelectedEvent()
    {
        var standup = new TmScheduleEvent { Id = "standup", Title = "Standup", Start = new DateTime(2025, 6, 15, 9, 0, 0), End = new DateTime(2025, 6, 15, 10, 0, 0) };
        TmScheduleEvent? cleared = standup;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, new List<TmScheduleEvent> { standup })
            .Add(c => c.SelectedEvent, standup)
            .Add(c => c.SelectedEventChanged, EventCallback.Factory.Create<TmScheduleEvent?>(this, e => cleared = e)));

        cut.Find("[data-date='2025-06-18']").Click();

        cleared.Should().BeSameAs(standup, "a day click selects the day and leaves the selected event alone");
    }

    /// <summary>Clicking an event raises OnEventClick and SelectedEventChanged with the same event.</summary>
    [Fact]
    public void Selecting_An_Event_Is_Two_Way()
    {
        var standup = new TmScheduleEvent
        {
            Id = "standup", Title = "Standup",
            Start = new DateTime(2025, 6, 15, 9, 0, 0),
            End = new DateTime(2025, 6, 15, 10, 0, 0)
        };
        var review = new TmScheduleEvent
        {
            Id = "review", Title = "Review",
            Start = new DateTime(2025, 6, 16, 11, 0, 0),
            End = new DateTime(2025, 6, 16, 12, 0, 0)
        };
        TmScheduleEvent? clicked = null;
        TmScheduleEvent? selected = null;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, new List<TmScheduleEvent> { standup, review })
            .Add(c => c.SelectedEvent, standup)
            .Add(c => c.OnEventClick, EventCallback.Factory.Create<TmScheduleEvent>(this, e => clicked = e))
            .Add(c => c.SelectedEventChanged, EventCallback.Factory.Create<TmScheduleEvent?>(this, e => selected = e)));

        cut.Find(".tm-scheduler-month-event--selected").GetAttribute("aria-current").Should().Be("true");
        cut.FindAll(".tm-scheduler-month-event").Single(e => e.TextContent.Contains("Review")).Click();
        clicked!.Id.Should().Be("review");
        selected!.Id.Should().Be("review");
    }

    /// <summary>A day click raises SelectedDateChanged exactly once: the host owns it, the view does not.</summary>
    [Fact]
    public void Day_Click_Raises_SelectedDateChanged_Exactly_Once()
    {
        var raised = 0;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.SelectedDateChanged, EventCallback.Factory.Create<DateTime?>(this, _ => raised++)));

        cut.Find("[data-date='2025-06-18']").Click();

        raised.Should().Be(1);
    }

    /// <summary>A Timeline slot click selects its day through the same slot handler as the other views.</summary>
    [Fact]
    public void Timeline_Slot_Click_Selects_The_Day()
    {
        DateTime? selected = null;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.SelectedDateChanged, EventCallback.Factory.Create<DateTime?>(this, d => selected = d)));

        cut.Find(".tm-scheduler-timeline-slot").Click();

        selected.Should().Be(new DateTime(2025, 6, 15));
    }

    /// <summary>Views, the add button and the host slots are forwarded to the toolbar.</summary>
    [Fact]
    public void Toolbar_Options_Are_Forwarded()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.Views, new[] { TmScheduleViewType.Week, TmScheduleViewType.Day })
            .Add(c => c.OnAddClick, EventCallback.Factory.Create(this, () => { }))
            .Add(c => c.HeaderStart, (RenderFragment)(b => b.AddMarkupContent(0, "<span data-testid='host-start'>S</span>"))));

        cut.FindAll("[role='tab']").Select(t => t.GetAttribute("data-view")).Should().Equal("Week", "Day");
        cut.Find("[data-testid='scheduler-add']").Should().NotBeNull();
        cut.Find("[data-testid='host-start']").Should().NotBeNull();
    }

    /// <summary>The title popover navigates: the picked day becomes CurrentDate.</summary>
    [Fact]
    public void Title_Popover_Navigates_To_The_Picked_Day()
    {
        DateTime? navigated = null;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.CurrentDateChanged, EventCallback.Factory.Create<DateTime>(this, d => navigated = d)));

        cut.Find(".tm-scheduler-toolbar-title [role='button']").Click();
        cut.Find(".tm-calendar-view [data-date='2025-06-20']").Click();

        navigated.Should().Be(new DateTime(2025, 6, 20));
    }
}
