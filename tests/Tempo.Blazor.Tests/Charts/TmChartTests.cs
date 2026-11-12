using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>TDD tests for TmChart SVG component.</summary>
public class TmChartTests : LocalizationTestBase
{
    private static ChartData SimpleBarData => new()
    {
        Labels = ["Jan", "Feb", "Mar"],
        Datasets =
        [
            new ChartDataset { Label = "Sales", Values = [10, 20, 30], Color = "#3b82f6" }
        ]
    };

    private static ChartData MultiDatasetData => new()
    {
        Labels = ["Q1", "Q2", "Q3"],
        Datasets =
        [
            new ChartDataset { Label = "2024", Values = [100, 200, 150], Color = "#3b82f6" },
            new ChartDataset { Label = "2025", Values = [120, 180, 220], Color = "#ef4444" }
        ]
    };

    private static ChartData StackedData => new()
    {
        Labels = ["Q1", "Q2", "Q3"],
        Datasets =
        [
            new ChartDataset { Label = "Licenses", Values = [10, 20, 30], Color = "#2563eb" },
            new ChartDataset { Label = "Services", Values = [5, 10, 15], Color = "#16a34a" }
        ]
    };

    private static ChartData PieData => new()
    {
        Labels = ["A", "B", "C"],
        Datasets =
        [
            new ChartDataset { Label = "Share", Values = [40, 35, 25], Color = "#3b82f6" }
        ]
    };

    // ── Render basics ──

    [Fact]
    public void Chart_Renders_SvgElement()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData));

        cut.Find("svg").Should().NotBeNull();
        cut.Find(".tm-chart").Should().NotBeNull();
    }

    [Fact]
    public void Chart_WidthHeight_AppliedAsStyle()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.Width, "500px")
            .Add(x => x.Height, "400px"));

        var wrapper = cut.Find(".tm-chart");
        var style = wrapper.GetAttribute("style") ?? "";
        style.Should().Contain("width:").And.Contain("500px");
        style.Should().Contain("height:").And.Contain("400px");
    }

    [Fact]
    public void Chart_HasAriaLabel()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData));

        cut.Find("svg").GetAttribute("aria-label").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Chart_CustomClass_Applied()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.Class, "my-chart"));

        cut.Find(".tm-chart").ClassList.Should().Contain("my-chart");
    }

    // ── Bar chart ──

    [Fact]
    public void BarChart_Renders_Rects()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData));

        // Should render rect elements for each data point
        var rects = cut.FindAll("rect.tm-chart__bar");
        rects.Count.Should().Be(3);
    }

    [Fact]
    public void BarChart_MultiDataset_GroupedBars()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiDatasetData));

        // 2 datasets × 3 labels = 6 bars
        var rects = cut.FindAll("rect.tm-chart__bar");
        rects.Count.Should().Be(6);
    }

    [Fact]
    public void BarChart_ShowsLabels()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData));

        var markup = cut.Markup;
        markup.Should().Contain("Jan");
        markup.Should().Contain("Feb");
        markup.Should().Contain("Mar");
    }

    // ── Line chart ──

    [Fact]
    public void LineChart_Renders_Polyline()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, SimpleBarData));

        cut.FindAll("polyline.tm-chart__line").Count.Should().Be(1);
    }

    [Fact]
    public void LineChart_Renders_DataPoints()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, SimpleBarData));

        cut.FindAll("circle.tm-chart__point").Count.Should().Be(3);
    }

    // ── Pie chart ──

    [Fact]
    public void PieChart_Renders_Paths()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Pie)
            .Add(x => x.Data, PieData));

        // 3 slices
        cut.FindAll("path.tm-chart__slice").Count.Should().Be(3);
    }

    // ── Donut chart ──

    [Fact]
    public void DonutChart_Renders_Paths()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Donut)
            .Add(x => x.Data, PieData));

        cut.FindAll("path.tm-chart__slice").Count.Should().Be(3);
    }

    // ── Horizontal bar ──

    [Fact]
    public void HorizontalBarChart_Renders_Rects()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.HorizontalBar)
            .Add(x => x.Data, SimpleBarData));

        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(3);
    }

    // ── Stacked bars ──

    [Fact]
    public void StackedBarChart_RendersSegmentsOnSharedCategoryPositions_UsingStackTotalScale()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.StackedBar)
            .Add(x => x.Data, StackedData)
            .Add(x => x.ShowValues, true));

        var bars = cut.FindAll("rect.tm-chart__bar");
        bars.Should().HaveCount(6);

        // Dataset segments for one label share X and touch vertically.
        AttributeAsDouble(bars[0], "x").Should().Be(AttributeAsDouble(bars[1], "x"));
        (AttributeAsDouble(bars[1], "y") + AttributeAsDouble(bars[1], "height"))
            .Should().BeApproximately(AttributeAsDouble(bars[0], "y"), 0.02);

        // The maximum stack is Q3 = 30 + 15, not the maximum individual value (30).
        cut.FindAll("text.tm-chart__axis-label")
            .Select(x => x.TextContent)
            .Should().Contain("45");

        cut.FindAll("text.tm-chart__value")
            .Select(x => x.TextContent)
            .Should().Equal(["10", "5", "20", "10", "30", "15"]);
        cut.FindAll(".tm-chart__legend-label")
            .Select(x => x.TextContent)
            .Should().Equal(["Licenses", "Services"]);
    }

    [Fact]
    public void StackedHorizontalBarChart_RendersSegmentsOnSharedCategoryPositions_UsingStackTotalScale()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.StackedHorizontalBar)
            .Add(x => x.Data, StackedData));

        var bars = cut.FindAll("rect.tm-chart__bar");
        bars.Should().HaveCount(6);

        // Dataset segments for one label share Y and touch horizontally.
        AttributeAsDouble(bars[0], "y").Should().Be(AttributeAsDouble(bars[1], "y"));
        (AttributeAsDouble(bars[0], "x") + AttributeAsDouble(bars[0], "width"))
            .Should().BeApproximately(AttributeAsDouble(bars[1], "x"), 0.02);

        // Q3 is the maximum stack (45), so its two segments span the full 530px plot width.
        (AttributeAsDouble(bars[4], "width") + AttributeAsDouble(bars[5], "width"))
            .Should().BeApproximately(530, 0.02);
    }

    [Fact]
    public void StackedBarChart_SegmentClick_ReturnsOriginalDatasetAndValue()
    {
        ChartSegment? clicked = null;
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.StackedBar)
            .Add(x => x.Data, StackedData)
            .Add(x => x.OnSegmentClick, segment => clicked = segment));

        cut.FindAll("rect.tm-chart__bar")[3].Click();

        clicked.Should().Be(new ChartSegment(1, 1, "Q2", 10));
    }

    [Theory]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.StackedHorizontalBar)]
    public void StackedBarChart_ShowValues_SuppressesLabelsThatDoNotFit(ChartType type)
    {
        var data = new ChartData
        {
            Labels = ["Total"],
            Datasets =
            [
                new ChartDataset { Label = "Large", Values = [1000], Color = "#2563eb" },
                new ChartDataset { Label = "Tiny", Values = [1], Color = "#16a34a" }
            ]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, type)
            .Add(x => x.Data, data)
            .Add(x => x.ShowValues, true));

        cut.FindAll("text.tm-chart__value")
            .Select(x => x.TextContent)
            .Should().Equal(["1000"]);
    }

    // ── Per-value colors ──

    [Fact]
    public void DonutChart_BackgroundColors_UsesCustomColorsForEachSlice()
    {
        var data = ChartDataWithBackgroundColors(["#dc2626", "#ea580c", "#16a34a"]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Donut)
            .Add(x => x.Data, data));

        AssertFillAttributes(cut.FindAll("path.tm-chart__slice"), ["#dc2626", "#ea580c", "#16a34a"]);
    }

    [Fact]
    public void PieChart_BackgroundColors_UsesCustomColorsForEachSlice()
    {
        var data = ChartDataWithBackgroundColors(["#dc2626", "#ea580c", "#16a34a"]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Pie)
            .Add(x => x.Data, data));

        AssertFillAttributes(cut.FindAll("path.tm-chart__slice"), ["#dc2626", "#ea580c", "#16a34a"]);
    }

    [Fact]
    public void BarChart_BackgroundColors_UsesCustomColorsForEachBar()
    {
        var data = ChartDataWithBackgroundColors(["#dc2626", "#ea580c", "#16a34a"]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        AssertFillAttributes(cut.FindAll("rect.tm-chart__bar"), ["#dc2626", "#ea580c", "#16a34a"]);
    }

    [Fact]
    public void HorizontalBarChart_BackgroundColors_UsesCustomColorsForEachBar()
    {
        var data = ChartDataWithBackgroundColors(["#dc2626", "#ea580c", "#16a34a"]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.HorizontalBar)
            .Add(x => x.Data, data));

        AssertFillAttributes(cut.FindAll("rect.tm-chart__bar"), ["#dc2626", "#ea580c", "#16a34a"]);
    }

    [Fact]
    public void BarChart_BackgroundColors_FallsBackToDatasetColor_WhenLessColorsThanValues()
    {
        var data = ChartDataWithBackgroundColors(
            ["#dc2626", "#ea580c"],
            backgroundColor: "#0d9488",
            color: "#2563eb");

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        AssertFillAttributes(cut.FindAll("rect.tm-chart__bar"), ["#dc2626", "#ea580c", "#0d9488"]);
    }

    [Fact]
    public void DonutChart_BackgroundColors_FallsBackToPalette_WhenLessColorsThanValues()
    {
        var data = ChartDataWithBackgroundColors(["#dc2626", "#ea580c"]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Donut)
            .Add(x => x.Data, data));

        AssertFillAttributes(cut.FindAll("path.tm-chart__slice"), ["#dc2626", "#ea580c", "var(--tm-chart-series-3)"]);
    }

    [Fact]
    public void Chart_NullBackgroundColors_KeepsBackwardCompatibleBehavior()
    {
        var donutCut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Donut)
            .Add(x => x.Data, PieData));
        var barCut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData));

        AssertFillAttributes(donutCut.FindAll("path.tm-chart__slice"),
            ["var(--tm-chart-series-1)", "var(--tm-chart-series-2)", "var(--tm-chart-series-3)"]);
        // The bar dataset carries its own Color — a caller-passed colour renders verbatim (the one
        // documented exception to the token-only markup contract).
        AssertFillAttributes(barCut.FindAll("rect.tm-chart__bar"), ["#3b82f6", "#3b82f6", "#3b82f6"]);
    }

    // ── Legend ──

    [Fact]
    public void Chart_ShowLegend_RendersLegend()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiDatasetData)
            .Add(x => x.ShowLegend, true));

        cut.FindAll(".tm-chart__legend-item").Count.Should().Be(2);
        cut.Markup.Should().Contain("2024");
        cut.Markup.Should().Contain("2025");
    }

    [Fact]
    public void Chart_ShowLegend_BackgroundColors_UsesPerValueLegendColors()
    {
        var data = ChartDataWithBackgroundColors(
            ["#dc2626", "#ea580c", "#16a34a"],
            color: "#3b82f6");

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Donut)
            .Add(x => x.Data, data)
            .Add(x => x.ShowLegend, true));

        cut.FindAll(".tm-chart__legend-label")
            .Select(element => element.TextContent)
            .Should()
            .Equal(["A", "B", "C"]);
        AssertLegendColorStyles(cut.FindAll(".tm-chart__legend-color"), ["#dc2626", "#ea580c", "#16a34a"]);
    }

    [Fact]
    public void Chart_HideLegend_NoLegend()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.ShowLegend, false));

        cut.FindAll(".tm-chart__legend").Count.Should().Be(0);
    }

    // ── Grid ──

    [Fact]
    public void Chart_ShowGrid_RendersGridLines()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.ShowGrid, true));

        cut.FindAll("line.tm-chart__grid-line").Count.Should().BeGreaterThan(0);
    }

    // ── Values ──

    [Fact]
    public void Chart_ShowValues_DisplaysValueText()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.ShowValues, true));

        var markup = cut.Markup;
        markup.Should().Contain("10");
        markup.Should().Contain("20");
        markup.Should().Contain("30");
    }

    // ── Segment click ──

    [Fact]
    public void Chart_SegmentClick_FiresCallback()
    {
        ChartSegment? clicked = null;
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.OnSegmentClick, seg => clicked = seg));

        // Click the first bar
        cut.FindAll("rect.tm-chart__bar")[0].Click();

        clicked.Should().NotBeNull();
        clicked!.DatasetIndex.Should().Be(0);
        clicked.Index.Should().Be(0);
        clicked.Label.Should().Be("Jan");
        clicked.Value.Should().Be(10);
    }

    // ── Animated ──

    [Fact]
    public void Chart_Animated_HasAnimatedClass()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.Animated, true));

        cut.Find(".tm-chart").ClassList.Should().Contain("tm-chart--animated");
    }

    // ── Empty data ──

    [Fact]
    public void Chart_EmptyData_ShowsMessage()
    {
        var emptyData = new ChartData { Labels = [], Datasets = [] };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, emptyData));

        cut.Find(".tm-chart__empty").Should().NotBeNull();
    }

    // ── Interactive legend (K10) ──

    [Fact]
    public void InteractiveLegend_Default_LegendItemsAreNotButtons()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiDatasetData));

        // Backward compatible: without opting in, legend items stay plain divs.
        cut.FindAll("button.tm-chart__legend-item").Count.Should().Be(0);
        cut.FindAll(".tm-chart__legend-item").Count.Should().Be(2);
    }

    [Fact]
    public void InteractiveLegend_Enabled_LegendItemsAreButtons()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiDatasetData)
            .Add(x => x.InteractiveLegend, true));

        cut.FindAll("button.tm-chart__legend-item").Count.Should().Be(2);
    }

    [Fact]
    public void InteractiveLegend_ClickDataset_HidesItsBars()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiDatasetData)
            .Add(x => x.InteractiveLegend, true));

        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(6);

        cut.FindAll("button.tm-chart__legend-item")[0].Click();

        // Dataset 0's 3 bars are hidden → only dataset 1's 3 bars remain.
        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(3);
        cut.FindAll(".tm-chart__legend-item--hidden").Count.Should().Be(1);
    }

    [Fact]
    public void InteractiveLegend_Toggle_FiresOnSeriesToggle()
    {
        ChartSeriesToggle? evt = null;
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiDatasetData)
            .Add(x => x.InteractiveLegend, true)
            .Add(x => x.OnSeriesToggle, e => evt = e));

        cut.FindAll("button.tm-chart__legend-item")[0].Click();

        evt.Should().NotBeNull();
        evt!.Index.Should().Be(0);
        evt.Label.Should().Be("2024");
        evt.Hidden.Should().BeTrue();
    }

    [Fact]
    public void InteractiveLegend_ClickTwice_RestoresSeries()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiDatasetData)
            .Add(x => x.InteractiveLegend, true));

        var btn = cut.FindAll("button.tm-chart__legend-item")[0];
        btn.Click();
        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(3);

        cut.FindAll("button.tm-chart__legend-item")[0].Click();
        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(6);
        cut.FindAll(".tm-chart__legend-item--hidden").Count.Should().Be(0);
    }

    [Fact]
    public void InteractiveLegend_PerValue_HidesSingleSlice()
    {
        var data = ChartDataWithBackgroundColors(["#dc2626", "#ea580c", "#16a34a"]);
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Donut)
            .Add(x => x.Data, data)
            .Add(x => x.InteractiveLegend, true));

        cut.FindAll("path.tm-chart__slice").Count.Should().Be(3);

        // Per-value legend → each legend item is one slice; hide the middle one.
        cut.FindAll("button.tm-chart__legend-item")[1].Click();

        cut.FindAll("path.tm-chart__slice").Count.Should().Be(2);
    }

    [Fact]
    public void InteractiveLegend_DataSwap_ClearsHiddenState()
    {
        var first = MultiDatasetData;
        var second = MultiDatasetData; // distinct instance, same shape

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, first)
            .Add(x => x.InteractiveLegend, true));

        cut.FindAll("button.tm-chart__legend-item")[0].Click();
        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(3);

        // Parent swaps in a different (same-shape) dataset → hidden state must reset.
        cut.Render(p => p.Add(x => x.Data, second));

        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(6);
        cut.FindAll(".tm-chart__legend-item--hidden").Count.Should().Be(0);
    }

    // ── Tooltips (K10) ──

    [Fact]
    public void Tooltip_Enabled_NotRenderedUntilHover()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData));

        cut.FindAll(".tm-chart__tooltip").Count.Should().Be(0);
    }

    [Fact]
    public void Tooltip_Hover_ShowsLabelAndValue()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData));

        cut.FindAll("rect.tm-chart__bar")[1].MouseOver();

        var tip = cut.Find(".tm-chart__tooltip");
        tip.TextContent.Should().Contain("Feb");
        tip.TextContent.Should().Contain("20");
    }

    [Fact]
    public void Tooltip_Disabled_NotShownOnHover()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.ShowTooltip, false));

        cut.FindAll("rect.tm-chart__bar")[1].MouseOver();

        cut.FindAll(".tm-chart__tooltip").Count.Should().Be(0);
    }

    [Fact]
    public void Tooltip_CustomTemplate_UsedOnHover()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SimpleBarData)
            .Add(x => x.TooltipTemplate, ctx => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "custom-tip");
                builder.AddContent(2, $"{ctx.Label}={ctx.Value}");
                builder.CloseElement();
            }));

        cut.FindAll("rect.tm-chart__bar")[0].MouseOver();

        var custom = cut.Find(".custom-tip");
        custom.TextContent.Should().Be("Jan=10");
    }

    // ── Localized empty message (K10) ──

    [Fact]
    public void EmptyData_Message_IsLocalized()
    {
        var emptyData = new ChartData { Labels = [], Datasets = [] };
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, emptyData));

        var expected = Services.GetRequiredService<Tempo.Blazor.Localization.ITmLocalizer>()["TmChart_NoData"];
        cut.Find(".tm-chart__empty").TextContent.Trim().Should().Be(expected);
    }

    private static ChartData ChartDataWithBackgroundColors(
        IReadOnlyList<string> backgroundColors,
        string? backgroundColor = null,
        string color = "#3b82f6")
    {
        return new ChartData
        {
            Labels = ["A", "B", "C"],
            Datasets =
            [
                new ChartDataset
                {
                    Label = "Outages",
                    Values = [10, 20, 30],
                    Color = color,
                    BackgroundColor = backgroundColor,
                    BackgroundColors = backgroundColors,
                }
            ]
        };
    }

    private static void AssertFillAttributes(IReadOnlyList<AngleSharp.Dom.IElement> elements, string[] expectedFills)
    {
        elements.Select(element => element.GetAttribute("fill")).Should().Equal(expectedFills);
    }

    private static void AssertLegendColorStyles(IReadOnlyList<AngleSharp.Dom.IElement> elements, string[] expectedColors)
    {
        elements.Should().HaveCount(expectedColors.Length);
        for (int i = 0; i < expectedColors.Length; i++)
        {
            elements[i].GetAttribute("style").Should().Contain($"background-color: {expectedColors[i]}");
        }
    }

    private static double AttributeAsDouble(AngleSharp.Dom.IElement element, string attribute)
        => double.Parse(
            element.GetAttribute(attribute).Should().NotBeNullOrWhiteSpace().And.Subject!,
            System.Globalization.CultureInfo.InvariantCulture);
}
