using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan ff043b2e, phase 3: <see cref="ChartTooltipMode.Index"/> — one hover zone per category, a
/// crosshair, a tooltip listing every series at the index, keyboard navigation and one polite live
/// region. <see cref="ChartTooltipMode.Point"/> must stay byte-for-byte what it was.
/// </summary>
public class TmChartIndexTooltipTests : LocalizationTestBase
{
    private static ChartData MultiData => new()
    {
        Labels = ["Q1", "Q2", "Q3"],
        Datasets =
        [
            new ChartDataset { Label = "Licenses", Values = [100, 200, 150], Color = "#2563eb" },
            new ChartDataset { Label = "Services", Values = [120, 180, 220], Color = "#16a34a" }
        ]
    };

    private static ChartData SingleData => new()
    {
        Labels = ["Jan", "Feb", "Mar"],
        Datasets =
        [
            new ChartDataset { Label = "Sales", Values = [10, 20, 30], Color = "#3b82f6" }
        ]
    };

    private static ChartData TimeData => new()
    {
        Labels = ["a", "b", "c"],
        TimePoints =
        [
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        ],
        Datasets =
        [
            new ChartDataset { Label = "Signups", Values = [4, 8, 6], Color = "#3b82f6" }
        ]
    };

    // ── Point mode regression: the new mode may not leak into the default ──

    [Fact]
    public void PointMode_IsTheDefault_AndRendersNoIndexChrome()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData));

        cut.FindAll(".tm-chart__index-zone").Count.Should().Be(0);
        cut.FindAll("[data-testid='chart-index-live']").Count.Should().Be(0);
    }

    [Fact]
    public void PointMode_SvgKeepsNoTabStopAndNoIndexAria()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData));

        var svg = cut.Find("svg");
        svg.GetAttribute("tabindex").Should().BeNull();
        svg.GetAttribute("aria-keyshortcuts").Should().BeNull(
            "a Point-mode chart advertises no key handling");
        svg.GetAttribute("aria-describedby").Should().BeNull(
            "the hidden keyboard hint does not even render outside index mode");
    }

    [Fact]
    public void PointMode_IgnoresIndexKeys()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData));

        // The handler is attached in every mode, so its first line must classify the mode and
        // return: a Point-mode chart never grows a crosshair, a card or an announcement.
        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find("svg").KeyDown("Home");

        cut.Find(".tm-chart").GetAttribute("data-index-ordinal").Should().Be("-1");
        cut.FindAll(".tm-chart__index-zone").Count.Should().Be(0);
        cut.FindAll(".tm-chart__crosshair").Count.Should().Be(0);
        cut.FindAll("[aria-live]").Count.Should().Be(0);
    }

    [Fact]
    public void PointMode_StillShowsThePerPointTooltip()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Point));

        cut.FindAll("rect.tm-chart__bar")[1].MouseOver();

        var tip = cut.Find(".tm-chart__tooltip");
        tip.TextContent.Should().Contain("Feb").And.Contain("20");
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0);
        cut.FindAll(".tm-chart__crosshair").Count.Should().Be(0);
    }

    // ── Hover zones ──

    [Fact]
    public void IndexMode_RendersOneFullHeightHoverZonePerCategory()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var zones = cut.FindAll("rect.tm-chart__index-zone");
        zones.Count.Should().Be(3);

        foreach (var zone in zones)
        {
            zone.GetAttribute("y").Should().Be("20", "the zone spans the plot from the top padding");
            zone.GetAttribute("height").Should().Be("340", "and down to the bottom padding");
        }

        zones[0].GetAttribute("data-testid").Should().Be("chart-index-zone-0");
        zones[2].GetAttribute("data-testid").Should().Be("chart-index-zone-2");
    }

    [Fact]
    public void IndexMode_RootCarriesItsModeAndTheActiveOrdinal_AsAStableStateContract()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var root = cut.Find(".tm-chart");
        root.GetAttribute("data-tooltip-mode").Should().Be("index");
        root.GetAttribute("data-index-ordinal").Should().Be("-1");

        cut.FindAll("rect.tm-chart__index-zone")[2].MouseOver();
        cut.Find(".tm-chart").GetAttribute("data-index-ordinal").Should().Be("2");

        cut.Find("svg").KeyDown("Escape");
        cut.Find(".tm-chart").GetAttribute("data-index-ordinal").Should().Be("-1");
    }

    [Fact]
    public void IndexMode_HoveringAZone_ShowsTheCrosshairAtThatCategory()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll(".tm-chart__crosshair").Count.Should().Be(0, "no crosshair before a hover");

        cut.FindAll("rect.tm-chart__index-zone")[1].MouseOver();

        var crosshair = cut.Find("line.tm-chart__crosshair");
        // Bar slots are CW/labels wide, so Feb's centre is 50 + 1.5 * 176.67 = 315.
        var x = double.Parse(crosshair.GetAttribute("x1")!, CultureInfo.InvariantCulture);
        x.Should().BeApproximately(50 + 1.5 * (530.0 / 3), 0.01);
        crosshair.GetAttribute("data-testid").Should().Be("chart-crosshair");
        crosshair.GetAttribute("y1").Should().Be("20");
        crosshair.GetAttribute("y2").Should().Be("360");
    }

    [Fact]
    public void IndexMode_TooltipListsEverySeriesWithColorLabelAndValue()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone")[1].MouseOver();

        var tip = cut.Find(".tm-chart__tooltip--index");
        tip.TextContent.Should().Contain("Q2");

        var rows = cut.FindAll(".tm-chart__tooltip-series");
        rows.Count.Should().Be(2, "both datasets are visible at the index");
        rows[0].TextContent.Should().Contain("Licenses").And.Contain("200");
        rows[1].TextContent.Should().Contain("Services").And.Contain("180");

        var dots = cut.FindAll(".tm-chart__tooltip-series .tm-chart__tooltip-dot");
        dots[0].GetAttribute("style").Should().Contain("#2563eb");
        dots[1].GetAttribute("style").Should().Contain("#16a34a");
    }

    [Fact]
    public void IndexMode_CustomTemplateReceivesTheWholeIndex()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index)
            .Add(x => x.TooltipIndexTemplate, ctx => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "custom-index-tip");
                builder.AddContent(2, $"{ctx.Label}|{ctx.Series.Count}|{ctx.Total}");
                builder.CloseElement();
            }));

        cut.FindAll("rect.tm-chart__index-zone")[2].MouseOver();

        cut.Find(".custom-index-tip").TextContent.Should().Be("Q3|2|370");
        cut.FindAll(".tm-chart__tooltip-series").Count.Should().Be(0, "the template replaces the rows");
    }

    [Fact]
    public void IndexMode_LegendHiddenSeriesIsAbsentFromTheTooltip()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index)
            .Add(x => x.InteractiveLegend, true));

        cut.FindAll("button.tm-chart__legend-item")[1].Click();
        cut.FindAll("rect.tm-chart__index-zone")[0].MouseOver();

        cut.FindAll(".tm-chart__tooltip-series").Count.Should().Be(1);
        cut.Find(".tm-chart__tooltip--index").TextContent.Should()
            .Contain("Licenses").And.NotContain("Services");
    }

    [Fact]
    public void IndexMode_WithEverySeriesHidden_KeepsTheCrosshairButOpensNoEmptyCard()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index)
            .Add(x => x.InteractiveLegend, true));

        var legend = cut.FindAll("button.tm-chart__legend-item");
        legend[0].Click();
        cut.FindAll("button.tm-chart__legend-item")[1].Click();

        cut.FindAll("rect.tm-chart__index-zone")[0].MouseOver();

        cut.FindAll("line.tm-chart__crosshair").Count.Should().Be(1, "the index is still readable");
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0,
            "an empty card with a lone label is noise");
        cut.Find("[aria-live]").TextContent.Should().Be("Q1", "the category is still announced");
    }

    [Fact]
    public void IndexMode_TooltipStaysInsideTheViewBox()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var zoneCount = cut.FindAll("rect.tm-chart__index-zone").Count;
        zoneCount.Should().Be(3);

        for (var i = 0; i < zoneCount; i++)
        {
            // Re-query: every hover re-renders, so a previously found element carries a dead handler id.
            cut.FindAll("rect.tm-chart__index-zone")[i].MouseOver();
            var fo = cut.Find("foreignObject.tm-chart__tooltip-fo");
            var x = double.Parse(fo.GetAttribute("x")!, CultureInfo.InvariantCulture);
            var y = double.Parse(fo.GetAttribute("y")!, CultureInfo.InvariantCulture);
            var w = double.Parse(fo.GetAttribute("width")!, CultureInfo.InvariantCulture);
            var h = double.Parse(fo.GetAttribute("height")!, CultureInfo.InvariantCulture);

            x.Should().BeGreaterThanOrEqualTo(0);
            y.Should().BeGreaterThanOrEqualTo(0);
            (x + w).Should().BeLessThanOrEqualTo(600);
            (y + h).Should().BeLessThanOrEqualTo(400);
        }
    }

    [Fact]
    public void IndexMode_ShowTooltipFalse_RendersNoZonesAndNoCrosshair()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index)
            .Add(x => x.ShowTooltip, false));

        cut.FindAll(".tm-chart__index-zone").Count.Should().Be(0);
        cut.Find("svg").GetAttribute("tabindex").Should().BeNull();
    }

    // ── Type support ──

    [Theory]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.StackedArea)]
    public void IndexMode_IsSupportedByTheCartesianTypes(ChartType type)
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, type)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone").Count.Should().Be(3);
        cut.Find("svg").GetAttribute("tabindex").Should().Be("0");
    }

    [Theory]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Donut)]
    [InlineData(ChartType.HorizontalBar)]
    [InlineData(ChartType.StackedHorizontalBar)]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.Heatmap)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Waterfall)]
    public void IndexMode_FallsBackToPointOnTypesThePlotCannotHonour(ChartType type)
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, type)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone").Count.Should().Be(0);
        cut.Find("svg").GetAttribute("tabindex").Should().BeNull();
    }

    // ── Keyboard ──

    [Fact]
    public void IndexMode_SvgIsFocusableAndAdvertisesItsKeys()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var svg = cut.Find("svg");
        svg.GetAttribute("tabindex").Should().Be("0");
        svg.GetAttribute("aria-keyshortcuts").Should().Contain("ArrowRight").And.Contain("Escape");
        svg.GetAttribute("role").Should().Be("img", "the chart is one graphic, not a group of widgets");
    }

    [Fact]
    public void IndexMode_SvgIsDescribedByALocalizedKeyboardHint()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var describedBy = cut.Find("svg").GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();

        var hint = cut.Find($"#{describedBy}");
        hint.TextContent.Should().Be(Localized("TmChart_IndexKeyboardHint"));
        hint.ClassList.Should().Contain("tm-sr-only");
    }

    [Fact]
    public void IndexMode_ArrowKeysWalkTheIndices()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0, "focus alone shows nothing");

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Jan");

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Feb");

        cut.Find("svg").KeyDown("ArrowLeft");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Jan");
    }

    [Fact]
    public void IndexMode_ArrowLeftAsTheFirstPressLandsOnTheLastIndex()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.Find("svg").KeyDown("ArrowLeft");

        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Mar");
    }

    [Fact]
    public void IndexMode_ArrowKeysClampAtBothEnds()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.Find("svg").KeyDown("End");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Mar");

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Mar");

        cut.Find("svg").KeyDown("Home");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Jan");

        cut.Find("svg").KeyDown("ArrowLeft");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Jan");
    }

    [Fact]
    public void IndexMode_EscapeHidesTheTooltipAndAnArrowBringsItBack()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.Find("svg").KeyDown("ArrowRight");
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(1);

        cut.Find("svg").KeyDown("Escape");
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0);
        cut.FindAll(".tm-chart__crosshair").Count.Should().Be(0);

        cut.Find("svg").KeyDown("ArrowRight");
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(1);
    }

    [Fact]
    public void IndexMode_EscapeOnANonFocusedChartIsAHarmlessNoOp()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var act = () => cut.Find("svg").KeyDown("Escape");

        act.Should().NotThrow();
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0);
    }

    [Fact]
    public void IndexMode_LeavingTheChartClearsTheIndex()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.Find("svg").KeyDown("ArrowRight");
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(1);

        cut.Find("svg").Blur();

        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0);
    }

    [Fact]
    public void IndexMode_UnhandledKeysKeepTheCurrentIndex()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find("svg").KeyDown("PageDown");
        cut.Find("svg").KeyDown("a");

        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Jan");
    }

    // ── Live region ──

    [Fact]
    public void IndexMode_OnePoliteLiveRegionAnnouncesEverySeries()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var regions = cut.FindAll("[aria-live]");
        regions.Count.Should().Be(1, "exactly one live region, or the index is announced twice");
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].GetAttribute("role").Should().Be("status");
        regions[0].GetAttribute("aria-atomic").Should().Be("true");
        regions[0].ClassList.Should().Contain("tm-sr-only");
        regions[0].TextContent.Should().BeEmpty("nothing is announced before the plot is explored");

        cut.Find("svg").KeyDown("ArrowRight");

        regions = cut.FindAll("[aria-live]");
        regions[0].TextContent.Should().Be(
            string.Format(Localized("TmChart_IndexAnnouncement"), "Q1",
                string.Join(Localized("TmChart_IndexSeparator"),
                    string.Format(Localized("TmChart_IndexSeries"), "Licenses", "100"),
                    string.Format(Localized("TmChart_IndexSeries"), "Services", "120"))));
    }

    [Fact]
    public void IndexMode_LiveRegionIsAlsoUpdatedByAPointerHover()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone")[2].MouseOver();

        cut.Find("[aria-live]").TextContent.Should().Contain("Mar").And.Contain("30");
    }

    [Fact]
    public void IndexMode_TooltipIsInsideTheRoleImgSvg_SoTheOnlyAnnouncementIsTheLiveRegion()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.Find("svg").KeyDown("ArrowRight");

        cut.Find("foreignObject.tm-chart__tooltip-fo").ParentElement!.TagName.Should().Be("svg");
        cut.FindAll(".tm-chart__tooltip[aria-live]").Count.Should().Be(0);
        cut.FindAll(".tm-chart__tooltip[role='status']").Count.Should().Be(0);
    }

    // ── Geometry / data contract ──

    [Fact]
    public void IndexMode_TimeAxisUsesTheTimeFormattedLabel()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, TimeData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone").Count.Should().Be(3);
        cut.Find("svg").KeyDown("ArrowRight");

        var expected = TimeData.TimePoints![0].ToString("d", CultureInfo.CurrentCulture);
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain(expected);
    }

    [Fact]
    public void IndexMode_TimeAxisZonesFollowTheTimestampSpacing()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, TimeData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var zones = cut.FindAll("rect.tm-chart__index-zone");
        var lefts = zones.Select(z => double.Parse(z.GetAttribute("x")!, CultureInfo.InvariantCulture)).ToArray();
        var widths = zones.Select(z => double.Parse(z.GetAttribute("width")!, CultureInfo.InvariantCulture)).ToArray();

        lefts[0].Should().Be(50, "the first zone starts on the Y axis");
        (lefts[^1] + widths[^1]).Should().BeApproximately(580, 0.01, "the last zone ends on the plot edge");
        lefts.Should().BeInAscendingOrder();
        widths.Should().OnlyContain(w => w > 0);

        // The zone centre is where the crosshair stands.
        zones[1].MouseOver();
        var crosshairX = double.Parse(
            cut.Find("line.tm-chart__crosshair").GetAttribute("x1")!, CultureInfo.InvariantCulture);
        crosshairX.Should().BeApproximately(lefts[1] + widths[1] / 2, 0.01);
    }

    [Fact]
    public void IndexMode_ContextExposesTheIndexLabelAndEveryVisibleSeries()
    {
        ChartIndexTooltipContext? captured = null;
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.StackedBar)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index)
            .Add(x => x.TooltipIndexTemplate, ctx => builder =>
            {
                captured = ctx;
                builder.AddContent(0, ctx.Label);
            }));

        cut.FindAll("rect.tm-chart__index-zone")[1].MouseOver();

        captured.Should().NotBeNull();
        captured!.Index.Should().Be(1);
        captured.Label.Should().Be("Q2");
        captured.Series.Should().HaveCount(2);
        captured.Series[0].Should().Be(new ChartIndexSeriesValue(0, "Licenses", 200, "#2563eb"));
        captured.Series[1].DatasetIndex.Should().Be(1);
        captured.Total.Should().Be(380);
    }

    [Fact]
    public void IndexMode_ContextDropsNaNAndOutOfRangeValues()
    {
        var data = new ChartData
        {
            Labels = ["A", "B"],
            Datasets =
            [
                new ChartDataset { Label = "Real", Values = [1, 2], Color = "#2563eb" },
                new ChartDataset { Label = "Gap", Values = [1, double.NaN], Color = "#16a34a" },
                new ChartDataset { Label = "Short", Values = [5], Color = "#f59e0b" }
            ]
        };

        ChartIndexTooltipContext? captured = null;
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index)
            .Add(x => x.TooltipIndexTemplate, ctx => builder =>
            {
                captured = ctx;
                builder.AddContent(0, ctx.Label);
            }));

        cut.FindAll("rect.tm-chart__index-zone")[1].MouseOver();

        captured!.Series.Select(s => s.Label).Should().Equal("Real");
    }

    [Fact]
    public void IndexMode_HoveringASecondZoneReplacesTheFirstInsteadOfStackingTooltips()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var zones = cut.FindAll("rect.tm-chart__index-zone");
        zones[0].MouseOver();
        cut.FindAll("rect.tm-chart__index-zone")[2].MouseOver();

        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(1);
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Mar");
    }

    [Fact]
    public void IndexMode_SwappingTheDataInstanceResetsTheIndex()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.Find("svg").KeyDown("End");
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(1);

        cut.Render(p => p.Add(x => x.Data, MultiData));

        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0);
    }

    // ── Click forwarding ──

    [Fact]
    public void IndexMode_ClickingAZoneReportsTheIndexThroughOnSegmentClick()
    {
        ChartSegment? raised = null;
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index)
            .Add(x => x.OnSegmentClick, (ChartSegment segment) => raised = segment));

        cut.FindAll("rect.tm-chart__index-zone")[1].Click();

        raised.Should().NotBeNull();
        raised!.Index.Should().Be(1);
        raised.Label.Should().Be("Q2");
        raised.Value.Should().Be(200, "the first visible series owns the click in index mode");
    }

    [Fact]
    public void IndexMode_TappingAZonePinsTheCard_SoACoarsePointerHasANonHoverPath()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone")[1].Click();

        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Feb");
        cut.Find("line.tm-chart__crosshair").Should().NotBeNull();
        cut.Find("[aria-live]").TextContent.Should().Contain("Feb");

        // A touch browser follows the tap with the "the pointer left" event (there was never a
        // pointer), so a pinned category must survive it — otherwise the card flashes and vanishes.
        cut.FindAll("rect.tm-chart__index-zone")[1].MouseOut();
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(1);

        // A real hover elsewhere takes the pin over.
        cut.FindAll("rect.tm-chart__index-zone")[2].MouseOver();
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Mar");
    }

    [Fact]
    public void IndexMode_EscapeReleasesTheTapPin()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone")[1].Click();
        cut.Find("svg").KeyDown("Escape");

        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0);

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Jan");
    }

    [Fact]
    public void IndexMode_ALateMouseoutOfThePreviousZone_DoesNotBlankTheNewCard()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        // The two DOM events cross the JS→.NET boundary separately, so their arrival order is not
        // guaranteed: the guard must make "enter B" and "leave A" commutative in both orders.
        cut.FindAll("rect.tm-chart__index-zone")[0].MouseOver();
        cut.FindAll("rect.tm-chart__index-zone")[2].MouseOver();
        cut.FindAll("rect.tm-chart__index-zone")[0].MouseOut();

        cut.Find(".tm-chart__tooltip--index").TextContent.Should().Contain("Mar");
        cut.FindAll(".tm-chart__crosshair").Count.Should().Be(1);
    }

    [Fact]
    public void IndexMode_LeavingThePlotClearsTheCard()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        cut.FindAll("rect.tm-chart__index-zone")[1].MouseOver();
        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(1);

        cut.FindAll("rect.tm-chart__index-zone")[1].MouseOut();

        cut.FindAll(".tm-chart__tooltip--index").Count.Should().Be(0);
        cut.FindAll(".tm-chart__crosshair").Count.Should().Be(0);
    }

    [Fact]
    public void IndexMode_ClickIsANoOpWithoutAnOnSegmentClickHandler()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, MultiData)
            .Add(x => x.TooltipMode, ChartTooltipMode.Index));

        var act = () => cut.FindAll("rect.tm-chart__index-zone")[0].Click();

        act.Should().NotThrow();
    }

    // ── Localized chart aria label (no hardcoded "chart" word) ──

    [Fact]
    public void ChartAriaLabel_ComesFromTheLocalizer()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Bar)
            .Add(x => x.Data, SingleData));

        cut.Find("svg").GetAttribute("aria-label").Should()
            .Be(string.Format(Localized("TmChart_AriaLabel"), ChartType.Bar));
    }

    private string Localized(string key) => Services.GetRequiredService<ITmLocalizer>()[key];
}
