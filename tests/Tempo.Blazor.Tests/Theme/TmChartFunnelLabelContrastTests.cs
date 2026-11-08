using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// The funnels' label ink and type scale are THEME contract, not component literals: the pre-2.11.0
/// funnel painted <c>#ffffff</c> inside a stage and <c>#6b7280</c> beside it, which measured 2,0–4,4:1
/// on the built-in palette — under WCAG AA — and could not be retuned by a host at all. These guards
/// read the shipped stylesheets and compute the contrast from the token graph, in light and in dark,
/// and they hold the type scale to the RENDERED size a reader actually meets: the SVG meets into its
/// box, so a viewBox unit is not a pixel.
/// </summary>
public class TmChartFunnelLabelContrastTests
{
    /// <summary>The component's built-in funnel palette — the fills an inside label has to survive on.</summary>
    private static readonly string[] Palette =
    [
        "#3b82f6", "#ef4444", "#10b981", "#f59e0b", "#8b5cf6",
        "#ec4899", "#06b6d4", "#84cc16", "#f97316", "#6366f1"
    ];

    /// <summary>The funnel's viewBox (<c>TmChart.SvgW</c> × <c>SvgH</c>) — the scale the type compensates.</summary>
    private const double ViewBoxWidth = 600;
    private const double ViewBoxHeight = 400;

    private static double Px(string token) =>
        double.Parse(token.TrimEnd('x', 'p'), CultureInfo.InvariantCulture);

    /// <summary>The size a viewBox font renders at inside a plot of <paramref name="width"/> × <paramref name="height"/>.</summary>
    private static double Rendered(double fontUnits, double width, double height) =>
        fontUnits * Math.Min(width / ViewBoxWidth, height / ViewBoxHeight);

    [Fact]
    public void FunnelLabelTokens_AreDeclaredInTokensCss()
    {
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        light.Should().ContainKey("--tm-chart-funnel-label-inside");
        light.Should().ContainKey("--tm-chart-funnel-label-inside-on-light");
        light["--tm-chart-funnel-label-outside"].Should().Be("var(--tm-text-secondary)",
            "an outside label sits on the chart SURFACE — it is ordinary secondary text");

        // A rendered FLOOR per line, plus one viewBox step per responsive bucket, plus the narrow bucket.
        light["--tm-chart-funnel-label-size"].Should().Be("12px",
            "the stage label must never RENDER below 12 px");
        light["--tm-chart-funnel-value-size"].Should().Be("11px",
            "the value line must never RENDER below 11 px");
        light.Should().ContainKey("--tm-chart-funnel-label-size-viewbox");
        light.Should().ContainKey("--tm-chart-funnel-value-size-viewbox");
        light.Should().ContainKey("--tm-chart-funnel-label-size-narrow");
        light.Should().ContainKey("--tm-chart-funnel-value-size-narrow");
    }

    [Fact]
    public void FunnelTypeScaleTokens_MatchTheGutterConstants()
    {
        // The Outside gutter is reserved in viewBox units from the WORST case of the type scale, and the
        // component cannot read CSS. The two numbers are therefore repeated in TmChart.Funnel.cs, and
        // this is the guard that keeps the pair from drifting apart.
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        Px(light["--tm-chart-funnel-label-size-narrow"]).Should().Be(TmChart.LabelWorstFontUnits);
        Px(light["--tm-chart-funnel-value-size-narrow"]).Should().Be(TmChart.ValueWorstFontUnits);
    }

    [Fact]
    public void FunnelTypeScale_ReachesTheRenderedFloor_OnTheDemoCards()
    {
        // The two boxes the demo actually renders — and the numbers the E2E measures. A 320 px chart
        // card gives its legend ≈ 41 px plus the root's 8 px gap, so the plot keeps ≈ 271 px: the SVG
        // meets at ≈ 0.68 on the `max-w-2xl` 672 px card and at ≈ 0.60 on the phone's ~358 px card.
        // These are the scales the DISCRETE steps have to reach the floor at; the exact formula in
        // _chart.css solves the scale from the plot's own axes, so no other aspect is left out.
        const double plotHeight = 271;
        var tokens = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));
        var labelFloor = Px(tokens["--tm-chart-funnel-label-size"]);
        var valueFloor = Px(tokens["--tm-chart-funnel-value-size"]);
        var labelWide = Px(tokens["--tm-chart-funnel-label-size-viewbox"]);
        var valueWide = Px(tokens["--tm-chart-funnel-value-size-viewbox"]);
        var labelNarrow = Px(tokens["--tm-chart-funnel-label-size-narrow"]);
        var valueNarrow = Px(tokens["--tm-chart-funnel-value-size-narrow"]);

        // Wide bucket (the container is over 32.5rem ≈ 520 px, so the discrete step is the base one).
        Rendered(labelWide, 672, plotHeight).Should().BeGreaterThanOrEqualTo(labelFloor,
            "the 672 px desktop card renders its viewBox type at ≈ 0,68 — 18 units is 12,2 px");
        Rendered(valueWide, 672, plotHeight).Should().BeGreaterThanOrEqualTo(valueFloor,
            "17 units is 11,5 px on the same card");

        // Narrow bucket (the phone card takes the -narrow step).
        Rendered(labelNarrow, 358, plotHeight).Should().BeGreaterThanOrEqualTo(labelFloor,
            "the ~358 px phone card renders its viewBox type at ≈ 0,60 — 23 units is 13,7 px");
        Rendered(valueNarrow, 358, plotHeight).Should().BeGreaterThanOrEqualTo(valueFloor,
            "21 units is 12,5 px on the same card");
    }

    [Fact]
    public void FunnelOutsideInk_IsReDeclaredOnTheDarkThemeRoots()
    {
        // The documented convention: a token declared ONLY on :root substitutes its var() where it is
        // declared, so a dark theme switched on a PARENT element would keep the light ink. The alias
        // has to be repeated on [data-theme="dark"], exactly like --tm-sort-indicator-active.
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));

        dark.Should().ContainKey("--tm-chart-funnel-label-outside");
        dark["--tm-chart-funnel-label-outside"].Should().Be("var(--tm-text-secondary)");
    }

    [Fact]
    public void FunnelLabelRules_PaintFromTokens_AndCarryNoHardcodedColour()
    {
        var css = ThemeCss.ComponentCss("_chart.css");

        ThemeCss.Property("_chart.css", ".tm-chart__funnel-label--inside", "fill")
            .Should().Be("var(--tm-chart-funnel-label-inside)");
        ThemeCss.Property("_chart.css", ".tm-chart__funnel-label--inside.tm-chart__funnel-label--on-light", "fill")
            .Should().Be("var(--tm-chart-funnel-label-inside-on-light)");
        ThemeCss.Property("_chart.css", ".tm-chart__funnel-label--outside", "fill")
            .Should().Be("var(--tm-chart-funnel-label-outside)");
        ThemeCss.Property("_chart.css", ".tm-chart__funnel-label", "font-size")
            .Should().Be("var(--tm-chart-funnel-label-size-viewbox)",
                "the viewBox step is the FALLBACK; the compensation below it is what makes the floor hold");
        ThemeCss.Property("_chart.css", ".tm-chart__funnel-value", "font-size")
            .Should().Be("var(--tm-chart-funnel-value-size-viewbox)");

        foreach (var selector in new[]
                 {
                     ".tm-chart__funnel-label--inside",
                     ".tm-chart__funnel-label--inside.tm-chart__funnel-label--on-light",
                     ".tm-chart__funnel-label--outside",
                     ".tm-chart__funnel-label",
                     ".tm-chart__funnel-value"
                 })
        {
            ThemeCss.RuleDeclarations(css, selector).Should().NotContain("#",
                "{0} must not hardcode a colour — a literal hex cannot follow a theme", selector);
        }
    }

    [Fact]
    public void FunnelLabelInk_HoldsAaContrast_ForEveryPaletteColour_InBothThemes()
    {
        foreach (var dark in new[] { false, true })
        {
            var tokens = ThemeCss.TokenGraph(dark);
            var white = ThemeCss.ResolveColour("var(--tm-chart-funnel-label-inside)", tokens);
            var black = ThemeCss.ResolveColour("var(--tm-chart-funnel-label-inside-on-light)", tokens);

            foreach (var fill in Palette)
            {
                var lightInk = ThemeCss.Contrast(white, fill);
                var darkInk = ThemeCss.Contrast(black, fill);
                var best = Math.Max(lightInk, darkInk);

                best.Should().BeGreaterThanOrEqualTo(4.5,
                    "at least one inside-ink token must clear WCAG AA on {0} ({1}) — white {2:0.00}:1, "
                    + "dark {3:0.00}:1", fill, dark ? "dark" : "light", lightInk, darkInk);
            }
        }
    }

    [Fact]
    public void OutsideLabelInk_KeepsAaContrast_OnTheChartSurface_InBothThemes()
    {
        foreach (var dark in new[] { false, true })
        {
            var tokens = ThemeCss.TokenGraph(dark);
            var ink = ThemeCss.ResolveColour("var(--tm-chart-funnel-label-outside)", tokens);
            var surface = ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens);

            ThemeCss.Contrast(ink, surface).Should().BeGreaterThanOrEqualTo(4.5,
                "an outside label is normal text on the surface ({0} theme)", dark ? "dark" : "light");
        }
    }

    [Fact]
    public void FunnelCss_CompensatesTheRealRenderedScale_NotJustTheContainerWidth()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_chart.css"));

        // Two containers, on purpose: the ROOT is the NAMED container the discrete width step queries
        // (naming it keeps a nested container from shadowing the query), and the PLOT is a SIZE
        // container, because the compensation needs the block axis — `min(width/600, height/400)`.
        ThemeCss.Property("_chart.css", ".tm-chart--funnel", "container")
            .Should().Be("tm-chart-funnel / inline-size");
        ThemeCss.Property("_chart.css", ".tm-chart--funnel .tm-chart__plot", "container")
            .Should().Be("tm-chart-funnel-plot / size");
        // The thresholds are component-local DENSITY steps, so they are declared in rem behind
        // half-open range syntax — the same form the scheduler's card queries use — while
        // TmBreakpoints owns the four LAYOUT widths in px (TmBreakpointsTests).
        css.Should().Contain("@container tm-chart-funnel (width < 32.5rem)",
            "the width step must query the NAMED container, never whichever container is nearest");

        // The exact compensation: `size × 600 / 100cqw` and `size × 400 / 100cqh`, floored by the
        // viewBox step — a length divided by a container length is what turns plot pixels into viewBox
        // units, so a 320 px-tall desktop card gets the same rendered floor as a phone.
        var supports = css[css.IndexOf("@supports (font-size: calc(1px * 600 / 100cqw * 1px))", StringComparison.Ordinal)..];
        supports.Should().NotBeEmpty("the compensation needs a feature probe: an engine without length " +
            "division must keep the discrete steps");
        supports.Should().Contain("font-size: max( var(--tm-chart-funnel-label-size-viewbox), " +
            "calc(var(--tm-chart-funnel-label-size) * 600 / 100cqw * 1px), " +
            "calc(var(--tm-chart-funnel-label-size) * 400 / 100cqh * 1px));");
        supports.Should().Contain("font-size: max( var(--tm-chart-funnel-value-size-viewbox), " +
            "calc(var(--tm-chart-funnel-value-size) * 600 / 100cqw * 1px), " +
            "calc(var(--tm-chart-funnel-value-size) * 400 / 100cqh * 1px));");

        // The discrete steps survive for the engines the probe excludes — and they answer BOTH axes,
        // because the SVG is height limited: a narrow container and a short plot each take the bigger
        // step, so a wide-but-short card is not left on the base step.
        var narrow = css[css.IndexOf("@container tm-chart-funnel", StringComparison.Ordinal)..];
        narrow.Should().Contain("var(--tm-chart-funnel-label-size-narrow)");
        narrow.Should().Contain("var(--tm-chart-funnel-value-size-narrow)");
        narrow.Should().Contain("@container tm-chart-funnel-plot (height < 15.3125rem)",
            "the block axis is where the scale comes from, so the fallback has to query it too");
    }

    [Fact]
    public void BundledCss_CarriesTheFunnelLabelContract()
    {
        // A host links the bundle, not the sources: a rule that only exists in components/_chart.css
        // never reaches it.
        var bundled = ThemeCss.Normalise(ThemeCss.BundledCss());

        bundled.Should().Contain(".tm-chart__funnel-label--inside");
        bundled.Should().Contain(".tm-chart__funnel-label--inside.tm-chart__funnel-label--on-light");
        bundled.Should().Contain(".tm-chart__funnel-label--outside");

        // The bundle is minified, so the container declarations and the conditional groups are matched
        // whitespace-tolerantly: what matters is that the rules reached the shipped file.
        bundled.Should().MatchRegex(@"container:\s*tm-chart-funnel / inline-size");
        bundled.Should().MatchRegex(@"container:\s*tm-chart-funnel-plot / size");
        bundled.Should().MatchRegex(@"@supports \(font-size:\s*calc\(1px \* 600 / 100cqw \* 1px\)\)");
        bundled.Should().MatchRegex(@"@container tm-chart-funnel \(width\s*<\s*32\.5rem\)");
        bundled.Should().MatchRegex(@"@container tm-chart-funnel-plot \(height\s*<\s*15\.3125rem\)");
    }
}
