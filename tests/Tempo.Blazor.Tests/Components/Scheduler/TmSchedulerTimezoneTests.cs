using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>Timezone + DST behavior for the DateTimeOffset scheduler migration.</summary>
public class TmSchedulerTimezoneTests : LocalizationTestBase
{
    private static TimeZoneInfo Prague()
    {
        foreach (var id in new[] { "Europe/Prague", "Central Europe Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz))
            {
                return tz;
            }
        }

        throw new InvalidOperationException("Prague timezone not available on this machine.");
    }

    [Fact]
    public void ResolveTimeZone_NullId_ReturnsLocal()
    {
        var cut = Render<TmScheduler>();
        cut.Instance.ResolveTimeZone().Should().Be(TimeZoneInfo.Local);
    }

    [Fact]
    public void ResolveTimeZone_KnownId_ReturnsThatZone()
    {
        var cut = Render<TmScheduler>(p => p.Add(c => c.TimeZoneId, "Europe/Prague"));
        cut.Instance.ResolveTimeZone().BaseUtcOffset.Should().Be(TimeSpan.FromHours(1)); // CET base
    }

    [Fact]
    public void ResolveTimeZone_InvalidId_FallsBackToLocal()
    {
        var cut = Render<TmScheduler>(p => p.Add(c => c.TimeZoneId, "Not/AReal_Zone"));
        cut.Instance.ResolveTimeZone().Should().Be(TimeZoneInfo.Local);
    }

    [Fact]
    public void Recurrence_DailyAcrossSpringDst_KeepsLocalTime_ShiftsOffset()
    {
        var prague = Prague();
        // Spring-forward 2025 in Prague: Sun 30 Mar, 02:00 CET (+1) -> 03:00 CEST (+2).
        var source = new TmScheduleEvent
        {
            Title = "Standup",
            Start = new DateTimeOffset(2025, 3, 28, 9, 0, 0, TimeSpan.FromHours(1)), // 28 Mar 09:00 CET
            End = new DateTimeOffset(2025, 3, 28, 9, 30, 0, TimeSpan.FromHours(1)),
            RecurrenceRule = "FREQ=DAILY;COUNT=6"
        };

        var occ = RecurrenceEngine.ExpandRecurrence(source, new DateTime(2025, 3, 28), new DateTime(2025, 4, 3), prague);

        occ.Should().HaveCount(6);
        occ.Should().OnlyContain(o => o.StartLocal.Hour == 9 && o.StartLocal.Minute == 0); // wall-clock stays 09:00

        var before = occ.First(o => o.StartLocal.Date == new DateTime(2025, 3, 28));
        var after = occ.First(o => o.StartLocal.Date == new DateTime(2025, 3, 31));
        before.Start.Offset.Should().Be(TimeSpan.FromHours(1)); // CET
        after.Start.Offset.Should().Be(TimeSpan.FromHours(2));  // CEST (DST)
    }

    [Fact]
    public void EventsPath_ExpandsRecurrence_ForPlainEvents()
    {
        // Regression: the plain Events path previously ignored RecurrenceRule (only the provider expanded).
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                new()
                {
                    Title = "Daily",
                    Start = new DateTime(2025, 6, 2, 9, 0, 0),
                    End = new DateTime(2025, 6, 2, 10, 0, 0),
                    RecurrenceRule = "FREQ=DAILY;COUNT=5"
                }
            }));

        // 5 daily occurrences on 5 distinct June days → 5 rendered month events.
        cut.FindAll(".tm-scheduler-month-event").Count.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void NoTimeZoneId_NonRecurring_PassesEventThroughUnchanged()
    {
        // Backward compatibility: without TimeZoneId, non-recurring events are not copied/converted.
        var evt = new TmScheduleEvent
        {
            Title = "Meeting",
            Start = new DateTime(2025, 6, 10, 9, 0, 0),
            End = new DateTime(2025, 6, 10, 10, 0, 0)
        };
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, new List<TmScheduleEvent> { evt }));

        cut.FindAll(".tm-scheduler-month-event").Count.Should().Be(1);
    }

    // ── Fáze 25b: without an explicit TimeZoneId the display zone is the USER's zone ──

    [Fact]
    public void ResolveTimeZone_NoExplicitId_UsesTheResolvedUserZone()
    {
        var prague = Prague();
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(prague));

        var cut = Render<TmScheduler>();

        cut.Instance.ResolveTimeZone().Should().Be(prague,
            "the Scheduler reuses the circuit's ITmUserTimeZone when the host set no TimeZoneId");
    }

    [Fact]
    public void ResolveTimeZone_ExplicitId_WinsOverTheUserZone()
    {
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));

        var cut = Render<TmScheduler>(p => p.Add(c => c.TimeZoneId, "America/New_York"));

        cut.Instance.ResolveTimeZone().Id.Should().Be(
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York").Id,
            "an explicitly set TimeZoneId wins over the browser zone");
    }

    [Fact]
    public void ResolveTimeZone_UnresolvedUserZone_IsReturnedAsIs()
    {
        // The fixed 2.9.1 decision: UTC until the browser probe resolves the zone (never the
        // server's zone, which the old fallback returned).
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc, resolved: false));

        var cut = Render<TmScheduler>();

        cut.Instance.ResolveTimeZone().Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task NavigateToday_UsesTodayInTheUserZone()
    {
        var prague = Prague();
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(prague));

        DateTime? received = null;
        var cut = Render<TmScheduler>(p => p.Add(c => c.CurrentDateChanged, v => received = v));

        var before = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, prague).Date;
        cut.Find("[data-testid='scheduler-today']").Click();
        var after = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, prague).Date;

        received.Should().NotBeNull();
        // DateTimeOffset.Now is read twice (test + component); tolerate a midnight crossing.
        new[] { before, after }.Should().Contain(received.Value,
            "'Today' navigates to today in the user's zone, not the server's");
    }

    [Fact]
    public void Scheduler_PassesTheZoneToday_ToTheMonthView()
    {
        // Fáze 25b: the month view highlights the today cell from a parameter so the Scheduler can
        // hand down the display-zone date; without the pass-through the cell follows the machine.
        var prague = Prague();
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(prague));

        var before = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, prague).Date;
        var cut = Render<TmScheduler>(p => p.Add(c => c.View, TmScheduleViewType.Month));
        var after = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, prague).Date;

        var month = cut.FindComponent<TmSchedulerMonthView>();
        month.Instance.Today.Should().BeOneOf([before, after],
            "the Scheduler passes today in the display zone down to the month view");
    }

    [Fact]
    public void Scheduler_PassesItsOwnToday_ToTheMonthView_WithoutAZoneService()
    {
        var cut = Render<TmScheduler>(p => p.Add(c => c.View, TmScheduleViewType.Month));

        cut.FindComponent<TmSchedulerMonthView>().Instance.Today.Should().Be(DateTime.Today);
    }

    // ── Fáze 25b: the zone change must not re-query the data provider ──

    [Fact]
    public async Task ZoneChange_WithADataProvider_DoesNotCallTheProviderAgain()
    {
        // The browser zone arriving must not turn into a surprise second provider query: with no
        // explicit TimeZoneId the provider path does not normalize its loaded events anyway, and
        // the provider owns its range filtering server-side.
        var prague = Prague();
        var zone = new StubTmUserTimeZone(TimeZoneInfo.Utc);
        Services.AddSingleton<ITmUserTimeZone>(zone);

        var callCount = 0;
        var provider = Substitute.For<IScheduleDataProvider>();
        provider.GetEventsAsync(Arg.Any<TmScheduleQuery>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return new List<TmScheduleEvent>();
            });
        Services.AddSingleton(provider);

        var cut = Render<TmScheduler>(p => p.Add(c => c.DataProvider, provider));

        cut.WaitForAssertion(() => callCount.Should().Be(1), TimeSpan.FromSeconds(5));

        zone.SetZone(prague);
        cut.WaitForState(() => cut.FindAll(".tm-scheduler").Count == 1);
        await Task.Delay(100);

        callCount.Should().Be(1,
            "the zone arrival re-renders but must not re-query the provider");
    }

    [Fact]
    public async Task ZoneChange_WithPlainEvents_ReloadsTheLocalList()
    {
        // The plain Events path normalizes in process, so the zone arrival DOES re-derive the
        // loaded events (no external call involved).
        var prague = Prague();
        var zone = new StubTmUserTimeZone(TimeZoneInfo.Utc);
        Services.AddSingleton<ITmUserTimeZone>(zone);

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, new List<TmScheduleEvent>
            {
                new() { Title = "Local", Start = new DateTime(2025, 6, 10, 9, 0, 0), End = new DateTime(2025, 6, 10, 10, 0, 0) }
            }));

        zone.SetZone(prague);

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-scheduler-month-event").Count.Should().Be(1),
            TimeSpan.FromSeconds(5));
        await Task.CompletedTask;
    }

}
