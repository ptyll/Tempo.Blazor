using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 6 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmMemoryPanel"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the memory surface's own share of it: the partial is imported by the package entry stylesheet, every
/// class the markup emits has a rule, a status is a knob a row modifier SETS and the base rule READS (so colour is never
/// the only carrier and source order cannot decide a state), the native buttons and the edit box carry real focus rings,
/// a coarse pointer reaches the 44 px target, the forced-colors block hands the palette back to the system, and the
/// partial animates nothing at all.
/// </para>
/// </summary>
public sealed class TmMemoryPanelCssTests
{
    /// <summary>Every <c>tm-ai-memory…</c> class the markup emits statically.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-memory",
        "tm-ai-memory--off",
        "tm-ai-memory__header",
        "tm-ai-memory__title",
        "tm-ai-memory__switch",
        "tm-ai-memory__off-note",
        "tm-ai-memory__toolbar",
        "tm-ai-memory__search",
        "tm-ai-memory__filter",
        "tm-ai-memory__bulk",
        "tm-ai-memory__bulk-count",
        "tm-ai-memory__bulk-actions",
        "tm-ai-memory__bulk-action",
        "tm-ai-memory__groups",
        "tm-ai-memory__group",
        "tm-ai-memory__group-head",
        "tm-ai-memory__group-title",
        "tm-ai-memory__group-toggle",
        "tm-ai-memory__group-chevron",
        "tm-ai-memory__group-name",
        "tm-ai-memory__group-count",
        "tm-ai-memory__group-select",
        "tm-ai-memory__list",
        "tm-ai-memory__item",
        "tm-ai-memory__select",
        "tm-ai-memory__select-hit",
        "tm-ai-memory__group-select-hit",
        "tm-ai-memory__body",
        "tm-ai-memory__text",
        "tm-ai-memory__edit",
        "tm-ai-memory__edit-box",
        "tm-ai-memory__edit-actions",
        "tm-ai-memory__save",
        "tm-ai-memory__cancel",
        "tm-ai-memory__meta",
        "tm-ai-memory__status",
        "tm-ai-memory__status-icon",
        "tm-ai-memory__status-text",
        "tm-ai-memory__confidence",
        "tm-ai-memory__source",
        "tm-ai-memory__updated",
        "tm-ai-memory__actions",
        "tm-ai-memory__action",
        "tm-ai-memory__busy",
        "tm-ai-memory__empty",
        "tm-ai-memory__undo",
        "tm-ai-memory__undo-text",
        "tm-ai-memory__undo-action",
        "tm-ai-memory__undo-timer",
        "tm-ai-memory__error",
    ];

    private static string MemoryCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-memory-panel.css");
        File.Exists(path).Should().BeTrue(
            "the memory surface ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheMemoryPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-memory-panel.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = MemoryCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void AStatusIsOneKnobARowModifierSets_AndTheBaseRuleReads()
    {
        var css = MemoryCss();

        css.Should().Contain("--tm-memory-status-ink:",
            "the status colour is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("color: var(--tm-memory-status-ink)");

        foreach (var status in new[] { "active", "paused", "pending-review", "forgotten" })
        {
            css.Should().Contain($".tm-ai-memory__status[data-status=\"{status}\"]",
                "the {0} status must paint its own tone", status);
        }
    }

    [Fact]
    public void AForgottenMemory_IsStillReadable_BecauseTheUndoWindowNeedsIt()
    {
        var css = MemoryCss();

        css.Should().Contain(".tm-ai-memory__item[data-status=\"forgotten\"]");
        css.Should().Contain(".tm-ai-memory__item[data-status=\"forgotten\"] .tm-ai-memory__text");
        css.Should().Contain("text-decoration: line-through",
            "the FACT is struck through — a second, non-colour cue on top of the status word");
    }

    [Fact]
    public void TheNativeControls_CarryRealFocusRings()
    {
        var css = MemoryCss();

        css.Should().Contain(".tm-ai-memory__group-toggle:focus-visible");
        css.Should().Contain(".tm-ai-memory__action:focus-visible");
        css.Should().Contain(".tm-ai-memory__edit-box:focus-visible");
        css.Should().Contain(".tm-ai-memory__search:focus-visible");
        css.Should().Contain(".tm-ai-memory__undo-action:focus-visible");
        css.Should().Contain(".tm-ai-memory__action:disabled",
            "an unavailable action must look unavailable, not merely do nothing");
    }

    [Fact]
    public void ACoarsePointer_GrowsEveryTargetToTheTouchTarget()
    {
        var css = MemoryCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
        css.Should().Contain(".tm-ai-memory__select-hit", "the LABEL is the target, the checkbox stays a checkbox");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = MemoryCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 3px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "the resting borders collapse under high contrast, so they keep a system colour");
    }

    [Fact]
    public void ThePartialAnimatesNothing_BecauseEveryStateIsAlsoText()
    {
        var css = MemoryCss();

        css.Should().NotContain("@keyframes");
        css.Should().NotContain("animation:");
        css.Should().NotContain("transition:",
            "a transition is movement too: either it is honest and reduced, or it is not there");
    }

    [Fact]
    public void TheLongTexts_WrapInsteadOfScrollingThePage()
    {
        var css = MemoryCss();

        css.Should().Contain("overflow-wrap: anywhere",
            "a memory is a sentence and a long one must wrap inside the card");
        css.Should().Contain("min-inline-size: 0",
            "a card in a grid column must not grow to its content and scroll the page sideways");
    }
}
