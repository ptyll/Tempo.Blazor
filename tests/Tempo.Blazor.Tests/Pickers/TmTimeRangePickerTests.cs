using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmTimeRangePickerTests : LocalizationTestBase
{
    [Fact]
    public void TimeRangePicker_RendersFromAndToInputs()
    {
        var cut = RenderComponent<TmTimeRangePicker>();

        cut.FindAll(".tm-time-range-from").Should().HaveCount(1);
        cut.FindAll(".tm-time-range-to").Should().HaveCount(1);
    }

    [Fact]
    public void TimeRangePicker_Value_Binding_Works()
    {
        var start = new TimeOnly(8, 0);
        var end   = new TimeOnly(17, 0);
        var cut   = RenderComponent<TmTimeRangePicker>(p => p
            .Add(c => c.Value, (start, end)));

        // Start inputs
        cut.Find(".tm-time-range-from .tm-time-seg--hours").GetAttribute("value").Should().Be("08");
        cut.Find(".tm-time-range-to .tm-time-seg--hours").GetAttribute("value").Should().Be("17");
    }

    [Fact]
    public void TimeRangePicker_EndTime_CannotBeBefore_StartTime()
    {
        var cut = RenderComponent<TmTimeRangePicker>(p => p
            .Add(c => c.Value, (new TimeOnly(17, 0), new TimeOnly(8, 0))));

        cut.FindAll(".tm-time-range--invalid").Should().NotBeEmpty();
    }

    [Fact]
    public void TimeRangePicker_Swap_SwapsValues()
    {
        (TimeOnly? s, TimeOnly? e)? captured = null;
        var cut = RenderComponent<TmTimeRangePicker>(p => p
            .Add(c => c.ShowSwapButton, true)
            .Add(c => c.Value,          (new TimeOnly(17, 0), new TimeOnly(8, 0)))
            .Add(c => c.ValueChanged,   ((TimeOnly? s, TimeOnly? e) v) => captured = v));

        cut.Find(".tm-time-range-swap-btn").Click();

        captured.Should().NotBeNull();
        captured!.Value.s.Should().Be(new TimeOnly(8, 0));
        captured.Value.e.Should().Be(new TimeOnly(17, 0));
    }

    [Fact]
    public void TimeRangePicker_Duration_Calculated()
    {
        var cut = RenderComponent<TmTimeRangePicker>(p => p
            .Add(c => c.ShowDuration, true)
            .Add(c => c.Value,        (new TimeOnly(8, 0), new TimeOnly(10, 30))));

        cut.Find(".tm-time-range-duration").TextContent.Should().Contain("2");
    }
}
