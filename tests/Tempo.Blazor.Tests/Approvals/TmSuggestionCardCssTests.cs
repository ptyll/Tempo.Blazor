using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Accessibility gates for the suggestion card's stylesheet: the sheet may only speak in semantic
/// tokens (so the dark theme and a host's brand override both reach it), every focus stop keeps a
/// system <c>Highlight</c> ring under <c>forced-colors</c> — where the box-shadow ring is forced away
/// — and the collapse transition has a reduced-motion opt-out.
/// </summary>
public sealed class TmSuggestionCardCssTests
{
    private const string Stylesheet = "_suggestion-card.css";

    private static readonly Regex ForcedColorsBlock = new(
        @"@media\s*\(\s*forced-colors\s*:\s*active\s*\)\s*\{(?<body>(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ReducedMotionBlock = new(
        @"@media\s*\(\s*prefers-reduced-motion\s*:\s*reduce\s*\)\s*\{(?<body>(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex RuleBlock = new(
        @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    private static string Css() => ThemeCss.ComponentCss(Stylesheet);

    [Fact]
    public void EveryFocusStopReDeclaresTheSystemHighlightRing()
    {
        var focusStops = RuleBlock.Matches(Css())
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups["selector"].Value))
            .Where(selector => selector.Contains(":focus-visible", StringComparison.Ordinal))
            .ToList();
        focusStops.Should().NotBeEmpty("the card paints its own focus ring and must survive forced colors");

        var forced = ForcedColorsBlock.Match(Css());
        forced.Success.Should().BeTrue("the sheet ships a forced-colors block for the system palette");

        var ringed = RuleBlock.Matches(forced.Groups["body"].Value)
            .Where(match => match.Groups["body"].Value.Contains("outline", StringComparison.Ordinal)
                && match.Groups["body"].Value.Contains("Highlight", StringComparison.Ordinal))
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups["selector"].Value))
            .ToHashSet(StringComparer.Ordinal);

        focusStops.Should().OnlyContain(selector => ringed.Contains(selector),
            "the box-shadow focus ring is forced away by the UA, so every :focus-visible stop needs "
            + "the system Highlight outline");
    }

    [Fact]
    public void TheSheetUsesSemanticTokensOnly()
    {
        var css = Css();

        css.Should().NotMatchRegex("(?<![\\w-])#[0-9a-fA-F]{3,8}\\b",
            "a raw hex step bypasses the dark theme override");
        css.Should().NotMatchRegex("\\b(rgb|rgba|hsl|hsla)\\s*\\(",
            "a raw colour function bypasses the dark theme override");
        css.Should().NotContain("[data-theme", "the dark theme is a token concern, not a component one");
    }

    [Fact]
    public void TheComponentReducedMotionBlockSuppressesTheCollapse()
    {
        var reduced = ReducedMotionBlock.Match(Css());
        reduced.Success.Should().BeTrue("the card transitions its border and collapses its body");

        var rules = RuleBlock.Matches(reduced.Groups["body"].Value)
            .Select(match => (
                Selectors: ThemeCss.SelectorParts(match.Groups["selector"].Value),
                Body: match.Groups["body"].Value))
            .ToList();

        rules.Should().Contain(rule => rule.Selectors.Contains(".tm-suggestion-card")
            && rule.Body.Contains("transition: none", StringComparison.Ordinal));
        rules.Should().Contain(rule => rule.Selectors.Contains(".tm-suggestion-card__collapse")
            && rule.Body.Contains("transition: none", StringComparison.Ordinal));
    }

    [Fact]
    public void TheActionsAndTheAlternativesAreFingerSizedOnCoarsePointers()
    {
        var css = Css();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
    }
}
