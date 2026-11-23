using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// CSS contract of TmTimeInput (plan 1c95309c phase 3): a coarse pointer gets a 44 px target
/// on every segment (WCAG 2.5.8 / internal --tm-touch-target), tokens only, and the committed
/// bundle carries the rule.
/// </summary>
public sealed class TmTimeInputCssTests
{
    private static readonly Regex CoarseBlock = new(
        @"@media\s*\(pointer:\s*coarse\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void CoarsePointer_GivesEverySegmentTheTouchTarget()
    {
        var body = CoarseBlockOf("_time-input.css");

        body.Should().Contain(".tm-time-seg",
            "the hour/minute/second inputs are the only targets a finger has");
        body.Should().Contain("min-block-size: var(--tm-touch-target);",
            "a segment is ~28 px tall today — it must become the shared 44 px target");
        body.Should().Contain("min-inline-size: var(--tm-touch-target);");
        body.Should().NotContain("#", "component CSS uses tokens, never a hex colour");
        body.Should().NotContain("px",
            "the 44 px floor is the --tm-touch-target token, not a literal");
    }

    [Fact]
    public void BundledStylesheet_CarriesTheCoarsePointerRule()
    {
        var bundled = ThemeCss.BundledCss();
        var blocks = CoarseBlock.Matches(bundled).Select(match => match.Groups["body"].Value).ToList();

        blocks.Should().Contain(body => body.Contains(".tm-time-seg", StringComparison.Ordinal)
            && body.Contains("--tm-touch-target", StringComparison.Ordinal),
            "the committed bundle must carry the time-input coarse rule — a partial edited without rebundling ships nothing (build src/Tempo.Blazor/Tempo.Blazor.csproj -t:BundleCssFiles)");
    }

    [Fact]
    public void BundledStylesheet_ReadOnlyUsesSurfaceSecondary()
    {
        var compact = Regex.Replace(ThemeCss.BundledCss(), @"\s+", string.Empty);
        compact.Should().Contain(".tm-time-input--readonly{background:var(--tm-bg-surface-secondary);}",
            "the committed bundle must carry the dark-safe read-only fill — a partial CSS edit without BundleCssFiles ships the old --tm-color-secondary-subtle");
        compact.Should().NotContain(".tm-time-input--readonly{background:var(--tm-color-secondary-subtle);}");
    }

    [Fact]
    public void InvalidAndReadonlyModifiers_HaveRules()
    {
        var css = ThemeCss.ComponentCss("_time-input.css");
        css.Should().Contain(".tm-time-input--invalid");
        css.Should().Contain(".tm-time-input--readonly");
        css.Should().Contain("background: var(--tm-bg-surface-secondary)",
            "read-only must use a token with a dark override (same as .tm-select.tm-select-readonly) — --tm-color-secondary-subtle stays #f9fafb in dark and digits vanish");
        css.Should().NotContain("--tm-color-secondary-subtle");
        css.Should().Contain(".tm-time-input__error");
        css.Should().Contain(".tm-time-input__period");
        css.Should().Contain(".tm-time-input-wrap");
    }

    [Fact]
    public void TimeRangePicker_OvernightAndWrap_UseTokensOnly()
    {
        var css = ThemeCss.ComponentCss("_time-range-picker.css");
        css.Should().Contain(".tm-time-range-overnight");
        css.Should().Contain(".tm-time-range-body");
        css.Should().Contain("flex-wrap: wrap");
        css.Should().Contain("var(--tm-space-");
        css.Should().Contain("var(--tm-touch-target)");
        css.Should().NotContain("#", "component CSS uses tokens, never a hex colour");
        Regex.IsMatch(css, @"(?<![\w.-])\d+px(?![\w.-])").Should().BeFalse(
            "range-picker CSS must not introduce a literal px size — 44 px is --tm-touch-target");
    }

    [Fact]
    public void EmptyPlaceholder_UsesTertiaryToken()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_time-input.css"));
        css.Should().Contain(".tm-time-input--empty .tm-time-seg");
        css.Should().Contain("color: var(--tm-text-tertiary)");
    }

    [Fact]
    public void DatePicker_ReservedClearSlot_UsesSpaceToken_AndReadonlyKeepsSurface()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_date-picker.css"));
        css.Should().Contain(".tm-date-picker--readonly .tm-date-picker-trigger");
        css.Should().Contain("cursor: default");

        var shared = ThemeCss.Normalise(ThemeCss.ComponentCss("_picker-shared.css"));
        shared.Should().Contain(".tm-picker-clear-slot");
        shared.Should().Contain("width: var(--tm-space-5)");
        shared.Should().Contain("height: var(--tm-space-5)");
        shared.Should().NotContain(".tm-picker-clear-slot { width: 20px");
    }

    [Fact]
    public void DatePicker_TypedField_UsesTokens_FocusInvalidAndCoarseTouch()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_date-picker.css"));
        css.Should().Contain(".tm-date-picker-typed");
        css.Should().Contain("block-size: var(--tm-input-height-md)");
        css.Should().Contain("background: var(--tm-bg-surface)");
        css.Should().Contain("color: var(--tm-text-primary)");
        css.Should().Contain("border: var(--tm-border-width) solid var(--tm-border-color)");
        css.Should().Contain(".tm-date-picker-typed:focus-visible");
        css.Should().Contain("box-shadow: var(--tm-focus-ring)");
        css.Should().Contain(".tm-date-picker-typed[aria-invalid=\"true\"]");
        css.Should().Contain("border-color: var(--tm-color-danger)");
        css.Should().NotContain("#", "component CSS uses tokens, never a hex colour");

        var coarse = CoarseBlockOf("_date-picker.css");
        coarse.Should().Contain(".tm-date-picker-typed");
        coarse.Should().Contain("min-block-size: var(--tm-touch-target)");
        coarse.Should().NotContain("px", "the 44 px floor is --tm-touch-target, not a literal");

        var sharedCoarse = CoarseBlockOf("_picker-shared.css");
        sharedCoarse.Should().Contain(".tm-picker-clear-slot");
        sharedCoarse.Should().Contain(".tm-picker-clear");
        sharedCoarse.Should().Contain("width: var(--tm-touch-target)");
        sharedCoarse.Should().Contain("height: var(--tm-touch-target)");
        sharedCoarse.Should().NotContain("px", "the 44 px floor is --tm-touch-target, not a literal");
    }

    [Fact]
    public void PickerClear_HasFocusVisibleRing_OnFinePointersUnchanged()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_picker-shared.css"));
        css.Should().Contain(".tm-picker-clear:focus-visible");
        css.Should().Contain("outline: var(--tm-border-width) solid transparent");
        css.Should().Contain("box-shadow: var(--tm-focus-ring)");
        css.Should().Contain("width: 20px", "fine pointers keep the compact 20 px clear");
    }

    [Fact]
    public void BundledStylesheet_CarriesTypedDateAndClearTouchRules()
    {
        var bundled = ThemeCss.BundledCss();
        bundled.Should().Contain(".tm-date-picker-typed",
            "the committed bundle must carry the typed-date field — a partial CSS edit without BundleCssFiles ships nothing");
        bundled.Should().Contain(".tm-picker-clear:focus-visible",
            "the committed bundle must carry the clear-button focus ring");
        var blocks = CoarseBlock.Matches(bundled).Select(match => match.Groups["body"].Value).ToList();
        blocks.Should().Contain(body => body.Contains(".tm-date-picker-typed", StringComparison.Ordinal)
            && body.Contains("--tm-touch-target", StringComparison.Ordinal),
            "the committed bundle must carry the typed-date coarse rule");
        blocks.Should().Contain(body => body.Contains(".tm-picker-clear-slot", StringComparison.Ordinal)
            && body.Contains(".tm-picker-clear", StringComparison.Ordinal)
            && body.Contains("--tm-touch-target", StringComparison.Ordinal),
            "the committed bundle must carry the 44 px clear slot on coarse pointers");
    }

    [Fact]
    public void DateTimePicker_SharesInputHeightToken()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_datetime-picker.css"));
        css.Should().Contain("height: var(--tm-input-height-md)");
        css.Should().Contain(".tm-datetime-picker");
        css.Should().NotContain(".tm-date-picker-trigger:hover",
            "readonly hover lives on .tm-date-picker--readonly in _date-picker.css so the datetime picker does not re-own the trigger");
    }

    [Fact]
    public void BundledStylesheet_CarriesTheOvernightAndWrapRules()
    {
        var bundled = ThemeCss.BundledCss();
        bundled.Should().Contain(".tm-time-range-overnight",
            "the committed bundle must carry the overnight class — a partial edited without rebundling ships nothing");
        bundled.Should().Contain("flex-wrap:wrap",
            "the committed bundle must wrap the range body so 374 px cover does not overflow");
    }

    private static string CoarseBlockOf(string file)
    {
        var css = ThemeCss.ComponentCss(file);
        var match = CoarseBlock.Match(css);

        match.Success.Should().BeTrue($"{file} must declare a @media (pointer: coarse) block");
        return ThemeCss.Normalise(match.Groups["body"].Value);
    }
}
