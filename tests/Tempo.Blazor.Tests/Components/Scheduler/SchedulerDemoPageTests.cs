using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The <c>/scheduler</c> demo page anchors its sample week to Wednesday of the week it is opened in,
/// not to today's weekday: the phone week then always draws the focus day plus its two neighbours and
/// the Mon–Fri range section always has its events. These facts render the whole page under a pinned
/// clock on every weekday — across three different weeks, so a date memoized in a static field fails
/// here instead of only on a weekend E2E run.
/// </summary>
public class SchedulerDemoPageTests : DemoPageLocalizationTestBase
{
    /// <summary>Pins the page's clock at noon UTC (the fake provider's local zone), so the weekday is unambiguous.</summary>
    private void UseClock(DateTime localToday)
    {
        Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(localToday.Year, localToday.Month, localToday.Day, 12, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ToastService>();
    }

    private static DateTime WednesdayOfWeek(DateTime today)
        => today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7) + 2);

    [Theory]
    // One full week (Mon 9 November 2026 – Sun 15 November 2026) …
    [InlineData(2026, 11, 9)]
    [InlineData(2026, 11, 10)]
    [InlineData(2026, 11, 11)]
    [InlineData(2026, 11, 12)]
    [InlineData(2026, 11, 13)]
    [InlineData(2026, 11, 14)]
    [InlineData(2026, 11, 15)]
    // … and later weeks, so the anchor has to be recomputed per instance instead of frozen once.
    [InlineData(2026, 11, 21)]
    [InlineData(2026, 12, 6)]
    public void ThePhoneWeekFocusesWednesdayOfTheCurrentWeek(int year, int month, int day)
    {
        var today = new DateTime(year, month, day);
        UseClock(today);
        var expected = WednesdayOfWeek(today);

        var cut = Render<SchedulerPage>();

        var columns = cut.FindAll("[data-testid='scheduler-mobile-plain'] .tm-scheduler-timegrid-col[data-date]");
        columns.Should().HaveCount(3, "the focus day is shown with its two neighbours");
        columns.Select(column => column.GetAttribute("data-date")).Should().Equal(
            expected.AddDays(-1).ToString("yyyy-MM-dd"),
            expected.ToString("yyyy-MM-dd"),
            expected.AddDays(1).ToString("yyyy-MM-dd"));

        // The strip above the grid lists the whole week and marks the focused day.
        var pressed = cut.FindAll("[data-testid='scheduler-mobile-plain'] .tm-scheduler-date-strip-day")
            .Where(stripDay => stripDay.GetAttribute("aria-pressed") == "true")
            .ToList();
        pressed.Should().ContainSingle("the strip presses the focused day")
            .Which.GetAttribute("data-date").Should().Be(expected.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData(2026, 11, 9)]
    [InlineData(2026, 11, 14)]
    [InlineData(2026, 11, 15)]
    [InlineData(2026, 11, 21)]
    [InlineData(2026, 12, 6)]
    public void TheWorkWeekRangeStillHasItsEvents_WhateverTheWeekday(int year, int month, int day)
    {
        var today = new DateTime(year, month, day);
        UseClock(today);

        var cut = Render<SchedulerPage>();

        var range = cut.Find("[data-testid='scheduler-range-section']");
        var columns = range.QuerySelectorAll(".tm-scheduler-timegrid-col");
        columns.Should().HaveCount(5, "the range section clips its week to Monday–Friday");
        range.QuerySelectorAll(".tm-scheduler-timegrid-event")
            .Any(evt => evt.TextContent.Contains("Morning Standup", StringComparison.Ordinal))
            .Should().BeTrue("the standup sits on the anchored Wednesday, which is inside the work week");

        // The shared month grid and the agenda card are anchored to the same day, so they are never
        // empty either (the agenda cards are the next days of the anchored week).
        cut.FindAll("[data-testid='scheduler-mobile-agenda'] .tm-scheduler-agenda-event")
            .Should().NotBeEmpty("the agenda card lists the anchored week's events");
    }
}
