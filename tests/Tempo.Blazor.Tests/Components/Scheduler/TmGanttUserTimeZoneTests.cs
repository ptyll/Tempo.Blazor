using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Fáze 25b: "today" in <see cref="TmGantt"/> (the today marker, new-task defaults, the overdue
/// highlight, the embedded calendar date) is judged in the circuit's <see cref="ITmUserTimeZone"/>
/// — UTC on Server until the browser reports — instead of the machine's <c>DateTime.Today</c>.
/// </summary>
public class TmGanttUserTimeZoneTests : LocalizationTestBase
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

    private static IReadOnlyList<TmWorkItem> Items() =>
    [
        new TmWorkItem { Id = "1", Title = "Design", Start = new DateTime(2026, 1, 5), End = new DateTime(2026, 1, 9) },
        new TmWorkItem { Id = "2", Title = "Build", Start = new DateTime(2026, 1, 8), End = new DateTime(2026, 1, 16) },
    ];

    private static double MarkerLeft(IRenderedComponent<TmGantt> cut)
    {
        var style = cut.Find("[data-testid='today-marker']").GetAttribute("style");
        style.Should().NotBeNull().And.Contain("left:");
        var px = style!.Split("left:")[1].Split("px")[0].Trim();
        return double.Parse(px, CultureInfo.InvariantCulture);
    }

    private static double PixelPerDay(IRenderedComponent<TmGantt> cut)
    {
        // The timeline content width is totalDays * pixelPerDay; the range is public API.
        var style = cut.Find(".tm-gantt__timeline-content").GetAttribute("style");
        style.Should().NotBeNull().And.Contain("width:");
        var width = double.Parse(style!.Split("width:")[1].Split("px")[0].Trim(), CultureInfo.InvariantCulture);
        var (start, end) = GanttHelper.GetTimeRange(Items());
        return width / (end - start).TotalDays;
    }

    [Fact]
    public void TodayMarker_UsesTodayInTheUserZone()
    {
        var prague = Prague();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 22, 30, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(prague));

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        var ppd = PixelPerDay(cut);
        var (start, _) = GanttHelper.GetTimeRange(Items());
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), prague).Date;

        MarkerLeft(cut).Should().BeApproximately((today - start.Date).TotalDays * ppd, 0.5,
            "the marker sits at (today in the user zone - timeline start) * pixelPerDay");
    }

    [Fact]
    public void TodayMarker_WithoutAUserZoneService_MatchesMachineToday()
    {
        // No ITmUserTimeZone registered (a bare test context): the machine-local today of the old
        // behaviour.
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        var ppd = PixelPerDay(cut);
        var (start, _) = GanttHelper.GetTimeRange(Items());
        var expected = (DateTime.Today - start.Date).TotalDays * ppd;

        MarkerLeft(cut).Should().BeApproximately(expected, 0.5,
            "without a zone service the marker falls back to the machine-local today");
    }

    [Fact]
    public void TodayInZone_UnresolvedUserZone_ReadsUtc()
    {
        // The fixed 2.9.1 decision: UTC until the browser probe resolves the zone.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 22, 30, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc, resolved: false));

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        cut.Instance.TodayInZone().Should().Be(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Utc).Date);
    }

    [Fact]
    public void TodayInZone_WithoutAService_ReadsMachineLocal()
    {
        // No zone service: the machine-local calendar day of the injected clock, never DateTime.Today.
        var clock = new ZonedClock(
            new DateTimeOffset(2026, 6, 15, 22, 30, 0, TimeSpan.Zero), TimeZoneInfo.Local);
        Services.AddSingleton<TimeProvider>(clock);

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        cut.Instance.TodayInZone().Should().Be(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Local).Date);
    }

    [Fact]
    public void TodayInZone_ResolvedLocalZone_ReadsTimeProvider_NotMachineToday()
    {
        // A resolved zone that equals TimeZoneInfo.Local used to short-circuit to DateTime.Today,
        // so a display zone matching the machine ignored the injected clock. The day comes from
        // TimeProvider, converted into that zone.
        var clock = new ZonedClock(
            DateTime.Today.AddDays(1).AddHours(15), TimeZoneInfo.Local);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Local));

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        var expected = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Local).Date;
        expected.Should().NotBe(DateTime.Today, "the fixture only proves the bug when the clocks disagree");
        cut.Instance.TodayInZone().Should().Be(expected);
    }

    [Fact]
    public void CurrentTimeOffset_HoursZoom_UsesTimeProvider()
    {
        // Hours zoom places the current-time marker from TimeProvider, not DateTime.Now.
        var clock = new ZonedClock(
            new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.ZoomPreset, GanttZoomPreset.Hours));

        var style = cut.Find("[data-testid='current-time-marker']").GetAttribute("style");
        style.Should().NotBeNull().And.Contain("left:");
        var left = double.Parse(style!.Split("left:")[1].Split("px")[0].Trim(), CultureInfo.InvariantCulture);

        var (start, _) = GanttHelper.GetTimeRange(Items());
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Utc).DateTime;
        var pixelPerDay = PixelPerDay(cut);
        var expected = (now - start).TotalHours * (pixelPerDay / 24.0);
        left.Should().BeApproximately(expected, 0.5);
        left.Should().NotBeApproximately(
            (DateTime.Now - start).TotalHours * (pixelPerDay / 24.0), 0.5,
            "the marker must not sit at the machine clock");
    }

    [Fact]
    public void CurrentTimeOffset_HoursZoom_UsesZonedClock_OnTheSpringForwardDay()
    {
        // 2026-03-29 is the Europe/Prague spring-forward day. 03:30 local is 01:30 UTC, and a
        // conversion that dropped the offset would place the marker an hour off.
        var prague = Prague();
        var local = new DateTime(2026, 3, 29, 3, 30, 0);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, prague);
        var clock = new ZonedClock(new DateTimeOffset(utc, TimeSpan.Zero), prague);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(prague));

        var items = new List<TmWorkItem>
        {
            new() { Id = "1", Title = "Design", Start = new DateTime(2026, 3, 23), End = new DateTime(2026, 3, 27) },
            new() { Id = "2", Title = "Build", Start = new DateTime(2026, 3, 26), End = new DateTime(2026, 4, 3) },
        };
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, items)
            .Add(c => c.ZoomPreset, GanttZoomPreset.Hours));

        var style = cut.Find("[data-testid='current-time-marker']").GetAttribute("style");
        var left = double.Parse(style!.Split("left:")[1].Split("px")[0].Trim(), CultureInfo.InvariantCulture);
        var (start, end) = GanttHelper.GetTimeRange(items);
        var width = double.Parse(
            cut.Find(".tm-gantt__timeline-content").GetAttribute("style")!.Split("width:")[1].Split("px")[0].Trim(),
            CultureInfo.InvariantCulture);
        var pixelPerHour = width / (end - start).TotalDays / 24.0;
        var expected = (local - start).TotalHours * pixelPerHour;
        left.Should().BeApproximately(expected, 0.5, "03:30 Prague on the spring-forward day");
        left.Should().NotBeApproximately(expected - pixelPerHour, 0.5, "the marker is not an hour early");
    }

    /// <summary>A fake clock whose local zone is the one the test pins. FakeTimeProvider.LocalTimeZone is read-only.</summary>
    private sealed class ZonedClock(DateTimeOffset start, TimeZoneInfo zone) : FakeTimeProvider(start)
    {
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    [Fact]
    public void WorkloadView_Window_IsCappedAtThePassedToday()
    {
        // Fáze 25b: TmGantt passes TodayInZone(); the rolling 4-week window must be capped at
        // (today − 7) in THAT zone's today, not the machine's DateTime.Today. A today 40 days out
        // from the machine's makes the difference visible: the first visible column is
        // today − 7 in the passed value, and machine-today-derived columns are all gone.
        var today = DateTime.Today.AddDays(40);
        var tasks = new[]
        {
            new TmWorkItem
            {
                Id = "1",
                Title = "Span",
                Start = today.AddDays(-10),
                End = today.AddDays(10),
                Assignees = [new TmWorkItemAssignee { Id = "u1", Name = "User One" }]
            },
        };

        var cut = Render<TmGanttWorkloadView>(p => p
            .Add(c => c.Tasks, tasks)
            .Add(c => c.WorkingSchedule, new WorkingSchedule { NonWorkingDaysOfWeek = [] })
            .Add(c => c.Today, today));

        var headers = cut.FindAll(".tm-gantt__workload-header-cell");
        headers.Count.Should().BeGreaterThan(1, "one assignee header plus one column per date");
        headers[1].TextContent.Trim().Should().Be(
            today.AddDays(-7).ToString("dd.MM", CultureInfo.InvariantCulture),
            "the window is capped at (Today parameter − 7 days)");
    }

    [Fact]
    public void WorkloadView_Today_Parameter_Defaults_To_Machine_Today()
    {
        var today = DateTime.Today;
        var tasks = new[]
        {
            new TmWorkItem
            {
                Id = "1",
                Title = "Span",
                Start = today.AddDays(-10),
                End = today.AddDays(10),
                Assignees = [new TmWorkItemAssignee { Id = "u1", Name = "User One" }]
            },
        };

        var cut = Render<TmGanttWorkloadView>(p => p
            .Add(c => c.Tasks, tasks)
            .Add(c => c.WorkingSchedule, new WorkingSchedule { NonWorkingDaysOfWeek = [] }));

        cut.Instance.Today.Should().Be(DateTime.Today);
        cut.FindAll(".tm-gantt__workload-header-cell")[1].TextContent.Trim().Should().Be(
            today.AddDays(-7).ToString("dd.MM", CultureInfo.InvariantCulture));
    }
}
