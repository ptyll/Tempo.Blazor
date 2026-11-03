using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 4: the demo page renders the facet filter for real — the popover presentation
/// (host-computed result count, a disabled option, a truncated group and an initially collapsed
/// group) and the inline presentation over the same groups. The E2E lane then drives the same
/// sections in a browser, including the phone sheet.
/// </summary>
public class DataDisplayPageFacetSectionTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    public DataDisplayPageFacetSectionTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(true);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    [Fact]
    public void FacetSection_RendersBothPresentations()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='facet-filter-section']");

        section.QuerySelectorAll(".tm-facet-filter").Length.Should().Be(2,
            "the popover presentation and the inline presentation share one showcase row");
        section.QuerySelectorAll(".tm-facet-filter[data-presentation='inline']").Length.Should().Be(1);
        section.QuerySelectorAll(".tm-facet-filter[data-presentation='popover']").Length.Should().Be(1);
    }

    [Fact]
    public void FacetSection_OpensWithAChipAndTheInlineHeading()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='facet-filter-section']");

        section.QuerySelectorAll(".tm-facet-filter__chip").Length.Should().BeGreaterThanOrEqualTo(2,
            "both cards open with one value picked, so the chips are visible on first paint");
        section.QuerySelectorAll(".tm-facet-filter__inline .tm-facet-filter__title").Length.Should().Be(1);
        section.QuerySelectorAll("[data-testid='facet-filter-confirm']").Length.Should().Be(0,
            "the popover is closed until the reader opens it");
    }

    [Fact]
    public void FacetSection_Popover_ListsGroupsWithCountsAndADisabledOption()
    {
        var cut = Render<DataDisplayPage>();

        cut.Find("[data-testid='facet-filter-section'] [data-testid='facet-filter-trigger']").Click();

        var panel = cut.Find("[data-testid='facet-filter-section'] [data-testid='facet-filter-panel']");
        panel.QuerySelectorAll("[data-testid='facet-filter-group']").Length.Should().Be(3);
        panel.QuerySelectorAll("[data-testid='facet-filter-option'] input[type='checkbox'][disabled]").Length
            .Should().Be(1, "the demo shows an option the current data cannot offer");
        panel.QuerySelectorAll("[data-testid='facet-filter-show-more']").Length.Should().Be(1,
            "one group is truncated, the two short ones are not");
        panel.QuerySelectorAll("[data-testid='facet-filter-search']").Length.Should().Be(1,
            "only the long group gets a search box");
        panel.QuerySelector("[data-testid='facet-filter-confirm']")!.TextContent.Trim().Should()
            .MatchRegex(@"^Show \d+ results$", "the demo host knows the result count, so the primary action counts it");
    }

    [Fact]
    public void FacetSection_TogglingAnOptionAddsItsChip()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='facet-filter-section']");
        var before = section.QuerySelectorAll(".tm-facet-filter__chip").Length;

        cut.Find("[data-testid='facet-filter-section'] [data-testid='facet-filter-trigger']").Click();
        cut.FindAll("[data-testid='facet-filter-section'] [data-testid='facet-filter-option'] input[type='checkbox']")
            .First(input => !input.HasAttribute("checked") && !input.HasAttribute("disabled"))
            .Change(true);

        cut.WaitForAssertion(() => cut.Find("[data-testid='facet-filter-section']")
            .QuerySelectorAll(".tm-facet-filter__chip").Length.Should().Be(before + 1));
    }

    [Fact]
    public void FacetSection_AddsExactlyOneLiveRegion()
    {
        var cut = Render<DataDisplayPage>();

        cut.FindAll("[data-testid='facet-filter-section'] [role='status']").Should().HaveCount(2,
            "one polite region per filter instance — the result count is the only thing announced");
        cut.FindAll("[data-testid='facet-filter-section'] [role='status'][aria-live='polite']").Should().HaveCount(2);
    }
}
