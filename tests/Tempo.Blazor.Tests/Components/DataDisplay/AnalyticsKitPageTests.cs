using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Components;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 9: the <c>/analytics-kit</c> page assembles the whole data and analytics kit into one
/// reporting screen. These tests are the "the page exists and is wired" guard the E2E lane then drives in a
/// browser: the period and its comparison window, the KPI row, the change summary, the comparison chart, the
/// channel breakdown, the faceted order filter and the quota meter.
/// </summary>
[Collection(HeavyDemoPageRenders.Name)]
public class AnalyticsKitPageTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    public AnalyticsKitPageTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(true);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    [Fact]
    public void Page_RendersEverySectionOfTheKit()
    {
        var cut = Render<AnalyticsKitPage>();

        foreach (var section in new[]
                 {
                     "analytics-kit-range",
                     "analytics-kit-kpis",
                     "analytics-kit-trend",
                     "analytics-kit-chart",
                     "analytics-kit-breakdown",
                     "analytics-kit-facets",
                     "analytics-kit-usage",
                 })
        {
            cut.FindAll($"[data-testid='{section}']").Should().ContainSingle($"the page ships the {section} section");
        }
    }

    [Fact]
    public void Page_ShowsNoRawLocalizationKey()
    {
        var cut = Render<AnalyticsKitPage>();

        cut.Markup.Should().NotContain("TmAnalyticsKitDemo_",
            "every heading and label of the page resolves through the localizer");
    }

    [Fact]
    public void KpiRow_RendersTheFourFiguresWithComparisonAndSparkline()
    {
        var cut = Render<AnalyticsKitPage>();

        var cards = cut.Find("[data-testid='analytics-kit-kpis']").QuerySelectorAll(".tm-stat-card");

        cards.Should().HaveCount(4);
        cards.Select(card => card.QuerySelector(".tm-stat-card__comparison")?.TextContent.Trim())
            .Should().OnlyContain(text => text == "vs. previous period");
        cards.SelectMany(card => card.QuerySelectorAll(".tm-stat-card__sparkline .tm-sparkline"))
            .Should().HaveCount(4, "each KPI card carries the history of the period");
        cards[0].QuerySelector(".tm-animated-number").Should().NotBeNull("the revenue card animates to its value");
        cards[3].QuerySelector(".tm-stat-card__trend")!.GetAttribute("data-trend").Should().NotBeNullOrEmpty(
            "the refund rate is compared too, and its tone is inverted by PositiveIsGood=false");
    }

    [Fact]
    public void Period_RendersTheComparisonWindowThePickerDerived()
    {
        var cut = Render<AnalyticsKitPage>();

        var picker = cut.Find("[data-testid='analytics-kit-picker'] .tm-date-range-picker");
        var today = DateOnly.FromDateTime(DateTime.Today);

        picker.GetAttribute("data-comparison").Should().Be("previousPeriod");
        picker.GetAttribute("data-comparison-end").Should().Be(
            today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "the comparison window ends the day before the 30-day window starts");
        picker.GetAttribute("data-comparison-start").Should().Be(
            today.AddDays(-59).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "the comparison window is 30 days long, the same as the reported period");

        cut.Find("[data-testid='analytics-kit-comparison-window']").TextContent.Trim().Should().Be(
            today.AddDays(-59).ToString("d", CultureInfo.CurrentCulture)
            + " – "
            + today.AddDays(-30).ToString("d", CultureInfo.CurrentCulture),
            "the page spells the comparison window with the reader's short-date pattern");

        // The picker prints the period right next to that line, so it has to use the same pattern —
        // its own dd.MM.yyyy default would put two date conventions on one card.
        var trigger = cut.Find("[data-testid='analytics-kit-picker'] .tm-date-range-trigger").TextContent;
        trigger.Should().Contain(today.AddDays(-29).ToString("d", CultureInfo.CurrentCulture))
            .And.Contain(today.ToString("d", CultureInfo.CurrentCulture),
                "the picker spells the period in the culture pattern the comparison line uses");
        cut.Find("[data-testid='analytics-kit-comparison-days']").TextContent.Should().Contain("30");
    }

    [Fact]
    public void Period_SpellsTheComparisonWindowInTheReadersCulture()
    {
        var ambient = CultureInfo.CurrentCulture;
        var french = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            CultureInfo.CurrentCulture = french;

            var cut = Render<AnalyticsKitPage>();
            var today = DateOnly.FromDateTime(DateTime.Today);

            cut.Find("[data-testid='analytics-kit-comparison-window']").TextContent.Trim().Should().Be(
                today.AddDays(-59).ToString("d", french)
                + " – "
                + today.AddDays(-30).ToString("d", french),
                "a French reader gets the French short date (dd/MM/yyyy), not a fixed Czech-style dd.MM.yyyy");
            cut.Find("[data-testid='analytics-kit-picker'] .tm-date-range-trigger").TextContent.Should().Contain(
                today.ToString("d", french),
                "the picker follows the same culture as the comparison line it sits beside");
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }
    }

    [Fact]
    public void KpiRow_TakesTheCurrencySymbolAndItsSideFromTheLocalizer()
    {
        var table = new DemoTableEnglishLocalizer(BuildEnglishLocalizer());

        table["TmAnalyticsKitDemo_CurrencyPrefix"].Should().Be("$",
            "the English demo table puts the symbol in front of the amount");
        table["TmAnalyticsKitDemo_CurrencySuffix"].Should().BeEmpty();

        // A localizer that moves the symbol behind the number: the page must follow the table, not a
        // literal "$" in the markup (Czech, Slovak and French tables place it after the amount).
        Services.AddSingleton<ITmLocalizer>(new CurrencySideLocalizer(table, string.Empty, " EUR"));

        var cut = Render<AnalyticsKitPage>();

        ValueText(cut, 0).Should().NotStartWith("$").And.EndWith(" EUR",
            "the revenue card takes both sides of the currency from the localizer");
        ValueText(cut, 2).Should().NotStartWith("$").And.EndWith(" EUR",
            "the average order value is money too");
        ValueText(cut, 1).Should().NotContain("EUR",
            "the order count is a count, not an amount");
    }

    [Fact]
    public void Period_ChoosingTheTodayPresetMovesBothThePeriodAndTheComparison()
    {
        var cut = Render<AnalyticsKitPage>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        cut.Find("[data-testid='analytics-kit-picker'] .tm-date-range-trigger").Click();
        cut.FindAll(".tm-date-range-preset-btn").First(b => b.TextContent.Trim() == "Today").Click();

        var picker = cut.Find("[data-testid='analytics-kit-picker'] .tm-date-range-picker");
        picker.GetAttribute("data-comparison-end").Should().Be(
            today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        picker.GetAttribute("data-comparison-start").Should().Be(
            today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "a one-day period is compared against the one day before it");
        cut.Find("[data-testid='analytics-kit-comparison-days']").TextContent.Should().Contain("1");
    }

    [Fact]
    public void Granularity_IsATrayThatReBucketsTheChart()
    {
        var cut = Render<AnalyticsKitPage>();

        var tray = cut.Find("[data-testid='analytics-kit-granularity'] .tm-segmented");
        tray.QuerySelectorAll("[role='radio']").Select(option => option.TextContent.Trim())
            .Should().Equal("Day", "Week", "Month");

        var weeklyPoints = ZoneCount(cut);
        cut.FindAll("[data-testid='analytics-kit-granularity'] [role='radio']").First().Click();
        var dailyPoints = ZoneCount(cut);

        dailyPoints.Should().BeGreaterThan(weeklyPoints,
            "the day granularity cuts the same 30-day window into more points");
    }

    [Fact]
    public void Chart_RendersOneIndexZonePerPointAndExactlyOneLiveRegion()
    {
        var cut = Render<AnalyticsKitPage>();

        var chart = cut.Find("[data-testid='analytics-kit-chart'] .tm-chart");
        var zones = chart.QuerySelectorAll(".tm-chart__index-zone");

        zones.Length.Should().BeGreaterThan(1);
        chart.QuerySelectorAll("[aria-live]").Length.Should().Be(1,
            "the chart announces the explored index in exactly one polite live region");
        chart.QuerySelector("svg[aria-keyshortcuts]").Should().NotBeNull("the arrow keys are advertised");
    }

    [Fact]
    public void Chart_IsNamedByItsVisibleHeading()
    {
        var cut = Render<AnalyticsKitPage>();
        var loc = Services.GetRequiredService<ITmLocalizer>();

        var figure = cut.Find("[data-testid='analytics-kit-chart'] figure");
        var labelledBy = figure.GetAttribute("aria-labelledby");

        labelledBy.Should().NotBeNullOrWhiteSpace(
            "a screen reader has to meet the chart's own heading, not a generic graphic");
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be(loc["TmAnalyticsKitDemo_ChartTitle"],
            "the figure is named by the visible h2 of the section");
        figure.QuerySelector("svg[role='img']")!.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace(
            "the SVG keeps its own text alternative as well");
    }

    [Fact]
    public void Breakdown_SwitchingThePeriodReordersTheChannels()
    {
        var cut = Render<AnalyticsKitPage>();

        var order = Parts(cut);
        order.Should().NotBeEmpty();

        // The switcher is the segmented tray inside the card, not a radio row.
        var switcher = cut.Find("[data-testid='analytics-kit-breakdown'] .tm-breakdown-card__period .tm-segmented");
        switcher.QuerySelectorAll("[role='option'], [role='radio']").Length.Should().Be(2);
        cut.FindAll("[data-testid='analytics-kit-breakdown'] [role='radio']")
            .First(option => option.TextContent.Trim() == "Comparison")
            .Click();

        string.Join("|", Parts(cut)).Should().NotBe(string.Join("|", order),
            "the comparison window ranks the channels differently, which is what the FLIP reorder animates");
    }

    [Fact]
    public void Facets_FilterTheNumbersAndTheChipsBringThemBack()
    {
        var cut = Render<AnalyticsKitPage>();
        var initial = ResultCount(cut);

        cut.Find("[data-testid='analytics-kit-facets'] .tm-facet-filter__trigger").Click();

        var paid = cut.FindAll("[data-testid='analytics-kit-facets'] .tm-facet-filter__option")
            .First(option => option.GetAttribute("data-option-value") == "paid");
        var countsBefore = OptionCounts(cut);
        paid.QuerySelector("input")!.Change(true);

        ResultCount(cut).Should().BeLessThan(initial, "the paid status is one of four, so it must exclude orders");
        cut.FindAll("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-chip']").Should().ContainSingle();
        OptionCounts(cut).Should().NotBe(countsBefore,
            "the counts are host-computed over the other groups, so another group re-counts");

        cut.Find("[data-testid='analytics-kit-facets'] .tm-filter-chip-remove").Click();

        ResultCount(cut).Should().Be(initial, "removing the chip restores the unfiltered period");
        cut.FindAll("[data-testid='analytics-kit-facets'] [data-testid='facet-filter-chip']").Should().BeEmpty();
    }

    [Fact]
    public void Facets_ConfirmingRecordsTheResultCount()
    {
        var cut = Render<AnalyticsKitPage>();

        var applied = cut.Find("[data-testid='analytics-kit-facet-applied']").TextContent;

        cut.Find("[data-testid='analytics-kit-facets'] .tm-facet-filter__trigger").Click();
        cut.FindAll("[data-testid='analytics-kit-facets'] .tm-facet-filter__option")
            .First(option => option.GetAttribute("data-option-value") == "search")
            .QuerySelector("input")!.Change(true);
        cut.Find("[data-testid='analytics-kit-facets'] .tm-facet-filter__confirm").Click();

        cut.Find("[data-testid='analytics-kit-facet-applied']").TextContent.Should().NotBe(applied,
            "the confirmation reports the number the filter left behind");
    }

    [Fact]
    public void UsageMeter_RendersTheQuotaInTheWarningStateWithItsCategories()
    {
        var cut = Render<AnalyticsKitPage>();

        var meter = cut.Find("[data-testid='analytics-kit-usage'] .tm-usage-meter");

        meter.GetAttribute("data-state").Should().Be("warning", "8 200 of 10 000 credits is 82 %");
        meter.QuerySelectorAll("[role='meter']").Length.Should().Be(1);
        meter.QuerySelectorAll(".tm-usage-meter__table tbody tr").Length.Should().BeGreaterThanOrEqualTo(3);
        meter.QuerySelector(".tm-usage-meter__cta").Should().NotBeNull(
            "the near-limit slot is what the warning state reveals");
        meter.QuerySelector(".tm-usage-meter__reset").Should().NotBeNull();
        meter.QuerySelector(".tm-usage-meter__daily .tm-usage-meter__sparkline").Should().NotBeNull();
    }

    [Fact]
    public void UsageMeter_SpendingMoreWalksItOverTheLimit()
    {
        var cut = Render<AnalyticsKitPage>();

        cut.Find("[data-testid='analytics-kit-usage-add']").Click();
        cut.Find("[data-testid='analytics-kit-usage-add']").Click();

        var meter = cut.Find("[data-testid='analytics-kit-usage'] .tm-usage-meter");
        meter.GetAttribute("data-state").Should().Be("critical", "11 200 of 10 000 credits is over the limit");
        meter.QuerySelector(".tm-usage-meter__over").Should().NotBeNull();

        cut.Find("[data-testid='analytics-kit-usage-reset']").Click();
        cut.Find("[data-testid='analytics-kit-usage'] .tm-usage-meter").GetAttribute("data-state").Should().Be("warning");
    }

    /// <summary>The number of hover zones the index-tooltip chart drew — its point count, in practice.</summary>
    private static int ZoneCount(IRenderedComponent<AnalyticsKitPage> cut)
        => cut.Find("[data-testid='analytics-kit-chart'] .tm-chart").QuerySelectorAll(".tm-chart__index-zone").Length;

    /// <summary>The channel rows of the breakdown card, in rendered order.</summary>
    private static string[] Parts(IRenderedComponent<AnalyticsKitPage> cut)
        => cut.Find("[data-testid='analytics-kit-breakdown'] .tm-breakdown-card")
            .QuerySelectorAll(".tm-breakdown-card__row")
            .Select(row => row.GetAttribute("data-part-id") ?? row.TextContent.Trim())
            .ToArray();

    /// <summary>The order count the page reports for the filtered period.</summary>
    private static int ResultCount(IRenderedComponent<AnalyticsKitPage> cut)
    {
        var text = cut.Find("[data-testid='analytics-kit-facet-result']").TextContent;
        var match = Regex.Match(text, @"([\d\s.,\u00a0\u202f]+)\s*orders");
        match.Success.Should().BeTrue($"the result line states a count (was '{text}')");
        return int.Parse(
            Regex.Replace(match.Groups[1].Value, @"[^\d]", string.Empty),
            CultureInfo.InvariantCulture);
    }

    /// <summary>The counts every visible facet option currently renders, in render order.</summary>
    private static string OptionCounts(IRenderedComponent<AnalyticsKitPage> cut)
        => string.Join(
            "|",
            cut.FindAll("[data-testid='analytics-kit-facets'] .tm-facet-filter__option")
                .Where(option => option.QuerySelector(".tm-facet-filter__option-count") is not null)
                .Select(option => option.GetAttribute("data-option-value") + "=" +
                                   option.QuerySelector(".tm-facet-filter__option-count")!.TextContent.Trim()));

    /// <summary>The rendered value of one KPI card, in KPI-row order.</summary>
    private static string ValueText(IRenderedComponent<AnalyticsKitPage> cut, int index)
        => cut.FindAll("[data-testid='analytics-kit-kpis'] .tm-stat-card")[index]
            .QuerySelector(".tm-stat-value")!.TextContent.Trim();

    /// <summary>
    /// The demo's English table with the currency on the other side of the number — the stand-in for a
    /// translator who writes <c>" Kč"</c> behind the amount instead of <c>"$"</c> in front of it.
    /// </summary>
    private sealed class CurrencySideLocalizer(ITmLocalizer inner, string prefix, string suffix) : ITmLocalizer
    {
        /// <inheritdoc />
        public CultureInfo Culture => inner.Culture;

        /// <inheritdoc />
        public string this[string key] => Resolve(key, inner[key]);

        /// <inheritdoc />
        public string this[string key, params object[] arguments]
            => Resolve(key, inner[key, arguments]);

        /// <inheritdoc />
        public bool TryGetString(string key, out string value)
            => inner.TryGetString(key, out value);

        private string Resolve(string key, string fallback) => key switch
        {
            "TmAnalyticsKitDemo_CurrencyPrefix" => prefix,
            "TmAnalyticsKitDemo_CurrencySuffix" => suffix,
            _ => fallback,
        };
    }
}
