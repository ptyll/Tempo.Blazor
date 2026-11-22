using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// CSS contract of TmTimeSelector (plan 1c95309c Fáze 4): 44 px targets via
/// <c>--tm-touch-target</c>, square fit independent of pointer, tokens only,
/// forced-colors, cutout defaults, and the committed bundle.
/// </summary>
public sealed class TmTimeSelectorCssTests
{
    private static readonly Regex CoarseBlock = new(
        @"@media\s*\(pointer:\s*coarse\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ForcedColorsBlock = new(
        @"@media\s*\(forced-colors:\s*active\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void CoarsePointer_GivesSpinAndStepTheTouchTarget()
    {
        var body = CoarseBlockOf("_time-selector.css");

        body.Should().Contain(".tm-time-selector__spin");
        body.Should().Contain(".tm-time-selector__step");
        body.Should().Contain("min-block-size: var(--tm-touch-target);");
        body.Should().Contain("min-inline-size: var(--tm-touch-target);");
        body.Should().NotContain("#", "component CSS uses tokens, never a hex colour");
        body.Should().NotContain("px",
            "the 44 px floor is the --tm-touch-target token, not a literal");
    }

    [Fact]
    public void SquareFit_UsesTouchTargetRegardlessOfPointer()
    {
        var css = ThemeCss.ComponentCss("_time-selector.css");
        var compact = Regex.Replace(css, @"\s+", string.Empty);

        compact.Should().Contain("[data-tm-time-fit=\"square\"]",
            "square fit sizes 44 px even on a fine pointer (Flex Window)");
        compact.Should().Contain("var(--tm-touch-target)");
    }

    [Fact]
    public void ForcedColors_HasABlock()
    {
        var css = ThemeCss.ComponentCss("_time-selector.css");
        var match = ForcedColorsBlock.Match(css);
        match.Success.Should().BeTrue("_time-selector.css must declare a forced-colors block");
        var body = match.Groups["body"].Value;
        body.Should().Contain("CanvasText");
        body.Should().Contain("outline: 2px solid Highlight");
        body.Should().Contain("outline-offset: 2px");
        body.Should().Contain(".tm-time-selector__spin:focus");
        body.Should().Contain(".tm-time-selector__step:focus-visible");
    }

    [Fact]
    public void CutoutTokens_DefaultToZero_AndBottomPaddingUsesSpace3()
    {
        var css = ThemeCss.ComponentCss("_time-selector.css");
        css.Should().Contain("--tm-time-selector-cutout-block:");
        css.Should().Contain("--tm-time-selector-cutout-inline:");
        css.Should().Contain("var(--tm-space-3)",
            "bottom inset of the square sheet is at least --tm-space-3 (12 px)");
    }

    [Fact]
    public void KeyboardOpen_Square_IsOneLineReadout()
    {
        var css = ThemeCss.ComponentCss("_time-selector.css");
        css.Should().Contain("[data-tm-time-keyboard=\"open\"]");
        css.Should().Contain("var(--tm-space-2)",
            "one-line readout padding is 2 × 8 px so 44 + 16 = 60 px ≤ 79 px");
        var compact = Regex.Replace(css, @"\s+", string.Empty);
        compact.Should().Contain(
            "[data-tm-time-fit=\"square\"][data-tm-time-keyboard=\"open\"].tm-time-selector__readout{min-block-size:var(--tm-touch-target);padding-block:var(--tm-space-2);flex-wrap:nowrap;}",
            "decision 15: square + keyboard=open must beat flex-wrap:wrap and stay one row");
    }

    [Fact]
    public void HourCycle12_WrapsPeriodOntoItsOwnCenteredRow()
    {
        var css = ThemeCss.ComponentCss("_time-selector.css");
        var compact = Regex.Replace(css, @"\s+", string.Empty);
        compact.Should().Contain(".tm-time-selector__readout{display:flex;flex-wrap:wrap;",
            "hours+minutes at 44 px already overflow a 306 px Flex Window; wrap is the default");
        compact.Should().NotContain(
            ".tm-time-selector[data-tm-hour-cycle=\"h12\"].tm-time-selector__readout{flex-wrap:wrap;}",
            "h12 wrap is already the base readout rule; a duplicate selector is dead CSS");
        compact.Should().NotContain(
            ".tm-time-selector--with-seconds[data-tm-time-fit=\"square\"].tm-time-selector__readout{flex-wrap:wrap;}",
            "seconds+square wrap is already the base readout rule; a duplicate selector is dead CSS");
        compact.Should().Contain(".tm-time-selector.tm-time-selector__period{");
        compact.Should().Contain("flex-basis:100%");
        compact.Should().Contain("flex-direction:row");
        compact.Should().Contain("justify-content:center");
    }

    [Fact]
    public void BordersAndDisabledOpacity_UseTokens()
    {
        var css = ThemeCss.ComponentCss("_time-selector.css");
        css.Should().Contain("border: var(--tm-border-width) solid var(--tm-border-color);");
        css.Should().NotContain("border: 1px solid var(--tm-border-color);");
        css.Should().Contain("opacity: var(--tm-opacity-disabled);");
        css.Should().NotContain("opacity: 0.5");
    }

    [Fact]
    public void NoHexColours()
    {
        ThemeCss.ComponentCss("_time-selector.css").Should().NotContain("#");
    }

    [Fact]
    public void GridAndPresets_UseTokens_AndForcedColorsSelectedState()
    {
        var css = ThemeCss.ComponentCss("_time-selector.css");
        css.Should().Contain(".tm-time-selector__grid");
        css.Should().Contain(".tm-time-selector__cell");
        css.Should().Contain(".tm-time-selector__presets");
        css.Should().Contain(".tm-time-selector--grid-open");
        css.Should().Contain("gap: var(--tm-space-1);");
        css.Should().Contain("var(--tm-space-2)");
        css.Should().Contain("var(--tm-space-3)");
        css.Should().Contain("[data-tm-time-grid-columns=\"4\"]");
        css.Should().Contain("[data-tm-time-grid-columns=\"6\"]");
        css.Should().Contain("[data-tm-time-grid-columns=\"8\"]");
        css.Should().Contain("repeat(4, minmax(0, 1fr))");
        css.Should().Contain("repeat(6, minmax(0, 1fr))");
        css.Should().Contain("repeat(8, minmax(0, 1fr))");
        css.Should().NotContain("text-underline-offset: 2px");
        css.Should().NotContain("text-decoration-thickness: 2px");

        var match = ForcedColorsBlock.Match(css);
        match.Success.Should().BeTrue();
        var body = match.Groups["body"].Value;
        body.Should().Contain("Highlight");
        body.Should().Contain("HighlightText");
        body.Should().Contain("GrayText");
        body.Should().Contain("text-decoration-color: CanvasText");
        body.Should().Contain(".tm-time-selector__cell[aria-checked=\"true\"]");
        body.Should().Contain(".tm-time-selector__preset[aria-pressed=\"true\"]");
    }

    [Fact]
    public void BundledStylesheet_CarriesTheSelectorRules()
    {
        var bundled = ThemeCss.BundledCss();
        bundled.Should().Contain(".tm-time-selector",
            "the committed bundle must carry the selector — a partial edited without rebundling ships nothing (build src/Tempo.Blazor/Tempo.Blazor.csproj -t:BundleCssFiles)");
        bundled.Should().Contain("--tm-time-selector-cutout-block");
        bundled.Should().Contain("[data-tm-time-fit=\"square\"]");
    }

    private static string CoarseBlockOf(string file)
    {
        var css = ThemeCss.ComponentCss(file);
        var match = CoarseBlock.Match(css);
        match.Success.Should().BeTrue($"{file} must declare a @media (pointer: coarse) block");
        return ThemeCss.Normalise(match.Groups["body"].Value);
    }
}
