using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 7: <c>TmUsageMeter</c> — how much of a quota is gone. These facts pin the
/// surface a host and a reader get: the <c>role="meter"</c> value contract (min/max/now plus the
/// culture-formatted <c>aria-valuetext</c>), the warning and critical states carried by an ICON, a
/// TEXT and a colour, the over-limit reading, the category segments with their real table, the reset
/// line, the daily sparkline, the compact presentation, the <c>NearLimitContent</c> slot and the one
/// polite live region that announces a state TRANSITION only.
/// </summary>
public class TmUsageMeterTests : LocalizationTestBase
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    // ---------- fixtures ----------

    private IRenderedComponent<TmUsageMeter> RenderMeter(
        Action<ComponentParameterCollectionBuilder<TmUsageMeter>> configure)
        => RenderMeterIn(English, configure);

    private IRenderedComponent<TmUsageMeter> RenderMeterIn(
        CultureInfo culture,
        Action<ComponentParameterCollectionBuilder<TmUsageMeter>> configure)
        => Render<TmUsageMeter>(parameters =>
        {
            parameters.Add(component => component.Culture, culture);
            configure(parameters);
        });

    private static IReadOnlyList<UsageCategory> Categories(params (string Key, string Label, double Used)[] values)
        => values.Select(value => new UsageCategory { Key = value.Key, Label = value.Label, Used = value.Used }).ToList();

    // ---------- the model ----------

    [Fact]
    public void UsageCategory_DefaultsAreEmptyAndZero()
    {
        var category = new UsageCategory();

        category.Key.Should().BeEmpty();
        category.Label.Should().BeEmpty();
        category.Used.Should().Be(0);
        category.Color.Should().BeNull();
    }

    // ---------- root / naming ----------

    [Fact]
    public void Root_RendersTheBaseClassAndTheDefaultTestId()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 1_000).Add(c => c.Limit, 10_000));

        cut.Find(".tm-usage-meter").Should().NotBeNull();
        cut.Find("[data-testid='usage-meter']").Should().NotBeNull();
        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be("normal");
    }

    [Fact]
    public void Title_RendersAsAHeadingAndNamesTheMeter()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Title, "Monthly credits")
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000));

        var title = cut.Find(".tm-usage-meter__title");
        title.TextContent.Trim().Should().Be("Monthly credits");
        title.GetAttribute("id").Should().NotBeNullOrEmpty();
        title.TagName.Should().Be("H3", "the title is a real heading, not a styled div");

        var meter = cut.Find("[role='meter']");
        meter.GetAttribute("aria-labelledby").Should().Be(title.GetAttribute("id"));
        meter.HasAttribute("aria-label").Should().BeFalse("the visible heading is the name — one label, not two");
    }

    [Theory]
    [InlineData(2, "H2")]
    [InlineData(4, "H4")]
    [InlineData(6, "H6")]
    public void HeadingLevel_PicksTheHeadingTag(int level, string expected)
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Title, "Monthly credits")
            .Add(c => c.HeadingLevel, level)
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000));

        cut.Find(".tm-usage-meter__title").TagName.Should().Be(expected,
            "a page whose outline is not an h3 outline must be able to keep the meter under its own heading");
    }

    [Theory]
    [InlineData(1, "H2")]
    [InlineData(0, "H2")]
    [InlineData(-4, "H2")]
    [InlineData(7, "H6")]
    public void HeadingLevel_IsClampedToTheTwoToSixBand(int level, string expected)
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Title, "Monthly credits")
            .Add(c => c.HeadingLevel, level)
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000));

        cut.Find(".tm-usage-meter__title").TagName.Should().Be(expected,
            "a level outside 2–6 falls back to the nearest valid level instead of rendering an h0/h9");
    }

    [Fact]
    public void WithoutTitle_TheAriaLabelParameterNamesTheMeter()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.AriaLabel, "API quota")
            .Add(c => c.Used, 10));

        var meter = cut.Find("[role='meter']");
        meter.GetAttribute("aria-label").Should().Be("API quota");
        meter.HasAttribute("aria-labelledby").Should().BeFalse();
    }

    [Fact]
    public void WithoutTitleOrAriaLabel_ALocalizedDefaultNamesTheMeter()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 10));

        cut.Find("[role='meter']").GetAttribute("aria-label").Should().Be("Usage");
    }

    [Fact]
    public void BlankTitle_RendersNoHeadingAndFallsBackToTheLocalizedName()
    {
        var cut = RenderMeter(p => p.Add(c => c.Title, "   ").Add(c => c.Used, 10));

        cut.FindAll(".tm-usage-meter__title").Should().BeEmpty("a blank title is no title");
        cut.Find("[role='meter']").GetAttribute("aria-label").Should().Be("Usage",
            "a whitespace title must not become an empty accessible name");
    }

    // ---------- the meter value contract ----------

    [Fact]
    public void Meter_ExposesMinMaxNowAndTheCultureFormattedValueText()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 7_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Unit, "credits"));

        var meter = cut.Find("[role='meter']");
        meter.GetAttribute("aria-valuemin").Should().Be("0");
        meter.GetAttribute("aria-valuemax").Should().Be("10000");
        meter.GetAttribute("aria-valuenow").Should().Be("7500");
        meter.GetAttribute("aria-valuetext").Should().Be("7,500 of 10,000 credits, 75%");
    }

    [Fact]
    public void Meter_WithoutAUnit_DoesNotLeaveAGapInTheValueText()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 7_500).Add(c => c.Limit, 10_000));

        cut.Find("[role='meter']").GetAttribute("aria-valuetext").Should().Be("7,500 of 10,000, 75%");
    }

    [Fact]
    public void Meter_FormatsTheValueTextWithTheSuppliedCulture()
    {
        Services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());

        var cut = RenderMeterIn(Czech, p => p
            .Add(c => c.Used, 7_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Unit, "kreditů"));

        cut.Find("[role='meter']").GetAttribute("aria-valuetext")
            .Should().Be("7\u00A0500 z 10\u00A0000 kreditů, 75\u00A0%",
                "Czech groups with a no-break space and puts one before the percent symbol");
    }

    [Fact]
    public void Meter_ValueFormatterOverridesTheAmounts()
    {
        // The host formatter interpolates with the ambient culture; the assertion needs the invariant baseline.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 7_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.ValueFormatter, value => value >= 1_000 ? $"{value / 1_000:0.#}k" : value.ToString("0", English)));

        cut.Find("[role='meter']").GetAttribute("aria-valuetext").Should().Be("7.5k of 10k, 75%");
        cut.Find(".tm-usage-meter__amount").TextContent.Should().Contain("7.5k");
    }

    [Fact]
    public void Meter_ValueFormatControlsThePlainNumberFormat()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 7_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.ValueFormat, "N1"));

        cut.Find("[role='meter']").GetAttribute("aria-valuetext").Should().Be("7,500.0 of 10,000.0, 75%");
    }

    [Fact]
    public void VisibleReadout_RepeatsTheSameAmountAndPercent()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 7_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Unit, "credits"));

        cut.Find(".tm-usage-meter__amount").TextContent.Trim().Should().Be("7,500 / 10,000 credits");
        cut.Find(".tm-usage-meter__percent").TextContent.Trim().Should().Be("75%");
    }

    [Fact]
    public void Fill_PaintsTheUsedShareOfTheTrack()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 2_500).Add(c => c.Limit, 10_000));

        cut.Find(".tm-usage-meter__fill").GetAttribute("style").Should().Contain("inline-size:25%");
    }

    [Fact]
    public void ZeroLimit_IsTreatedAsAnUnknownLimit_NotAsAnInfinity()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 40).Add(c => c.Limit, 0));

        var meter = cut.Find("[role='meter']");
        meter.GetAttribute("aria-valuemax").Should().Be("0");
        meter.GetAttribute("aria-valuenow").Should().Be("0");
        meter.GetAttribute("aria-valuetext").Should().Be("0 of 0, 0%");
        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be("normal");
    }

    [Fact]
    public void ZeroLimit_StaysNormal_EvenWithNonPositiveThresholds()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 40)
            .Add(c => c.Limit, 0)
            .Add(c => c.WarningThreshold, -1)
            .Add(c => c.CriticalThreshold, -1));

        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be("normal",
            "nothing is measurable against an unknown limit, so a threshold cannot claim a state");
        cut.FindAll(".tm-usage-meter__state").Should().BeEmpty();
    }

    [Fact]
    public void NaNWarningThreshold_FallsBackToTheDefault()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.WarningThreshold, double.NaN));

        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be("warning",
            "NaN is not a ratio; the documented default (0.8) decides instead of a silent normal state");
    }

    [Fact]
    public void NaNCriticalThreshold_FallsBackToTheDefault()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 10_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.CriticalThreshold, double.NaN));

        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be("critical",
            "the default critical threshold is the limit itself");
    }

    [Fact]
    public void NegativeWarningThreshold_CountsAsZero()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 0)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.WarningThreshold, -0.5)
            .Add(c => c.CriticalThreshold, 2));

        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be("warning",
            "0 behaves like the documented WarningThreshold=0 (warn immediately), never like a negative ratio");
    }

    [Fact]
    public void NegativeUsed_ClampsToZeroWithoutLosingTheLimit()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, -40).Add(c => c.Limit, 100));

        var meter = cut.Find("[role='meter']");
        meter.GetAttribute("aria-valuenow").Should().Be("0");
        meter.GetAttribute("aria-valuetext").Should().Be("0 of 100, 0%");
    }

    // ---------- states ----------

    [Theory]
    [InlineData(0, "normal")]
    [InlineData(7_999, "normal")]
    [InlineData(8_000, "warning")]
    [InlineData(9_999, "warning")]
    [InlineData(10_000, "critical")]
    [InlineData(12_000, "critical")]
    public void State_FollowsTheThresholds(double used, string expected)
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, used).Add(c => c.Limit, 10_000));

        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be(expected);
    }

    [Fact]
    public void WarningState_CarriesAnIconATextAndAColour()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 8_500).Add(c => c.Limit, 10_000));

        var root = cut.Find("[data-testid='usage-meter']");
        root.ClassList.Should().Contain("tm-usage-meter--warning");

        var state = cut.Find(".tm-usage-meter__state");
        state.QuerySelector("svg").Should().NotBeNull("the state is never colour-only");
        state.TextContent.Trim().Should().Be("Near limit");
    }

    [Fact]
    public void CriticalState_ReadsAsTheLimitReached()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 10_000).Add(c => c.Limit, 10_000));

        var root = cut.Find("[data-testid='usage-meter']");
        root.ClassList.Should().Contain("tm-usage-meter--critical");
        cut.Find(".tm-usage-meter__state").QuerySelector("svg").Should().NotBeNull();
        cut.Find(".tm-usage-meter__state").TextContent.Trim().Should().Be("Limit reached");
    }

    [Fact]
    public void NormalState_RendersNoStateBadge()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 1_000).Add(c => c.Limit, 10_000));

        cut.FindAll(".tm-usage-meter__state").Should().BeEmpty();
        cut.Find("[data-testid='usage-meter']").ClassList.Should().NotContain("tm-usage-meter--warning");
    }

    [Fact]
    public void CustomThresholds_MoveTheState()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 6_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.WarningThreshold, 0.5)
            .Add(c => c.CriticalThreshold, 0.6));

        cut.Find("[data-testid='usage-meter']").GetAttribute("data-state").Should().Be("critical");
    }

    [Fact]
    public void OverLimit_IsShown_AndTheFillStaysCapped()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 12_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Unit, "credits"));

        var root = cut.Find("[data-testid='usage-meter']");
        root.GetAttribute("data-state").Should().Be("critical");
        root.GetAttribute("data-over-limit").Should().Be("true");
        cut.Find(".tm-usage-meter__fill").GetAttribute("style").Should().Contain("inline-size:100%");
        cut.Find(".tm-usage-meter__over").TextContent.Trim().Should().Be("Over limit by 2,000 credits");
        cut.Find("[role='meter']").GetAttribute("aria-valuenow").Should().Be("12000",
            "the reader hears the true amount, not the capped bar");
        cut.Find("[role='meter']").GetAttribute("aria-valuetext").Should().Be("12,000 of 10,000 credits, 120%");
    }

    [Fact]
    public void WithinTheLimit_RendersNoOverLimitLine()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 9_000).Add(c => c.Limit, 10_000));

        cut.FindAll(".tm-usage-meter__over").Should().BeEmpty();
        cut.Find("[data-testid='usage-meter']").HasAttribute("data-over-limit").Should().BeFalse();
    }

    // ---------- CTA slot ----------

    [Fact]
    public void NearLimitContent_RendersAtWarning()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.NearLimitContent, builder => builder.AddMarkupContent(0, "<button type='button'>Upgrade</button>")));

        cut.Find(".tm-usage-meter__cta").QuerySelector("button").Should().NotBeNull();
    }

    [Fact]
    public void NearLimitContent_SitsAfterTheReadoutAndBeforeTheData()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.DailyUsage, new double[] { 1, 2, 3 })
            .Add(c => c.Categories, Categories(("llm", "LLM", 8_500)))
            .Add(c => c.NearLimitContent, builder => builder.AddMarkupContent(0, "<button type='button'>Upgrade</button>")));

        var markup = cut.Markup;
        var cta = markup.IndexOf("tm-usage-meter__cta", StringComparison.Ordinal);
        var reset = markup.IndexOf("tm-usage-meter__reset", StringComparison.Ordinal);
        var daily = markup.IndexOf("tm-usage-meter__daily", StringComparison.Ordinal);
        var table = markup.IndexOf("tm-usage-meter__table", StringComparison.Ordinal);

        cta.Should().BeGreaterThan(reset, "the action follows the verdict it answers");
        cta.Should().BeLessThan(daily, "the action comes BEFORE the history and the breakdown a reader may not need");
        cta.Should().BeLessThan(table);
    }

    [Fact]
    public void NearLimitContent_RendersAtCriticalToo()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 11_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.NearLimitContent, builder => builder.AddMarkupContent(0, "<button type='button'>Upgrade</button>")));

        cut.Find(".tm-usage-meter__cta").QuerySelector("button").Should().NotBeNull();
    }

    [Fact]
    public void NearLimitContent_StaysOutOfTheMarkupWhileTheUsageIsNormal()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.NearLimitContent, builder => builder.AddMarkupContent(0, "<button type='button'>Upgrade</button>")));

        cut.FindAll(".tm-usage-meter__cta").Should().BeEmpty();
        cut.Markup.Should().NotContain("Upgrade");
    }

    // ---------- reset line ----------

    [Fact]
    public void ResetsAt_RendersTheLocalizedLine()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 100)
            .Add(c => c.ResetsAt, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        cut.Find(".tm-usage-meter__reset").TextContent.Trim().Should().Be("Resets on 7/1/2026");
    }

    [Fact]
    public void ResetsAt_FormatsWithTheSuppliedCulture()
    {
        Services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());

        var cut = RenderMeterIn(Czech, p => p
            .Add(c => c.Used, 100)
            .Add(c => c.ResetsAt, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        cut.Find(".tm-usage-meter__reset").TextContent.Trim().Should().Be("Obnoví se 01.07.2026");
    }

    [Fact]
    public void WithoutResetsAt_NoResetLineIsRendered()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 100));

        cut.FindAll(".tm-usage-meter__reset").Should().BeEmpty();
    }

    // ---------- daily usage ----------

    [Fact]
    public void DailyUsage_RendersASparklineWithNumbersInItsName()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 100)
            .Add(c => c.DailyUsage, new double[] { 3, 9, 4, 12 }));

        var daily = cut.Find(".tm-usage-meter__daily");
        var chart = daily.QuerySelector("svg");
        chart.Should().NotBeNull("the history is a graphic");
        chart!.GetAttribute("aria-label")
            .Should().Be("Daily usage: minimum 3, maximum 12, last value 12");
    }

    [Fact]
    public void WithoutDailyUsage_NoSparklineIsRendered()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 100).Add(c => c.DailyUsage, []));

        cut.FindAll(".tm-usage-meter__daily").Should().BeEmpty();
    }

    // ---------- categories ----------

    [Fact]
    public void Categories_RenderSegmentsInsideTheFill()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 10_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, Categories(("llm", "LLM", 6_000), ("tools", "Tools", 4_000))));

        var segments = cut.FindAll(".tm-usage-meter__fill .tm-usage-meter__segment");
        segments.Should().HaveCount(2);
        segments[0].GetAttribute("style").Should().Contain("flex-grow:0.6");
        segments[1].GetAttribute("style").Should().Contain("flex-grow:0.4");
        segments[0].GetAttribute("data-category-key").Should().Be("llm");
    }

    [Fact]
    public void Categories_RenderARealTable_WithCaptionHeadersAndRowHeaders()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 10_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, Categories(("llm", "LLM", 6_000), ("tools", "Tools", 4_000))));

        var table = cut.Find(".tm-usage-meter__table");
        table.QuerySelector("caption")!.TextContent.Trim().Should().Be("Usage by category");

        var headers = table.QuerySelectorAll("thead th").Select(th => th.TextContent.Trim()).ToList();
        headers.Should().Equal("Category", "Used", "Share");

        var rows = table.QuerySelectorAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].QuerySelector("th")!.GetAttribute("scope").Should().Be("row");
        rows[0].QuerySelector("th")!.TextContent.Trim().Should().Be("LLM");
        rows[0].QuerySelectorAll("td").Select(td => td.TextContent.Trim())
            .Should().Equal("6,000", "60%");
        rows[1].QuerySelectorAll("td").Select(td => td.TextContent.Trim())
            .Should().Equal("4,000", "40%");
    }

    [Fact]
    public void Categories_SharesAlwaysAddUpToAHundred()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 10_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, Categories(("a", "A", 3_333), ("b", "B", 3_333), ("c", "C", 3_334))));

        var shares = cut.FindAll(".tm-usage-meter__table tbody tr .tm-usage-meter__share")
            .Select(cell => cell.TextContent.Trim())
            .ToList();

        shares.Should().Equal(
            new[] { "33%", "33%", "34%" },
            "largest-remainder rounding keeps the shares summing to 100 %");
    }

    [Fact]
    public void Categories_LeaveAnExplicitOtherRowForUsageTheyDoNotExplain()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 10_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, Categories(("llm", "LLM", 7_500))));

        var rows = cut.Find(".tm-usage-meter__table").QuerySelectorAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[1].ClassList.Should().Contain("tm-usage-meter__row--other");
        rows[1].QuerySelector("th")!.TextContent.Trim().Should().Be("Other");
        rows[1].QuerySelectorAll("td").Select(td => td.TextContent.Trim()).Should().Equal("2,500", "25%");

        var segments = cut.FindAll(".tm-usage-meter__fill .tm-usage-meter__segment");
        segments.Should().HaveCount(2);
        segments[1].ClassList.Should().Contain("tm-usage-meter__segment--remainder");
        segments[1].GetAttribute("style").Should().Contain("flex-grow:0.25");
    }

    [Fact]
    public void Categories_OverrideNothingAboutTheMeterItself()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 7_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, Categories(("llm", "LLM", 3_000))));

        cut.Find("[role='meter']").GetAttribute("aria-valuenow").Should().Be("7500",
            "Used is the headline; categories only explain it");
    }

    [Fact]
    public void Categories_ANegativeAmountCountsAsZero_ButTheRowStays()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 10_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, Categories(("refund", "Refund", -2_000), ("llm", "LLM", 10_000))));

        var rows = cut.Find(".tm-usage-meter__table").QuerySelectorAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].QuerySelectorAll("td")[0].TextContent.Trim().Should().Be("0",
            "the row stays visible and counts as zero; the host's negative number is never printed");
        rows[0].QuerySelectorAll("td")[1].TextContent.Trim().Should().Be("0%");
    }

    [Fact]
    public void CategorySegments_CarryNoMouseOnlyTooltip()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 10_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, Categories(("llm", "LLM", 6_000), ("tools", "Tools", 4_000))));

        var strip = cut.Find(".tm-usage-meter__fill");
        strip.GetAttribute("aria-hidden").Should().Be("true",
            "the strip is decorative; the table below is the accessible presentation");

        foreach (var segment in cut.FindAll(".tm-usage-meter__segment"))
        {
            segment.HasAttribute("title").Should().BeFalse(
                "a title attribute is a mouse-only hint and the table already carries every label");
        }
    }

    [Fact]
    public void Categories_UseTheHostColourWhenItIsAValidCssColour()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, new List<UsageCategory>
            {
                new() { Key = "llm", Label = "LLM", Used = 1_000, Color = "var(--tm-color-primary)" }
            }));

        cut.Find(".tm-usage-meter__segment").GetAttribute("style")
            .Should().Contain("background-color: var(--tm-color-primary)");
        cut.Find(".tm-usage-meter__swatch").GetAttribute("style")
            .Should().Contain("background-color: var(--tm-color-primary)");
    }

    [Fact]
    public void Categories_FallBackToATokenToneWhenTheColourIsNotACssColour()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, new List<UsageCategory>
            {
                new() { Key = "llm", Label = "LLM", Used = 1_000, Color = "text-green-600" }
            }));

        cut.Find(".tm-usage-meter__segment").ClassList.Should().Contain("tm-usage-meter__segment--tone-1");
        cut.Find(".tm-usage-meter__segment").GetAttribute("style").Should().NotContain("background-color");
        cut.Find(".tm-usage-meter__swatch").ClassList.Should().Contain("tm-usage-meter__swatch--tone-1");
    }

    [Fact]
    public void Categories_SkipsEntriesWithoutAKey()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, new List<UsageCategory>
            {
                new() { Key = string.Empty, Label = "Nameless", Used = 500 },
                new() { Key = "llm", Label = "LLM", Used = 500 }
            }));

        var rows = cut.FindAll(".tm-usage-meter__table tbody tr");
        rows.Should().HaveCount(2, "the keyless category is not rendered as its own row");
        rows.Should().NotContain(row => row.TextContent.Contains("Nameless"),
            "a category that cannot be reported back is skipped, and its usage becomes the Other row");
        rows[1].QuerySelector("th")!.TextContent.Trim().Should().Be("Other");
        cut.FindAll(".tm-usage-meter__segment").Should().HaveCount(2);
    }

    [Fact]
    public void Categories_FallBackToTheKeyWhenTheLabelIsBlank()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.Categories, new List<UsageCategory>
            {
                new() { Key = "llm", Label = "  ", Used = 1_000 }
            }));

        cut.Find(".tm-usage-meter__table tbody th").TextContent.Trim().Should().Be("llm");
    }

    [Fact]
    public void NoCategories_MeansNoTableAndNoSegments()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 1_000).Add(c => c.Limit, 10_000));

        cut.FindAll(".tm-usage-meter__table").Should().BeEmpty();
        cut.FindAll(".tm-usage-meter__segment").Should().BeEmpty();
    }

    // ---------- compact ----------

    [Fact]
    public void Compact_DropsTheChromeButKeepsTheTitle()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Compact, true)
            .Add(c => c.Title, "Monthly credits")
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.ResetsAt, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero))
            .Add(c => c.DailyUsage, new double[] { 1, 2, 3 })
            .Add(c => c.Categories, Categories(("llm", "LLM", 8_500))));

        var root = cut.Find("[data-testid='usage-meter']");
        root.ClassList.Should().Contain("tm-usage-meter--compact");
        cut.FindAll(".tm-usage-meter__reset").Should().BeEmpty();
        cut.FindAll(".tm-usage-meter__daily").Should().BeEmpty();
        cut.FindAll(".tm-usage-meter__table").Should().BeEmpty();
        cut.FindAll(".tm-usage-meter__segment").Should().BeEmpty("a breakdown without its numbers would be colour-only");
    }

    [Fact]
    public void Compact_ShowsTheTitleAsAVisibleLabelAndNamesTheMeter()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Compact, true)
            .Add(c => c.Title, "Monthly credits")
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000));

        var title = cut.Find(".tm-usage-meter__title");
        title.TextContent.Trim().Should().Be("Monthly credits",
            "a sighted reader of a compact meter needs the label too, especially when several sit side by side");
        title.TagName.Should().Be("SPAN", "a compact meter is a label plus a bar, not a new heading in the outline");
        title.GetAttribute("id").Should().NotBeNullOrEmpty();

        var meter = cut.Find("[role='meter']");
        meter.GetAttribute("aria-labelledby").Should().Be(title.GetAttribute("id"),
            "the one visible label is also the accessible name");
        meter.HasAttribute("aria-label").Should().BeFalse("one name only");
    }

    [Fact]
    public void Compact_TitleWinsOverAriaLabel()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Compact, true)
            .Add(c => c.Title, "Monthly credits")
            .Add(c => c.AriaLabel, "Hidden name")
            .Add(c => c.Used, 1_000)
            .Add(c => c.Limit, 10_000));

        var meter = cut.Find("[role='meter']");
        meter.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty();
        meter.HasAttribute("aria-label").Should().BeFalse(
            "a meter never carries a visible label and a second, different accessible name");
        cut.Markup.Should().NotContain("Hidden name");
    }

    [Fact]
    public void Compact_KeepsTheMeterTheReadoutAndTheState()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Compact, true)
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.AriaLabel, "Monthly credits"));

        cut.Find("[role='meter']").GetAttribute("aria-label").Should().Be("Monthly credits");
        cut.Find(".tm-usage-meter__amount").TextContent.Trim().Should().Be("8,500 / 10,000");
        cut.Find(".tm-usage-meter__percent").TextContent.Trim().Should().Be("85%");
        cut.Find(".tm-usage-meter__state").TextContent.Trim().Should().Be("Near limit");
    }

    // ---------- announcements ----------

    [Fact]
    public void LiveRegion_StartsEmpty()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 8_500).Add(c => c.Limit, 10_000));

        var live = cut.Find("[data-testid='usage-meter-live']");
        live.GetAttribute("role").Should().Be("status");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.GetAttribute("aria-atomic").Should().Be("true");
        live.TextContent.Should().BeEmpty("a freshly rendered meter has nothing to announce");
    }

    [Fact]
    public void LiveRegion_AnnouncesTheTransitionIntoWarning()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 1_000).Add(c => c.Limit, 10_000));

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000));

        cut.Find("[data-testid='usage-meter-live']").TextContent.Trim().Should().Be("Usage is near the limit.");
    }

    [Fact]
    public void LiveRegion_AnnouncesTheTransitionIntoCritical()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 8_500).Add(c => c.Limit, 10_000));

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 10_500)
            .Add(c => c.Limit, 10_000));

        cut.Find("[data-testid='usage-meter-live']").TextContent.Trim()
            .Should().Be("Usage has reached the limit.");
    }

    [Fact]
    public void LiveRegion_AnnouncesTheReturnWithinTheLimit()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 9_500).Add(c => c.Limit, 10_000));

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 500)
            .Add(c => c.Limit, 10_000));

        cut.Find("[data-testid='usage-meter-live']").TextContent.Trim()
            .Should().Be("Usage is back within the limit.");
    }

    [Fact]
    public void LiveRegion_StaysSilentWhileTheStateDoesNotChange()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 8_500).Add(c => c.Limit, 10_000));

        cut.Find("[data-testid='usage-meter-live']").TextContent.Should().BeEmpty();

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 9_000)
            .Add(c => c.Limit, 10_000));

        cut.Find("[data-testid='usage-meter-live']").TextContent.Should().BeEmpty(
            "the state did not change, so nothing is announced twice");
    }

    [Fact]
    public void LiveRegion_AnnouncesARepeatedWarningAfterTheUsageReturnsWithinTheLimit()
    {
        var cut = RenderMeter(p => p.Add(c => c.Used, 8_500).Add(c => c.Limit, 10_000));

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 500)
            .Add(c => c.Limit, 10_000));
        cut.Find("[data-testid='usage-meter-live']").TextContent.Trim()
            .Should().Be("Usage is back within the limit.");

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000));
        cut.Find("[data-testid='usage-meter-live']").TextContent.Trim()
            .Should().Be("Usage is near the limit.", "the second visit to warning is a real transition, so it speaks again");

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 500)
            .Add(c => c.Limit, 10_000));
        cut.Find("[data-testid='usage-meter-live']").TextContent.Trim()
            .Should().Be("Usage is back within the limit.", "every transition replaces the previous sentence");
    }

    [Fact]
    public void LiveRegion_AlternatesAnInvisibleMarker_WhenTwoTransitionsShareTheSameSentence()
    {
        // Two localized sentences may legitimately coincide (or a host may override both). A live region
        // that receives the SAME text again does not announce anything in some screen readers, so the
        // component toggles one invisible character on a repeat — and only then.
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmUsageMeter_AnnounceNormal"] = "Quota state changed.",
            ["TmUsageMeter_AnnounceWarning"] = "Quota state changed.",
            ["TmUsageMeter_AnnounceCritical"] = "Quota state changed.",
        });

        var cut = RenderMeter(p => p.Add(c => c.Used, 8_500).Add(c => c.Limit, 10_000));

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 500)
            .Add(c => c.Limit, 10_000));
        var first = cut.Find("[data-testid='usage-meter-live']").TextContent;
        first.Trim().Should().Be("Quota state changed.");

        cut.Render(parameters => parameters
            .Add(c => c.Culture, English)
            .Add(c => c.Used, 10_500)
            .Add(c => c.Limit, 10_000));
        var second = cut.Find("[data-testid='usage-meter-live']").TextContent;

        second.Trim().Should().StartWith("Quota state changed.");
        second.Should().NotBe(first, "an identical mutation is silent in some screen readers, so it must differ");
        second.Trim().Length.Should().Be("Quota state changed.".Length + 1);
    }


    [Fact]
    public void Exactly_OneLiveRegionExists()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.DailyUsage, new double[] { 1, 2, 3 })
            .Add(c => c.Categories, Categories(("llm", "LLM", 8_500))));

        cut.FindAll("[role='status']").Should().HaveCount(1);
        cut.FindAll("[aria-live]").Should().HaveCount(1);
    }

    // ---------- localisation ----------

    [Fact]
    public void StateAndResetTexts_ComeFromTheLocalizer()
    {
        Services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());

        var cut = Render<TmUsageMeter>(parameters => parameters
            .Add(c => c.Culture, Czech)
            .Add(c => c.Used, 8_500)
            .Add(c => c.Limit, 10_000)
            .Add(c => c.ResetsAt, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero))
            .Add(c => c.Categories, Categories(("llm", "LLM", 8_500))));

        cut.Find(".tm-usage-meter__state").TextContent.Trim().Should().Be("Blízko limitu");
        cut.Find(".tm-usage-meter__reset").TextContent.Trim().Should().Be("Obnoví se 01.07.2026");
        cut.Find("[role='meter']").GetAttribute("aria-label").Should().Be("Využití");
        cut.Find(".tm-usage-meter__table caption").TextContent.Trim().Should().Be("Využití podle kategorií");
    }

    // ---------- plumbing ----------

    [Fact]
    public void Class_IsAppendedToTheRoot()
    {
        var cut = RenderMeter(p => p.Add(c => c.Class, "my-meter").Add(c => c.Used, 10));

        cut.Find(".tm-usage-meter").ClassList.Should().Contain("my-meter");
    }

    [Fact]
    public void DataTestId_OverridesTheRootId()
    {
        var cut = RenderMeter(p => p.Add(c => c.DataTestId, "quota").Add(c => c.Used, 10));

        cut.Find("[data-testid='quota']").Should().NotBeNull();
        cut.FindAll("[data-testid='usage-meter']").Should().BeEmpty();
    }

    [Fact]
    public void TestIdPrefix_NamespacesTheInternalIds()
    {
        var cut = RenderMeter(p => p
            .Add(c => c.TestIdPrefix, "seat")
            .Add(c => c.Used, 10));

        cut.Find("[data-testid='seat-usage-meter']").Should().NotBeNull();
        cut.Find("[data-testid='seat-usage-meter-live']").Should().NotBeNull();
    }
}
