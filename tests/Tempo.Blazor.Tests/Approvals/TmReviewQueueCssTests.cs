using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Accessibility gates for the review queue's stylesheet (phase 3 UX review). Two failures are
/// invisible to markup and ARIA tests, so they get a CSS guard:
/// <list type="bullet">
/// <item>the focus ring: the sheet uses the two-layer <c>--tm-focus-ring</c> box shadow, and a UA
///   forced-colors mode drops every box shadow — so each focus stop must re-declare the system
///   <c>Highlight</c> outline, or a keyboard user loses the indicator entirely;</item>
/// <item>the colour contract: a raw palette step (<c>#hex</c>, <c>rgb()</c>) would bypass the dark
///   theme override, which is what "use the <c>--tm-*</c> tokens" exists to prevent.</item>
/// </list>
/// </summary>
public sealed class TmReviewQueueCssTests
{
    private const string Stylesheet = "_review-queue.css";

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

    private static IReadOnlyList<string> SelectorsWithFocusVisible()
        => RuleBlock.Matches(Css())
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups["selector"].Value))
            .Where(selector => selector.Contains(":focus-visible", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void EveryFocusStopReDeclaresTheSystemHighlightRing()
    {
        var focusStops = SelectorsWithFocusVisible();
        focusStops.Should().NotBeEmpty("the queue paints its own focus ring and must survive forced colors");

        var forced = ForcedColorsBlock.Match(Css());
        forced.Success.Should().BeTrue("the sheet ships a forced-colors block for the system palette");

        var ringed = RuleBlock.Matches(forced.Groups["body"].Value)
            .Where(match => match.Groups["body"].Value.Contains("outline", StringComparison.Ordinal)
                && match.Groups["body"].Value.Contains("Highlight", StringComparison.Ordinal))
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups["selector"].Value))
            .ToHashSet(StringComparer.Ordinal);

        focusStops.Should().OnlyContain(selector => ringed.Contains(selector),
            "a box-shadow focus ring is forced away by the UA, so every :focus-visible stop needs the "
            + "system Highlight outline — otherwise the queue's keyboard indicator disappears");
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
    public void TheComponentReducedMotionBlockSuppressesTheRowTransitionAndTheSpinner()
    {
        var reduced = ReducedMotionBlock.Match(Css());
        reduced.Success.Should().BeTrue(
            "the handled row collapses with a height transition and the busy state spins — both need a "
            + "reduced-motion opt-out next to the rule they animate");

        var rules = RuleBlock.Matches(reduced.Groups["body"].Value)
            .Select(match => (
                Selectors: ThemeCss.SelectorParts(match.Groups["selector"].Value),
                Body: match.Groups["body"].Value))
            .ToList();

        rules.Should().Contain(rule => rule.Selectors.Contains(".tm-review-queue__row")
            && rule.Body.Contains("transition: none", StringComparison.Ordinal));
        rules.Should().Contain(rule => rule.Selectors.Contains(".tm-review-queue__row-collapse")
            && rule.Body.Contains("transition: none", StringComparison.Ordinal));
        rules.Should().Contain(rule => rule.Selectors.Contains(".tm-review-queue__spinner")
            && rule.Body.Contains("animation: none", StringComparison.Ordinal));
    }
}
