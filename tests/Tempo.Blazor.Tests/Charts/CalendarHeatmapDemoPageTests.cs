using System.Globalization;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

public class CalendarHeatmapDemoPageTests : DemoPageLocalizationTestBase
{
    [Fact]
    public void ChartsPage_RendersCalendarHeatmapVariantsAndClickOutput()
    {
        var cut = Render<ChartsPage>();

        var section = cut.Find("[data-testid='calendar-heatmap']");
        var annual = section.QuerySelector("[data-testid='calendar-heatmap-annual']");
        var range = section.QuerySelector("[data-testid='calendar-heatmap-range']");

        annual.Should().NotBeNull();
        range.Should().NotBeNull();
        annual!.QuerySelector(".tm-calendar-heatmap--primary").Should().NotBeNull();
        range!.QuerySelector(".tm-calendar-heatmap--danger").Should().NotBeNull();
        annual.QuerySelectorAll(".tm-calendar-heatmap__day").Should().HaveCount(365);
        range.QuerySelectorAll(".tm-calendar-heatmap__day").Should().HaveCount(90);

        var firstDay = annual.QuerySelector("[data-date='2026-01-01']")!;
        firstDay.GetAttribute("aria-label").Should()
            .Contain(CultureInfo.CurrentUICulture.NumberFormat.CurrencySymbol);
        firstDay.Click();

        section.QuerySelector("[data-testid='calendar-heatmap-clicked']")!.TextContent
            .Should().Contain(new DateOnly(2026, 1, 1).ToString("d", CultureInfo.CurrentUICulture));
    }
}
