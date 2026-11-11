using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan 7ba95ed6 phase 12 — a coarse pointer is the only pointer a phone or a tablet has, and every control a
/// finger is expected to hit keeps the shared <c>--tm-touch-target</c> (2.75rem = 44 px). Two of the surfaces
/// this plan series added shipped WITHOUT that rule: the trace waterfall's disclosure chevron stayed 20 px and
/// the usage meter's host-supplied call to action inherited whatever size the host's button asked for.
/// <para>
/// The guard reads the stylesheet source AND the committed bundle, because a partial edited without rebundling
/// ships nothing — the same reasoning as <c>TraceWaterfallContainmentCssTests</c>. The behaviour itself is
/// proven in a real browser at a coarse pointer by
/// <c>Fze12TouchEmulationSweepE2ETests.TraceWaterfall_TapSelectsARow_AndTheDisclosureKeepsAFortyFourPixelTarget</c>
/// and <c>…UsageMeter_CallToActionKeepsAFortyFourPixelTarget_AndATapReachesTheHost</c>.
/// </para>
/// </summary>
public sealed class CoarsePointerTargetCssTests
{
    private static readonly Regex CoarseBlock = new(
        @"@media\s*\(pointer:\s*coarse\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void TraceWaterfall_GivesTheDisclosureTheTouchTarget()
    {
        var body = CoarseBlockOf("_trace-waterfall.css");

        body.Should().Contain(".tm-trace-waterfall__toggle",
            "the chevron is the only control a finger has for folding a subtree");
        body.Should().Contain(".tm-trace-waterfall__toggle-spacer",
            "the spacer grows with the chevron, otherwise every leaf label shifts against its parent's");
        body.Should().Contain("min-block-size: var(--tm-touch-target);",
            "the 20 px chevron must become the shared 44 px target on a coarse pointer");
        body.Should().Contain("min-inline-size: var(--tm-touch-target);");
        body.Should().Contain("flex: 0 0 var(--tm-touch-target);",
            "the token basis plus flex-shrink 0 keeps the chevron at 44 px in a narrow tree column");
    }

    [Fact]
    public void UsageMeter_SizesTheHostCallToActionForAFinger()
    {
        var body = CoarseBlockOf("_usage-meter.css");

        body.Should().Contain(".tm-usage-meter__cta button",
            "the call to action is a HOST slot, so the meter — not the host's button size — owns its target");
        body.Should().Contain(".tm-usage-meter__cta a",
            "an anchor is the other shape a host puts in the slot");
        body.Should().Contain(".tm-usage-meter__cta [role='button']",
            "a host may put a role-based control in the slot instead of a native button or link");
        body.Should().Contain("min-block-size: var(--tm-touch-target);");

        // The descendant combinator must stay a DESCENDANT: the bundler minifies `:is(` without its leading
        // space, which silently turns `.tm-usage-meter__cta :is(button, a)` into a compound selector that
        // never matches. Two explicit rules are the shape that survives minification.
        body.Should().NotContain(":is(");
    }

    [Fact]
    public void SegmentedControl_OutSpecifiesItsSizeModifiers()
    {
        var body = CoarseBlockOf("_segmented-control.css");

        body.Should().Contain(".tm-segmented .tm-segmented__option",
            "the size modifiers are `.tm-segmented--md .tm-segmented__option` (two classes), so the coarse "
            + "rule needs the root class too — a bare class loses on specificity and the tray stayed 32 px");
        body.Should().Contain("min-block-size: var(--tm-touch-target);");

        // …and it has to come AFTER the size rules in source order, or an equal-specificity tie would go
        // to the modifier.
        var css = ThemeCss.ComponentCss("_segmented-control.css");
        var modifier = css.IndexOf(".tm-segmented--md .tm-segmented__option", StringComparison.Ordinal);
        var coarse = css.IndexOf("@media (pointer: coarse)", StringComparison.Ordinal);
        modifier.Should().BeGreaterThan(-1);
        coarse.Should().BeGreaterThan(modifier, "the coarse block must be the last word on the tray height");
    }

    [Fact]
    public void BundledStylesheet_CarriesBothCoarsePointerRules()
    {
        var bundled = ThemeCss.BundledCss();
        var blocks = CoarseBlock.Matches(bundled).Select(match => match.Groups["body"].Value).ToList();

        blocks.Should().Contain(body => body.Contains(".tm-trace-waterfall__toggle", StringComparison.Ordinal),
            "the committed bundle must carry the waterfall's coarse-pointer rule — a partial edited without "
            + "rebundling ships nothing (build src/Tempo.Blazor/Tempo.Blazor.csproj -t:BundleCssFiles)");
        blocks.Should().Contain(body => body.Contains(".tm-usage-meter__cta button", StringComparison.Ordinal),
            "the committed bundle must carry the usage meter's coarse-pointer rule");
    }

    private static string CoarseBlockOf(string file)
    {
        var css = ThemeCss.ComponentCss(file);
        var match = CoarseBlock.Match(css);

        match.Success.Should().BeTrue($"{file} must declare a @media (pointer: coarse) block");
        return ThemeCss.Normalise(match.Groups["body"].Value);
    }
}
