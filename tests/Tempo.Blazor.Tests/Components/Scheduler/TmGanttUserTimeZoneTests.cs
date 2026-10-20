using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(prague));

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        var ppd = PixelPerDay(cut);
        var (start, _) = GanttHelper.GetTimeRange(Items());
        var before = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, prague).Date;
        var after = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, prague).Date;

        var left = MarkerLeft(cut);
        // DateTimeOffset.Now is read twice (test + component); tolerate a midnight crossing.
        var candidates = new[] { before, after }.Distinct()
            .Select(d => (d - start.Date).TotalDays * ppd);
        candidates.Should().Contain(e => Math.Abs(e - left) < 0.5,
            "the marker sits at (today in the user zone - timeline start) * pixelPerDay, got {0}", left);
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
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc, resolved: false));

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        cut.Instance.TodayInZone().Should().Be(
            TimeZoneInfo.ConvertTime(DateTimeOffset.Now, TimeZoneInfo.Utc).Date);
    }

    [Fact]
    public void TodayInZone_WithoutAService_ReadsMachineLocal()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Items()));

        cut.Instance.TodayInZone().Should().Be(DateTime.Today);
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
