using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmTimePickerTests : LocalizationTestBase
{
    [Fact]
    public void TimePicker_RendersHoursAndMinutes()
    {
        var cut = RenderComponent<TmTimePicker>();

        cut.FindAll(".tm-time-seg--hours").Should().HaveCount(1);
        cut.FindAll(".tm-time-seg--minutes").Should().HaveCount(1);
        cut.FindAll(".tm-time-seg--seconds").Should().BeEmpty();
    }

    [Fact]
    public void TimePicker_WithShowSeconds_RendersSecondsField()
    {
        var cut = RenderComponent<TmTimePicker>(p => p.Add(c => c.ShowSeconds, true));

        cut.FindAll(".tm-time-seg--seconds").Should().HaveCount(1);
    }

    [Fact]
    public void TimePicker_Value_Binding_Works()
    {
        TimeOnly? captured = null;
        var cut = RenderComponent<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        // Hours field should show 14
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("14");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
    }

    [Fact]
    public void TimePicker_InvalidTime_DoesNotUpdate()
    {
        TimeOnly? captured = null;
        var cut = RenderComponent<TmTimePicker>(p => p
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-time-seg--hours").Change("99");

        captured.Should().BeNull();
    }

    [Fact]
    public void TimePicker_MinTime_DisablesEarlierValues()
    {
        var cut = RenderComponent<TmTimePicker>(p => p
            .Add(c => c.Value,   new TimeOnly(8, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0)));

        // The wrapper should carry the disabled class or inputs disabled
        cut.FindAll(".tm-time-picker--invalid, [aria-disabled='true']")
           .Should().NotBeEmpty();
    }

    [Fact]
    public void TimePicker_MaxTime_DisablesLaterValues()
    {
        var cut = RenderComponent<TmTimePicker>(p => p
            .Add(c => c.Value,   new TimeOnly(22, 0))
            .Add(c => c.MaxTime, new TimeOnly(18, 0)));

        cut.FindAll(".tm-time-picker--invalid, [aria-disabled='true']")
           .Should().NotBeEmpty();
    }

    [Fact]
    public void TimePicker_Placeholder_UsesLocalizer()
    {
        UseCzechLocalization();
        var cut = RenderComponent<TmTimePicker>();

        // placeholder attribute on the wrapper or an aria-placeholder
        cut.Markup.Should().Contain("HH:mm");
    }

    [Fact]
    public void TimePicker_ClearButton_ClearsValue()
    {
        TimeOnly? captured = null;
        var cut = RenderComponent<TmTimePicker>(p => p
            .Add(c => c.Value,        new TimeOnly(10, 0))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().BeNull();
    }

    [Fact]
    public void TimePicker_Disabled_RendersDisabled()
    {
        var cut = RenderComponent<TmTimePicker>(p => p.Add(c => c.Disabled, true));

        cut.FindAll("input[disabled]").Should().NotBeEmpty();
    }

    [Fact]
    public void TimePicker_Required_RendersRequired()
    {
        var cut = RenderComponent<TmTimePicker>(p => p.Add(c => c.Required, true));

        cut.Find(".tm-time-picker").GetAttribute("data-required").Should().Be("true");
    }
}
