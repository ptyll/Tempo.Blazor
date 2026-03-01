using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmDatePickerTests : LocalizationTestBase
{
    [Fact]
    public void DatePicker_ClosedByDefault_ShowsInputOnly()
    {
        var cut = RenderComponent<TmDatePicker>();

        cut.FindAll(".tm-calendar").Should().BeEmpty();
        cut.FindAll(".tm-date-picker-trigger").Should().HaveCount(1);
    }

    [Fact]
    public void DatePicker_ClickInput_OpensCalendar()
    {
        var cut = RenderComponent<TmDatePicker>();

        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-calendar").Should().HaveCount(1);
    }

    [Fact]
    public void DatePicker_SelectDate_ClosesCalendarAndUpdatesValue()
    {
        DateOnly? captured = null;
        var cut = RenderComponent<TmDatePicker>(p => p
            .Add(c => c.ValueChanged, (DateOnly? v) => captured = v));

        cut.Find(".tm-date-picker-trigger").Click();
        // Click on a day button that is in current month (not disabled, not other-month)
        var dayBtn = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)").First();
        dayBtn.Click();

        cut.FindAll(".tm-calendar").Should().BeEmpty();
        captured.Should().NotBeNull();
    }

    [Fact]
    public void DatePicker_Value_Binding_Works()
    {
        var date = new DateOnly(2025, 6, 15);
        var cut  = RenderComponent<TmDatePicker>(p => p.Add(c => c.Value, date));

        cut.Find(".tm-date-picker-trigger").TextContent.Should().Contain("15");
    }

    [Fact]
    public void DatePicker_MinDate_DisablesEarlierDays()
    {
        var min = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 15);
        var cut = RenderComponent<TmDatePicker>(p => p.Add(c => c.MinDate, min));

        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--disabled").Should().NotBeEmpty();
    }

    [Fact]
    public void DatePicker_MaxDate_DisablesLaterDays()
    {
        var max = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 5);
        var cut = RenderComponent<TmDatePicker>(p => p.Add(c => c.MaxDate, max));

        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--disabled").Should().NotBeEmpty();
    }

    [Fact]
    public void DatePicker_Navigate_PrevMonth_ChangesDisplayMonth()
    {
        var cut = RenderComponent<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();

        var titleBefore = cut.Find(".tm-cal-title").TextContent;
        cut.Find("[aria-label='Previous month']").Click();
        var titleAfter = cut.Find(".tm-cal-title").TextContent;

        titleAfter.Should().NotBe(titleBefore);
    }

    [Fact]
    public void DatePicker_Navigate_NextMonth_ChangesDisplayMonth()
    {
        var cut = RenderComponent<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();

        var titleBefore = cut.Find(".tm-cal-title").TextContent;
        cut.Find("[aria-label='Next month']").Click();
        var titleAfter = cut.Find(".tm-cal-title").TextContent;

        titleAfter.Should().NotBe(titleBefore);
    }

    [Fact]
    public void DatePicker_Today_Highlighted()
    {
        var cut = RenderComponent<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--today").Should().HaveCount(1);
    }

    [Fact]
    public void DatePicker_SelectedDate_Highlighted()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var cut = RenderComponent<TmDatePicker>(p => p.Add(c => c.Value, today));
        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--selected").Should().HaveCount(1);
    }

    [Fact]
    public void DatePicker_ClearButton_ClearsValue()
    {
        DateOnly? captured = null;
        var cut = RenderComponent<TmDatePicker>(p => p
            .Add(c => c.Value,        new DateOnly(2025, 1, 1))
            .Add(c => c.ValueChanged, (DateOnly? v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().BeNull();
    }

    [Fact]
    public void DatePicker_Escape_ClosesCalendar()
    {
        var cut = RenderComponent<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();
        cut.FindAll(".tm-calendar").Should().HaveCount(1);

        cut.Find(".tm-date-picker").KeyDown(Key.Escape);

        cut.FindAll(".tm-calendar").Should().BeEmpty();
    }

    [Fact]
    public void DatePicker_Format_AppliedToDisplayValue()
    {
        var date = new DateOnly(2025, 3, 5);
        var cut  = RenderComponent<TmDatePicker>(p => p
            .Add(c => c.Value,      date)
            .Add(c => c.DateFormat, "yyyy-MM-dd"));

        cut.Find(".tm-date-picker-trigger").TextContent.Trim().Should().Be("2025-03-05");
    }
}
