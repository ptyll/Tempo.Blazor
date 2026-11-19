using FluentAssertions;
using Tempo.Blazor.Components.Charts;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan 98c80bc3, phase 10b — the pure arithmetic behind the TmChart value axis: "nice" 1/2/5 × 10^n
/// ticks, an axis whose min and max sit on a tick, and the width-derived label spacing that replaced
/// the fixed <c>lc &gt; 24</c> / <c>maxLabels = 8</c> thresholds.
/// </summary>
public class ChartAxisScaleTests
{
    [Theory]
    [InlineData(0, 45, 0, 50, 10)]
    [InlineData(0, 16.14, 0, 20, 5)]
    [InlineData(0, 150, 0, 150, 50)]
    [InlineData(0, 100, 0, 100, 20)]
    [InlineData(0, 1, 0, 1, 0.2)]
    [InlineData(-80, 50, -80, 60, 20)]
    [InlineData(0, 0.0042, 0, 0.005, 0.001)]
    [InlineData(0, 1_200_000_000, 0, 1_200_000_000, 200_000_000)]
    public void Nice_PutsMinAndMaxOnATick_WithAOneTwoFiveStep(
        double min, double max, double expectedMin, double expectedMax, double expectedStep)
    {
        var scale = ChartAxisScale.Nice(min, max);

        scale.Min.Should().BeApproximately(expectedMin, expectedStep * 1e-9);
        scale.Max.Should().BeApproximately(expectedMax, expectedStep * 1e-9);
        scale.Step.Should().BeApproximately(expectedStep, expectedStep * 1e-9);
        scale.Min.Should().BeLessThanOrEqualTo(min);
        scale.Max.Should().BeGreaterThanOrEqualTo(max);
    }

    [Theory]
    [InlineData(0.37)]
    [InlineData(3)]
    [InlineData(7.7)]
    [InlineData(42)]
    [InlineData(999)]
    [InlineData(123456)]
    public void NiceStep_IsAlwaysOneTwoOrFiveTimesAPowerOfTen(double raw)
    {
        var step = ChartAxisScale.NiceStep(raw);

        var exponent = Math.Floor(Math.Log10(step) + 1e-9);
        var mantissa = step / Math.Pow(10, exponent);
        new[] { 1.0, 2.0, 5.0 }.Should().Contain(m => Math.Abs(m - mantissa) < 1e-9,
            "a nice step is 1, 2 or 5 × 10^n (got {0})", step);
    }

    [Fact]
    public void Ticks_AreExactMultiplesOfTheStep_WithoutFloatingPointNoise()
    {
        var scale = ChartAxisScale.Nice(0, 0.9);

        scale.Ticks().Should().Equal(0, 0.2, 0.4, 0.6, 0.8, 1.0);
    }

    [Fact]
    public void Ticks_IncludeEveryStepBetweenMinAndMax()
    {
        var scale = ChartAxisScale.Nice(-80, 50);

        scale.Ticks().Should().Equal(-80, -60, -40, -20, 0, 20, 40, 60);
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(5, 0)]
    [InlineData(0.001, 3)]
    [InlineData(250, 0)]
    public void Decimals_IsTheNumberOfFractionDigitsAStepNeeds(double step, int expected)
    {
        ChartAxisScale.DecimalsFor(step).Should().Be(expected);
    }

    [Theory]
    [InlineData(24, 22.0, 5, 2)]      // 24 hourly "00:00" labels in the 600 unit plot: 36 > 22
    [InlineData(12, 44.0, 3, 1)]      // twelve month names fit — nothing is thinned
    [InlineData(6, 88.0, 17, 2)]      // long labels still collide when 108 > 88
    [InlineData(4, 132.0, 3, 1)]
    [InlineData(60, 8.8, 2, 2)]
    public void LabelStep_KeepsEveryLabelThatFitsItsSlot_AndThinsTheRest(
        int count, double slotWidth, int longestLabelChars, int expectedStep)
    {
        ChartAxisScale.LabelStep(count, slotWidth, longestLabelChars).Should().Be(expectedStep);
    }

    [Fact]
    public void LabelStep_NeverReturnsLessThanOne_ForAnEmptyOrDegenerateSlot()
    {
        ChartAxisScale.LabelStep(0, 100, 3).Should().Be(1);
        ChartAxisScale.LabelStep(5, 0, 3).Should().Be(1);
        ChartAxisScale.LabelStep(5, double.NaN, 3).Should().Be(1);
    }

    [Fact]
    public void LabelBudget_GrowsWithTheAvailableWidth_AndNeverDropsBelowTwo()
    {
        var narrow = ChartAxisScale.LabelBudget(300, 10);
        var wide = ChartAxisScale.LabelBudget(1200, 10);

        narrow.Should().BeGreaterThanOrEqualTo(2);
        wide.Should().BeGreaterThan(narrow);
        ChartAxisScale.LabelBudget(0, 10).Should().Be(2);
        ChartAxisScale.LabelBudget(double.NaN, 10).Should().Be(2);
    }

    [Theory]
    [InlineData(8, 40.0, 1)]
    [InlineData(30, 11.3, 2)]
    [InlineData(60, 5.6, 3)]
    [InlineData(1, 5.0, 1)]
    public void RowStep_ThinsRowLabelsOnlyWhenTheyWouldTouchVertically(int count, double slotHeight, int expectedStep)
    {
        ChartAxisScale.RowStep(count, slotHeight).Should().Be(expectedStep);
    }
}