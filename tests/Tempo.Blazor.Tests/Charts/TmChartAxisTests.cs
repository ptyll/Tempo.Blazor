using System.Globalization;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan 98c80bc3, phase 10b — TmChart value axis (nice 1/2/5 ticks aligned to the domain), culture-aware
/// text (axis, values, tooltips; <c>data-*</c> and SVG coordinates stay invariant), the opt-in
/// <c>ValueFormat</c>/<c>Culture</c> parameters and the width-derived thinning of category labels.
/// </summary>
public class TmChartAxisTests : LocalizationTestBase
{
    // The report the finding comes from: a temperature series whose raw min..max/5 ticks read 16.14, 11.28, 6.42.
    private static ChartData Temperatures => new()
    {
        Labels = ["Po", "Út", "St", "Čt", "Pá"],
        Datasets = [new ChartDataset { Label = "°C", Values = [6.42, 11.28, 16.14, 9.5, 12.1], Color = "#2563eb" }]
    };

    private static ChartData Hourly(int count) => new()
    {
        Labels = Enumerable.Range(0, count).Select(i => $"{i:00}:00").ToArray(),
        Datasets = [new ChartDataset { Label = "kW", Values = Enumerable.Range(0, count).Select(i => (double)(i % 7)).ToArray(), Color = "#2563eb" }]
    };

    private static double D(AngleSharp.Dom.IElement el, string name)
        => double.Parse(el.GetAttribute(name)!, CultureInfo.InvariantCulture);

    private static string[] AxisTexts(IRenderedComponent<TmChart> cut)
        => cut.FindAll("text.tm-chart__axis-label").Select(t => t.TextContent).ToArray();

    // ── nice ticks ──

    [Theory]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Area)]
    public void ValueAxis_UsesNiceTicks_InsteadOfMinMaxOverFive(ChartType type)
    {
        using var _ = CultureScope.Use("en-US");
        var cut = Render<TmChart>(p => p.Add(x => x.Type, type).Add(x => x.Data, Temperatures));

        AxisTexts(cut).Should().Equal("0", "5", "10", "15", "20");
    }

    [Fact]
    public void ValueAxis_TopTickSitsOnTheTopOfThePlot_AndTheZeroTickOnTheBottom()
    {
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, Temperatures));

        var lines = cut.FindAll("line.tm-chart__grid-line").Select(l => D(l, "y1")).ToArray();
        lines.Should().HaveCount(5);
        lines.Max().Should().BeApproximately(360, 0.01, "the 0 tick is the bottom of the plot");
        lines.Min().Should().BeApproximately(20, 0.01, "the 20 tick is the top of the plot — max aligned to a tick");
    }

    [Fact]
    public void ValueAxis_NegativeValues_ExtendTheDomainToATickBelowZero()
    {
        using var _ = CultureScope.Use("en-US");
        var data = new ChartData
        {
            Labels = ["A", "B", "C"],
            Datasets = [new ChartDataset { Label = "Cash", Values = [-80, 12, 50], Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, data));

        AxisTexts(cut).Should().Equal("-80", "-60", "-40", "-20", "0", "20", "40", "60");
        cut.FindAll("line.tm-chart__axis-zero").Should().HaveCount(1);
    }

    [Fact]
    public void ValueAxis_StackedBar_TopsOutOnATickAboveTheLargestStack()
    {
        using var _ = CultureScope.Use("en-US");
        var data = new ChartData
        {
            Labels = ["Q1", "Q2"],
            Datasets =
            [
                new ChartDataset { Label = "A", Values = [30, 10], Color = "#2563eb" },
                new ChartDataset { Label = "B", Values = [15, 20], Color = "#16a34a" }
            ]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.StackedBar).Add(x => x.Data, data));

        AxisTexts(cut).Should().Equal("0", "10", "20", "30", "40", "50");
    }

    [Fact]
    public void ValueAxis_FlatSeries_StillGetsAReadableAxis()
    {
        using var _ = CultureScope.Use("en-US");
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets = [new ChartDataset { Label = "Flat", Values = [0, 0], Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Line).Add(x => x.Data, data));

        AxisTexts(cut).Should().Equal("0", "0.2", "0.4", "0.6", "0.8", "1");
    }

    [Fact]
    public void ValueAxis_WideNumbers_WidenTheLeftGutter_SoTheLabelsAreNotClipped()
    {
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets = [new ChartDataset { Label = "Big", Values = [1_500_000, 2_400_000], Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, data));

        var gridStart = D(cut.Find("line.tm-chart__grid-line"), "x1");
        gridStart.Should().BeGreaterThan(50, "a 7-digit label needs more than the 50-unit default gutter");
        var longest = AxisTexts(cut).Max(t => t.Length);
        gridStart.Should().BeGreaterThanOrEqualTo(longest * 7, "every label ends 8 units left of the plot and must fit in the gutter");
    }

    // ── culture ──

    [Fact]
    public void ValueAxis_UsesTheCurrentCulture_DecimalComma()
    {
        using var _ = CultureScope.Use("cs-CZ");
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets = [new ChartDataset { Label = "x", Values = [0.2, 0.9], Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, data));

        AxisTexts(cut).Should().Equal("0", "0,2", "0,4", "0,6", "0,8", "1");
    }

    [Fact]
    public void ValueLabels_AreCultureFormatted_ButDataAttributesStayInvariant()
    {
        using var _ = CultureScope.Use("cs-CZ");
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets = [new ChartDataset { Label = "t", Values = [6.42, 11.5], Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Heatmap)
            .Add(x => x.Data, data)
            .Add(x => x.ShowValues, true));

        cut.FindAll("rect.tm-chart__heatmap-cell").Select(c => c.GetAttribute("data-value"))
            .Should().Equal("6.42", "11.5");
        cut.FindAll("text.tm-chart__heatmap-value").Select(t => t.TextContent)
            .Should().OnlyContain(t => t == "6,42" || t == "11,5");

        var bar = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data)
            .Add(x => x.ShowValues, true));
        bar.FindAll("text.tm-chart__value").Select(t => t.TextContent).Should().Equal("6,42", "11,5");
        bar.FindAll("rect.tm-chart__bar").Select(r => r.GetAttribute("x")).Should().OnlyContain(
            x => !x!.Contains(','), "SVG coordinates are invariant, never a decimal comma");
    }

    [Fact]
    public void Tooltip_FormatsTheValueWithTheCurrentCulture()
    {
        using var _ = CultureScope.Use("cs-CZ");
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, Temperatures));

        cut.FindAll("rect.tm-chart__bar")[0].MouseOver();

        cut.Find(".tm-chart__tooltip-value").TextContent.Should().Contain("6,42");
    }

    [Fact]
    public void IndexTooltip_FormatsEveryValueWithTheCurrentCulture()
    {
        using var _ = CultureScope.Use("cs-CZ");
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, Temperatures)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll(".tm-chart__index-zone")[0].MouseOver();

        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("6,42");
    }

    [Fact]
    public void Culture_Parameter_OverridesTheCurrentCulture()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Temperatures)
            .Add(x => x.ShowValues, true)
            .Add(x => x.Culture, CultureInfo.GetCultureInfo("cs-CZ")));

        cut.FindAll("text.tm-chart__value").Select(t => t.TextContent).First().Should().Be("6,42");
    }

    [Fact]
    public void ValueFormat_AppliesToAxisValuesAndTooltips()
    {
        using var _ = CultureScope.Use("cs-CZ");
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Temperatures)
            .Add(x => x.ShowValues, true)
            .Add(x => x.ValueFormat, "0.0 °C"));

        AxisTexts(cut).Should().Equal("0,0 °C", "5,0 °C", "10,0 °C", "15,0 °C", "20,0 °C");
        cut.FindAll("text.tm-chart__value").Select(t => t.TextContent).First().Should().Be("6,4 °C");

        cut.FindAll("rect.tm-chart__bar")[0].MouseOver();
        cut.Find(".tm-chart__tooltip-value").TextContent.Should().Contain("6,4 °C");
    }

    [Fact]
    public void ValueFormat_Null_KeepsTheDefaultTwoDecimalCultureFormat()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, Temperatures)
            .Add(x => x.ShowValues, true));

        cut.FindAll("text.tm-chart__value").Select(t => t.TextContent).First().Should().Be("6.42");
    }

    [Fact]
    public void TimeAxis_LabelsAndTooltips_FollowTheCulture()
    {
        using var _ = CultureScope.Use("cs-CZ");
        var data = new ChartData
        {
            Labels = [],
            TimePoints = [new DateTime(2025, 1, 1), new DateTime(2025, 1, 4), new DateTime(2025, 1, 9)],
            Datasets = [new ChartDataset { Label = "x", Values = [1.5, 2.5, 3.5], Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Line).Add(x => x.Data, data));

        cut.FindAll("text.tm-chart__label").Select(t => t.TextContent).First().Should().Be(new DateTime(2025, 1, 1).ToString("d", CultureInfo.GetCultureInfo("cs-CZ")));
    }

    // ── label thinning from the available width ──

    [Fact]
    public void CategoryLabels_TwentyFourHourlyLabels_AreThinnedSoTheyNeverOverlap()
    {
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, Hourly(24)));

        var xs = cut.FindAll("text.tm-chart__label").Select(t => D(t, "x")).ToArray();
        xs.Length.Should().BeLessThan(24, "24 five-character labels do not fit 530 units");
        xs.Zip(xs.Skip(1), (a, b) => b - a).Should().OnlyContain(
            gap => gap >= 5 * ChartAxisScale.LabelCharWidth, "neighbouring labels keep their own width");
    }

    [Fact]
    public void CategoryLabels_ShortLabels_AreAllKept()
    {
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, Hourly(6)));

        cut.FindAll("text.tm-chart__label").Should().HaveCount(6);
    }

    [Fact]
    public void CategoryLabels_FortyEightMonthlyPoints_AreThinnedToTheWidth()
    {
        var labels = Enumerable.Range(0, 48).Select(i => $"M{i}").ToArray();
        var data = new ChartData
        {
            Labels = labels,
            Datasets = [new ChartDataset { Label = "V", Values = Enumerable.Range(0, 48).Select(i => (double)i).ToArray(), Color = "#3b82f6" }]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Bar).Add(x => x.Data, data));

        var xs = cut.FindAll("text.tm-chart__label").Select(t => D(t, "x")).ToArray();
        xs.Length.Should().BeInRange(12, 26);
        xs.Zip(xs.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= 3 * ChartAxisScale.LabelCharWidth);
    }

    [Fact]
    public void CategoryLabels_LineAndStackedBar_AreThinnedLikeBars()
    {
        foreach (var type in new[] { ChartType.Line, ChartType.StackedBar, ChartType.Area })
        {
            var cut = Render<TmChart>(p => p.Add(x => x.Type, type).Add(x => x.Data, Hourly(24)));

            cut.FindAll("text.tm-chart__label").Count.Should().BeLessThan(24, "{0} thins its category labels", type);
        }
    }

    [Fact]
    public void CategoryLabels_Waterfall_AreThinnedLikeBars()
    {
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Waterfall).Add(x => x.Data, Hourly(24)));

        var xs = cut.FindAll("text.tm-chart__label").Select(t => D(t, "x")).ToArray();
        xs.Length.Should().BeLessThan(24);
        xs.Zip(xs.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= 5 * ChartAxisScale.LabelCharWidth);
    }

    [Fact]
    public void CategoryLabels_HeatmapColumns_AreThinnedLikeBars()
    {
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Heatmap).Add(x => x.Data, Hourly(24)));

        var columns = cut.FindAll("text.tm-chart__axis-label")
            .Where(t => t.TextContent.Contains(':'))
            .Select(t => D(t, "x")).ToArray();
        columns.Length.Should().BeInRange(1, 23);
        columns.Zip(columns.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= 5 * ChartAxisScale.LabelCharWidth);
    }
    [Fact]
    public void HorizontalBar_RowLabels_AreThinnedWhenTheRowsAreTooTight()
    {
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.HorizontalBar).Add(x => x.Data, Hourly(48)));

        var ys = cut.FindAll("text.tm-chart__label").Select(t => D(t, "y")).ToArray();
        ys.Length.Should().BeLessThan(48);
        ys.Zip(ys.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= ChartAxisScale.RowLabelHeight - 0.01);
    }

    [Fact]
    public void TimeAxis_LabelCount_FollowsTheAvailableWidth_NotAFixedEight()
    {
        var points = Enumerable.Range(0, 60).Select(i => new DateTime(2025, 1, 1).AddDays(i)).ToArray();
        var data = new ChartData
        {
            Labels = [],
            TimePoints = points,
            Datasets = [new ChartDataset { Label = "x", Values = points.Select((_, i) => (double)i).ToArray(), Color = "#2563eb" }]
        };
        var cut = Render<TmChart>(p => p.Add(x => x.Type, ChartType.Line).Add(x => x.Data, data));

        var xs = cut.FindAll("text.tm-chart__label").Select(t => D(t, "x")).ToArray();
        var longest = cut.FindAll("text.tm-chart__label").Max(t => t.TextContent.Length);
        xs.Zip(xs.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= longest * ChartAxisScale.LabelCharWidth);
    }
}
