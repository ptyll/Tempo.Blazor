using System.Globalization;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// TDD tests for the Fáze N8 chart types: Funnel (conversion trapezoids with per-stage
/// conversion percentages), Heatmap (dataset-rows × label-columns intensity matrix), and
/// Treemap (squarified shares, flat from ChartData or hierarchical from ChartTreeNode) —
/// all in the existing TmChart architecture: SVG render, hover tooltips, click segments,
/// legends with per-value colors.
/// </summary>
public class TmChartNewTypesTests : LocalizationTestBase
{
    private static ChartData FunnelData => new()
    {
        Labels = ["Poptávky", "Kvalifikace", "Nabídka", "Smlouva"],
        Datasets =
        [
            new ChartDataset
            {
                Label = "Konverze",
                Values = [1000, 450, 200, 90],
                Color = "#3b82f6",
                BackgroundColors = ["#3b82f6", "#8b5cf6", "#f59e0b", "#10b981"]
            }
        ]
    };

    private static ChartData HeatmapData => new()
    {
        Labels = ["8:00", "12:00", "16:00"],
        Datasets =
        [
            new ChartDataset { Label = "Pondělí", Values = [2, 10, 4], Color = "#3b82f6" },
            new ChartDataset { Label = "Úterý", Values = [0, 6, 8], Color = "#3b82f6" }
        ]
    };

    private static ChartData FlatTreemapData => new()
    {
        Labels = ["Alfa", "Beta", "Gama"],
        Datasets =
        [
            new ChartDataset
            {
                Label = "Podíl",
                Values = [60, 30, 10],
                Color = "#3b82f6",
                BackgroundColors = ["#3b82f6", "#ef4444", "#10b981"]
            }
        ]
    };

    private static IReadOnlyList<ChartTreeNode> TreeNodes =>
    [
        new ChartTreeNode
        {
            Label = "Sporná agenda",
            Color = "#3b82f6",
            Children =
            [
                new ChartTreeNode { Label = "Klient A", Value = 40 },
                new ChartTreeNode { Label = "Klient B", Value = 20 }
            ]
        },
        new ChartTreeNode
        {
            Label = "Smluvní agenda",
            Color = "#ef4444",
            Children =
            [
                new ChartTreeNode { Label = "Klient C", Value = 25 },
                new ChartTreeNode { Label = "Klient D", Value = 10 },
                new ChartTreeNode { Label = "Klient E", Value = 5 }
            ]
        }
    ];

    private IRenderedComponent<TmChart> Render(
        ChartType type, ChartData? data,
        Action<Bunit.ComponentParameterCollectionBuilder<TmChart>>? configure = null)
        => Render<TmChart>(p =>
        {
            p.Add(x => x.Type, type);
            if (data is not null)
            {
                p.Add(x => x.Data, data);
            }

            configure?.Invoke(p);
        });

    // ── Funnel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Funnel_RendersOneSegmentPerStage_WidestFirst()
    {
        var cut = Render(ChartType.Funnel, FunnelData);

        var segments = cut.FindAll(".tm-chart__funnel-segment");
        segments.Should().HaveCount(4);
        segments[0].GetAttribute("data-value").Should().Be("1000");
        double.Parse(segments[0].GetAttribute("data-width")!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(
                double.Parse(segments[3].GetAttribute("data-width")!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Funnel_ShowValues_RendersConversionPercentages()
    {
        // The thousands-grouping assertion needs the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = Render(ChartType.Funnel, FunnelData, p => p.Add(x => x.ShowValues, true));

        var text = cut.Find("svg").TextContent;
        text.Should().Contain("45");    // 450 / 1000
        text.Should().Contain("%");
        text.Should().Contain("1,000", "a stage value reads with the reader's thousands grouping");
    }

    [Fact]
    public void Funnel_Click_RaisesSegmentWithStage()
    {
        ChartSegment? clicked = null;
        var cut = Render(ChartType.Funnel, FunnelData,
            p => p.Add(x => x.OnSegmentClick, (ChartSegment s) => clicked = s));

        cut.FindAll(".tm-chart__funnel-segment")[1].Click();

        clicked.Should().NotBeNull();
        clicked!.Index.Should().Be(1);
        clicked.Label.Should().Be("Kvalifikace");
        clicked.Value.Should().Be(450);
    }

    [Fact]
    public void Funnel_Tooltip_OnHover()
    {
        var cut = Render(ChartType.Funnel, FunnelData);

        cut.FindAll(".tm-chart__funnel-segment")[2].MouseOver();

        var tip = cut.Find(".tm-chart__tooltip");
        tip.TextContent.Should().Contain("Nabídka");
        tip.TextContent.Should().Contain("200");
    }

    [Fact]
    public void Funnel_InteractiveLegend_TogglesStage()
    {
        var cut = Render(ChartType.Funnel, FunnelData, p => p.Add(x => x.InteractiveLegend, true));

        var legendItems = cut.FindAll(".tm-chart__legend-item");
        legendItems.Should().HaveCount(4);   // per-value legend from stage labels

        cut.Find("[data-testid='chart-legend-1']").Click();

        cut.FindAll(".tm-chart__funnel-segment").Should().HaveCount(3);
    }

    // ── Heatmap ──────────────────────────────────────────────────────────────

    [Fact]
    public void Heatmap_RendersRowByColumnCells()
    {
        var cut = Render(ChartType.Heatmap, HeatmapData);

        var cells = cut.FindAll(".tm-chart__heatmap-cell");
        cells.Should().HaveCount(6);
        cells.Should().Contain(c => c.GetAttribute("data-row") == "1" && c.GetAttribute("data-col") == "2");
    }

    [Fact]
    public void Heatmap_CellIntensity_ScalesWithValue()
    {
        var cut = Render(ChartType.Heatmap, HeatmapData);

        var cells = cut.FindAll(".tm-chart__heatmap-cell");
        var max = cells.Single(c => c.GetAttribute("data-value") == "10");
        var min = cells.Single(c => c.GetAttribute("data-value") == "0");

        double.Parse(max.GetAttribute("fill-opacity")!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(1d);
        double.Parse(min.GetAttribute("fill-opacity")!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeLessThan(0.2);
    }

    [Fact]
    public void Heatmap_RendersRowAndColumnAxisLabels()
    {
        var cut = Render(ChartType.Heatmap, HeatmapData);

        var text = cut.Find("svg").TextContent;
        text.Should().Contain("Pondělí");
        text.Should().Contain("16:00");
    }

    [Fact]
    public void Heatmap_Click_RaisesSegmentWithRowAndColumn()
    {
        ChartSegment? clicked = null;
        var cut = Render(ChartType.Heatmap, HeatmapData,
            p => p.Add(x => x.OnSegmentClick, (ChartSegment s) => clicked = s));

        cut.FindAll(".tm-chart__heatmap-cell")
            .Single(c => c.GetAttribute("data-row") == "1" && c.GetAttribute("data-col") == "2")
            .Click();

        clicked.Should().NotBeNull();
        clicked!.DatasetIndex.Should().Be(1);
        clicked.Index.Should().Be(2);
        clicked.Value.Should().Be(8);
    }

    [Fact]
    public void Heatmap_Tooltip_ShowsRowColumnAndValue()
    {
        var cut = Render(ChartType.Heatmap, HeatmapData);

        cut.FindAll(".tm-chart__heatmap-cell")
            .Single(c => c.GetAttribute("data-row") == "0" && c.GetAttribute("data-col") == "1")
            .MouseOver();

        var tip = cut.Find(".tm-chart__tooltip");
        tip.TextContent.Should().Contain("Pondělí");
        tip.TextContent.Should().Contain("12:00");
        tip.TextContent.Should().Contain("10");
    }

    [Fact]
    public void Heatmap_InteractiveLegend_HidesRow()
    {
        var cut = Render(ChartType.Heatmap, HeatmapData, p => p.Add(x => x.InteractiveLegend, true));

        cut.Find("[data-testid='chart-legend-0']").Click();

        var cells = cut.FindAll(".tm-chart__heatmap-cell");
        cells.Should().HaveCount(3);
        cells.Should().OnlyContain(c => c.GetAttribute("data-row") == "1");
    }

    [Fact]
    public void Heatmap_DarkCallerColour_StrongCells_NeverTakeTheThemeInverseInk()
    {
        // TG-002 review B1: --tm-text-inverse resolves DARK in dark mode (#0f172a), so the old
        // norm-only rule painted dark-on-dark on a caller colour like #1d4ed8. The ink is picked
        // from the FILL now: a strong cell on a dark caller colour takes the light inside ink,
        // a weak cell the ordinary primary text — never --tm-text-inverse.
        var data = new ChartData
        {
            Labels = ["8:00", "12:00", "16:00"],
            Datasets =
            [
                new ChartDataset { Label = "Pondělí", Values = [2, 10, 4], Color = "#1d4ed8" },
                new ChartDataset { Label = "Úterý", Values = [0, 6, 8], Color = "#1d4ed8" },
            ],
        };
        var cut = Render(ChartType.Heatmap, data, p => p.Add(x => x.ShowValues, true));

        var cells = cut.FindAll(".tm-chart__heatmap-cell");
        var values = cells
            .Select(c => double.Parse(c.GetAttribute("data-value")!, CultureInfo.InvariantCulture))
            .ToArray();
        var min = values.Min();
        var range = values.Max() - min;

        foreach (var cell in cells)
        {
            var norm = range > 0
                ? (double.Parse(cell.GetAttribute("data-value")!, CultureInfo.InvariantCulture) - min) / range
                : 1;
            if (norm <= 0.55)
            {
                continue;
            }

            var label = cut.FindAll(".tm-chart__heatmap-value")
                .Single(text => text.TextContent == cell.GetAttribute("data-value"));
            label.GetAttribute("fill")
                .Should().NotBe("var(--tm-text-inverse)",
                    "a strong cell (norm {0:0.00}) on the dark caller colour #1d4ed8 would read dark-on-dark in dark mode",
                    norm);
            label.GetAttribute("fill")
                .Should().Be("var(--tm-chart-funnel-label-inside)",
                    "a strong cell on a dark caller colour takes the light inside ink");
        }

        // Weak cells on the same caller colour fall back to the primary text token.
        cut.FindAll(".tm-chart__heatmap-value")
            .Select(text => text.GetAttribute("fill"))
            .Should().NotContain("var(--tm-text-inverse)");
    }

    [Fact]
    public void Heatmap_SeriesTokenFill_TakesThePairedSeriesInk()
    {
        // The other half of B1: a fill that IS a built-in series token takes the fill's paired
        // --tm-chart-series-n-ink (theme-relative since TG-002), not the caller-colour rule.
        var data = new ChartData
        {
            Labels = ["8:00", "12:00", "16:00"],
            Datasets =
            [
                new ChartDataset { Label = "Pondělí", Values = [2, 10, 4], Color = "var(--tm-chart-series-1)" },
                new ChartDataset { Label = "Úterý", Values = [0, 6, 8], Color = "var(--tm-chart-series-1)" },
            ],
        };
        var cut = Render(ChartType.Heatmap, data, p => p.Add(x => x.ShowValues, true));

        cut.FindAll(".tm-chart__heatmap-value")
            .Select(text => text.GetAttribute("fill"))
            .Should().AllBe("var(--tm-chart-series-1-ink)",
                "a series-token fill takes the paired series ink on every cell");
    }

    // ── Treemap ──────────────────────────────────────────────────────────────

    [Fact]
    public void Treemap_Flat_RendersTilePerValue_WithProportionalAreas()
    {
        var cut = Render(ChartType.Treemap, FlatTreemapData);

        var tiles = cut.FindAll(".tm-chart__treemap-tile");
        tiles.Should().HaveCount(3);

        double Area(AngleSharp.Dom.IElement tile)
            => double.Parse(tile.GetAttribute("width")!, System.Globalization.CultureInfo.InvariantCulture)
               * double.Parse(tile.GetAttribute("height")!, System.Globalization.CultureInfo.InvariantCulture);

        var largest = tiles.Single(t => t.GetAttribute("data-value") == "60");
        var smallest = tiles.Single(t => t.GetAttribute("data-value") == "10");
        (Area(largest) / Area(smallest)).Should().BeApproximately(6d, 0.4);
    }

    [Fact]
    public void Treemap_Hierarchical_RendersGroupHeadersAndLeafTiles()
    {
        var cut = Render(ChartType.Treemap, null, p => p.Add(x => x.TreeNodes, TreeNodes));

        cut.FindAll(".tm-chart__treemap-group").Should().HaveCount(2);
        var tiles = cut.FindAll(".tm-chart__treemap-tile");
        tiles.Should().HaveCount(5);
        tiles.Should().Contain(t => t.GetAttribute("data-path") == "Sporná agenda / Klient A");
    }

    [Fact]
    public void Treemap_Click_LeafRaisesSegmentWithPath()
    {
        ChartSegment? clicked = null;
        var cut = Render(ChartType.Treemap, null, p =>
        {
            p.Add(x => x.TreeNodes, TreeNodes);
            p.Add(x => x.OnSegmentClick, (ChartSegment s) => clicked = s);
        });

        cut.FindAll(".tm-chart__treemap-tile")
            .Single(t => t.GetAttribute("data-path") == "Smluvní agenda / Klient C")
            .Click();

        clicked.Should().NotBeNull();
        clicked!.Label.Should().Be("Smluvní agenda / Klient C");
        clicked.Value.Should().Be(25);
    }

    [Fact]
    public void Treemap_Tooltip_ShowsLeafAndValue()
    {
        var cut = Render(ChartType.Treemap, null, p => p.Add(x => x.TreeNodes, TreeNodes));

        cut.FindAll(".tm-chart__treemap-tile")
            .Single(t => t.GetAttribute("data-path") == "Sporná agenda / Klient B")
            .MouseOver();

        var tip = cut.Find(".tm-chart__tooltip");
        tip.TextContent.Should().Contain("Klient B");
        tip.TextContent.Should().Contain("20");
    }

    [Fact]
    public void Treemap_Legend_ListsTopLevelNodes_AndToggleHidesSubtree()
    {
        var cut = Render(ChartType.Treemap, null, p =>
        {
            p.Add(x => x.TreeNodes, TreeNodes);
            p.Add(x => x.InteractiveLegend, true);
        });

        var labels = cut.FindAll(".tm-chart__legend-label").Select(l => l.TextContent.Trim()).ToList();
        labels.Should().Equal("Sporná agenda", "Smluvní agenda");

        cut.Find("[data-testid='chart-legend-0']").Click();

        var tiles = cut.FindAll(".tm-chart__treemap-tile");
        tiles.Should().HaveCount(3);
        tiles.Should().OnlyContain(t => t.GetAttribute("data-path")!.StartsWith("Smluvní agenda"));
    }

    [Fact]
    public void Treemap_WithoutDataButWithTreeNodes_DoesNotShowEmptyState()
    {
        var cut = Render(ChartType.Treemap, null, p => p.Add(x => x.TreeNodes, TreeNodes));

        cut.FindAll(".tm-chart__empty").Should().BeEmpty();
        cut.Find("svg");
    }
}
