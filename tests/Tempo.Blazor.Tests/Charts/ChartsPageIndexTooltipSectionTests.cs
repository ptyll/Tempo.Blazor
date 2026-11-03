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
                Localizer["TmChartDemo_IndexTooltipCustom"].Trim());
        }
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
