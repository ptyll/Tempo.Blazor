using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 7 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmDelegateBoard"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the delegate board's own share of it: the partial is imported by the package entry stylesheet,
/// every class the markup emits has a rule, a priority and an availability state are knobs a modifier SETS and the base
/// rule READS (so colour is never the only carrier and source order cannot decide a state), the Assign menu carries a
/// real focus ring, a coarse pointer reaches the 44 px target, the forced-colors block hands the palette back to the
/// system, and the partial animates nothing at all.
/// </para>
/// </summary>
public sealed class TmDelegateBoardCssTests
{
    /// <summary>Every <c>tm-ai-delegate…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-delegate",
        "tm-ai-delegate__header",
        "tm-ai-delegate__title",
        "tm-ai-delegate__summary",
        "tm-ai-delegate__auto",
        "tm-ai-delegate__hint",
        "tm-ai-delegate__instructions",
        "tm-ai-delegate__notice",
        "tm-ai-delegate__board",
        "tm-ai-delegate__column",
        "tm-ai-delegate__column--backlog",
        "tm-ai-delegate__column--agent",
        "tm-ai-delegate__column--full",
        "tm-ai-delegate__column-head",
        "tm-ai-delegate__column-title",
        "tm-ai-delegate__column-meta",
        "tm-ai-delegate__capacity",
        "tm-ai-delegate__separator",
        "tm-ai-delegate__queued",
        "tm-ai-delegate__availability",
        "tm-ai-delegate__full",
        "tm-ai-delegate__full-icon",
        "tm-ai-delegate__full-text",
        "tm-ai-delegate__task",
        "tm-ai-delegate__task-head",
        "tm-ai-delegate__task-title",
        "tm-ai-delegate__priority",
        "tm-ai-delegate__priority--low",
        "tm-ai-delegate__priority--normal",
        "tm-ai-delegate__priority--high",
        "tm-ai-delegate__priority--urgent",
        "tm-ai-delegate__priority-icon",
        "tm-ai-delegate__priority-text",
        "tm-ai-delegate__task-meta",
        "tm-ai-delegate__task-icon",
        "tm-ai-delegate__task-actions",
        "tm-ai-delegate__assign",
        "tm-ai-delegate__assign-trigger",
        "tm-ai-delegate__empty",
        "tm-ai-delegate__empty--agents",
    ];

    private static string BoardCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-delegate-board.css");
        File.Exists(path).Should().BeTrue(
            "the delegate board ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheBoardPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-delegate-board.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = BoardCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void APriorityIsOneKnobAModifierSets_AndTheBaseRuleReads()
    {
        var css = BoardCss();

        css.Should().Contain("--tm-delegate-priority-ink:",
            "the tone is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("color: var(--tm-delegate-priority-ink)");
        css.Should().Contain("background-color: var(--tm-delegate-priority-fill)");

        foreach (var grade in new[] { "low", "normal", "high", "urgent" })
        {
            css.Should().Contain($".tm-ai-delegate__priority--{grade}",
                "the {0} grade must paint its own tone", grade);
        }
    }

    [Fact]
    public void TheAvailabilityLine_StatesItsOwnToneThroughAStateAttribute()
    {
        var css = BoardCss();

        css.Should().Contain(".tm-ai-delegate__availability[data-state='busy']");
        css.Should().Contain(".tm-ai-delegate__availability[data-state='preview']");
        css.Should().Contain("--tm-delegate-availability-ink:",
            "the projection changes the ink through a knob, not through a copy of the base rule");
    }

    [Fact]
    public void TheFullColumn_CarriesAWordNotJustATone()
    {
        var css = BoardCss();

        css.Should().Contain(".tm-ai-delegate__full-text");
        css.Should().Contain(".tm-ai-delegate__column--full",
            "the column frame agrees with the word inside it");
    }

    [Fact]
    public void AFullDragTarget_IsRefusedWithoutColourAlone()
    {
        const string selector = ".tm-ai-delegate__column[data-drag-over='true'][data-full='true']";
        var css = BoardCss();
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "a full target is still marked, so the reader learns why the drop was refused");

        var end = css.IndexOf('}', start);
        end.Should().BeGreaterThan(start);
        css[start..end].Should().Contain("border-style: dashed",
            "the refusal must carry a cue that does not depend on colour (WCAG 1.4.1)");
    }

    [Fact]
    public void TheAssignMenu_CarriesARealFocusRing()
    {
        var css = BoardCss();

        css.Should().Contain(".tm-ai-delegate__assign-trigger:focus-visible");
        css.Should().Contain(".tm-ai-delegate__assign:focus-within",
            "the wrapper the menu renders is the tab stop, so it needs the ring too");
        css.Should().Contain("outline: var(--tm-border-width) solid var(--tm-border-color-focus)");
    }

    [Fact]
    public void ACoarsePointer_GrowsTheAssignTargetToTheTouchTarget()
    {
        var css = BoardCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = BoardCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 2px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "the resting borders collapse under high contrast, so they keep a system colour");
    }

    [Fact]
    public void ThePartialAnimatesNothing_BecauseEveryStateIsAlsoText()
    {
        var css = BoardCss();

        css.Should().NotContain("@keyframes");
        css.Should().NotContain("animation:");
        css.Should().NotContain("transition:",
            "a transition is movement too: either it is honest and reduced, or it is not there");
    }

    [Fact]
    public void TheLongTexts_WrapInsteadOfScrollingThePage()
    {
        var css = BoardCss();

        css.Should().Contain("overflow-wrap: anywhere",
            "a task title is a sentence and a long one must wrap inside the card");
        css.Should().Contain("min-inline-size: 0",
            "a board in a grid column must not grow to its content and scroll the page sideways");
    }
}
