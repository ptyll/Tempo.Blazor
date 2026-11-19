using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// TDD tests for combo charts: a Bar chart whose datasets can opt into a Line overlay
/// through <see cref="ChartDataset.RenderAs"/> (bars = periodic flows, lines = cumulative
/// values in one plot, shared Y scale).
/// </summary>
public class TmChartComboTests : LocalizationTestBase
{
    private static ChartData ComboData => new()
    {
        Labels = ["Jan", "Feb", "Mar", "Apr"],
        Datasets =
        [
            new ChartDataset { Label = "Income", Values = [10, 20, 30, 25], Color = "#22c55e" },
            new ChartDataset { Label = "Costs", Values = [8, 12, 18, 16], Color = "#ef4444" },
            new ChartDataset { Label = "Cashflow", Values = [2, 8, 12, 9], Color = "#3b82f6", RenderAs = ChartDatasetRenderAs.Line },
        ]
    };

    [Fact]
    public void ComboChart_Renders_Bars_Only_For_Bar_Datasets()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, ComboData));

        // 2 bar datasets × 4 labels = 8 rects; the Line dataset must not add bars.
        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(8);
    }

    [Fact]
    public void ComboChart_Renders_Line_Overlay_With_Points()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, ComboData));

        var lines = cut.FindAll("polyline.tm-chart__line");
        lines.Count.Should().Be(1);
        lines[0].GetAttribute("points")!.Split(' ').Length.Should().Be(4);
        cut.FindAll("circle.tm-chart__point").Count.Should().Be(4);
    }

    [Fact]
    public void ComboChart_Line_Overlay_Uses_Shared_Y_Scale()
    {
        // Overlay value equal to the global max must sit at the very top of the plot area.
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets =
            [
                new ChartDataset { Label = "Bars", Values = [10, 20], Color = "#ef4444" },
                new ChartDataset { Label = "Line", Values = [30, 15], Color = "#3b82f6", RenderAs = ChartDatasetRenderAs.Line },
            ]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        var points = cut.FindAll("circle.tm-chart__point");
        var yTop = double.Parse(points[0].GetAttribute("cy")!, System.Globalization.CultureInfo.InvariantCulture);
        var yHalf = double.Parse(points[1].GetAttribute("cy")!, System.Globalization.CultureInfo.InvariantCulture);
        yTop.Should().BeLessThan(yHalf);
        yTop.Should().BeApproximately(20, 0.5); // PT (top padding) = value == max
    }

    [Fact]
    public void ComboChart_Legend_Contains_All_Datasets()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, ComboData));

        var legend = cut.FindAll(".tm-chart__legend-item");
        legend.Count.Should().Be(3);
    }

    [Fact]
    public void ComboChart_Overlay_Point_Click_Reports_Original_Dataset_Index()
    {
        ChartSegment? clicked = null;
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, ComboData)
            .Add(x => x.OnSegmentClick, EventCallback.Factory.Create<ChartSegment>(this, s => clicked = s)));

        cut.FindAll("circle.tm-chart__point")[1].Click();

        clicked.Should().NotBeNull();
        clicked!.DatasetIndex.Should().Be(2); // the overlay dataset keeps its original index
        clicked.Index.Should().Be(1);
        clicked.Value.Should().Be(8);
    }

    [Fact]
    public void BarChart_Without_RenderAs_Keeps_Existing_Behavior()
    {
        var data = new ChartData
        {
            Labels = ["A", "B", "C"],
            Datasets =
            [
                new ChartDataset { Label = "One", Values = [1, 2, 3], Color = "#3b82f6" },
                new ChartDataset { Label = "Two", Values = [2, 3, 4], Color = "#ef4444" },
            ]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        cut.FindAll("rect.tm-chart__bar").Count.Should().Be(6);
        cut.FindAll("polyline.tm-chart__line").Should().BeEmpty();
    }

    [Fact]
    public void ComboChart_All_Line_Datasets_Renders_No_Bars()
    {
        // Edge case: every dataset opts into the overlay — no bars, only lines.
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets =
            [
                new ChartDataset { Label = "L1", Values = [1, 2], Color = "#3b82f6", RenderAs = ChartDatasetRenderAs.Line },
                new ChartDataset { Label = "L2", Values = [2, 1], Color = "#ef4444", RenderAs = ChartDatasetRenderAs.Line },
            ]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        cut.FindAll("rect.tm-chart__bar").Should().BeEmpty();
        cut.FindAll("polyline.tm-chart__line").Count.Should().Be(2);
    }

    [Fact]
    public void BarChart_With_Many_Labels_Thins_Axis_Labels()
    {
        // Edge case: dense categorical axis (e.g. 48 monthly points) must not render
        // every label on top of each other.
        var labels = Enumerable.Range(0, 48).Select(i => $"M{i}").ToArray();
        var data = new ChartData
        {
            Labels = labels,
            Datasets = [new ChartDataset { Label = "V", Values = Enumerable.Range(0, 48).Select(i => (double)i).ToArray(), Color = "#3b82f6" }]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        // The step is derived from the width a category owns (3-character labels in 11-unit slots -> every 3rd).
        cut.FindAll("text.tm-chart__label").Count.Should().BeInRange(12, 16);
    }

    [Fact]
    public void BarChart_With_Few_Labels_Renders_All_Labels()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, ComboData));

        cut.FindAll("text.tm-chart__label").Count.Should().Be(4);
    }

    [Fact]
    public void ComboChart_NegativeOverlayValues_Render_Below_Zero_Axis()
    {
        // Cashflow can be negative: the plot domain must extend below zero, with an
        // emphasized zero axis, and overlay points must sit below it.
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets =
            [
                new ChartDataset { Label = "Bars", Values = [10, 20], Color = "#ef4444" },
                new ChartDataset { Label = "Cashflow", Values = [-5, 15], Color = "#3b82f6", RenderAs = ChartDatasetRenderAs.Line },
            ]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        var zero = cut.Find("line.tm-chart__axis-zero");
        var zeroY = double.Parse(zero.GetAttribute("y1")!, System.Globalization.CultureInfo.InvariantCulture);

        var points = cut.FindAll("circle.tm-chart__point");
        var negY = double.Parse(points[0].GetAttribute("cy")!, System.Globalization.CultureInfo.InvariantCulture);
        var posY = double.Parse(points[1].GetAttribute("cy")!, System.Globalization.CultureInfo.InvariantCulture);
        negY.Should().BeGreaterThan(zeroY);  // negative value below the zero axis
        posY.Should().BeLessThan(zeroY);     // positive value above it
    }

    [Fact]
    public void BarChart_NegativeValue_Renders_Bar_Below_Zero_Baseline()
    {
        var data = new ChartData
        {
            Labels = ["Up", "Down"],
            Datasets = [new ChartDataset { Label = "V", Values = [10, -10], Color = "#3b82f6" }]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        var zero = cut.Find("line.tm-chart__axis-zero");
        var zeroY = double.Parse(zero.GetAttribute("y1")!, System.Globalization.CultureInfo.InvariantCulture);

        var bars = cut.FindAll("rect.tm-chart__bar");
        bars.Count.Should().Be(2);
        var upTop = double.Parse(bars[0].GetAttribute("y")!, System.Globalization.CultureInfo.InvariantCulture);
        var downTop = double.Parse(bars[1].GetAttribute("y")!, System.Globalization.CultureInfo.InvariantCulture);
        var downHeight = double.Parse(bars[1].GetAttribute("height")!, System.Globalization.CultureInfo.InvariantCulture);

        upTop.Should().BeLessThan(zeroY);                      // positive bar grows up from zero
        downTop.Should().BeApproximately(zeroY, 0.5);          // negative bar starts at the zero axis
        downHeight.Should().BeGreaterThan(0);                  // and extends downward with valid height
    }

    [Fact]
    public void BarChart_AllPositive_Has_No_Zero_Axis_Emphasis()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, ComboData));

        cut.FindAll("line.tm-chart__axis-zero").Should().BeEmpty();
    }

    [Fact]
    public void Dense_Line_Series_Thins_Point_Markers_But_Keeps_Full_Polyline()
    {
        // 360 monthly values: drawing a white-stroked circle per value makes the line look
        // dotted (circles overlap). Markers must thin out while the polyline stays complete.
        var n = 360;
        var data = new ChartData
        {
            Labels = Enumerable.Range(0, n).Select(i => $"M{i}").ToArray(),
            Datasets =
            [
                new ChartDataset { Label = "Bars", Values = Enumerable.Range(0, n).Select(_ => 10.0).ToArray(), Color = "#ef4444" },
                new ChartDataset { Label = "Line", Values = Enumerable.Range(0, n).Select(i => (double)i).ToArray(), Color = "#3b82f6", RenderAs = ChartDatasetRenderAs.Line },
            ]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, data));

        var polyline = cut.Find("polyline.tm-chart__line");
        polyline.GetAttribute("points")!.Split(' ').Length.Should().Be(360);
        // Markers are ~12 SVG units wide (r=4 + stroke); the ~24-unit spacing threshold
        // must leave visible line segments between them (≤ CW/24 + 1 ≈ 23 markers).
        cut.FindAll("circle.tm-chart__point").Count.Should().BeLessThan(30);
    }

    [Fact]
    public void Sparse_Line_Series_Keeps_All_Point_Markers()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, ComboData));

        cut.FindAll("circle.tm-chart__point").Count.Should().Be(4);
    }

    [Fact]
    public void LineChart_Ignores_RenderAs_Line()
    {
        // RenderAs=Line on a Line chart is a no-op: the dataset renders as a normal line.
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets =
            [
                new ChartDataset { Label = "L", Values = [1, 2], Color = "#3b82f6", RenderAs = ChartDatasetRenderAs.Line },
            ]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        cut.FindAll("polyline.tm-chart__line").Count.Should().Be(1);
    }
}
