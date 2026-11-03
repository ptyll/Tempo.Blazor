using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// The CSS contract of the phase-3 chart additions (plan ff043b2e): the index-tooltip chrome in
/// <c>_chart.css</c> and the sparkline extras in <c>_sparkline.css</c>. Both paint through semantic
/// tokens only — the dark theme redefines tokens, never these files — every class the components emit
/// has a rule, the new tab stop keeps a visible focus ring under forced colours, and the one new
/// animated subject is enumerated in the reduced-motion block.
/// </summary>
public class TmChartSparklineCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A colour literal outside a custom-property definition — the stylesheet rule.</summary>
    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*['""]dark['""]\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    /// <summary>The index-tooltip section appended by phase 3, isolated from the pre-existing sheet.</summary>
    private static string IndexTooltipSection()
    {
        var css = ChartSheet();
        var start = css.IndexOf("── Index tooltip mode", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "_chart.css must carry the phase-3 index-tooltip section");

        var end = css.IndexOf("@media (prefers-reduced-motion: reduce)", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return css[start..end];
    }

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(
            new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    private static string ChartSheet()
        => Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_chart.css");

    private static string SparklineSheet()
        => Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_sparkline.css");

    private static string AnimationsSheet()
        => Read("src", "Tempo.Blazor", "wwwroot", "css", "animations.css");

    [Fact]
    public void TheNewSections_UseSemanticTokensOnly()
    {
        foreach (var (name, css) in new (string, string)[]
                 {
                     ("_chart.css (index tooltip section)", IndexTooltipSection()),
                     ("_sparkline.css", SparklineSheet())
                 })
        {
            ColorLiteral.Matches(css).Should().BeEmpty(
                $"{name} paints through --tm-* tokens; the dark theme redefines tokens, never this file");
            ManualDarkBlock.Matches(css).Should().BeEmpty(
                $"{name} must not carry a component-local dark block: dark values live in tokens-dark.css");
        }
    }

    [Theory]
    [InlineData("tm-chart__index-zones")]
    [InlineData("tm-chart__index-zone")]
    [InlineData("tm-chart__crosshair")]
    [InlineData("tm-chart__index-dot")]
    [InlineData("tm-chart__tooltip--index")]
    [InlineData("tm-chart__tooltip-series")]
    [InlineData("tm-chart__tooltip-series-label")]
    [InlineData("tm-chart__tooltip-series-value")]
    public void EveryChartIndexClass_HasARule(string className)
    {
        ChartSheet().Should().Contain("." + className);
    }

    [Theory]
    [InlineData("tm-sparkline__point")]
    [InlineData("tm-sparkline__point--active")]
    [InlineData("tm-sparkline__bar--active")]
    [InlineData("tm-sparkline__last-point")]
    [InlineData("tm-sparkline__minmax")]
    [InlineData("tm-sparkline__tooltip")]
    public void EverySparklineExtraClass_HasARule(string className)
    {
        SparklineSheet().Should().Contain("." + className);
    }

    [Fact]
    public void TheIndexZoneIsHitTestable_EvenThoughItsFillIsTransparent()
    {
        IndexTooltipSection().Should().Contain("pointer-events: all",
            "a transparent fill would otherwise depend on how the browser treats a fully transparent plane");
    }

    [Fact]
    public void TheSparklineTooltip_StaysInsideTheBox()
    {
        var sheet = SparklineSheet();

        sheet.Should().Contain("inset-block-start: 0");
        sheet.Should().Contain("inset-inline-end: 0");
        sheet.Should().NotContain("bottom: calc(100%",
            "a bubble above the box is clipped by the table cell a sparkline lives in");
    }

    [Fact]
    public void TheMinMaxCaption_StepsAsideWhileTheTooltipCardIsOn()
    {
        var sheet = SparklineSheet();

        sheet.Should().Contain(":has(.tm-sparkline__tooltip)",
            "the caption (top-start) and the card (top-end) would overlap in a narrow grid cell");
        sheet.Should().Contain("visibility: hidden",
            "the caption hides only while the card shows; it is never removed from the box");
    }

    [Fact]
    public void BothNewTabStops_KeepAVisibleFocusIndicator_AndSurviveForcedColours()
    {
        foreach (var (name, css) in new (string, string)[]
                 {
                     ("_chart.css", ChartSheet()),
                     ("_sparkline.css", SparklineSheet())
                 })
        {
            css.Should().Contain("svg[tabindex='0']:focus-visible", $"{name} owns a focusable graphic now");
            css.Should().Contain("var(--tm-focus-ring)", $"{name} uses the library focus ring");
            css.Should().Contain("@media (forced-colors: active)", $"{name} must survive high contrast");
            css.Should().Contain("Highlight", "forced colours turn box-shadow off; the outline takes over");
        }
    }

    [Fact]
    public void TheNewAnimatedSubject_IsCoveredByTheReducedMotionBlock()
    {
        AnimationsSheet().Should().Contain(".tm-sparkline__point,",
            "the active-point transition must be suppressed for a reader who asked for less motion");
    }

    [Fact]
    public void BothSheets_AreImportedAndShipInTheCommittedBundle()
    {
        var entry = Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.css");
        entry.Should().Contain("@import \"components/_chart.css\";");
        entry.Should().Contain("@import \"components/_sparkline.css\";");

        var bundle = Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css");
        bundle.Should().Contain(".tm-chart__crosshair{");
        bundle.Should().Contain(".tm-sparkline__tooltip{");
    }
}
