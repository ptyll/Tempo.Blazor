using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 4 — the stylesheet contract of
/// <see cref="Tempo.Blazor.Components.AI.TmParallelAttempts"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the package paints with tokens only; this
/// guard proves the attempts surface's own share of it: the partial is imported by the package entry stylesheet, every
/// class the markup emits has a rule, the columns are one horizontal track that becomes a scroll-snap carousel under a
/// CONTAINER query (never the viewport), the best and the chosen attempts carry a border high contrast cannot remove
/// (so "best", "chosen" and the focus ring stay three different things), a coarse pointer grows every target to 44 px,
/// and the partial animates nothing at all.
/// </para>
/// </summary>
public sealed class TmParallelAttemptsCssTests
{
    /// <summary>Every <c>tm-ai-parallel-attempts…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-parallel-attempts",
        "tm-ai-parallel-attempts__title",
        "tm-ai-parallel-attempts__empty",
        "tm-ai-parallel-attempts__track",
        "tm-ai-parallel-attempts__attempt",
        "tm-ai-parallel-attempts__attempt--best",
        "tm-ai-parallel-attempts__attempt--chosen",
        "tm-ai-parallel-attempts__attempt-head",
        "tm-ai-parallel-attempts__attempt-label",
        "tm-ai-parallel-attempts__best",
        "tm-ai-parallel-attempts__best-icon",
        "tm-ai-parallel-attempts__chosen",
        "tm-ai-parallel-attempts__metrics",
        "tm-ai-parallel-attempts__metric",
        "tm-ai-parallel-attempts__metric-label",
        "tm-ai-parallel-attempts__state",
        "tm-ai-parallel-attempts__state-icon",
        "tm-ai-parallel-attempts__score",
        "tm-ai-parallel-attempts__summary",
        "tm-ai-parallel-attempts__body",
        "tm-ai-parallel-attempts__votes-toggle",
        "tm-ai-parallel-attempts__votes",
        "tm-ai-parallel-attempts__vote-list",
        "tm-ai-parallel-attempts__vote",
        "tm-ai-parallel-attempts__vote-head",
        "tm-ai-parallel-attempts__vote-voter",
        "tm-ai-parallel-attempts__vote-outcome",
        "tm-ai-parallel-attempts__vote-score",
        "tm-ai-parallel-attempts__vote-rationale",
        "tm-ai-parallel-attempts__no-votes",
        "tm-ai-parallel-attempts__radio",
        "tm-ai-parallel-attempts__confirm",
        "tm-ai-parallel-attempts__confirm-question",
        "tm-ai-parallel-attempts__confirm-actions",
        "tm-ai-parallel-attempts__action",
        "tm-ai-parallel-attempts__action--primary",
        "tm-ai-parallel-attempts__table",
    ];

    private static string AttemptsCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-parallel-attempts.css");
        File.Exists(path).Should().BeTrue(
            "the attempts surface ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheAttemptsPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-parallel-attempts.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = AttemptsCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }

        // `__votes` is the panel the toggle opens; its rule may only be the layout wrapper, but it must exist.
        css.Should().Contain(".tm-ai-parallel-attempts__votes",
            "the disclosure panel is a real element and needs a rule");
    }

    [Fact]
    public void TheColumnsAreATrack_ThatScrollsIntoACarousel()
    {
        var css = AttemptsCss();

        css.Should().Contain("container-type: inline-size",
            "the carousel breakpoint is measured on the component, not on the window");
        css.Should().Contain("container-name: tm-ai-parallel-attempts");
        css.Should().Contain("@container tm-ai-parallel-attempts",
            "the narrow presentation is a CONTAINER query: two surfaces in one row decide for themselves");
        css.Should().Contain("scroll-snap-type: x mandatory", "a phone gets one attempt at a time, with no JavaScript");
        css.Should().Contain("scroll-snap-align: start");
        css.Should().Contain("overflow-x: auto", "the track scrolls its columns instead of the page");
    }

    [Fact]
    public void BestChosenAndFocus_AreThreeDifferentMarks()
    {
        var css = AttemptsCss();

        css.Should().Contain(".tm-ai-parallel-attempts__attempt--best");
        css.Should().Contain(".tm-ai-parallel-attempts__attempt--chosen");
        css.Should().Contain("--tm-parallel-attempts-best-tone",
            "the best mark is one knob, so the border and the badge cannot disagree");

        var forced = css[css.IndexOf("@media (forced-colors: active)", StringComparison.Ordinal)..];
        forced.Should().Contain("border: 2px solid CanvasText", "the best attempt keeps a mark under high contrast");
        forced.Should().Contain("border: 2px solid Highlight", "and the chosen one keeps a DIFFERENT mark");
        forced.Should().Contain("outline: 3px solid Highlight", "the focus ring stays a third, distinct mark");
    }

    [Fact]
    public void ACoarsePointer_GrowsEveryTargetToTheTouchTarget()
    {
        var css = AttemptsCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("inline-size: var(--tm-touch-target)",
            "a native radio is 20 px wide, which is not a finger target");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void TheFocusRing_SurvivesEveryInteractiveSurface()
    {
        var css = AttemptsCss();

        css.Should().Contain(".tm-ai-parallel-attempts__radio:focus-visible");
        css.Should().Contain(".tm-ai-parallel-attempts__votes-toggle:focus-visible");
        css.Should().Contain(".tm-ai-parallel-attempts__action:focus-visible");
    }

    [Fact]
    public void ThePartialAnimatesNothing()
    {
        var css = AttemptsCss();

        css.Should().NotContain("@keyframes",
            "nothing in this surface moves, so it cannot ship an animation without a reduced-motion block");
        css.Should().NotContain("animation:");
        css.Should().NotContain("transition:");
    }

    [Fact]
    public void TheScoreColumn_IsTabularSoTheNumbersLineUp()
    {
        AttemptsCss().Should().Contain("font-variant-numeric: tabular-nums",
            "a column of scores is read by comparing the digits");
    }
}
