using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 2: the demo page renders both new components for real — a trend list (list and
/// table layouts, delta, sparkline, loading) and a breakdown card whose period switch re-sorts the
/// parts (which is what the FLIP reorder animates in a browser). The E2E lane then exercises the same
/// sections against the running demo.
/// </summary>
public class DataDisplayPageTrendSectionTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    public DataDisplayPageTrendSectionTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(false);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    [Fact]
    public void TrendListSection_RendersRowsTableAndLoading()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='trend-list-section']");
        section.Should().NotBeNull("the demo page showcases the new component");

        var list = section.QuerySelectorAll(".tm-trend-list").Length;
        list.Should().BeGreaterThanOrEqualTo(2, "the list and the table layout are both shown");

        section.QuerySelectorAll(".tm-trend-list__item").Length.Should().BeGreaterThanOrEqualTo(3);
        section.QuerySelectorAll("table.tm-trend-list__table").Length.Should().BeGreaterThanOrEqualTo(1);
        section.QuerySelectorAll(".tm-trend-list__item .tm-sr-only").Length
            .Should().BeGreaterThanOrEqualTo(3, "every row names its direction for a reader");
        section.QuerySelectorAll(".tm-trend-list__sparkline .tm-sparkline").Length
            .Should().BeGreaterThanOrEqualTo(1);
        section.QuerySelectorAll(".tm-trend-list[aria-busy='true']").Length
            .Should().BeGreaterThanOrEqualTo(1, "the loading state is part of the showcase");
    }

    [Fact]
    public void BreakdownSection_RendersTheBarTheTableAndThePeriodSwitch()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='breakdown-card-section']");
        section.QuerySelectorAll(".tm-breakdown-card").Length.Should().BeGreaterThanOrEqualTo(1);

        var card = section.QuerySelector(".tm-breakdown-card")!;
        card.QuerySelectorAll(".tm-breakdown-card__segment").Length.Should().BeGreaterThanOrEqualTo(3);
        card.QuerySelectorAll("table.tm-breakdown-card__table tbody tr").Length.Should()
            .BeGreaterThanOrEqualTo(3);
        card.QuerySelectorAll(".tm-breakdown-card__period [role='radio']").Length.Should().Be(2,
            "the period switcher is the segmented control, so the periods are radios of one group");
    }

    [Fact]
    public void PeriodSwitch_ReSortsTheParts()
    {
        var cut = Render<DataDisplayPage>();

        var card = cut.Find("[data-testid='breakdown-card-section'] .tm-breakdown-card");
        string FirstLabel() => card.QuerySelectorAll(".tm-breakdown-card__row .tm-breakdown-card__label")[0]
            .TextContent.Trim();
        var before = FirstLabel();

        // "Last week" is the second period; the demo swaps the values so another channel leads.
        cut.Find("[data-testid='breakdown-card-section'] .tm-breakdown-card__period")
            .QuerySelectorAll("[role='radio']")[1].Click();

        cut.WaitForAssertion(() => FirstLabel().Should().NotBe(before,
            "the period switch drives the host data, so the biggest channel changes"));
    }

    [Fact]
    public void TrendSection_AddsNoLiveRegion()
    {
        var cut = Render<DataDisplayPage>();

        cut.FindAll("[data-testid='trend-list-section'] [aria-live]").Should().BeEmpty();
        cut.FindAll("[data-testid='breakdown-card-section'] [aria-live]").Should().BeEmpty();
    }
}
