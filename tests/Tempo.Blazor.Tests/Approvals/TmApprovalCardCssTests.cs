using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Colour-meaning gate for the approval card's countdown (phase 2 UX review). The danger colour is
/// the only visual signal that a deadline has PASSED and that the last minute is running out, so a
/// rule that paints every countdown red removes both: a card with hours left would look exactly as
/// urgent as an expired one, and the expired state would stop standing out at all.
/// </summary>
/// <remarks>
/// The sweep reads the shipped bundle as well as the component source. The bundle is the file a host
/// actually links, so the flat <c>color: var(--tm-color-danger-text)</c> rule this guards against was
/// copied into it by the bundler — a source-only guard would have stayed green while every consumer
/// still saw the red countdown.
/// </remarks>
public sealed class TmApprovalCardCssTests
{
    private const string Stylesheet = "_approval-card.css";

    private const string Countdown = ".tm-approval-card__countdown";

    private const string ExpiredCountdown =
        ".tm-approval-card__deadline--expired .tm-approval-card__countdown";

    private const string DangerToken = "--tm-color-danger-text";

    private static readonly Regex RuleBlock = new(
        @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    /// <summary>The rules of the committed (minified) bundle, selector normalised per rule.</summary>
    private static IReadOnlyList<(string Selector, string Body)> BundledRules()
        => RuleBlock.Matches(ThemeCss.BundledCss())
            .Select(match => (
                ThemeCss.Normalise(match.Groups["selector"].Value),
                match.Groups["body"].Value))
            .ToList();

    /// <summary>The rules of the component stylesheet whose selector list names the countdown.</summary>
    private static IReadOnlyList<(string Selector, string Body)> CountdownRules(
        IEnumerable<(string Selector, string Body)> rules)
        => rules
            .Where(rule => ThemeCss.SelectorParts(rule.Selector).Contains(Countdown, StringComparer.Ordinal))
            .ToList();

    [Fact]
    public void TheCountdownIsNeutralUntilTheDeadlinePasses()
    {
        var bare = CountdownRules(ThemeCss.Rules(Stylesheet));

        bare.Should().NotBeEmpty("the countdown has a base rule");
        bare.Should().NotContain(
            rule => ThemeCss.Normalise(rule.Body).Contains(DangerToken, StringComparison.Ordinal),
            "a bare countdown must not claim the danger colour — red is what says 'this is urgent', "
            + "and a pending card with hours left is not urgent");

        ThemeCss.Property(Stylesheet, Countdown, "color").Should().Be(
            "var(--tm-text-primary)",
            "the countdown at rest reads as ordinary text");
        ThemeCss.Property(Stylesheet, ExpiredCountdown, "color").Should().Be(
            "var(--tm-color-danger-text)",
            "only the expired state carries the danger colour");
    }

    [Fact]
    public void TheShippedBundlePaintsOnlyTheExpiredCountdownInTheDangerColour()
    {
        var bundled = BundledRules();

        CountdownRules(bundled)
            .Where(rule => ThemeCss.Normalise(rule.Body).Contains(DangerToken, StringComparison.Ordinal))
            .Select(rule => rule.Selector)
            .Should().BeEmpty(
                "the bundle a host links must be regenerated from the scoped rule, otherwise every "
                + "approval card still shows a red countdown");

        bundled
            .Select(rule => (rule.Selector, rule.Body))
            .Where(rule => ThemeCss.SelectorParts(rule.Selector).Contains(ExpiredCountdown, StringComparer.Ordinal))
            .Select(rule => ThemeCss.Normalise(rule.Body))
            .Should().Contain(
                body => body.Contains(DangerToken, StringComparison.Ordinal),
                "the expired countdown keeps its danger colour");
    }
}
