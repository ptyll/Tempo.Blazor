using System.Globalization;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Fáze 1 of the funnel-label plan (8fa7319c): the funnel's stage labels.
/// <para>
/// The pre-2.11.0 funnel decided the label's position from the stage width alone and painted it from
/// two hardcoded hex inks (<c>#ffffff</c> inside, <c>#6b7280</c> beside) that measured as low as
/// 2.0:1 on the palette — and a stage with the value 0 collapsed to a zero-width path with no
/// visible row. These tests pin the three opt-in placement choices (Auto = the old behavior), the
/// one-<c>&lt;text&gt;</c>-two-<c>&lt;tspan&gt;</c> label, the token-driven ink, the zero/NaN stage
/// floor, the gutter arithmetic and the screen-reader data table in the reader's culture.
/// </para>
/// </summary>
public class TmChartFunnelLabelTests : LocalizationTestBase
{
    /// <summary>The component's built-in funnel palette — the series tokens the ink guard has to cover.</summary>
    private static readonly string[] Palette =
    [
        "var(--tm-chart-series-1)", "var(--tm-chart-series-2)", "var(--tm-chart-series-3)",
        "var(--tm-chart-series-4)", "var(--tm-chart-series-5)", "var(--tm-chart-series-6)",
        "var(--tm-chart-series-7)", "var(--tm-chart-series-8)",
    ];

    private static ChartData FunnelData => new()
    {
        Labels = ["Poptávky", "Kvalifikace", "Nabídka", "Smlouva"],
        Datasets =
        [
            new ChartDataset
            {
                Label = "Konverze",
                Values = [1000, 450, 200, 90],
                Color = "#3b82f6",
                BackgroundColors = ["#3b82f6", "#8b5cf6", "#f59e0b", "#10b981"]
            }
        ]
    };

    private IRenderedComponent<TmChart> Render(
        ChartData? data = null,
        Action<Bunit.ComponentParameterCollectionBuilder<TmChart>>? configure = null,
        bool showValues = true)
        => Render<TmChart>(p =>
        {
            p.Add(x => x.Type, ChartType.Funnel);
            p.Add(x => x.Data, data ?? FunnelData);
            p.Add(x => x.ShowValues, showValues);
            configure?.Invoke(p);
        });

    private static double Number(AngleSharp.Dom.IElement element, string attribute)
        => double.Parse(element.GetAttribute(attribute)!, CultureInfo.InvariantCulture);

    private static IReadOnlyList<AngleSharp.Dom.IElement> StageLabels(IRenderedComponent<TmChart> cut)
        => cut.FindAll(".tm-chart__funnel-label");

    private static AngleSharp.Dom.IElement[] StageValues(IRenderedComponent<TmChart> cut)
        => [.. cut.FindAll(".tm-chart__funnel-value")];

    /// <summary>The <c>&lt;text&gt;</c> a stage's <c>&lt;tspan&gt;</c>s belong to.</summary>
    private static AngleSharp.Dom.IElement Line(IRenderedComponent<TmChart> cut, int stage)
        => cut.FindAll(".tm-chart__funnel-text")[stage];

    /// <summary>The anchor of a label line — the property belongs to the <c>&lt;text&gt;</c>.</summary>
    private static string Anchor(AngleSharp.Dom.IElement tspan)
        => tspan.ParentElement!.GetAttribute("text-anchor")!;

    // ── Placement ────────────────────────────────────────────────────────────

    [Fact]
    public void Funnel_AutoPlacement_KeepsThePreExistingGeometry()
    {
        // Auto is the DEFAULT and must stay byte-compatible with the pre-2.11.0 output: the widest
        // stages keep their centered inside label, the narrow ones keep their beside-the-funnel one
        // at exactly the old x (center + half the wider side + 10).
        var cut = Render();

        var labels = StageLabels(cut);
        labels.Should().HaveCount(4);

        Anchor(labels[0]).Should().Be("middle");
        Number(labels[0], "x").Should().Be(315);   // PL + CW / 2
        labels[0].GetAttribute("class").Should().Contain("tm-chart__funnel-label--inside");

        Anchor(labels[1]).Should().Be("middle");
        Number(labels[1], "x").Should().Be(315);

        Anchor(labels[2]).Should().Be("start");
        Number(labels[2], "x").Should().Be(378);   // 315 + max(106, 47.7) / 2 + 10
        labels[2].GetAttribute("class").Should().Contain("tm-chart__funnel-label--outside");

        Anchor(labels[3]).Should().Be("start");
        Number(labels[3], "x").Should().Be(348.85); // 315 + 47.7 / 2 + 10

        cut.FindAll(".tm-chart__funnel-segment").Select(s => Number(s, "data-width"))
            .Should().Equal(530, 238.5, 106, 47.7);
    }

    [Fact]
    public void Funnel_InsidePlacement_AlwaysDrawsTheLabelInsideTheStage()
    {
        var cut = Render(configure: p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Inside));

        var labels = StageLabels(cut);
        labels.Should().HaveCount(4);
        labels.Should().OnlyContain(l =>
            Anchor(l) == "middle"
            && Number(l, "x") == 315
            && l.GetAttribute("class")!.Contains("tm-chart__funnel-label--inside"));
        labels.Should().NotContain(l => l.GetAttribute("class")!.Contains("tm-chart__funnel-label--outside"));

        // The value line follows the label's placement — one stage, one position.
        StageValues(cut).Should().OnlyContain(v => Anchor(v) == "middle" && Number(v, "x") == 315);
    }

    [Fact]
    public void Funnel_OutsidePlacement_AlwaysDrawsTheLabelBesideTheStage()
    {
        var cut = Render(configure: p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Outside));

        var labels = StageLabels(cut);
        labels.Should().HaveCount(4);
        labels.Should().OnlyContain(l =>
            Anchor(l) == "start"
            && l.GetAttribute("class")!.Contains("tm-chart__funnel-label--outside"));
        labels.Should().NotContain(l => l.GetAttribute("class")!.Contains("tm-chart__funnel-label--inside"));

        // Outside is a COLUMN: every stage label shares one x, right of the widest band.
        labels.Select(l => Number(l, "x")).Distinct().Should().ContainSingle()
            .Which.Should().BeGreaterThan(315);
        StageValues(cut).Should().OnlyContain(v => Anchor(v) == "start" && Number(v, "x") > 315);
    }

    [Fact]
    public void Funnel_OutsidePlacement_ReservesAGutterForTheLabels()
    {
        // Outside labels live to the RIGHT of the funnel, so the funnel itself narrows — otherwise a
        // long stage name runs past the viewBox edge and is clipped instead of readable.
        var auto = Number(Render().FindAll(".tm-chart__funnel-segment")[0], "data-width");
        var outside = Number(
            Render(configure: p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Outside))
                .FindAll(".tm-chart__funnel-segment")[0],
            "data-width");

        outside.Should().BeLessThan(auto,
            "the outside placement owns the right-hand gutter out of the same viewBox width");
        outside.Should().BeGreaterThan(auto / 2, "the funnel stays the chart's dominant shape");
    }

    [Fact]
    public void Funnel_OutsidePlacement_KeepsEveryLineInsideTheViewBox()
    {
        // The gutter is sized from the LONGEST label (and value line) at the worst case of the
        // responsive type scale, so no line may cross the 600-unit edge of the viewBox: a clipped stage
        // name is not "beside the stage", it is text nobody can read.
        var cut = Render(configure: p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Outside));

        const double svgW = 600;
        foreach (var label in StageLabels(cut))
        {
            var x = Number(label, "x");
            (x + TmChart.FunnelTextEstimate(label.TextContent, TmChart.LabelWorstFontUnits))
                .Should().BeLessThanOrEqualTo(svgW, "the stage name '{0}' must fit the gutter", label.TextContent);
        }

        foreach (var value in StageValues(cut))
        {
            var x = Number(value, "x");
            (x + TmChart.FunnelTextEstimate(value.TextContent, TmChart.ValueWorstFontUnits))
                .Should().BeLessThanOrEqualTo(svgW, "the value line '{0}' must fit the gutter", value.TextContent);
        }
    }

    [Fact]
    public void Funnel_OutsidePlacement_TruncatesALineTheGutterCannotHold_AndKeepsTheFullTextInATitle()
    {
        var name = "Smlouva zástupce obchodního oddělení";
        var data = new ChartData
        {
            Labels = ["Poptávky", "Kvalifikace", "Nabídka", name],
            Datasets =
            [
                new ChartDataset
                {
                    Label = "Konverze",
                    Values = [1000, 450, 200, 90],
                    Color = "#3b82f6"
                }
            ]
        };

        var cut = Render(data, p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Outside));

        var labels = StageLabels(cut);
        labels[3].TextContent.Should().EndWith("…",
            "no gutter can hold a 35-character stage name, so it ends in an ellipsis instead of running "
            + "past the viewBox edge");
        labels[3].TextContent.Length.Should().BeLessThan(name.Length);

        // The ellipsis answer is not a data loss: the whole stage name is the label's tooltip.
        Line(cut, 3).QuerySelector("title")!.TextContent.Should().Be(name);
        Line(cut, 0).QuerySelector("title").Should().BeNull(
            "a label that fits carries no tooltip — a title on every stage would be noise");
    }

    // ── One <text>, two <tspan>s ─────────────────────────────────────────────

    [Fact]
    public void Funnel_LabelAndValue_AreOneTextWithTwoTspans_SoTheGapFollowsTheFont()
    {
        // Two independent <text> nodes needed two fixed baselines: the gap between the stage name and
        // its value could not follow the font size the container resolves. One <text> with two
        // <tspan>s carries the gap IN the font (dy in em), at no extra geometry in C#.
        var cut = Render();

        var texts = cut.FindAll(".tm-chart__funnel-text");
        texts.Should().HaveCount(4, "one <text> per stage");
        texts[0].QuerySelectorAll("tspan").Should().HaveCount(2);

        var label = StageLabels(cut)[0];
        var value = StageValues(cut)[0];
        label.ParentElement.Should().BeSameAs(value.ParentElement);
        label.GetAttribute("dy").Should().Be("-0.2em", "the first line sits a touch above the anchor");
        value.GetAttribute("dy").Should().Be("1.25em", "the value line is one line below the name");
        Number(label, "x").Should().Be(Number(value, "x"), "both lines start at the same x");
    }

    [Fact]
    public void Funnel_WithoutValues_KeepsASingleTspan()
    {
        var cut = Render(showValues: false);

        foreach (var text in cut.FindAll(".tm-chart__funnel-text"))
        {
            text.QuerySelectorAll("tspan").Should().HaveCount(1);
        }

        StageValues(cut).Should().BeEmpty();
    }

    // ── Ink tokens ───────────────────────────────────────────────────────────

    [Fact]
    public void Funnel_LabelsCarryTokenClasses_InsteadOfInlineColours()
    {
        var cut = Render(configure: p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Inside));

        foreach (var label in cut.FindAll(".tm-chart__funnel-text, .tm-chart__funnel-label, .tm-chart__funnel-value"))
        {
            label.HasAttribute("fill").Should().BeFalse(
                "the ink comes from the --tm-chart-funnel-label-* tokens through the placement class; "
                + "an inline fill is either a hardcoded hex or a value no theme can retune");
            label.HasAttribute("font-size").Should().BeFalse(
                "the size comes from the responsive token, so the container can compensate the viewBox "
                + "scale on a phone AND on a 320 px-tall desktop card");
        }
    }

    [Fact]
    public void Funnel_InsideLabels_PickAContrastingInk_ForEveryPaletteColour()
    {
        foreach (var fill in Palette)
        {
            var data = new ChartData
            {
                Labels = ["Vstup", "Výstup"],
                Datasets =
                [
                    new ChartDataset
                    {
                        Label = "Konverze",
                        Values = [100, 100],
                        Color = fill,
                        BackgroundColors = [fill, fill]
                    }
                ]
            };

            var cut = Render(data, p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Inside));

            foreach (var dark in new[] { false, true })
            {
                var tokens = ThemeCss.TokenGraph(dark);
                var label = StageLabels(cut)[0];
                var token = SelectorInk(label)
                    ?? throw new Xunit.Sdk.XunitException(
                        $"the funnel label over {fill} carries neither --inside nor --outside");

                var ratio = ThemeCss.Contrast(
                    ThemeCss.ResolveColour(token, tokens),
                    ThemeCss.ResolveColour(fill, tokens));

                ratio.Should().BeGreaterThanOrEqualTo(4.5,
                    "the label inside a {0}-filled stage ({1}) must clear WCAG AA — the ink token "
                    + "{2} measured {3:0.00}:1", fill, dark ? "dark" : "light", token, ratio);
            }
        }
    }

    [Fact]
    public void Funnel_ResolvesRgbPercentageAndHslFills_ForTheInkChoice()
    {
        // A host's BackgroundColors is not always a hex. A percentage channel and an hsl() fill both
        // name a colour the label sits on, so both have to reach the same luminance verdict a hex does.
        var light = new ChartData
        {
            Labels = ["Vstup", "Výstup"],
            Datasets =
            [
                new ChartDataset
                {
                    Label = "Konverze",
                    Values = [100, 100],
                    Color = "rgb(100% 100% 100%)",
                    BackgroundColors = ["rgb(100% 100% 100%)", "hsl(60, 100%, 90%)"]
                }
            ]
        };

        var cut = Render(light, p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Inside));

        StageLabels(cut).Should().OnlyContain(l =>
            l.GetAttribute("class")!.Contains("tm-chart__funnel-label--on-light"));

        var dark = new ChartData
        {
            Labels = ["Vstup", "Výstup"],
            Datasets =
            [
                new ChartDataset
                {
                    Label = "Konverze",
                    Values = [100, 100],
                    Color = "#000",
                    BackgroundColors = ["rgb(0 0 0)", "hsl(220, 80%, 20%)"]
                }
            ]
        };

        cut = Render(dark, p => p.Add(x => x.FunnelLabelPlacement, FunnelLabelPlacement.Inside));

        StageLabels(cut).Should().OnlyContain(l =>
            l.GetAttribute("class")!.Contains("tm-chart__funnel-label--inside")
            && !l.GetAttribute("class")!.Contains("--on-light"));
    }

    /// <summary>The ink token the rendered placement class names, or null when it names none.</summary>
    private static string? SelectorInk(AngleSharp.Dom.IElement label)
    {
        var classes = label.GetAttribute("class") ?? "";
        var series = System.Text.RegularExpressions.Regex.Match(
            classes, @"tm-chart__funnel-label--series-(\d)");
        if (series.Success)
        {
            // A built-in series fill takes its paired ink token (TG-002): the palette is theme-relative,
            // so the ink is a token pair, not a fixed-lightness verdict.
            return $"var(--tm-chart-series-{series.Groups[1].Value}-ink)";
        }

        if (classes.Contains("tm-chart__funnel-label--on-light"))
        {
            return "var(--tm-chart-funnel-label-inside-on-light)";
        }

        if (classes.Contains("tm-chart__funnel-label--inside"))
        {
            return "var(--tm-chart-funnel-label-inside)";
        }

        return classes.Contains("tm-chart__funnel-label--outside")
            ? "var(--tm-chart-funnel-label-outside)"
            : null;
    }

    // ── Zero / NaN stages ────────────────────────────────────────────────────

    private static ChartData StageValues(params double[] values) => new()
    {
        Labels = values.Select((_, i) => $"Stupeň {i + 1}").ToArray(),
        Datasets = [new ChartDataset { Label = "Konverze", Values = values, Color = "#3b82f6" }]
    };

    [Fact]
    public void Funnel_ZeroValueStage_DrawsAMinimumWidthSegment_AndKeepsItsLabel()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = Render(StageValues(1000, 0, 200));

        var segments = cut.FindAll(".tm-chart__funnel-segment");
        segments.Should().HaveCount(3, "a zero stage is still a stage — it must not collapse out of the funnel");
        Number(segments[1], "data-width").Should().Be(4,
            "the default FunnelMinSegmentWidth keeps a 0-value stage visible");
        segments[1].GetAttribute("data-value").Should().Be("0");

        StageLabels(cut)[1].TextContent.Should().Be("Stupeň 2");
        StageValues(cut)[1].TextContent.Should().Be("0 (0\u00A0%)",
            "a zero stage reads as zero, not as a missing row");
    }

    [Fact]
    public void Funnel_NaNValueStage_IsDrawnAsZero_NeverAsNaNGeometry()
    {
        var cut = Render(StageValues(1000, double.NaN, 200));

        var segments = cut.FindAll(".tm-chart__funnel-segment");
        segments.Should().HaveCount(3);
        segments.Should().NotContain(s => s.GetAttribute("d")!.Contains("NaN", StringComparison.Ordinal));
        Number(segments[1], "data-width").Should().Be(4);
        segments[1].GetAttribute("data-value").Should().Be("0");
        StageValues(cut)[1].TextContent.Should().StartWith("0 (0");
    }

    [Fact]
    public void Funnel_MinSegmentWidth_IsConfigurable()
    {
        var cut = Render(StageValues(1000, 0, 200),
            p => p.Add(x => x.FunnelMinSegmentWidth, 12));

        var segments = cut.FindAll(".tm-chart__funnel-segment");
        Number(segments[1], "data-width").Should().Be(12);
        segments.Should().NotContain(s => s.GetAttribute("d")!.Contains("NaN", StringComparison.Ordinal));
    }

    [Fact]
    public void Funnel_DefaultParameters_AreTheBackwardCompatibleOnes()
    {
        var chart = new TmChart();

        chart.FunnelLabelPlacement.Should().Be(FunnelLabelPlacement.Auto);
        chart.FunnelMinSegmentWidth.Should().Be(4);
    }

    // ── Screen-reader alternative ────────────────────────────────────────────

    [Fact]
    public void Funnel_RendersAScreenReaderDataTable_WithStagesAndConversions()
    {
        using var _ = CultureScope.Use("en-US");
        var cut = Render();

        var table = cut.Find("[data-testid='chart-funnel-table']");
        table.TagName.Should().Be("TABLE");
        table.TextContent.Should().Contain("Poptávky");
        table.TextContent.Should().Contain("1,000", "a value reads with the reader's thousands grouping");
        table.TextContent.Should().Contain("450 / 1,000 (45\u00A0%)",
            "the reader hears the flow between stages, not just a bare percentage");

        table.QuerySelectorAll("tbody tr").Should().HaveCount(4);
        table.QuerySelectorAll("th[scope='col']").Should().HaveCount(3);
        table.QuerySelector("caption").Should().NotBeNull("the table needs a name a reader can navigate to");
    }

    [Fact]
    public void Funnel_ScreenReaderDataTable_UsesTheReadersCulture()
    {
        // The conversion column is the one number family a reader reads as text, so it follows the
        // CURRENT culture: two decimals at most, the culture's decimal comma and thousands grouping.
        using var _ = CultureScope.Use("cs-CZ");
        var cut = Render(new ChartData
        {
            Labels = ["Poptávky", "Kvalifikace", "Nabídka", "Smlouva"],
            Datasets =
            [
                new ChartDataset { Label = "Konverze", Values = [1240, 610, 155, 0], Color = "#3b82f6" }
            ]
        });

        var text = cut.Find("[data-testid='chart-funnel-table']").TextContent;
        text.Should().Contain("49,19", "610 of 1 240 is 49,19 % under cs-CZ");
        text.Should().Contain("12,5", "155 of 1 240 is exactly 12,5 % — not 12,50 %");
        text.Should().Contain("1\u00A0240", "the Czech thousands separator is a non-breaking space");
        text.Should().NotContain("49.19", "a Czech reader never sees a decimal point");
    }

    [Fact]
    public void Funnel_ScreenReaderDataTable_FollowsTheInteractiveLegendToggle()
    {
        var cut = Render(configure: p => p.Add(x => x.InteractiveLegend, true));

        cut.Find("[data-testid='chart-legend-1']").Click();

        cut.Find("[data-testid='chart-funnel-table']").QuerySelectorAll("tbody tr").Should().HaveCount(3,
            "the reader's table describes the stages that are actually drawn");
        cut.Find("[data-testid='chart-funnel-table']").TextContent.Should().NotContain("Kvalifikace");
    }
}
