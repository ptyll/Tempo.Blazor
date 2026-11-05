using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 5 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmFallbackChain"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the fallback surface's own share of it: the partial is imported by the package entry stylesheet,
/// every class the markup emits has a rule, the result tone is a knob a modifier SETS and the base rule READS, the
/// Answer node is tinted only when it was actually reached, the editor's native select and buttons carry real focus
/// rings and 44 px coarse targets, the forced-colors block hands the palette back to the system, and the partial
/// animates nothing at all.
/// </para>
/// </summary>
public sealed class TmFallbackChainCssTests
{
    /// <summary>Every <c>tm-ai-fallback…</c> class the markup emits statically.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-fallback",
        "tm-ai-fallback--edit",
        "tm-ai-fallback__header",
        "tm-ai-fallback__title",
        "tm-ai-fallback__view",
        "tm-ai-fallback__policy",
        "tm-ai-fallback__policy-label",
        "tm-ai-fallback__policy-value",
        "tm-ai-fallback__flow",
        "tm-ai-fallback__node",
        "tm-ai-fallback__node-head",
        "tm-ai-fallback__node-position",
        "tm-ai-fallback__node-label",
        "tm-ai-fallback__node-state",
        "tm-ai-fallback__node-state-icon",
        "tm-ai-fallback__node-state-text",
        "tm-ai-fallback__node-reason",
        "tm-ai-fallback__node-reason-label",
        "tm-ai-fallback__node-reason-text",
        "tm-ai-fallback__node-facts",
        "tm-ai-fallback__arrow",
        "tm-ai-fallback__answer-arrow",
        "tm-ai-fallback__answer",
        "tm-ai-fallback__answer-icon",
        "tm-ai-fallback__answer-text",
        "tm-ai-fallback__answer-label",
        "tm-ai-fallback__answer-model",
        "tm-ai-fallback__editor",
        "tm-ai-fallback__editor-hint",
        "tm-ai-fallback__editor-empty",
        "tm-ai-fallback__editor-list",
        "tm-ai-fallback__editor-row",
        "tm-ai-fallback__editor-row-label",
        "tm-ai-fallback__editor-row-id",
        "tm-ai-fallback__editor-remove",
        "tm-ai-fallback__editor-add",
        "tm-ai-fallback__editor-add-label",
        "tm-ai-fallback__editor-select",
        "tm-ai-fallback__editor-add-button",
        "tm-ai-fallback__editor-note",
        "tm-ai-fallback__editor-policy",
        "tm-ai-fallback__table",
        "tm-ai-fallback__empty",
    ];

    private static string FallbackCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-fallback-chain.css");
        File.Exists(path).Should().BeTrue(
            "the fallback surface ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheFallbackPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-fallback-chain.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = FallbackCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheResultTones_AreKnobsAModifierSets_AndTheBaseRuleReads()
    {
        var css = FallbackCss();

        css.Should().Contain("--tm-fallback-node-tone:",
            "the tone is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("border-inline-start: calc(var(--tm-border-width) * 3) solid var(--tm-fallback-node-tone)",
            "the node reads the knob with the tone the modifier set, and its width comes from the border token");

        foreach (var tone in new[] { "not-tried", "skipped", "failed", "succeeded" })
        {
            css.Should().Contain($".tm-ai-fallback__node--{tone}",
                "the {0} result must paint its own tone", tone);
        }

        var baseBlock = css[..css.IndexOf(".tm-ai-fallback__node--not-tried", StringComparison.Ordinal)];
        baseBlock.Should().Contain("--tm-fallback-node-tone: var(--tm-border-color-strong)");
    }

    [Fact]
    public void TheStatusChip_SetsItsOwnInkAndFill_ForEveryOutcome()
    {
        var css = FallbackCss();

        css.Should().Contain(".tm-ai-fallback__node-state[data-status=\"skipped\"]");
        css.Should().Contain(".tm-ai-fallback__node-state[data-status=\"failed\"]");
        css.Should().Contain(".tm-ai-fallback__node-state[data-status=\"succeeded\"]");
        css.Should().Contain("--tm-fallback-status-ink:");
    }

    [Fact]
    public void TheAnswerNode_IsTintedOnlyWhenItWasReached()
    {
        var css = FallbackCss();

        css.Should().Contain(".tm-ai-fallback__answer[data-answered=\"true\"]",
            "an unanswered chain must not wear the success tone");
        css.Should().Contain(".tm-ai-fallback__answer-arrow[data-success=\"true\"]",
            "the branch to the answer is toned by the same fact");
        css.Should().Contain(".tm-ai-fallback__answer[data-answered=\"false\"]");
    }

    [Fact]
    public void TheArrowsAreDecorativeSvg_ThatFollowsTheKnob()
    {
        var css = FallbackCss();

        css.Should().Contain(".tm-ai-fallback__arrow svg path");
        css.Should().Contain("stroke: currentColor",
            "the arrow takes its colour from the token the rule above it set");
        css.Should().Contain("--tm-fallback-arrow-size:",
            "the arrow's length is a knob, not a magic number in two places");
    }

    [Fact]
    public void TheEditorUsesTheNativeControls_WithRealFocusRings()
    {
        var css = FallbackCss();

        css.Should().Contain(".tm-ai-fallback__editor-select:focus-visible");
        css.Should().Contain(".tm-ai-fallback__editor-add-button:focus-visible");
        css.Should().Contain(".tm-ai-fallback__editor-remove:focus-visible");
        css.Should().Contain(".tm-ai-fallback__editor-add-button:disabled",
            "a disabled Add must look disabled, not merely do nothing");
    }

    [Fact]
    public void ACoarsePointer_GrowsEveryTargetToTheTouchTarget()
    {
        var css = FallbackCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)",
            "the remove button, the add button and the model select reach the 44 px target");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = FallbackCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 3px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "the resting borders collapse under high contrast, so they keep a system colour");
        css.Should().Contain("color: Highlight",
            "the success arrow keeps a system mark where its tone would collapse");
    }

    [Fact]
    public void ThePartialAnimatesNothing_BecauseTheOutcomeIsText()
    {
        var css = FallbackCss();

        css.Should().NotContain("@keyframes");
        css.Should().NotContain("animation:");
        css.Should().NotContain("transition:",
            "a transition is movement too: either it is honest and reduced, or it is not there");
    }

    [Fact]
    public void TheTable_IsTheTextAlternative()
    {
        var css = FallbackCss();

        css.Should().Contain("border-collapse: collapse");
        css.Should().Contain(".tm-ai-fallback__table caption");
        css.Should().Contain("overflow-wrap: anywhere");
    }
}
