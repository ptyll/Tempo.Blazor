using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// The CSS contract of the fze-4 toast additions: the stacked „deck" presentation (at most three
/// full layers, older cards collapse to strips, hover/focus-within expands), the two new center
/// positions, the loading spinner, the swipe-to-dismiss return transition and the
/// reduced-motion/forced-colours coverage of every new animated subject.
/// </summary>
public class TmToastDeckCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    private static string Sheet() => Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_toast.css");

    private static string Block(string css, string atRule)
    {
        var start = css.IndexOf(atRule, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{atRule} block must exist");
        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return css[start..(i + 1)];
        }

        return css[start..];
    }

    [Fact]
    public void TheDeck_CollapsesOlderCards_AndExpandsOnHoverOrFocusWithin()
    {
        var css = Sheet();

        css.Should().Contain(".tm-toast-container--stacked", "the deck is a container modifier");
        // At most three full layers: Blazor assigns .tm-toast--deck-collapsed to every card older
        // than the newest three, and the CSS shrinks it to a strip. The
        // :not(:hover):not(:focus-within) guard IS the expansion: hovering the container or
        // moving focus into it releases the collapse with nothing else to undo. (No :nth-of-type
        // here — the computed-style cascade probe only models :not/:is/:where/:matches.)
        var collapse = Block(css, ".tm-toast-container--stacked:not(:hover):not(:focus-within) .tm-toast--deck-collapsed");
        collapse.Should().Contain("max-height", "an older card collapses to a thin strip");
        collapse.Should().Contain("overflow: hidden");
        collapse.Should().Contain(":not(:hover)", "hovering the container expands the deck");
        collapse.Should().Contain(":focus-within", "moving focus inside the container expands the deck");
    }

    [Fact]
    public void TheDeck_StripHeight_UsesTheSpacingToken_WithoutANonAnimatingTransition()
    {
        var css = Sheet();

        Block(css, ".tm-toast-container--stacked:not(:hover):not(:focus-within) .tm-toast--deck-collapsed")
            .Should().Contain("var(--tm-space-3",
                "the 12px strip resolves through the spacing token, not a hardcoded length");
        css.Should().NotContain("transition: max-height",
            "a strip toggles between a fixed 12px and the auto-height of an expanded card — not interpolable, "
            + "so a max-height transition is a no-op the reduced-motion block would still have to enumerate");
    }

    [Fact]
    public void TheSpinner_UsesTheIconSizeAndDurationTokens()
    {
        var spinner = Block(Sheet(), ".tm-toast-spinner {");

        spinner.Should().Contain("var(--tm-icon-size-md", "the spinner box tracks the shared icon-size scale");
        spinner.Should().Contain("var(--tm-duration-spin", "the rotation duration resolves through a duration token");
        spinner.Should().NotContain("0.8s");
        Block(Sheet(), ".tm-toast--swipe-return {").Should().Contain("var(--tm-duration-quick",
            "the swipe rollback duration resolves through a duration token, not a hardcoded 0.18s");
    }

    [Fact]
    public void TheDeck_StripsCannotTrapPointerOrFocus()
    {
        var collapse = Block(Sheet(), ".tm-toast-container--stacked:not(:hover):not(:focus-within) .tm-toast--deck-collapsed");

        collapse.Should().Contain("pointer-events: none",
            "a collapsed strip is only a depth hint — it must not swallow taps (the close button and Escape remain the alternative)");

        var body = Block(Sheet(), ".tm-toast-container--stacked:not(:hover):not(:focus-within) .tm-toast--deck-collapsed .tm-toast-body");
        body.Should().Contain("visibility: hidden",
            "a collapsed strip's content leaves the accessibility tree and cannot hold focus");
    }

    [Fact]
    public void CenterPositions_CenterTheContainer()
    {
        var css = Sheet();

        Block(css, ".tm-toast-container--top-center {").Should().Contain("translateX(-50%)");
        Block(css, ".tm-toast-container--bottom-center {").Should().Contain("translateX(-50%)");

        var mobile = Block(css, "@media (max-width: 640px)");
        mobile.Should().Contain(".tm-toast-container--top-center", "the center offset must not push a full-width mobile container off-screen");
        mobile.Should().Contain(".tm-toast-container--bottom-center");
        mobile.Should().Contain("transform: none");
    }

    [Fact]
    public void TheSpinner_IsATokenPaintedAnimatedSubject_WithReducedMotionAndForcedColoursCoverage()
    {
        var css = Sheet();

        css.Should().Contain("@keyframes tmToastSpin");
        Block(css, ".tm-toast-spinner {").Should().Contain("animation: tmToastSpin");

        // No bare colour literal on the spinner: its paint resolves through --tm-* tokens (the
        // sheet's var() fallbacks are the established convention here, so they are stripped first).
        var withoutFallbacks = Regex.Replace(Block(css, ".tm-toast-spinner {"), @"var\([^)]*\)", "var()");
        Regex.Matches(withoutFallbacks, @"#[0-9a-fA-F]{3,8}\b|rgba?\(")
            .Should().BeEmpty("the spinner resolves its paint through design tokens");

        // The LAST forced-colors block carries the spinner (an earlier one restyles the action).
        Block(css[(css.LastIndexOf("@media (forced-colors: active)", StringComparison.Ordinal)..)],
            "@media (forced-colors: active)").Should().Contain(".tm-toast-spinner");

        var reduce = Block(Read("src", "Tempo.Blazor", "wwwroot", "css", "animations.css"),
            "@media (prefers-reduced-motion: reduce)");
        reduce.Should().Contain(".tm-toast-spinner",
            "every animated Tempo subject must appear in the reduced-motion enumeration (ReducedMotionSelectorTests recomputes it)");
    }

    [Fact]
    public void TheSwipe_ReturnTransition_LivesOnTheCard_AndIsCoveredByReducedMotion()
    {
        var css = Sheet();

        // The swipe drag drives the inline transform directly; the rollback animates the same
        // property, so it must not fight the card's own slide-in animation (fill mode backwards).
        Block(css, ".tm-toast {").Should().Contain("backwards",
            "fill-forwards would pin transform: translateX(0) over the inline swipe transform forever");
        Block(css, ".tm-toast--swipe-return {").Should().Contain("transition-property: transform");

        var reduce = Block(css, "@media (prefers-reduced-motion: reduce)");
        reduce.Should().Contain(".tm-toast--swipe-return",
            "the swipe rollback snaps back instantly under reduced motion");
    }

    [Fact]
    public void TheDeckAndSpinner_ShipInTheCommittedBundle()
    {
        var bundle = Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css");

        bundle.Should().Contain(".tm-toast-container--stacked");
        bundle.Should().Contain(".tm-toast-spinner");
        bundle.Should().Contain(".tm-toast-container--top-center");
        bundle.Should().Contain("tmToastSpin");
    }
}
