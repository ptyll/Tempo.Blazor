using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 7: the demo page renders the usage meter for real — a monthly credit quota in
/// the warning state with the near-limit call to action, the category table, the reset date and the
/// daily sparkline, plus the compact copy beside it. The buttons walk the same meter through critical
/// and over the limit (which grows the "Other" row, because the categories stop explaining the usage).
/// The E2E lane drives the same section in a browser.
/// </summary>
public class DataDisplayPageUsageMeterSectionTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    public DataDisplayPageUsageMeterSectionTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(true);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    private static AngleSharp.Dom.IElement Meter(IRenderedComponent<DataDisplayPage> cut)
        => cut.Find("[data-testid='usage-meter-section'] > div > [data-testid='usage-meter']");

    [Fact]
    public void UsageMeterSection_RendersTheFullMeterAndTheCompactCopy()
    {
        var cut = Render<DataDisplayPage>();

        var meters = cut.FindAll("[data-testid='usage-meter-section'] [role='meter']");
        meters.Should().HaveCount(2, "the section shows the full meter and one compact copy");

        var full = Meter(cut);
        full.GetAttribute("data-state").Should().Be("warning");
        full.TextContent.Should().Contain("Monthly credits");
        full.TextContent.Should().Contain("Resets on",
            "the reset line formats the date with the ambient culture, so the test pins the wording only");
    }

    [Fact]
    public void UsageMeterSection_ExposesTheQuotaToAssistiveTechnology()
    {
        var cut = Render<DataDisplayPage>();

        var meter = Meter(cut).QuerySelector("[role='meter']")!;
        meter.GetAttribute("aria-valuemin").Should().Be("0");
        meter.GetAttribute("aria-valuemax").Should().Be("10000");
        meter.GetAttribute("aria-valuenow").Should().Be("8200");
        // The demo does not pin a culture (it follows the request culture), and the percent symbol's
        // spacing comes from that culture's pattern — normalize the no-break space before comparing.
        meter.GetAttribute("aria-valuetext")!.Replace("\u00A0", " ").Should()
            .BeOneOf("8,200 of 10,000 credits, 82%", "8,200 of 10,000 credits, 82 %");
    }

    [Fact]
    public void UsageMeterSection_ShowsTheNearLimitCallToActionWhileWarning()
    {
        var cut = Render<DataDisplayPage>();

        var cta = Meter(cut).QuerySelector("[data-testid='usage-meter-upgrade']");
        cta.Should().NotBeNull();
        cta!.TextContent.Trim().Should().Be("Buy more credits");
    }

    [Fact]
    public void UsageMeterSection_ListsEveryCategoryWithoutAnOtherRow()
    {
        var cut = Render<DataDisplayPage>();

        var rows = Meter(cut).QuerySelectorAll(".tm-usage-meter__table tbody tr");
        rows.Should().HaveCount(3);
        rows.Select(row => row.QuerySelector("th")!.TextContent.Trim())
            .Should().Equal(new[] { "LLM", "Tools", "Storage" });
        Meter(cut).QuerySelectorAll(".tm-usage-meter__row--other").Should().BeEmpty(
            "the demo categories explain the whole usage at load");
    }

    [Fact]
    public void UsageMeterSection_RendersTheDailySparkline()
    {
        var cut = Render<DataDisplayPage>();

        var sparkline = Meter(cut).QuerySelector(".tm-usage-meter__daily svg");
        sparkline.Should().NotBeNull();
        sparkline!.GetAttribute("aria-label").Should().StartWith("Daily usage:");
    }

    [Fact]
    public void UsageMeterSection_AddButtonWalksFromWarningToCriticalAndOver()
    {
        var cut = Render<DataDisplayPage>();

        cut.Find("[data-testid='usage-meter-add']").Click();
        Meter(cut).GetAttribute("data-state").Should().Be("warning",
            "8 200 + 1 500 is 9 700 — still under the limit");

        cut.Find("[data-testid='usage-meter-add']").Click();
        var full = Meter(cut);
        full.GetAttribute("data-state").Should().Be("critical");
        full.GetAttribute("data-over-limit").Should().Be("true");
        full.QuerySelector(".tm-usage-meter__over").Should().NotBeNull();
        full.QuerySelector(".tm-usage-meter__row--other").Should().NotBeNull(
            "the extra usage is no longer explained by the categories");
        full.QuerySelector("[role='meter']")!.GetAttribute("aria-valuenow").Should().Be("11200");
    }

    [Fact]
    public void UsageMeterSection_ResetButtonReturnsToTheInitialMonth()
    {
        var cut = Render<DataDisplayPage>();

        cut.Find("[data-testid='usage-meter-add']").Click();
        cut.Find("[data-testid='usage-meter-add']").Click();
        cut.Find("[data-testid='usage-meter-reset']").Click();

        var full = Meter(cut);
        full.GetAttribute("data-state").Should().Be("warning");
        full.QuerySelector("[role='meter']")!.GetAttribute("aria-valuenow").Should().Be("8200");
        full.QuerySelector(".tm-usage-meter__row--other").Should().BeNull();
    }

    [Fact]
    public void UsageMeterSection_CompactCopyKeepsItsVisibleTitleAndDropsTheChrome()
    {
        var cut = Render<DataDisplayPage>();

        var compact = cut.FindAll("[data-testid='usage-meter-section'] [data-testid='usage-meter']")[1];
        compact.ClassList.Should().Contain("tm-usage-meter--compact");
        compact.QuerySelectorAll(".tm-usage-meter__table").Should().BeEmpty();
        compact.QuerySelectorAll(".tm-usage-meter__daily").Should().BeEmpty();

        var title = compact.QuerySelector(".tm-usage-meter__title");
        title.Should().NotBeNull("a compact meter keeps a visible label, not only an accessible name");
        title!.TextContent.Trim().Should().Be("Monthly credits");

        var meter = compact.QuerySelector("[role='meter']")!;
        meter.GetAttribute("aria-labelledby").Should().Be(title.GetAttribute("id"));
        meter.HasAttribute("aria-label").Should().BeFalse("one name only");
    }

    [Fact]
    public void UsageMeterSection_UsesLocalizedKeysOnly()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='usage-meter-section']");

        section.TextContent.Should().NotContain("[TmDataDisplayDemo",
            "a missing demo key renders its bracket marker, so this catches an unlocalized label");
    }
}
