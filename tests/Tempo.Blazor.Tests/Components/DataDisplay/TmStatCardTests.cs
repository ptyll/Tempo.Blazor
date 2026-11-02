using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>TDD tests for TmStatCard.</summary>
public class TmStatCardTests : LocalizationTestBase
{
    [Fact]
    public void TmStatCard_Has_Base_CssClass()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        cut.Find(".tm-stat-card").Should().NotBeNull();
    }

    [Fact]
    public void TmStatCard_Renders_Value()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        cut.Find(".tm-stat-value").TextContent.Should().Contain("1,234");
    }

    [Fact]
    public void TmStatCard_Renders_Title()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Active users")
            .Add(c => c.Value, "42"));

        cut.Find(".tm-stat-label").TextContent.Should().Contain("Active users");
    }

    [Fact]
    public void TmStatCard_Renders_SubValue_When_Set()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12% this month"));

        cut.Find(".tm-stat-subvalue").TextContent.Should().Contain("+12% this month");
    }

    [Fact]
    public void TmStatCard_No_SubValue_When_Null()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000"));

        cut.FindAll(".tm-stat-subvalue").Should().BeEmpty();
    }

    /// <summary>
    /// <c>SubValueColor</c> without <c>SubValue</c> is an affordance without a mechanism: the call site
    /// states an intent that rendering silently denies, because the whole span hangs off
    /// <c>SubValue</c>. The card refuses it instead of ignoring it.
    /// </summary>
    /// <remarks>
    /// The invariant lives in the COMPONENT, not in a markup scanner, and that placement is the point: it
    /// covers splatted <c>@attributes</c>, <c>DynamicComponent</c> and consumers outside this repository,
    /// and it fails at the moment of misuse. A scanner is a legitimate second line for the release gate,
    /// but it cannot see those paths and must carry its own denominator.
    /// </remarks>
    [Fact]
    public void TmStatCard_Rejects_SubValueColor_Without_SubValue()
    {
        var act = () => Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValueColor, "tm-text-success"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SubValueColor*")
            .WithMessage("*SubValue*")
            .WithMessage("*Revenue*", "the message has to say WHICH card, or it is a riddle in a log");
    }

    /// <summary>The invariant is about the PAIR, so neither half alone may be made illegal.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("+12%", null)]
    [InlineData("+12%", "tm-text-success")]
    public void TmStatCard_Accepts_EverySoundCombination(string? subValue, string? subValueColor)
    {
        var act = () => Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, subValue)
            .Add(c => c.SubValueColor, subValueColor));

        act.Should().NotThrow();
    }

    /// <summary>
    /// The rejection must survive a LATER parameter change too — a card that starts sound and is then
    /// re-rendered with the value removed is the same broken state, reached one render later.
    /// </summary>
    [Fact]
    public void TmStatCard_Rejects_SubValue_Removed_On_Rerender()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "tm-text-success"));

        var act = () => cut.Render(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, (string?)null)
            .Add(c => c.SubValueColor, "tm-text-success"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*SubValueColor*");
    }

    /// <summary>
    /// ed256446: <c>SubValueColor</c> must WIN over the base <c>.tm-stat-subvalue</c> colour, resolved
    /// the way a browser resolves it — through the shipped bundle, not by hoping a consumer rule lands
    /// later in source order. The base declaration names the custom property with the secondary token
    /// as fallback, and the component wires the parameter into that property on the element itself.
    /// </summary>
    /// <remarks>
    /// Mutation: reverting the declaration to <c>color: var(--tm-text-secondary)</c> makes the winner
    /// the plain token and this test goes red, because the painted colour then equals the base.
    /// </remarks>
    [Fact]
    public void TmStatCard_SubValueColor_Wins_Over_The_Base_Rule()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "var(--tm-color-success-text)"));

        var span = cut.Find(".tm-stat-subvalue");
        (span.GetAttribute("style") ?? string.Empty).Should().Contain(
            "--tm-stat-subvalue-color: var(--tm-color-success-text)",
            "the parameter has to reach the element's own custom property — a class cannot promise "
            + "a colour wins, because specificity and source order decide that elsewhere");

        var winner = CssCascade.Winning(
            ThemeCss.BundledCss(),
            [new CssCascade.Element("div", "tm-stat-card"), new CssCascade.Element("span", "tm-stat-subvalue")],
            "color");

        var tokens = ThemeCss.TokenGraph(dark: false);
        tokens["--tm-stat-subvalue-color"] = "var(--tm-color-success-text)";

        var painted = ThemeCss.ResolveColour(winner, tokens);
        painted.Should().Be(
            ThemeCss.ResolveColour("var(--tm-color-success-text)", tokens),
            "SubValueColor vítězí nad základem — element nese vlastní hodnotu custom property, "
            + "takže fallback na --tm-text-secondary se neuplatní");
        painted.Should().NotBe(
            ThemeCss.ResolveColour("var(--tm-text-secondary)", tokens),
            "jinak parametr maluje stejnou barvou jako základ — přesně vada ed256446");
    }

    /// <summary>Without <c>SubValueColor</c> the span carries no stray <c>style</c> attribute.</summary>
    [Fact]
    public void TmStatCard_No_Style_Attribute_Without_SubValueColor()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%"));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().BeNull();
    }

    /// <summary>
    /// 2.9.0 breaking change made honest: <c>SubValueColor</c> is interpolated verbatim into an
    /// inline <c>style</c>, so the component must VALIDATE it as a CSS colour before emitting.
    /// Every grammar the contract names reaches the attribute — token reference, every hex length,
    /// the rgb/hsl/oklch/color-mix functions, and the named-colour keywords.
    /// </summary>
    [Theory]
    [InlineData("var(--tm-color-success-text)")]
    [InlineData("var(--x, green)")]
    [InlineData("#fff")]
    [InlineData("#ffff")]
    [InlineData("#16a34a")]
    [InlineData("#16a34a80")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("rgb(1 2 3 / .5)")]
    [InlineData("rgba(1,2,3,.5)")]
    [InlineData("hsl(120,50%,50%)")]
    [InlineData("hsla(120,50%,50%,.5)")]
    [InlineData("oklch(0.7 0.15 150)")]
    [InlineData("oklch(from var(--x) l c h)")]
    [InlineData("color-mix(in srgb, red 50%, blue)")]
    [InlineData("green")]
    [InlineData("rebeccapurple")]
    [InlineData("currentcolor")]
    [InlineData("transparent")]
    public void TmStatCard_SubValueColor_ValidCssColor_ReachesTheStyleAttribute(string color)
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, color));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().Be(
            $"--tm-stat-subvalue-color: {color}",
            "platná CSS barva se propíše do custom property beze změny");
    }

    /// <summary>
    /// The other half of the contract: a value that is not a CSS colour emits NO style and logs a
    /// warning — the old class-name usage (<c>text-green-600</c>) is exactly this case, and an
    /// injected payload (<c>; background:url(…)</c>) must never reach the attribute. No throw:
    /// the caller gets a warning and an uncoloured sub-value, not a crashed render.
    /// </summary>
    [Theory]
    [InlineData("text-green-600")]
    [InlineData("tm-text-success")]
    [InlineData("not-a-color")]
    [InlineData("red; background:url(x)")]
    [InlineData("red; --tm-stat-subvalue-color: blue")]
    [InlineData("url(//evil.example/x)")]
    [InlineData("rgb(1,2,3};x{y")]
    [InlineData("rgb(1,2,3")]
    [InlineData("var(")]
    public void TmStatCard_SubValueColor_InvalidValue_EmitsNoStyleAndLogsWarning(string color)
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);

        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, color));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().BeNull(
            "neplatná hodnota nesmí dosáhnout atributu — jinak je injektovaná deklarace zpět ve hře");
        factory.Entries.Should().Contain(
            entry => entry.Level == LogLevel.Warning
                     && entry.Message.Contains("SubValueColor expects a CSS color")
                     && entry.Message.Contains("CSS class names are no longer accepted"),
            "neplatná hodnota musí zalogovat warning s textem breaking-change kontraktu, " +
            $"zaznamenáno: {string.Join(" | ", factory.Entries.Select(e => e.Message))}");
    }

    /// <summary>
    /// The warning must fire again when a VALID colour is swapped for an invalid one on re-render —
    /// the check lives in <c>OnParametersSet</c>, not in a first-render branch.
    /// </summary>
    [Fact]
    public void TmStatCard_SubValueColor_TurnedInvalidOnRerender_DropsTheStyle()
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "var(--tm-color-success-text)"));

        cut.Render(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12%")
            .Add(c => c.SubValueColor, "text-green-600"));

        cut.Find(".tm-stat-subvalue").GetAttribute("style").Should().BeNull();
        factory.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    // ── 2.10.0 additive surface (plan ff043b2e, phase 1) ──────────────────────────────────────────
    // The card grew a delta trend, a comparison label, an icon, a sparkline, a loading skeleton, an
    // animated numeric value and a whole-card target. Every one of them is OPT-IN: the regression
    // test above this block pins the byte-identical markup of the pre-2.10.0 parameter set.

    private static readonly System.Globalization.CultureInfo Czech =
        System.Globalization.CultureInfo.GetCultureInfo("cs-CZ");

    private static readonly System.Globalization.CultureInfo English =
        System.Globalization.CultureInfo.GetCultureInfo("en-US");

    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    private BunitJSModuleInterop SetupAnimatedNumberModule()
    {
        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(false);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>
    /// The additivity contract: a card that passes none of the new parameters renders EXACTLY the
    /// 2.9.x markup — same root element, same three spans, same whitespace, no extra attribute.
    /// A regression in this string is a breaking change to every host that styles `.tm-stat-card`
    /// or asserts on its DOM.
    /// </summary>
    [Fact]
    public void WithoutNewParameters_RendersTheExactPreTwoTenMarkup()
    {
        var plain = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        plain.Markup.Should().Be(
            "<div class=\"tm-stat-card\"><span class=\"tm-stat-value\">1,234</span>\n"
            + "    <span class=\"tm-stat-label\">Users</span></div>",
            "the default card must stay byte-identical");

        var withSub = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12% this month")
            .Add(c => c.SubValueColor, "var(--tm-color-success-text)"));

        withSub.Markup.Should().Be(
            "<div class=\"tm-stat-card\"><span class=\"tm-stat-value\">$5,000</span>\n"
            + "    <span class=\"tm-stat-label\">Revenue</span><span class=\"tm-stat-subvalue\" "
            + "style=\"--tm-stat-subvalue-color: var(--tm-color-success-text)\">+12% this month</span></div>");
    }

    /// <summary>No new part leaks into the default card — no icon, trend, sparkline or target element.</summary>
    [Fact]
    public void WithoutNewParameters_RendersNoNewPart()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        cut.FindAll(".tm-stat-card__trend").Should().BeEmpty();
        cut.FindAll(".tm-stat-card__comparison").Should().BeEmpty();
        cut.FindAll(".tm-stat-card__icon").Should().BeEmpty();
        cut.FindAll(".tm-stat-card__sparkline").Should().BeEmpty();
        cut.Find(".tm-stat-card").GetAttribute("data-testid").Should().BeNull();
        cut.Find(".tm-stat-card").ClassList.Should().Equal(["tm-stat-card"]);
    }

    [Fact]
    public void Delta_AutoTrend_RendersArrowTextAndTheGoodClass()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Delta, 12.5)
            .Add(c => c.Culture, Czech));

        var trend = cut.Find(".tm-stat-card__trend");
        trend.ClassList.Should().Contain("tm-stat-card__trend--positive",
            "PositiveIsGood defaults to true, so a rise is the good outcome");
        trend.TextContent.Should().Contain("+12,5\u00A0%");
        trend.QuerySelector("svg").Should().NotBeNull("the arrow icon is the second, non-colour cue");
        trend.QuerySelector("svg")!.GetAttribute("aria-hidden").Should().Be("true");
    }

    /// <summary>Colour is never the only carrier: the trend word travels in a visually hidden span.</summary>
    [Theory]
    [InlineData(12.5, "Increase")]
    [InlineData(-12.5, "Decrease")]
    [InlineData(0.0, "No change")]
    public void Delta_CarriesAVisuallyHiddenTrendWord(double delta, string expected)
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Delta, delta)
            .Add(c => c.Culture, English));

        var hidden = cut.FindAll(".tm-stat-card__trend .tm-sr-only")
            .Should().ContainSingle("one hidden word, so the direction is never colour-only").Subject;
        hidden.TextContent.Should().Be(expected);
    }

    [Fact]
    public void Delta_TextIsFormattedByTheCulture_AndDeltaFormatOverridesIt()
    {
        var czech = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Delta, 12.5)
            .Add(c => c.Culture, Czech));
        czech.Find(".tm-stat-card__delta").TextContent.Should().Be("+12,5\u00A0%");

        var custom = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Delta, 1.5)
            .Add(c => c.DeltaFormat, "+0.00")
            .Add(c => c.Culture, English));
        custom.Find(".tm-stat-card__delta").TextContent.Should().Be("+1.50");
    }

    /// <summary>
    /// PositiveIsGood inverts the COLOUR class, never the direction: a metric where less is better
    /// (churn, latency) still reads "Decrease" but is painted as the good outcome.
    /// </summary>
    [Fact]
    public void PositiveIsGood_False_InvertsTheOutcomeClass_ButNotTheDirection()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Churn")
            .Add(c => c.Value, "2,1 %")
            .Add(c => c.Delta, -3.0)
            .Add(c => c.PositiveIsGood, false)
            .Add(c => c.Culture, Czech));

        var trend = cut.Find(".tm-stat-card__trend");
        trend.ClassList.Should().Contain("tm-stat-card__trend--positive",
            "a falling churn is good news when positive is not good");
        trend.QuerySelector(".tm-sr-only")!.TextContent.Should().Be("Decrease");
    }

    [Fact]
    public void TrendDirection_OverridesTheSignOfDelta()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Delta, 5.0)
            .Add(c => c.TrendDirection, TrendDirection.Flat)
            .Add(c => c.Culture, English));

        var trend = cut.Find(".tm-stat-card__trend");
        trend.GetAttribute("data-trend").Should().Be("flat");
        trend.ClassList.Should().Contain("tm-stat-card__trend--neutral");
        trend.QuerySelector(".tm-sr-only")!.TextContent.Should().Be("No change");
    }

    /// <summary>A direction without a number is legitimate ("we can only see the arrow").</summary>
    [Fact]
    public void TrendDirection_WithoutDelta_RendersTheMarkerWithoutANumber()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.TrendDirection, TrendDirection.Down));

        cut.Find(".tm-stat-card__trend").ClassList.Should().Contain("tm-stat-card__trend--negative");
        cut.FindAll(".tm-stat-card__delta").Should().BeEmpty();
    }

    [Fact]
    public void ComparisonLabel_RendersNextToTheTrend()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Delta, 12.5)
            .Add(c => c.ComparisonLabel, "vs. last month")
            .Add(c => c.Culture, English));

        cut.Find(".tm-stat-card__comparison").TextContent.Should().Be("vs. last month");
    }

    [Fact]
    public void Icon_RendersADecorativeSvg()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Icon, "trending-up"));

        var icon = cut.Find(".tm-stat-card__icon svg");
        icon.GetAttribute("aria-hidden").Should().Be("true",
            "the metric label already names the card; a decorative icon must not be read twice");
    }

    /// <summary>
    /// The icon is absolutely positioned in the top-end corner, so it covers whatever text runs under
    /// it unless the card RESERVES that space. The modifier is what the stylesheet keys the reservation
    /// off; it appears only when the icon actually paints (a loading card renders no icon, so it also
    /// needs no gutter). The default card keeps its single class, so the byte-identical promise holds.
    /// </summary>
    [Fact]
    public void Icon_AddsTheHasIconModifier_ThatReservesTheCornerSpace()
    {
        var withIcon = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Icon, "trending-up"));

        withIcon.Find(".tm-stat-card").ClassList.Should().Contain("tm-stat-card--has-icon",
            "the corner gutter has to exist exactly when the icon does, or a long label runs under it");

        var withoutIcon = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000"));

        withoutIcon.Find(".tm-stat-card").ClassList.Should().NotContain("tm-stat-card--has-icon");
        withoutIcon.Find(".tm-stat-card").ClassList.Should().Equal(["tm-stat-card"]);

        var loading = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Icon, "trending-up")
            .Add(c => c.IsLoading, true));

        loading.FindAll(".tm-stat-card__icon").Should().BeEmpty("a loading card paints no icon");
        loading.Find(".tm-stat-card").ClassList.Should().NotContain("tm-stat-card--has-icon",
            "reserving space for an icon that is not painted would just shorten the skeletons");
    }

    [Fact]
    public void Sparkline_RendersTheMiniChart()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Sparkline, new double[] { 1, 4, 2, 8 }));

        cut.FindAll(".tm-stat-card__sparkline .tm-sparkline").Should().ContainSingle();
        cut.Find(".tm-stat-card__sparkline").GetAttribute("aria-hidden").Should().BeNull(
            "a static card keeps the chart's role=img name — it is the only description of the trend");
        ReachableImages(cut).Should().ContainSingle("a static card exposes the chart to a reader");
    }

    /// <summary>
    /// A card whose whole surface is a link or a button builds its ACCESSIBLE NAME from its content, so
    /// a nested <c>role="img"</c> chart appends its label ("… Sparkline") to every focus stop and turns
    /// one metric into a sentence with a stray word in it. The chart is decoration next to the value, so
    /// an interactive card hides that subtree — and only an interactive card does.
    /// </summary>
    /// <param name="asButton">Whether the card is a button (OnClick) rather than a link (Href).</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InteractiveCard_HidesTheSparklineFromTheCardsOwnAccessibleName(bool asButton)
    {
        var cut = asButton
            ? Render<TmStatCard>(p => p
                .Add(c => c.Title, "Revenue")
                .Add(c => c.Value, "$5,000")
                .Add(c => c.Sparkline, new double[] { 1, 4, 2, 8 })
                .Add(c => c.OnClick, () => { }))
            : Render<TmStatCard>(p => p
                .Add(c => c.Title, "Revenue")
                .Add(c => c.Value, "$5,000")
                .Add(c => c.Sparkline, new double[] { 1, 4, 2, 8 })
                .Add(c => c.Href, "/reports/revenue"));

        cut.Find(".tm-stat-card__sparkline").GetAttribute("aria-hidden").Should().Be("true",
            "the chart's label must not be spliced into the whole-card target's name");
        ReachableImages(cut).Should().BeEmpty(
            "no role=img may sit outside an aria-hidden subtree inside the target");
    }

    /// <summary>Every <c>role="img"</c> element that is NOT inside an <c>aria-hidden="true"</c> subtree.</summary>
    private static List<AngleSharp.Dom.IElement> ReachableImages(IRenderedComponent<TmStatCard> cut)
        => cut.FindAll("[role='img']").Where(element => !IsAriaHidden(element)).ToList();

    private static bool IsAriaHidden(AngleSharp.Dom.IElement element)
    {
        for (var node = element; node is not null; node = node.ParentElement)
        {
            if (node.GetAttribute("aria-hidden") == "true")
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void IsLoading_RendersSkeletonsAndABusyFlag()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.IsLoading, true));

        cut.Find(".tm-stat-card").GetAttribute("aria-busy").Should().Be("true");
        cut.FindAll(".tm-stat-card .tm-skeleton").Should().HaveCountGreaterThanOrEqualTo(3);
        cut.FindAll(".tm-stat-value").Should().BeEmpty("the stale value must not stay on screen while loading");
        cut.Find(".tm-stat-card").TextContent.Should().Contain("Loading");
    }

    [Fact]
    public void Href_RendersTheWholeCardAsALink()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Href, "/reports/revenue"));

        var root = cut.Find("a.tm-stat-card");
        root.GetAttribute("href").Should().Be("/reports/revenue");
        root.ClassList.Should().Contain("tm-stat-card--interactive");
        cut.FindAll("button.tm-stat-card").Should().BeEmpty();
    }

    /// <summary>
    /// A refused href means NO target at all: rendering <c>&lt;a&gt;</c> without an href would lie
    /// about being a link (not focusable, no navigation), and rewriting the value would hide the
    /// host's mistake.
    /// </summary>
    /// <remarks>
    /// The scheme cannot be read with <c>Uri</c>: the WHATWG URL parser strips ASCII tab, LF and CR
    /// from a URL BEFORE it looks at the scheme, so <c>"java\tscript:alert(1)"</c> and
    /// <c>"javascript:alert(1)"</c> are the same navigation while <c>Uri.TryCreate</c> calls the first
    /// one relative (probe: <c>IsAbsoluteUri == false</c>). A value carrying ANY control character is
    /// therefore refused outright — stripping it is not an option, because the stripped text can still
    /// spell a script URL — and the scheme is matched by hand against the three accepted names.
    /// </remarks>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("//evil.example/x")]
    [InlineData("\\\\evil.example/x")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("java\rscript:alert(1)")]
    [InlineData("\u0001javascript:alert(1)")]
    [InlineData("https://example.com/\u0000report")]
    public void UnsafeHref_RendersNoTarget(string href)
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Href, href));

        cut.FindAll("a").Should().BeEmpty();
        cut.Find("div.tm-stat-card").Should().NotBeNull();
    }

    [Theory]
    [InlineData("/reports/revenue")]
    [InlineData("reports/revenue")]
    [InlineData("https://example.com/report")]
    [InlineData("HTTPS://example.com/report")]
    [InlineData("mailto:finance@example.com")]
    // A colon AFTER a path or query separator is not a scheme: these are relative URLs and must keep
    // rendering (the scheme rule is "a colon before the first /, ? or #", not "any colon anywhere").
    [InlineData("/reports/page:1")]
    [InlineData("reports/a?next=b:c")]
    [InlineData("reports/a#b:c")]
    public void SafeHref_RendersTheLink(string href)
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Href, href));

        cut.Find("a.tm-stat-card").GetAttribute("href").Should().Be(href);
    }

    [Fact]
    public void OnClick_RendersTheWholeCardAsAButton()
    {
        var clicks = 0;
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.OnClick, () => clicks++));

        var root = cut.Find("button.tm-stat-card");
        root.GetAttribute("type").Should().Be("button");
        root.ClassList.Should().Contain("tm-stat-card--interactive");
        root.Click();
        clicks.Should().Be(1);
        cut.FindAll("a.tm-stat-card").Should().BeEmpty();
    }

    [Fact]
    public void OnClick_WinsOverHref()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.Href, "/reports/revenue")
            .Add(c => c.OnClick, () => { }));

        cut.FindAll("button.tm-stat-card").Should().ContainSingle();
        cut.FindAll("a.tm-stat-card").Should().BeEmpty("one target per card — the callback wins");
    }

    [Fact]
    public void DataTestId_ReachesTheRoot_OnlyWhenAsked()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.DataTestId, "kpi-revenue"));

        cut.Find(".tm-stat-card").GetAttribute("data-testid").Should().Be("kpi-revenue");
    }

    /// <summary>
    /// NumericValue + AnimateValue compose TmAnimatedNumber into the value slot, so the metric
    /// counts up to its target while a reader is handed the final number once.
    /// </summary>
    [Fact]
    public void NumericValue_WithAnimation_RendersTheAnimatedNumber()
    {
        var module = SetupAnimatedNumberModule();
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.NumericValue, 1234.0)
            .Add(c => c.AnimateValue, true)
            .Add(c => c.ValueFormat, "N0")
            .Add(c => c.Culture, English));

        cut.FindAll(".tm-stat-value .tm-animated-number").Should().ContainSingle();
        cut.Find(".tm-animated-number__value").TextContent.Should().Be("1,234");
        cut.WaitForAssertion(() => module.Invocations.Where(i => i.Identifier == "animate").Should().NotBeEmpty());
    }

    [Fact]
    public void NumericValue_WithoutAnimation_IsPureBlazor()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.NumericValue, 1234.0)
            .Add(c => c.Culture, English));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be("1,234");
        JSInterop.Invocations.Should().BeEmpty("AnimateValue defaults to false, so no module is imported");
    }

    [Fact]
    public void NumericValue_WinsOverTheStringValue()
    {
        var cut = Render<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "stale")
            .Add(c => c.NumericValue, 42.0)
            .Add(c => c.Culture, English));

        cut.Find(".tm-stat-value").TextContent.Should().Be("42");
        cut.Find(".tm-stat-value").TextContent.Should().NotContain("stale");
    }

    /// <summary>A logger the test owns: records (level, formatted message) so the assertion reads intent, not NSubstitute call-shape.</summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
