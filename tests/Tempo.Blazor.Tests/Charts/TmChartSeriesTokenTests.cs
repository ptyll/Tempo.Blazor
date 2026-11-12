using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan 92e96b25, phase 2 (TG-002): the chart series palette is token-driven. The built-in palette of
/// every chart component resolves through the <c>--tm-chart-series-1..8</c> design tokens (plus the
/// semantic <c>--tm-chart-positive/-negative/-neutral</c>) instead of hardcoded hexes, so a theme
/// retints the series and dark mode lifts them to tones that hold contrast on the dark surface.
/// </summary>
/// <remarks>
/// The markup contract: a chart that renders the LIBRARY's own colours writes <c>var(--tm-…)</c>
/// references (or <c>currentColor</c>) into its fill/stroke attributes — never a literal hex. The only
/// hex a rendered chart may carry is a colour the CALLER handed in (<c>Color</c>,
/// <c>BackgroundColors</c>, a Sankey node colour, a gauge range colour).
/// </remarks>
public class TmChartSeriesTokenTests : LocalizationTestBase
{
    /// <summary>
    /// A literal colour in a fill/stroke attribute — the shape TG-002 bans from library output
    /// (a <c>#hex</c> or a named colour; <c>none</c> and <c>currentColor</c> carry no colour).
    /// </summary>
    private static readonly Regex HexPaint = new(
        "(fill|stroke)=\"(#[0-9a-fA-F]{3,8}|white|black|red|blue|gray|grey|green|yellow|orange|purple)\"",
        RegexOptions.Compiled);

    private static ChartData SeriesData => new()
    {
        Labels = ["Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7", "Q8", "Q9"],
        Datasets =
        [
            new ChartDataset { Label = "A", Color = "var(--tm-chart-series-1)", Values = [10, 12, 11, 14, 13, 15, 16, 18, 17] },
            new ChartDataset { Label = "B", Color = "var(--tm-chart-series-2)", Values = [8, 9, 10, 9, 11, 12, 11, 13, 14] },
        ],
    };

    private static ChartData SingleSeriesData => new()
    {
        Labels = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"],
        Datasets = [new ChartDataset { Label = "One", Color = "var(--tm-chart-series-1)", Values = [5, 3, 7, 2, 8, 6, 4, 9, 1, 10] }],
    };

    private static IReadOnlyList<StockChartDataPoint> StockData =>
    [
        new(new DateTime(2026, 1, 1), 100, 110, 95, 105, 1000),
        new(new DateTime(2026, 1, 2), 105, 108, 102, 106, 1200),
        new(new DateTime(2026, 1, 3), 106, 115, 104, 114, 1500),
    ];

    private static SankeyData FlowData => new()
    {
        Nodes =
        [
            new SankeyNode { Id = "a", Label = "A" },
            new SankeyNode { Id = "b", Label = "B" },
            new SankeyNode { Id = "c", Label = "C" },
        ],
        Links =
        [
            new SankeyLink { SourceId = "a", TargetId = "b", Value = 5 },
            new SankeyLink { SourceId = "a", TargetId = "c", Value = 3 },
        ],
    };

    // ── The markup contract: no hardcoded hex in library-rendered paint ──────

    [Theory]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Donut)]
    [InlineData(ChartType.HorizontalBar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.StackedArea)]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.Heatmap)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Waterfall)]
    public void TmChart_DefaultPalette_RendersNoHardcodedHex(ChartType type)
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, type)
            .Add(x => x.Data, SingleSeriesData)
            .Add(x => x.ShowValues, true)
            .Add(x => x.ShowLegend, true));

        cut.Markup.Should().NotMatchRegex(HexPaint,
            "the library palette is token-driven: {0} must paint through var(--tm-…) only", type);
    }

    [Theory]
    [InlineData(SparklineType.Line)]
    [InlineData(SparklineType.Bar)]
    [InlineData(SparklineType.Area)]
    [InlineData(SparklineType.Pie)]
    public void TmSparkline_DefaultPalette_RendersNoHardcodedHex(SparklineType type)
    {
        var cut = Render<TmSparkline>(p => p.Add(x => x.Data, [3.0, 7, 5, 9, 4]));

        cut.Markup.Should().NotMatchRegex(HexPaint, "the {0} sparkline paints through tokens only", type);
    }

    [Theory]
    [InlineData(GaugeType.Arc)]
    [InlineData(GaugeType.Circular)]
    [InlineData(GaugeType.Linear)]
    public void TmGauge_DefaultPalette_RendersNoHardcodedHex(GaugeType type)
    {
        var cut = Render<TmGauge>(p => p
            .Add(x => x.Type, type)
            .Add(x => x.Value, 60));

        cut.Markup.Should().NotMatchRegex(HexPaint, "the {0} gauge paints through tokens only", type);
    }

    [Fact]
    public void TmSankeyChart_DefaultPalette_RendersNoHardcodedHex()
    {
        var cut = Render<TmSankeyChart>(p => p.Add(x => x.Data, FlowData));

        cut.Markup.Should().NotMatchRegex(HexPaint, "Sankey nodes/links paint through the series tokens");
        cut.FindAll("rect.tm-sankey__node")
            .Select(node => node.GetAttribute("fill"))
            .Should().Equal(
                ["var(--tm-chart-series-1)", "var(--tm-chart-series-2)", "var(--tm-chart-series-3)"]);
    }

    [Theory]
    [InlineData(StockChartType.Candlestick)]
    [InlineData(StockChartType.OHLC)]
    [InlineData(StockChartType.Line)]
    public void TmStockChart_DefaultPalette_RendersNoHardcodedHex(StockChartType type)
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Type, type)
            .Add(x => x.Data, StockData));

        cut.Markup.Should().NotMatchRegex(HexPaint, "the {0} stock chart paints through tokens only", type);
    }

    [Fact]
    public void TmStockChart_UsesTheSemanticGainLossTokens()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Type, StockChartType.Candlestick)
            .Add(x => x.Data, StockData));

        var fills = cut.FindAll("rect.tm-stock-chart__body").Select(body => body.GetAttribute("fill"));
        fills.Should().OnlyContain(fill =>
            fill == "var(--tm-chart-positive)" || fill == "var(--tm-chart-negative)");
        cut.Find("line.tm-stock-chart__wick").GetAttribute("stroke").Should().Be("var(--tm-chart-neutral)");
    }

    // ── The exception: a caller-passed colour renders as-is ──────────────────

    [Fact]
    public void CallerPassed_Color_RendersVerbatim_ThatIsTheDocumentedException()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, [1.0, 2, 3])
            .Add(x => x.Color, "#123456"));

        cut.Find("polyline.tm-sparkline__line").GetAttribute("stroke").Should().Be("#123456");
    }

    [Fact]
    public void CallerPassed_BackgroundColors_RenderVerbatim_OnTmChart()
    {
        var data = SingleSeriesData with
        {
            Datasets =
            [
                SingleSeriesData.Datasets[0] with { BackgroundColors = ["#123456", "#654321"] },
            ],
        };
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Pie)
            .Add(x => x.Data, data));

        cut.FindAll("path.tm-chart__slice")
            .Select(slice => slice.GetAttribute("fill"))
            .Should().StartWith(["#123456", "#654321"]);
    }

    [Fact]
    public void CallerPassed_GaugeRangeColors_RenderVerbatim()
    {
        var cut = Render<TmGauge>(p => p
            .Add(x => x.Value, 70)
            .Add(x => x.Ranges, [new GaugeRange(0, 50, "#123456")]));

        cut.Find("path.tm-gauge__range").GetAttribute("stroke").Should().Be("#123456");
    }

    // ── The token contract in the shipped stylesheets ────────────────────────

    [Fact]
    public void SeriesTokens_AreDeclared_InLightDarkAndBothThemes()
    {
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));
        var indigo = ThemeCss.StripComments(
            File.ReadAllText(ThemeCss.CssPath("theme-indigo.css")));

        for (var i = 1; i <= 8; i++)
        {
            light.Should().ContainKey($"--tm-chart-series-{i}");
            light.Should().ContainKey($"--tm-chart-series-{i}-ink");
            dark.Should().ContainKey($"--tm-chart-series-{i}",
                "the dark theme lifts every series slot (a :root literal would not recompute)");
            dark.Should().ContainKey($"--tm-chart-series-{i}-ink");
        }

        light.Should().ContainKey("--tm-chart-positive");
        light.Should().ContainKey("--tm-chart-negative");
        light.Should().ContainKey("--tm-chart-neutral");
        dark.Should().ContainKey("--tm-chart-positive");
        dark.Should().ContainKey("--tm-chart-negative");
        dark.Should().ContainKey("--tm-chart-neutral");

        // Series 1 follows the theme primary: default light primary-500 IS the historical series blue,
        // the indigo theme retints it to indigo-500, and both dark themes take the primary-400 step.
        light["--tm-chart-series-1"].Should().Be("var(--tm-color-primary-500)");
        dark["--tm-chart-series-1"].Should().Be("var(--tm-color-primary-400)");
        indigo.Should().Contain("--tm-chart-series-1: var(--tm-color-primary-500)",
            "the indigo light block retints series 1 to the indigo primary");
        indigo.Should().Contain("--tm-chart-series-1: var(--tm-color-primary-400)",
            "the indigo dark block takes the indigo dark primary step");
    }

    [Fact]
    public void LightSeriesValues_KeepTheLegacySeriesFills()
    {
        // The pre-TG-002 palette, resolved through the token graph: light mode must not move a series
        // FILL (the light token values equal the legacy hexes). Label ink and series 9+ still change
        // — see the Breaking table.
        var tokens = ThemeCss.TokenGraph(dark: false);
        for (var i = 1; i <= 8; i++)
        {
            var legacy = i switch
            {
                1 => "#3b82f6",
                2 => "#ef4444",
                3 => "#10b981",
                4 => "#f59e0b",
                5 => "#8b5cf6",
                6 => "#ec4899",
                7 => "#06b6d4",
                8 => "#84cc16",
                _ => throw new InvalidOperationException(),
            };

            ThemeCss.ResolveColour($"var(--tm-chart-series-{i})", tokens).Should().Be(legacy,
                "series {0} in the light theme must stay the legacy colour", i);
        }
    }

    [Fact]
    public void DarkSeriesValues_HoldGraphicalContrast_OnTheDarkSurface()
    {
        // The point of the dark lift: every series must clear WCAG 1.4.11 (3:1 for graphics) against the
        // dark page — the light palette fails there (lime-500 measures 1.9:1 on #0f172a).
        var tokens = ThemeCss.TokenGraph(dark: true);
        var surface = ThemeCss.ResolveColour("var(--tm-bg-page)", tokens);

        for (var i = 1; i <= 8; i++)
        {
            var fill = ThemeCss.ResolveColour($"var(--tm-chart-series-{i})", tokens);
            ThemeCss.Contrast(fill, surface).Should().BeGreaterThanOrEqualTo(3,
                "dark series {0} ({1}) must hold 3:1 on the dark page", i, fill);
        }
    }

    [Fact]
    public void SemanticChartTokens_AliasTheThemeSemanticColours()
    {
        foreach (var dark in new[] { false, true })
        {
            var tokens = ThemeCss.TokenGraph(dark);
            ThemeCss.ResolveColour("var(--tm-chart-positive)", tokens).Should().Be(
                ThemeCss.ResolveColour("var(--tm-color-success)", tokens));
            ThemeCss.ResolveColour("var(--tm-chart-negative)", tokens).Should().Be(
                ThemeCss.ResolveColour("var(--tm-color-danger)", tokens));
        }
    }

    [Fact]
    public void PieChart_CyclesThePaletteThroughAllEightSlots()
    {
        // Nine values in one pie cycle the per-value palette 1..8,1 — the wrap contract.
        var data = new ChartData
        {
            Labels = ["A", "B", "C", "D", "E", "F", "G", "H", "I"],
            Datasets =
            [
                new ChartDataset
                {
                    Label = "Slices",
                    Color = "var(--tm-chart-series-1)",
                    Values = [9, 8, 7, 6, 5, 4, 3, 2, 1],
                },
            ],
        };
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Pie)
            .Add(x => x.Data, data));

        cut.FindAll("path.tm-chart__slice").Select(slice => slice.GetAttribute("fill")).Should().Equal(
            ["var(--tm-chart-series-1)", "var(--tm-chart-series-2)", "var(--tm-chart-series-3)",
             "var(--tm-chart-series-4)", "var(--tm-chart-series-5)", "var(--tm-chart-series-6)",
             "var(--tm-chart-series-7)", "var(--tm-chart-series-8)", "var(--tm-chart-series-1)"]);
    }
}
