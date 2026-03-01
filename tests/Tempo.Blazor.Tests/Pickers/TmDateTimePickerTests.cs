using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmDateTimePickerTests : LocalizationTestBase
{
    [Fact]
    public void DateTimePicker_RendersDateAndTimeSections()
    {
        var cut = RenderComponent<TmDateTimePicker>();

        cut.Find(".tm-date-picker-trigger").Should().NotBeNull();
        cut.FindAll(".tm-time-input").Should().HaveCount(1);
    }

    [Fact]
    public void DateTimePicker_Value_Binding_Works()
    {
        var dt  = new DateTime(2025, 6, 15, 10, 30, 0);
        var cut = RenderComponent<TmDateTimePicker>(p => p.Add(c => c.Value, dt));

        cut.Find(".tm-date-picker-trigger").TextContent.Should().Contain("15");
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("10");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
    }

    [Fact]
    public void DateTimePicker_SelectDate_KeepsExistingTime()
    {
        DateTime? captured = null;
        var cut = RenderComponent<TmDateTimePicker>(p => p
            .Add(c => c.Value,        new DateTime(2025, 6, 15, 14, 45, 0))
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        // Open calendar and select a day
        cut.Find(".tm-date-picker-trigger").Click();
        var day = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)").First();
        day.Click();

        captured.Should().NotBeNull();
        captured!.Value.Hour.Should().Be(14);
        captured.Value.Minute.Should().Be(45);
    }

    [Fact]
    public void DateTimePicker_ChangeTime_KeepsExistingDate()
    {
        DateTime? captured = null;
        var cut = RenderComponent<TmDateTimePicker>(p => p
            .Add(c => c.Value,        new DateTime(2025, 6, 15, 14, 45, 0))
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        cut.Find(".tm-time-seg--hours").Change("9");

        captured.Should().NotBeNull();
        captured!.Value.Year.Should().Be(2025);
        captured.Value.Month.Should().Be(6);
        captured.Value.Day.Should().Be(15);
    }

    [Fact]
    public void DateTimePicker_ClearButton_ClearsDateAndTime()
    {
        DateTime? captured = null;
        var cut = RenderComponent<TmDateTimePicker>(p => p
            .Add(c => c.Value,        new DateTime(2025, 6, 15, 10, 0, 0))
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().BeNull();
    }

    [Fact]
    public void DateTimePicker_MinDateTime_Enforced()
    {
        var cut = RenderComponent<TmDateTimePicker>(p => p
            .Add(c => c.Value,    new DateTime(2025, 1, 1, 7, 0, 0))
            .Add(c => c.MinValue, new DateTime(2025, 1, 1, 9, 0, 0)));

        cut.FindAll(".tm-time-picker--invalid, .tm-datetime-picker--invalid")
           .Should().NotBeEmpty();
    }

    [Fact]
    public void DateTimePicker_MaxDateTime_Enforced()
    {
        var cut = RenderComponent<TmDateTimePicker>(p => p
            .Add(c => c.Value,    new DateTime(2025, 12, 31, 23, 0, 0))
            .Add(c => c.MaxValue, new DateTime(2025, 12, 31, 20, 0, 0)));

        cut.FindAll(".tm-time-picker--invalid, .tm-datetime-picker--invalid")
           .Should().NotBeEmpty();
    }
}
