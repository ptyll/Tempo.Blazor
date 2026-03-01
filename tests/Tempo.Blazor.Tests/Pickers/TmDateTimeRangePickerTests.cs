using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmDateTimeRangePickerTests : LocalizationTestBase
{
    [Fact]
    public void DateTimeRangePicker_ShowsDateAndTimeForBothBounds()
    {
        var cut = RenderComponent<TmDateTimeRangePicker>();

        // Should render two TmDateTimePicker sections
        cut.FindAll(".tm-datetime-range-start").Should().HaveCount(1);
        cut.FindAll(".tm-datetime-range-end").Should().HaveCount(1);
    }

    [Fact]
    public void DateTimeRangePicker_Value_Binding_Works()
    {
        var start = new DateTime(2025, 6, 1, 8, 0, 0);
        var end   = new DateTime(2025, 6, 30, 18, 0, 0);
        var cut   = RenderComponent<TmDateTimeRangePicker>(p => p
            .Add(c => c.Value, (start, end)));

        // Start section shows hours 08
        cut.Find(".tm-datetime-range-start .tm-time-seg--hours")
           .GetAttribute("value").Should().Be("08");
        // End section shows hours 18
        cut.Find(".tm-datetime-range-end .tm-time-seg--hours")
           .GetAttribute("value").Should().Be("18");
    }

    [Fact]
    public void DateTimeRangePicker_EndBeforeStart_IsValidationError()
    {
        var start = new DateTime(2025, 6, 15, 12, 0, 0);
        var end   = new DateTime(2025, 6, 10, 8, 0, 0);
        var cut   = RenderComponent<TmDateTimeRangePicker>(p => p
            .Add(c => c.Value, (start, end)));

        cut.FindAll(".tm-datetime-range--invalid, [role='alert']").Should().NotBeEmpty();
    }

    [Fact]
    public void DateTimeRangePicker_ClearButton_ClearsBothValues()
    {
        (DateTime? s, DateTime? e)? captured = null;
        var cut = RenderComponent<TmDateTimeRangePicker>(p => p
            .Add(c => c.Value,        (new DateTime(2025, 1, 1, 9, 0, 0), new DateTime(2025, 1, 31, 17, 0, 0)))
            .Add(c => c.ValueChanged, ((DateTime? s, DateTime? e) v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().NotBeNull();
        captured!.Value.s.Should().BeNull();
        captured.Value.e.Should().BeNull();
    }
}
