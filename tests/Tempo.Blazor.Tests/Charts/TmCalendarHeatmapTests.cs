using System.Globalization;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>TDD tests for the calendar heatmap date grid.</summary>
public class TmCalendarHeatmapTests : LocalizationTestBase
{
    private static readonly IReadOnlyDictionary<DateOnly, decimal> NoValues =
        new Dictionary<DateOnly, decimal>();

    [Fact]
    public void CalendarHeatmap_RendersRootAndEveryDayOfNonLeapYear()
    {
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.Year, 2025));

        cut.Find(".tm-calendar-heatmap").Should().NotBeNull();
        cut.FindAll(".tm-calendar-heatmap__day").Should().HaveCount(365);
    }

    [Theory]
    [InlineData("cs-CZ", "grid-column: 1", "grid-row: 3")]
    [InlineData("en-US", "grid-column: 1", "grid-row: 4")]
    public void CalendarHeatmap_AlignsFirstWeekToCulturesFirstDay(
        string cultureName,
        string expectedColumn,
        string expectedRow)
    {
        using var _ = CultureScope.Use(cultureName);

        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.Year, 2025));

        var firstDay = cut.Find("[data-date='2025-01-01']");
        firstDay.GetAttribute("style").Should()
            .Contain(expectedColumn)
            .And.Contain(expectedRow);
    }

    [Fact]
    public void CalendarHeatmap_FromAndToLimitRenderedDatesInclusively()
    {
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.From, new DateOnly(2025, 3, 10))
            .Add(component => component.To, new DateOnly(2025, 3, 16)));

        var days = cut.FindAll(".tm-calendar-heatmap__day");

        days.Should().HaveCount(7);
        days.Should().OnlyContain(day => day.HasAttribute("data-date"));
        days.Select(day => day.GetAttribute("data-date")).Should().Equal(
            "2025-03-10",
            "2025-03-11",
            "2025-03-12",
            "2025-03-13",
            "2025-03-14",
            "2025-03-15",
            "2025-03-16");
        cut.FindAll("[data-date='2025-03-09']").Should().BeEmpty();
        cut.FindAll("[data-date='2025-03-17']").Should().BeEmpty();
    }

    [Fact]
    public void CalendarHeatmap_YearTakesPrecedenceOverFromAndTo()
    {
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.Year, 2025)
            .Add(component => component.From, new DateOnly(2024, 3, 10))
            .Add(component => component.To, new DateOnly(2024, 3, 16)));

        cut.FindAll(".tm-calendar-heatmap__day").Should().HaveCount(365);
        cut.Find("[data-date='2025-01-01']").Should().NotBeNull();
        cut.FindAll("[data-date='2024-03-10']").Should().BeEmpty();
    }

    [Fact]
    public void CalendarHeatmap_CellSizeSetsRootCssCustomProperty()
    {
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.Year, 2025)
            .Add(component => component.CellSize, 18));

        cut.Find(".tm-calendar-heatmap")
            .GetAttribute("style")
            .Should().Contain("--tm-heatmap-cell-size: 18px");
    }

    [Fact]
    public void CalendarHeatmap_AssignsLevelsFromExplicitMaximum()
    {
        var values = new Dictionary<DateOnly, decimal>
        {
            [new(2025, 1, 2)] = 0m,
            [new(2025, 1, 3)] = 25m,
            [new(2025, 1, 4)] = 100m,
        };

        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, values)
            .Add(component => component.From, new DateOnly(2025, 1, 1))
            .Add(component => component.To, new DateOnly(2025, 1, 4))
            .Add(component => component.MaxValue, 100m));

        cut.Find("[data-date='2025-01-01']").ClassList.Should()
            .Contain("tm-calendar-heatmap__day--level-0");
        cut.Find("[data-date='2025-01-02']").ClassList.Should()
            .Contain("tm-calendar-heatmap__day--level-0");
        cut.Find("[data-date='2025-01-03']").ClassList.Should()
            .Contain("tm-calendar-heatmap__day--level-1");
        cut.Find("[data-date='2025-01-04']").ClassList.Should()
            .Contain("tm-calendar-heatmap__day--level-4");
    }

    [Fact]
    public void CalendarHeatmap_InfersMaximumFromValuesInsideRenderedRange()
    {
        var values = new Dictionary<DateOnly, decimal>
        {
            [new(2024, 12, 31)] = 1_000m,
            [new(2025, 1, 1)] = 2m,
            [new(2025, 1, 2)] = 8m,
        };

        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, values)
            .Add(component => component.From, new DateOnly(2025, 1, 1))
            .Add(component => component.To, new DateOnly(2025, 1, 2)));

        cut.Find("[data-date='2025-01-01']").ClassList.Should()
            .Contain("tm-calendar-heatmap__day--level-1");
        cut.Find("[data-date='2025-01-02']").ClassList.Should()
            .Contain("tm-calendar-heatmap__day--level-4");
    }

    [Fact]
    public void CalendarHeatmap_AllZeroValuesStayAtLevelZero()
    {
        var values = new Dictionary<DateOnly, decimal>
        {
            [new(2025, 1, 1)] = 0m,
            [new(2025, 1, 2)] = 0m,
        };

        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, values)
            .Add(component => component.From, new DateOnly(2025, 1, 1))
            .Add(component => component.To, new DateOnly(2025, 1, 2)));

        cut.FindAll(".tm-calendar-heatmap__day").Should()
            .OnlyContain(day => day.ClassList.Contains("tm-calendar-heatmap__day--level-0"));
    }

    [Fact]
    public void CalendarHeatmap_LevelsChangesHighestLevel()
    {
        var date = new DateOnly(2025, 1, 1);

        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, new Dictionary<DateOnly, decimal> { [date] = 10m })
            .Add(component => component.From, date)
            .Add(component => component.To, date)
            .Add(component => component.Levels, 7));

        cut.Find("[data-date='2025-01-01']").ClassList.Should()
            .Contain("tm-calendar-heatmap__day--level-6");
    }

    [Fact]
    public void CalendarHeatmap_DaysInSameBucketUseSamePaletteLevel()
    {
        var values = new Dictionary<DateOnly, decimal>
        {
            [new(2025, 1, 1)] = 75m,
            [new(2025, 1, 2)] = 100m,
        };

        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, values)
            .Add(component => component.From, new DateOnly(2025, 1, 1))
            .Add(component => component.To, new DateOnly(2025, 1, 2))
            .Add(component => component.MaxValue, 100m)
            .Add(component => component.Levels, 3));

        var days = cut.FindAll(".tm-calendar-heatmap__grid .tm-calendar-heatmap__day--level-2");
        days.Should().HaveCount(2);
        days.Should().OnlyContain(day =>
            day.GetAttribute("style")!.Contains(
                "--tm-heatmap-day-color: var(--tm-heatmap-level-4)",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CalendarHeatmap_SuccessSchemeAddsRootModifier()
    {
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.Year, 2025)
            .Add(component => component.ColorScheme, CalendarHeatmapColorScheme.Success));

        cut.Find(".tm-calendar-heatmap").ClassList.Should()
            .Contain("tm-calendar-heatmap--success");
    }

    [Fact]
    public void CalendarHeatmap_TooltipAndAriaUseLocalizedDateAndFormattedValue()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var date = new DateOnly(2025, 1, 2);
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, new Dictionary<DateOnly, decimal> { [date] = 1_234.5m })
            .Add(component => component.From, date)
            .Add(component => component.To, date)
            .Add(component => component.Culture, culture));

        var day = cut.Find("[data-date='2025-01-02']");
        day.GetAttribute("title").Should().Be("1/2/2025 — 1,234.50");
        day.GetAttribute("aria-label").Should().Be("1/2/2025 — 1,234.50");
    }

    [Fact]
    public void CalendarHeatmap_UsesValueFormatterAndLocalizedNoData()
    {
        var valuedDate = new DateOnly(2025, 1, 1);
        var emptyDate = new DateOnly(2025, 1, 2);
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, new Dictionary<DateOnly, decimal> { [valuedDate] = 42m })
            .Add(component => component.From, valuedDate)
            .Add(component => component.To, emptyDate)
            .Add(component => component.Culture, CultureInfo.GetCultureInfo("en-US"))
            .Add(component => component.ValueFormatter, value => $"USD {value:0}"));

        cut.Find("[data-date='2025-01-01']").GetAttribute("title")
            .Should().Be("1/1/2025 — USD 42");
        cut.Find("[data-date='2025-01-02']").GetAttribute("title")
            .Should().Be("1/2/2025 — No data");
    }

    [Fact]
    public void CalendarHeatmap_ClickReportsValueOrNull()
    {
        var valuedDate = new DateOnly(2025, 1, 1);
        var emptyDate = new DateOnly(2025, 1, 2);
        var clicks = new List<CalendarHeatmapDayClickEventArgs>();
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, new Dictionary<DateOnly, decimal> { [valuedDate] = 42m })
            .Add(component => component.From, valuedDate)
            .Add(component => component.To, emptyDate)
            .Add(component => component.OnDayClick, clicks.Add));

        cut.Find("[data-date='2025-01-01']").Click();
        cut.Find("[data-date='2025-01-02']").Click();

        clicks.Should().Equal(
            new CalendarHeatmapDayClickEventArgs(valuedDate, 42m),
            new CalendarHeatmapDayClickEventArgs(emptyDate, null));
    }

    [Fact]
    public void CalendarHeatmap_MonthLabelsUseCultureAndAlignToFirstWeek()
    {
        var culture = CultureInfo.GetCultureInfo("cs-CZ");
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.From, new DateOnly(2025, 1, 1))
            .Add(component => component.To, new DateOnly(2025, 2, 28))
            .Add(component => component.Culture, culture));

        var labels = cut.FindAll(".tm-calendar-heatmap__month-label");
        labels.Select(label => label.TextContent).Should().Equal(
            culture.DateTimeFormat.GetAbbreviatedMonthName(1),
            culture.DateTimeFormat.GetAbbreviatedMonthName(2));
        labels[0].GetAttribute("style").Should().Contain("grid-column: 1");
        labels[1].GetAttribute("style").Should().Contain("grid-column: 5");
    }

    [Fact]
    public void CalendarHeatmap_DayLabelsUseFirstThirdAndFifthCultureRows()
    {
        var culture = CultureInfo.GetCultureInfo("cs-CZ");
        var expected = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }
            .Select(day => culture.DateTimeFormat.GetAbbreviatedDayName(day));
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.From, new DateOnly(2025, 1, 1))
            .Add(component => component.To, new DateOnly(2025, 1, 7))
            .Add(component => component.Culture, culture));

        var labels = cut.FindAll(".tm-calendar-heatmap__day-label");
        labels.Select(label => label.TextContent).Should().Equal(expected);
        labels.Select(label => label.GetAttribute("style")).Should().Equal(
            "grid-row: 1",
            "grid-row: 3",
            "grid-row: 5");
    }

    [Fact]
    public void CalendarHeatmap_VisibilityFlagsHideLabelsAndLegend()
    {
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.Year, 2025)
            .Add(component => component.ShowMonthLabels, false)
            .Add(component => component.ShowDayLabels, false)
            .Add(component => component.ShowLegend, false));

        cut.FindAll(".tm-calendar-heatmap__month-label").Should().BeEmpty();
        cut.FindAll(".tm-calendar-heatmap__day-label").Should().BeEmpty();
        cut.FindAll(".tm-calendar-heatmap__legend").Should().BeEmpty();
    }

    [Fact]
    public void CalendarHeatmap_LegendUsesLocalizedEndpointsAndOneSwatchPerLevel()
    {
        var cut = Render<TmCalendarHeatmap>(parameters => parameters
            .Add(component => component.Values, NoValues)
            .Add(component => component.Year, 2025)
            .Add(component => component.Levels, 7));

        var legend = cut.Find(".tm-calendar-heatmap__legend");
        legend.TextContent.Should().Contain("Less").And.Contain("More");
        legend.QuerySelectorAll(".tm-calendar-heatmap__legend-level").Should().HaveCount(7);
    }

    private static IDisposable UseCulture(string name)
        => CultureScope.Use(name);
}
