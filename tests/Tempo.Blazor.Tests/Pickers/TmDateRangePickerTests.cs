using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmDateRangePickerTests : LocalizationTestBase
{
    [Fact]
    public void DateRangePicker_ShowsTwoCalendars()
    {
        var cut = RenderComponent<TmDateRangePicker>();

        // Open the picker first
        cut.Find(".tm-date-range-trigger").Click();

        cut.FindAll(".tm-calendar").Should().HaveCount(2);
    }

    [Fact]
    public void DateRangePicker_SelectStartDate_HighlightsIt()
    {
        var cut = RenderComponent<TmDateRangePicker>();
        cut.Find(".tm-date-range-trigger").Click();

        // Click first available day in left calendar
        var firstCalendar = cut.FindAll(".tm-calendar")[0];
        firstCalendar.QuerySelectorAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)")
                     .First().Click();

        cut.FindAll(".tm-cal-day--range-start").Should().HaveCount(1);
    }

    [Fact]
    public void DateRangePicker_SelectEndDate_HighlightsRange()
    {
        var cut = RenderComponent<TmDateRangePicker>();
        cut.Find(".tm-date-range-trigger").Click();

        // Select start
        var days = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)");
        days[0].Click();
        // Select end (later day in same list)
        cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)")[4].Click();

        cut.FindAll(".tm-cal-day--in-range, .tm-cal-day--range-end").Should().NotBeEmpty();
    }

    [Fact]
    public void DateRangePicker_Hover_ShowsPreviewRange()
    {
        var cut = RenderComponent<TmDateRangePicker>();
        cut.Find(".tm-date-range-trigger").Click();

        // Click start
        var days = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)");
        days[0].Click();

        // Hover over a later day
        cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)")[5].MouseOver();

        // There should be a preview range rendered
        cut.FindAll(".tm-cal-day--in-range").Should().NotBeEmpty();
    }

    [Fact]
    public void DateRangePicker_EndBeforeStart_SetsEndAsNewStart()
    {
        (DateOnly? s, DateOnly? e)? captured = null;
        var cut = RenderComponent<TmDateRangePicker>(p => p
            .Add(c => c.ValueChanged, ((DateOnly? s, DateOnly? e) v) => captured = v)
            .Add(c => c.Value, (new DateOnly(2025, 6, 15), (DateOnly?)null)));

        cut.Find(".tm-date-range-trigger").Click();

        // Click a day earlier than the start
        var days = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)");
        days[0].Click(); // This will be earlier — treated as new start

        captured.Should().NotBeNull();
        captured!.Value.e.Should().BeNull(); // End reset
    }

    [Fact]
    public void DateRangePicker_Value_Binding_Works()
    {
        var start = new DateOnly(2025, 3, 1);
        var end   = new DateOnly(2025, 3, 31);
        var cut   = RenderComponent<TmDateRangePicker>(p => p
            .Add(c => c.Value, (start, end)));

        cut.Find(".tm-date-range-trigger").TextContent.Should().Contain("01");
    }

    [Fact]
    public void DateRangePicker_PresetButtons_ApplyRange()
    {
        (DateOnly? s, DateOnly? e)? captured = null;
        var presets = new[] { DateRangePresets.Today };
        var cut = RenderComponent<TmDateRangePicker>(p => p
            .Add(c => c.ShowPresets,  true)
            .Add(c => c.Presets,      presets)
            .Add(c => c.ValueChanged, ((DateOnly? s, DateOnly? e) v) => captured = v));

        cut.Find(".tm-date-range-trigger").Click();
        cut.Find(".tm-date-range-preset-btn").Click();

        captured.Should().NotBeNull();
        captured!.Value.s.Should().Be(DateOnly.FromDateTime(DateTime.Today));
    }

    [Fact]
    public void DateRangePicker_MinDate_MaxDate_Enforced()
    {
        var min = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 10);
        var max = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 20);
        var cut = RenderComponent<TmDateRangePicker>(p => p
            .Add(c => c.MinDate, min)
            .Add(c => c.MaxDate, max));

        cut.Find(".tm-date-range-trigger").Click();

        cut.FindAll(".tm-cal-day--disabled").Should().NotBeEmpty();
    }

    [Fact]
    public void DateRangePicker_ClearButton_ClearsRange()
    {
        (DateOnly? s, DateOnly? e)? captured = null;
        var cut = RenderComponent<TmDateRangePicker>(p => p
            .Add(c => c.Value,        (new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31)))
            .Add(c => c.ValueChanged, ((DateOnly? s, DateOnly? e) v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().NotBeNull();
        captured!.Value.s.Should().BeNull();
        captured.Value.e.Should().BeNull();
    }
}
