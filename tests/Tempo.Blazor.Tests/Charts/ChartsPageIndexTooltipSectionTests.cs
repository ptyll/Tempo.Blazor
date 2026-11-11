using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan ff043b2e phase 3: the demo page renders both additions for real — a line chart in
/// <c>TooltipMode.Index</c>, a stacked chart with a custom <c>TooltipIndexTemplate</c> and four
/// sparklines that showcase the text alternative, the min/max caption, the last-value ring and the
/// tooltip. The E2E lane then exercises the same sections in a browser.
/// </summary>
public class ChartsPageIndexTooltipSectionTests : DemoPageLocalizationTestBase
{
    private ITmLocalizer Localizer => Services.GetRequiredService<ITmLocalizer>();

    [Fact]
    public void IndexTooltipSection_RendersOneHoverZonePerCategory()
    {
        var cut = Render<ChartsPage>();

        var section = cut.Find("[data-testid='charts-demo-index-tooltip']");
        var chart = section.QuerySelector("[data-testid='charts-demo-index-default'] .tm-chart")!;

        chart.QuerySelectorAll(".tm-chart__index-zone").Length.Should().Be(6, "one zone per month");
        chart.QuerySelector("svg")!.GetAttribute("tabindex").Should().Be("0", "the chart is a tab stop");
        chart.QuerySelectorAll("[aria-live]").Length.Should().Be(1, "exactly one live region");
    }

    [Fact]
    public void IndexTooltipSection_CustomTemplateShowsTheCategoryTotal()
    {
        var cut = Render<ChartsPage>();

        var custom = cut.Find("[data-testid='charts-demo-index-custom']");
        var chart = custom.QuerySelector(".tm-chart")!;

        chart.QuerySelectorAll(".tm-chart__index-zone").Length.Should().Be(6);

        // Hover the second month through the real handler chain and read the rendered card.
        cut.FindAll("[data-testid='charts-demo-index-custom'] .tm-chart__index-zone")[1].MouseOver();

        var total = cut.Find("[data-testid='charts-demo-index-total']");
        total.TextContent.Should().Contain("650", "410 + 240 is the category total the template prints");
    }

    [Fact]
    public void IndexTooltipSection_HasNoHardcodedEnglish()
    {
        var cut = Render<ChartsPage>();
        var section = cut.Find("[data-testid='charts-demo-index-tooltip']");

        section.QuerySelector("h2")!.TextContent.Trim()
            .Should().Be(Localizer["TmChartDemo_IndexTooltipTitle"]);
        section.QuerySelector("p")!.TextContent.Trim()
            .Should().Be(Localizer["TmChartDemo_IndexTooltipDescription"]);

        foreach (var heading in section.QuerySelectorAll("h3"))
        {
            var text = heading.TextContent.Trim();
            text.Should().BeOneOf(
                Localizer["TmChartDemo_IndexTooltipDefault"].Trim(),
                Localizer["TmChartDemo_IndexTooltipCustom"].Trim(),
                Localizer["TmChartDemo_IndexManyTitle"].Trim());
        }
    }

    /// <summary>
    /// Plan 7ba95ed6, Fáze 17 — the SEVEN-series fixture of the index tooltip. The two-series cards above
    /// never reach the path the review found broken: a chart with more series than the two the demo had
    /// collided its overlay sequence bands once the dot band passed the zones group, which replaced the
    /// hover zone under the pointer. The browser lanes can only assert that DOM identity through a chart
    /// that really has seven series, so the page renders one.
    /// </summary>
    [Fact]
    public void IndexTooltipSection_SevenSeriesCard_RendersTheManySeriesFixture()
    {
        var cut = Render<ChartsPage>();

        var many = cut.Find("[data-testid='charts-demo-index-many']");
        var chart = many.QuerySelector(".tm-chart")!;

        chart.QuerySelectorAll(".tm-chart__index-zone").Length.Should().Be(6, "one zone per month");
        chart.QuerySelectorAll(".tm-chart__legend-item").Length.Should().Be(7, "seven series, one legend row each");
        chart.QuerySelector("svg")!.GetAttribute("aria-label")
            .Should().Be(Localizer["TmChartDemo_IndexManyAriaLabel"],
                "the demo names its own chart instead of leaning on the generic text alternative");

        // The card lists EVERY series at the index — seven rows, each with its own swatch. bUnit rebuilds its DOM
        // from the render tree on every render, so the chart is re-found after the hover instead of reusing the
        // element captured before it (the same rule the TmChart index-tooltip suite documents).
        cut.FindAll("[data-testid='charts-demo-index-many'] .tm-chart__index-zone")[2].MouseOver();

        var hovered = cut.Find("[data-testid='charts-demo-index-many'] .tm-chart");
        hovered.QuerySelectorAll(".tm-chart__tooltip-series").Length.Should().Be(7);
        hovered.QuerySelectorAll(".tm-chart__tooltip-series .tm-chart__tooltip-dot").Length.Should().Be(7);

        // Every series label is the page's own localized content, never a raw key.
        var labels = hovered.QuerySelectorAll(".tm-chart__tooltip-series-label")
            .Select(label => label.TextContent.Trim())
            .ToArray();
        labels.Should().OnlyContain(label => !label.StartsWith("TmChartDemo_", StringComparison.Ordinal));
        labels.Should().OnlyHaveUniqueItems("seven distinguishable series need seven distinct names");
    }

    [Fact]
    public void SparklineSection_ShowsTheFourVariantsAndTheirOptIns()
    {
        var cut = Render<ChartsPage>();

        var section = cut.Find("[data-testid='charts-demo-sparkline-extras']");
        section.QuerySelectorAll(".tm-sparkline").Length.Should().Be(4);

        var plain = section.QuerySelector("[data-testid='charts-demo-sparkline-plain'] .tm-sparkline")!;
        plain.QuerySelector("svg")!.HasAttribute("tabindex").Should().BeFalse("the default is unchanged");
        plain.QuerySelectorAll(".tm-sparkline__last-point").Length.Should().Be(0);

        var minMax = section.QuerySelector("[data-testid='charts-demo-sparkline-minmax'] .tm-sparkline")!;
        minMax.QuerySelector(".tm-sparkline__minmax").Should().NotBeNull();
        minMax.QuerySelector(".tm-sparkline__minmax")!.GetAttribute("aria-hidden").Should().Be("true");

        var lastPoint = section.QuerySelector("[data-testid='charts-demo-sparkline-last-point'] .tm-sparkline")!;
        lastPoint.QuerySelectorAll(".tm-sparkline__last-point").Length.Should().Be(1);
        lastPoint.QuerySelector("path.tm-sparkline__area")!.GetAttribute("fill")
            .Should().Be("var(--tm-color-success)", "the demo shows the Color override");

        var tooltip = section.QuerySelector("[data-testid='charts-demo-sparkline-tooltip'] .tm-sparkline")!;
        tooltip.QuerySelector("svg")!.GetAttribute("tabindex").Should().Be("0");
        tooltip.QuerySelectorAll("[aria-live]").Length.Should().Be(1);
    }

    [Fact]
    public void SparklineSection_HasNoHardcodedEnglish()
    {
        var cut = Render<ChartsPage>();
        var section = cut.Find("[data-testid='charts-demo-sparkline-extras']");

        section.QuerySelector("h2")!.TextContent.Trim()
            .Should().Be(Localizer["TmChartDemo_SparklineExtrasTitle"]);
        section.QuerySelector("p")!.TextContent.Trim()
            .Should().Be(Localizer["TmChartDemo_SparklineExtrasDescription"]);

        var labels = section.QuerySelectorAll(".tm-sparkline")
            .Select((sparkline, index) => section.QuerySelectorAll("span.text-sm")[index].TextContent.Trim())
            .ToArray();
        labels.Should().BeEquivalentTo(
        [
            Localizer["TmChartDemo_SparklinePlain"].Trim(),
            Localizer["TmChartDemo_SparklineMinMax"].Trim(),
            Localizer["TmChartDemo_SparklineLastPoint"].Trim(),
            Localizer["TmChartDemo_SparklineTooltip"].Trim()
        ]);
    }
}
