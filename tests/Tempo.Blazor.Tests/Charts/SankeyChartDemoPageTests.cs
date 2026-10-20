using System.Globalization;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

public class SankeyChartDemoPageTests : DemoPageLocalizationTestBase
{
    [Fact]
    public void ChartsPage_RendersFinancialSankeyVariantsAndClickOutput()
    {
        var cut = Render<ChartsPage>();

        var section = cut.Find("[data-testid='sankey-chart']");
        var cashFlow = section.QuerySelector("[data-testid='sankey-cashflow']");
        var customColors = section.QuerySelector("[data-testid='sankey-custom-colors']");

        cashFlow.Should().NotBeNull();
        customColors.Should().NotBeNull();
        section.QuerySelectorAll(".tm-sankey").Should().HaveCount(2);
        cashFlow!.QuerySelectorAll("rect.tm-sankey__node").Should().HaveCount(7);
        cashFlow.QuerySelectorAll("path.tm-sankey__link").Should().HaveCount(6);
        cashFlow.TextContent.Should().Contain("Salary");
        cashFlow.TextContent.Should().Contain("Savings");
        var expectedCurrencySymbol = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "cs" => CultureInfo.GetCultureInfo("cs-CZ").NumberFormat.CurrencySymbol,
            "fr" => CultureInfo.GetCultureInfo("fr-FR").NumberFormat.CurrencySymbol,
            _ => CultureInfo.GetCultureInfo("en-US").NumberFormat.CurrencySymbol
        };
        cashFlow.TextContent.Should().Contain(expectedCurrencySymbol);
        customColors!.QuerySelector("rect[data-node-id='budget']")!
            .GetAttribute("fill").Should().Be("#7c3aed");

        cashFlow.QuerySelector("rect[data-node-id='salary']")!.Click();
        cut.Find("[data-testid='sankey-clicked']").TextContent
            .Should().Contain("Salary");

        cut.Find("[data-testid='sankey-cashflow'] path[data-link-index='0']").Click();
        cut.Find("[data-testid='sankey-clicked']").TextContent
            .Should().Contain("Salary → Budget");
    }
}
