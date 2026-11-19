using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan 98c80bc3, phase 10b: the /charts demo shows the opt-in <c>Responsive</c> chart next to the default one, on
/// the same 24 hourly values, with localized headings and a real accessible name on both plots.
/// </summary>
public class ChartsPageResponsiveSectionTests : DemoPageLocalizationTestBase
{
    private ITmLocalizer Localizer => Services.GetRequiredService<ITmLocalizer>();

    [Fact]
    public void ResponsiveSection_RendersTheDefaultAndTheResponsiveChart_SideBySide()
    {
        var cut = Render<ChartsPage>();

        var section = cut.Find("[data-testid='charts-demo-responsive']");
        var off = section.QuerySelector("[data-testid='charts-demo-responsive-off'] .tm-chart__plot")!;
        var on = section.QuerySelector("[data-testid='charts-demo-responsive-on'] .tm-chart__plot")!;

        off.HasAttribute("data-responsive").Should().BeFalse("the left chart keeps the fixed viewBox");
        on.GetAttribute("data-responsive").Should().Be("true");
        on.QuerySelector("svg")!.GetAttribute("viewBox").Should().Be("0 0 600 400", "no measurement before the first frame");
    }

    [Fact]
    public void ResponsiveSection_UsesLocalizedTextAndNamesBothPlots()
    {
        var cut = Render<ChartsPage>();
        var section = cut.Find("[data-testid='charts-demo-responsive']");

        section.QuerySelector("h2")!.TextContent.Trim().Should().Be(Localizer["TmChartDemo_ResponsiveTitle"]);
        section.QuerySelector("p")!.TextContent.Trim().Should().Be(Localizer["TmChartDemo_ResponsiveDescription"]);
        section.QuerySelectorAll("h3").Select(h => h.TextContent.Trim()).Should().Equal(
            Localizer["TmChartDemo_ResponsiveDefault"], Localizer["TmChartDemo_ResponsiveOn"]);
        section.QuerySelectorAll("svg").Select(s => s.GetAttribute("aria-label")).Should()
            .OnlyContain(label => label == Localizer["TmChartDemo_ResponsiveAria"]);
    }
}
