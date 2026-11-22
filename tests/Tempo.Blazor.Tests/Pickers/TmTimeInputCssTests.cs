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

    private static string CoarseBlockOf(string file)
    {
        var css = ThemeCss.ComponentCss(file);
        var match = CoarseBlock.Match(css);

        match.Success.Should().BeTrue($"{file} must declare a @media (pointer: coarse) block");
        return ThemeCss.Normalise(match.Groups["body"].Value);
    }
}
