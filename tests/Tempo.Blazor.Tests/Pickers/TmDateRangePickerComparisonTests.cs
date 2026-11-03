using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Fáze 5 of plan ff043b2e — <c>TmDateRangePicker</c>'s comparison period: presets computed from the
/// injected <see cref="TimeProvider"/> with localized labels, and the
/// <c>Comparison</c> contract (previous period, same period last year, or a second window the
/// reader picks themselves) with its calendar marking and hover preview.
/// </summary>
public class TmDateRangePickerComparisonTests : LocalizationTestBase
{
    private static readonly DateOnly Oct10 = new(2026, 10, 10);
    private static readonly DateOnly Oct14 = new(2026, 10, 14);

    /// <summary>A clock pinned far away from the machine's own date, so a preset that read
    /// <c>DateTime.Today</c> could never accidentally agree with it.</summary>
    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2031, 7, 4, 9, 30, 0, TimeSpan.Zero));

    private static void UseClock(IServiceCollection services, FakeTimeProvider clock)
        => services.AddSingleton<TimeProvider>(clock);

    // bUnit 2 removed IRenderedFragment; a rendered component is the fragment a test can search.
    private static IElement Day<TComponent>(IRenderedComponent<TComponent> cut, DateOnly date)
        where TComponent : IComponent
        => cut.FindAll($".tm-cal-day[data-date='{date:yyyy-MM-dd}']:not(.tm-cal-day--other-month)").First();

    private static void UseCzechLocalizer(IServiceCollection services)
        => services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());

    // ── Presets: the clock and the labels ────────────────────────────────────────────────────

    [Fact]
    public void Presets_AreComputedFromTheInjectedClock()
    {
        UseClock(Services, Clock());
        (DateOnly? s, DateOnly? e)? captured = null;

        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.ValueChanged, ((DateOnly? s, DateOnly? e) v) => captured = v));

        cut.Find(".tm-date-range-trigger").Click();
        cut.FindAll(".tm-date-range-preset-btn").Should().HaveCount(5);
        cut.FindAll(".tm-date-range-preset-btn").First().TextContent.Trim().Should().Be("Today");
        cut.FindAll(".tm-date-range-preset-btn").First().Click();

        captured.Should().Be(((DateOnly?)new DateOnly(2031, 7, 4), (DateOnly?)new DateOnly(2031, 7, 4)),
            "the 'Today' preset must come from the injected TimeProvider, not from DateTime.Today");
    }

    [Fact]
    public void Presets_UseTheLocalizedLabels()
    {
        UseCzechLocalizer(Services);

        var cut = Render<TmDateRangePicker>();
        cut.Find(".tm-date-range-trigger").Click();

        cut.FindAll(".tm-date-range-preset-btn").Select(b => b.TextContent.Trim()).Should().Equal(
            "Dnes", "Minulý týden", "Minulý měsíc", "Minulý kvartál", "Letos");
    }

    [Fact]
    public void Presets_HostSuppliedListStillWins()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Presets, new[] { new DateRangePreset("Mine", Oct10, Oct14) }));

        cut.Find(".tm-date-range-trigger").Click();

        cut.FindAll(".tm-date-range-preset-btn").Select(b => b.TextContent.Trim()).Should().Equal("Mine");
    }

    // ── The default, comparison-free surface ─────────────────────────────────────────────────

    [Fact]
    public void WithoutComparison_RendersNoComparisonSurfaceAndRaisesNothing()
    {
        var raised = new List<(DateOnly? Start, DateOnly? End)>();
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.ComparisonRangeChanged, ((DateOnly? s, DateOnly? e) v) => raised.Add(v)));

        cut.Find(".tm-date-range-trigger").Click();

        cut.FindAll(".tm-date-range-comparison").Should().BeEmpty();
        cut.Find(".tm-date-range-picker").HasAttribute("data-comparison").Should().BeFalse(
            "a picker that compares nothing renders the pre-2.10 surface");
        cut.FindAll(".tm-cal-day--comparison, .tm-cal-day--comparison-start, .tm-cal-day--comparison-end")
            .Should().BeEmpty();
        raised.Should().BeEmpty();
    }

    // ── PreviousPeriod / PreviousYear ───────────────────────────────────────────────────────

    [Fact]
    public void PreviousPeriod_RaisesTheEquallyLongWindowThatEndsTheDayBefore()
    {
        var raised = new List<(DateOnly? Start, DateOnly? End)>();

        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.PreviousPeriod)
            .Add(c => c.ComparisonRangeChanged, ((DateOnly? s, DateOnly? e) v) => raised.Add(v)));

        raised.Should().ContainSingle().Which.Should().Be(((DateOnly?)new DateOnly(2026, 10, 5),
            (DateOnly?)new DateOnly(2026, 10, 9)),
            "a five-day window is compared against the five days immediately before it");

        cut.Find(".tm-date-range-picker").GetAttribute("data-comparison").Should().Be("previousPeriod");

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find(".tm-date-range-comparison__range").TextContent.Should()
            .Be("Previous period: 05.10.2026 – 09.10.2026");
    }

    [Fact]
    public void PreviousYear_RaisesTheSameWindowOneYearEarlier_ClampingTheLeapDay()
    {
        var raised = new List<(DateOnly? Start, DateOnly? End)>();
        var leapDay = new DateOnly(2024, 2, 29);

        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (leapDay, (DateOnly?)leapDay))
            .Add(c => c.Comparison, DateRangeComparison.PreviousYear)
            .Add(c => c.ComparisonRangeChanged, ((DateOnly? s, DateOnly? e) v) => raised.Add(v)));

        raised.Should().ContainSingle().Which.Should().Be(((DateOnly?)new DateOnly(2023, 2, 28),
            (DateOnly?)new DateOnly(2023, 2, 28)),
            "2023 has no 29 February, so the window lands on the 28th");

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find(".tm-date-range-comparison__range").TextContent.Should()
            .Be("Same period last year: 28.02.2023 – 28.02.2023");
    }

    [Fact]
    public void PreviousPeriod_FollowsTheRangeTheReaderPicks()
    {
        var raised = new List<(DateOnly? Start, DateOnly? End)>();
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Comparison, DateRangeComparison.PreviousPeriod)
            .Add(c => c.ComparisonRangeChanged, ((DateOnly? s, DateOnly? e) v) => raised.Add(v)));

        cut.Find(".tm-date-range-trigger").Click();
        var start = FirstSelectableDay(cut);
        Day(cut, start).Click();
        var end = start.AddDays(4);
        Day(cut, end).Click();

        raised.Last().Should().Be(((DateOnly?)start.AddDays(-5), (DateOnly?)start.AddDays(-1)),
            "the derived window follows the picker's own selection");
    }

    [Fact]
    public void PreviousPeriod_WithNoPrimaryRange_ReportsAnEmptyComparison()
    {
        var raised = new List<(DateOnly? Start, DateOnly? End)>();

        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Comparison, DateRangeComparison.PreviousPeriod)
            .Add(c => c.ComparisonRangeChanged, ((DateOnly? s, DateOnly? e) v) => raised.Add(v)));

        raised.Should().BeEmpty("there is nothing to compare before a primary range exists");

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find(".tm-date-range-comparison__range").TextContent.Should().Be("Not set");
    }

    // ── Custom ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Custom_LetsTheReaderPickTheSecondWindow()
    {
        var raised = new List<(DateOnly? Start, DateOnly? End)>();
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.Custom)
            .Add(c => c.ComparisonRangeChanged, ((DateOnly? s, DateOnly? e) v) => raised.Add(v)));

        cut.Find(".tm-date-range-trigger").Click();

        var targets = cut.FindAll(".tm-date-range-comparison__target");
        targets.Should().HaveCount(2, "Custom is the only mode that has to say which window the calendars edit");
        targets[0].GetAttribute("data-target").Should().Be("primary");
        targets[1].GetAttribute("data-target").Should().Be("comparison");
        targets[0].GetAttribute("aria-pressed").Should().Be("true");
        targets[1].GetAttribute("aria-pressed").Should().Be("false");

        targets[1].Click();

        cut.FindAll(".tm-date-range-comparison__target")[0].GetAttribute("aria-pressed").Should().Be("false");
        cut.FindAll(".tm-date-range-comparison__target")[1].GetAttribute("aria-pressed").Should().Be("true");

        var comparisonStart = new DateOnly(2026, 10, 20);
        var comparisonEnd = new DateOnly(2026, 10, 24);
        Day(cut, comparisonStart).Click();
        Day(cut, comparisonEnd).Click();

        raised.Should().Equal(
            new[]
            {
                ((DateOnly?)comparisonStart, (DateOnly?)null),
                ((DateOnly?)comparisonStart, (DateOnly?)comparisonEnd),
            },
            "the first click opens the window and the second completes it, exactly like the primary range");

        cut.Find(".tm-date-range-comparison__range").TextContent.Should()
            .Be("20.10.2026 – 24.10.2026");
    }

    [Fact]
    public void Custom_PickingTheComparisonWindowLeavesThePrimaryRangeAlone()
    {
        (DateOnly? s, DateOnly? e)? primary = null;
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.ValueChanged, ((DateOnly? s, DateOnly? e) v) => primary = v)
            .Add(c => c.Comparison, DateRangeComparison.Custom));

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find("[data-target='comparison']").Click();
        Day(cut, new DateOnly(2026, 10, 20)).Click();
        Day(cut, new DateOnly(2026, 10, 24)).Click();

        primary.Should().BeNull("the comparison window is a second range, not a second primary range");
    }

    [Fact]
    public void Custom_SeedsItsWindowFromTheHostSuppliedComparisonRange()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.Custom)
            .Add(c => c.ComparisonRange, ((DateOnly?)new DateOnly(2026, 10, 20),
                (DateOnly?)new DateOnly(2026, 10, 31))));

        cut.Find(".tm-date-range-trigger").Click();

        cut.Find(".tm-date-range-comparison__range").TextContent.Should().Be("20.10.2026 – 31.10.2026");
        cut.FindAll(".tm-cal-day--comparison-start").First().GetAttribute("data-date").Should().Be("2026-10-20");
    }

    // ── Calendar marking, hidden text and hover preview ─────────────────────────────────────

    [Fact]
    public void Calendar_MarksTheComparisonWindowWithItsOwnClassAndHiddenText()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.Custom)
            .Add(c => c.ComparisonRange, ((DateOnly?)new DateOnly(2026, 10, 1),
                (DateOnly?)new DateOnly(2026, 10, 5))));

        cut.Find(".tm-date-range-trigger").Click();

        var comparisonDays = cut.FindAll(".tm-cal-day--comparison");
        comparisonDays.Should().HaveCount(3, "1 and 5 October are the window's ends, 2-4 are its middle");
        comparisonDays.Select(d => d.GetAttribute("data-date")).Should()
            .Equal("2026-10-02", "2026-10-03", "2026-10-04");

        Day(cut, new DateOnly(2026, 10, 1)).ClassList.Should().Contain("tm-cal-day--comparison-start");
        Day(cut, new DateOnly(2026, 10, 5)).ClassList.Should().Contain("tm-cal-day--comparison-end");

        var hidden = cut.FindAll(".tm-sr-only").Select(e => e.TextContent.Trim()).ToList();
        hidden.Should().Contain("Comparison period: 01.10.2026 – 05.10.2026",
            "the distinct fill must have a textual counterpart, twice (one per calendar)");

        var descriptionId = Day(cut, new DateOnly(2026, 10, 2)).GetAttribute("aria-describedby");
        descriptionId.Should().NotBeNullOrEmpty("a comparison day points at the hidden description");
        cut.Find($"#{descriptionId}").TextContent.Should().Be("Comparison period: 01.10.2026 – 05.10.2026");
    }

    [Fact]
    public void Calendar_LetsThePrimaryRangeWinWhereTheTwoOverlap()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.Custom)
            .Add(c => c.ComparisonRange, ((DateOnly?)new DateOnly(2026, 10, 1),
                (DateOnly?)new DateOnly(2026, 10, 12))));

        cut.Find(".tm-date-range-trigger").Click();

        var shared = Day(cut, new DateOnly(2026, 10, 11));
        shared.ClassList.Should().Contain("tm-cal-day--in-range");
        shared.ClassList.Should().NotContain("tm-cal-day--comparison");
    }

    [Fact]
    public void Custom_HoverPreviewsTheComparisonWindowWhilePickingItsSecondDate()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.Custom));

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find("[data-target='comparison']").Click();
        Day(cut, new DateOnly(2026, 10, 20)).Click();

        Day(cut, new DateOnly(2026, 10, 24)).MouseOver();

        cut.FindAll(".tm-cal-day--comparison-end").First().GetAttribute("data-date")
            .Should().Be("2026-10-24", "the hover previews the second date of the comparison window");
        cut.FindAll(".tm-cal-day--comparison").Should().HaveCount(3);
        Day(cut, new DateOnly(2026, 10, 10)).ClassList.Should().Contain("tm-cal-day--range-start",
            "the primary range keeps its own preview semantics");
    }

    [Fact]
    public void PrimaryHover_PreviewsThePrimaryWindowAndNotTheComparison()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)null))
            .Add(c => c.Comparison, DateRangeComparison.Custom)
            .Add(c => c.ComparisonRange, ((DateOnly?)new DateOnly(2026, 10, 20),
                (DateOnly?)new DateOnly(2026, 10, 24))));

        cut.Find(".tm-date-range-trigger").Click();
        Day(cut, new DateOnly(2026, 10, 14)).MouseOver();

        cut.FindAll(".tm-cal-day--range-end").First().GetAttribute("data-date").Should().Be("2026-10-14");
        cut.FindAll(".tm-cal-day--comparison-end").First().GetAttribute("data-date").Should().Be("2026-10-24",
            "the committed comparison window is not dragged along by the primary hover");
    }

    // ── The trigger names the comparison too ────────────────────────────────────────────────

    [Fact]
    public void Trigger_ShowsTheComparisonAndNamesItInItsAccessibleName()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Label, "Report period")
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.PreviousPeriod));

        var trigger = cut.Find(".tm-date-range-trigger");
        trigger.TextContent.Should().Contain("Previous period: 05.10.2026 – 09.10.2026");

        var labelledBy = trigger.GetAttribute("aria-labelledby")!.Split(' ').ToList();
        var names = labelledBy.Select(id => cut.Find($"#{id}").TextContent.Trim()).ToList();
        names.Should().Contain(text => text.Contains("Previous period: 05.10.2026 – 09.10.2026"),
            "visible text inside the trigger must also be part of its accessible name (WCAG 2.5.3)");
    }

    [Fact]
    public void Custom_TriggerSaysWhichRangeIsTheComparedOne()
    {
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Label, "Report period")
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.Comparison, DateRangeComparison.Custom)
            .Add(c => c.ComparisonRange, ((DateOnly?)new DateOnly(2026, 10, 1),
                (DateOnly?)new DateOnly(2026, 10, 5))));

        var triggerComparison = cut.Find(".tm-date-range-trigger__comparison");
        triggerComparison.TextContent.Trim().Should()
            .Be("Comparison period: 01.10.2026 – 05.10.2026",
                "two bare date ranges inside one trigger would not say which of them is the comparison (WCAG 1.3.1 / 2.4.6)");

        var labelledBy = cut.Find(".tm-date-range-trigger").GetAttribute("aria-labelledby")!.Split(' ').ToList();
        labelledBy.Select(id => cut.Find($"#{id}").TextContent.Trim())
            .Should().Contain("Comparison period: 01.10.2026 – 05.10.2026",
                "the trigger's accessible name must carry the same wording as its visible text (WCAG 2.5.3)");

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find(".tm-date-range-comparison__range").TextContent.Trim().Should()
            .Be("01.10.2026 – 05.10.2026",
                "the popup's own visible label already says 'Comparison period', so its range span must not repeat it");
    }

    [Fact]
    public void Custom_ReopeningThePickerEditsThePrimaryRangeAgain()
    {
        var picked = new List<(DateOnly? Start, DateOnly? End)>();
        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.Value, (Oct10, (DateOnly?)Oct14))
            .Add(c => c.ValueChanged, ((DateOnly? s, DateOnly? e) v) => picked.Add(v))
            .Add(c => c.Comparison, DateRangeComparison.Custom));

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find("[data-target='comparison']").Click();
        Day(cut, new DateOnly(2026, 10, 20)).Click();
        Day(cut, new DateOnly(2026, 10, 24)).Click();

        // Finishing the comparison window closes the popup after the "look at your pick" delay.
        cut.WaitForAssertion(
            () => cut.FindAll(".tm-date-range-calendars").Should().BeEmpty(),
            TimeSpan.FromSeconds(5));

        cut.Find(".tm-date-range-trigger").Click();
        // The reopen is rendered by the picker itself, so wait for the calendars instead of assuming
        // the click has already produced the day grid.
        cut.WaitForAssertion(
            () => cut.FindAll(".tm-date-range-calendars").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(2));
        Day(cut, new DateOnly(2026, 10, 2)).Click();

        picked.Should().Contain(((DateOnly?)new DateOnly(2026, 10, 2), (DateOnly?)null),
            "reopening the picker must go back to editing the main range instead of silently carrying on "
            + "with the comparison window (WCAG 3.2 Predictable)");
    }

    [Fact]
    public void ReopeningThePicker_ShowsThePrimaryRangeMonthAgain()
    {
        var cut = Render<TmDateRangePicker>(p => p.Add(c => c.Value, (Oct10, (DateOnly?)Oct14)));

        cut.Find(".tm-date-range-trigger").Click();
        // Browse two months ahead, the way a reader checking a neighbouring month would.
        cut.FindAll(".tm-cal-next")[0].Click();
        cut.FindAll(".tm-cal-next")[0].Click();
        cut.FindAll(".tm-cal-day[data-date='2026-10-02']:not(.tm-cal-day--other-month)").Should()
            .BeEmpty("the left calendar is showing December by now");

        cut.Find(".tm-date-range-footer button").Click();
        cut.Find(".tm-date-range-trigger").Click();

        Day(cut, new DateOnly(2026, 10, 2)).Should().NotBeNull(
            "a reopen must come back to the reported range's month, not to the month browsed last time "
            + "(WCAG 3.2 Predictable)");
    }

    [Fact]
    public async Task ReopeningBeforeTheAutoCloseFires_KeepsThePopupOpen()
    {
        var cut = Render<TmDateRangePicker>(p => p.Add(c => c.Value, (Oct10, (DateOnly?)Oct14)));

        cut.Find(".tm-date-range-trigger").Click();
        // Completing the range schedules the "look at your pick" auto-close a moment later.
        Day(cut, new DateOnly(2026, 10, 20)).Click();
        Day(cut, new DateOnly(2026, 10, 24)).Click();

        // Close and reopen before that delay elapses — the scheduled close must not fire at the
        // reopened popup.
        cut.Find(".tm-date-range-footer button").Click();
        cut.Find(".tm-date-range-trigger").Click();

        await Task.Delay(TimeSpan.FromMilliseconds(800));
        var stillOpen = false;
        await cut.InvokeAsync(() => stillOpen = cut.FindAll(".tm-date-range-calendars").Count > 0);

        stillOpen.Should().BeTrue(
            "a pending auto-close belongs to the pick that scheduled it and must never shut a popup "
            + "the reader has already reopened");
    }

    // ── Preset caching stays correct across midnight ────────────────────────────────────────

    [Fact]
    public void DefaultPresets_FollowTheClockToANewDay()
    {
        var clock = Clock();
        UseClock(Services, clock);
        var picked = new List<(DateOnly? Start, DateOnly? End)>();

        var cut = Render<TmDateRangePicker>(p => p
            .Add(c => c.ValueChanged, ((DateOnly? s, DateOnly? e) v) => picked.Add(v)));

        cut.Find(".tm-date-range-trigger").Click();

        // A parent re-render after midnight must not keep yesterday's cached preset row.
        clock.Advance(TimeSpan.FromDays(1));
        cut.Render();
        cut.FindAll(".tm-date-range-preset-btn").First().Click();

        picked.Should().ContainSingle().Which.Should().Be(
            ((DateOnly?)new DateOnly(2031, 7, 5), (DateOnly?)new DateOnly(2031, 7, 5)),
            "the preset row is cached per day, so the day it was built for has to be part of the cache key");
    }

    // ── Stylesheet guard: the compared window is more than a fill ──────────────────────────

    [Fact]
    public void ComparisonDays_CarryAVisibleEdgeBeyondTheirFill()
    {
        ThemeCss.Property("_calendar-grid.css", ".tm-cal-day--comparison", "box-shadow").Should()
            .NotBeNullOrWhiteSpace(
                "the middle days' fill is ~1.2:1 against the light surface, so the span needs an edge "
                + "that sighted low-vision readers can follow (WCAG 1.4.11)")
            .And.Contain("var(--tm-cal-comparison-border)",
                "the edge has to be the same semantic token as the window's ends");
    }

    private static DateOnly FirstSelectableDay<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => DateOnly.ParseExact(
            cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)")
               .First().GetAttribute("data-date")!,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
}
