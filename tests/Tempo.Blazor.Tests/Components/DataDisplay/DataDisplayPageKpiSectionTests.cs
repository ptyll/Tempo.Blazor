using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 1: the demo page renders the new surface for real — the KPI row (delta,
/// hidden direction word, sparkline, loading skeleton), the whole-card targets and the animated
/// numbers, whose bump button retargets the ticker. These tests are the "demo section exists and is
/// wired" guard the E2E lane then exercises in a browser.
/// </summary>
public class DataDisplayPageKpiSectionTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    public DataDisplayPageKpiSectionTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(false);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    [Fact]
    public void KpiRow_RendersDeltaHiddenDirectionAndSparkline()
    {
        var cut = Render<DataDisplayPage>();

        var kpi = cut.Find("[data-testid='stat-cards-kpi']");
        var cards = kpi.QuerySelectorAll(".tm-stat-card");
        cards.Should().HaveCount(4);

        var revenue = cards[0];
        revenue.QuerySelector(".tm-stat-card__delta")!.TextContent.Should().MatchRegex(
            @"^\+12[.,]5\s?%$", "the delta is the culture-formatted signed number plus the percent symbol");
        revenue.QuerySelector(".tm-stat-card__trend .tm-sr-only")!.TextContent.Should().Be("Increase");
        revenue.QuerySelector(".tm-stat-card__comparison")!.TextContent.Should().Be("vs. last month");
        revenue.QuerySelector(".tm-stat-card__sparkline .tm-sparkline").Should().NotBeNull();
        revenue.QuerySelector(".tm-stat-card__icon svg").Should().NotBeNull();
        revenue.QuerySelector(".tm-animated-number").Should().NotBeNull("the revenue card animates its value");

        var loading = cards[3];
        loading.GetAttribute("aria-busy").Should().Be("true");
        loading.QuerySelectorAll(".tm-skeleton").Length.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void InteractiveRow_RendersOneNativeTargetPerCard()
    {
        var cut = Render<DataDisplayPage>();

        var row = cut.Find("[data-testid='stat-cards-interactive']");

        var link = row.QuerySelector("a.tm-stat-card");
        link.Should().NotBeNull();
        link!.GetAttribute("href").Should().Be("data-display");

        var button = row.QuerySelector("button.tm-stat-card");
        button.Should().NotBeNull();
        button!.GetAttribute("type").Should().Be("button");
    }

    [Fact]
    public void AnimatedNumbers_BumpButtonRetargetsTheValue()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='animated-numbers']");
        section.QuerySelectorAll(".tm-animated-number").Length.Should().Be(3);

        var expected = 12450d.ToString("N0", CultureInfo.CurrentCulture);
        section.QuerySelectorAll(".tm-animated-number__value")[0].TextContent.Should().Be(expected);

        cut.Find("[data-testid='animated-bump']").Click();

        var bumped = 13450d.ToString("N0", CultureInfo.CurrentCulture);
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='animated-numbers']").QuerySelectorAll(".tm-animated-number__value")[0]
                .TextContent.Should().Be(bumped, "the demo bumps the target, so the rendered value follows"));
    }

    [Fact]
    public void NeverARegionWithoutTheOptIn()
    {
        var cut = Render<DataDisplayPage>();

        cut.Find("[data-testid='animated-numbers']").QuerySelectorAll("[aria-live]").Length.Should()
            .Be(0, "the demo does not opt into announcements, so a page with several tickers stays silent");
    }
}
