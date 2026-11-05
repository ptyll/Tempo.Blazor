using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 5 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmAgentHandoff"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the handoff surface's own share of it: the partial is imported by the package entry stylesheet,
/// every class the markup emits has a rule, the state tone is a knob a modifier SETS and the base rule READS, the two
/// orientations are modifiers over ONE list, a coarse pointer grows the details toggle to 44 px, the forced-colors block
/// hands the palette back to the system, and the partial animates nothing at all (so it cannot ship movement without a
/// reduced-motion block).
/// </para>
/// </summary>
public sealed class TmAgentHandoffCssTests
{
    /// <summary>Every <c>tm-ai-handoff…</c> class the markup emits statically.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-handoff",
        "tm-ai-handoff--vertical",
        "tm-ai-handoff--horizontal",
        "tm-ai-handoff__header",
        "tm-ai-handoff__title",
        "tm-ai-handoff__view",
        "tm-ai-handoff__summary",
        "tm-ai-handoff__stats",
        "tm-ai-handoff__stat",
        "tm-ai-handoff__stat-label",
        "tm-ai-handoff__stat-value",
        "tm-ai-handoff__summary-state",
        "tm-ai-handoff__empty",
        "tm-ai-handoff__table",
        "tm-ai-handoff__chain",
        "tm-ai-handoff__step",
        "tm-ai-handoff__step-head",
        "tm-ai-handoff__index",
        "tm-ai-handoff__step-title",
        "tm-ai-handoff__status",
        "tm-ai-handoff__status-icon",
        "tm-ai-handoff__status-text",
        "tm-ai-handoff__route",
        "tm-ai-handoff__agent",
        "tm-ai-handoff__agent-name",
        "tm-ai-handoff__route-arrow",
        "tm-ai-handoff__at",
        "tm-ai-handoff__step-summary",
        "tm-ai-handoff__toggle",
        "tm-ai-handoff__toggle-text",
        "tm-ai-handoff__toggle-icon",
        "tm-ai-handoff__detail",
        "tm-ai-handoff__group",
        "tm-ai-handoff__group--lost",
        "tm-ai-handoff__group-title",
        "tm-ai-handoff__group-icon",
        "tm-ai-handoff__group-text",
        "tm-ai-handoff__group-empty",
        "tm-ai-handoff__items",
        "tm-ai-handoff__item",
        "tm-ai-handoff__item--lost",
        "tm-ai-handoff__item-icon",
        "tm-ai-handoff__item-label",
        "tm-ai-handoff__item-kind",
        "tm-ai-handoff__item-reason",
        "tm-ai-handoff__connector",
        "tm-ai-handoff__lost-summary",
    ];

    private static string HandoffCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-agent-handoff.css");
        File.Exists(path).Should().BeTrue(
            "the handoff surface ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheHandoffPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-agent-handoff.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = HandoffCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheStateTones_AreKnobsAModifierSets_AndTheBaseRuleReads()
    {
        var css = HandoffCss();

        css.Should().Contain("--tm-handoff-step-tone:",
            "the tone is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("border-inline-start: calc(var(--tm-border-width) * 3) solid var(--tm-handoff-step-tone)",
            "the step reads the knob with the tone the modifier set, and its width comes from the border token");

        foreach (var tone in new[] { "pending", "accepted", "rejected", "completed" })
        {
            css.Should().Contain($".tm-ai-handoff__step--{tone}",
                "the {0} state must paint its own tone", tone);
        }

        var baseBlock = css[..css.IndexOf(".tm-ai-handoff__step--pending", StringComparison.Ordinal)];
        baseBlock.Should().Contain("--tm-handoff-step-tone: var(--tm-border-color-strong)");
    }

    [Fact]
    public void TheStatusChip_SetsItsOwnInkAndFill_ForEveryDeclaredState()
    {
        var css = HandoffCss();

        css.Should().Contain(".tm-ai-handoff__status[data-status=\"accepted\"]");
        css.Should().Contain(".tm-ai-handoff__status[data-status=\"rejected\"]");
        css.Should().Contain(".tm-ai-handoff__status[data-status=\"completed\"]");
        css.Should().Contain("--tm-handoff-status-ink:",
            "the chip's ink is a knob, so the state modifier decides it");
    }

    [Fact]
    public void TheOrientation_IsAModifierOverOneList()
    {
        var css = HandoffCss();

        css.Should().Contain(".tm-ai-handoff--horizontal .tm-ai-handoff__chain",
            "the horizontal chain is the same list laid out on its side, not a second markup shape");
        css.Should().Contain("flex-direction: row");
        css.Should().Contain("transform: rotate(-90deg)",
            "the connector turns with the chain instead of pointing the wrong way");
    }

    [Fact]
    public void BothTables_AreTheOneScrollFreeTextAlternative()
    {
        var css = HandoffCss();

        css.Should().Contain("border-collapse: collapse");
        css.Should().Contain(".tm-ai-handoff__table caption");
        css.Should().Contain("overflow-wrap: anywhere",
            "an item label or a reason may be long: it wraps instead of widening the table");
    }

    [Fact]
    public void ACoarsePointer_GrowsEveryTargetToTheTouchTarget()
    {
        var css = HandoffCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)",
            "the details toggle reaches the 44 px target");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = HandoffCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 3px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "the resting borders collapse under high contrast, so they keep a system colour");
        css.Should().Contain(".tm-ai-handoff__status",
            "the chip keeps a system border where its tinted fill would collapse");
    }

    [Fact]
    public void TheFocusRing_SurvivesEveryInteractiveSurface()
    {
        var css = HandoffCss();

        css.Should().Contain(".tm-ai-handoff__toggle:focus-visible");
        css.Should().Contain("outline: 2px solid var(--tm-border-color-focus)",
            "the ring is the surface's token, never the browser's default outline");
        css.Should().Contain("outline-offset: 2px");
    }

    [Fact]
    public void ThePartialAnimatesNothing_BecauseTheStateIsText()
    {
        var css = HandoffCss();

        css.Should().NotContain("@keyframes",
            "a state is an icon and a word, so the stylesheet has no animated subject");
        css.Should().NotContain("animation:",
            "an animation without a reduced-motion block is exactly what this guard exists to prevent");
        css.Should().NotContain("transition:",
            "a transition is movement too: either it is honest and reduced, or it is not there");
    }

    [Fact]
    public void TheLostGroup_IsNamed_AndItsInkIsAKnob()
    {
        var css = HandoffCss();

        css.Should().Contain(".tm-ai-handoff__group--lost",
            "the lost list is marked as a group, and its title carries an icon and a word");
        css.Should().Contain("--tm-handoff-lost-ink:",
            "the lost ink is a knob the forced-colors block can hand back to the system");
    }
}
