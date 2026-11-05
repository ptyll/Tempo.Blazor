using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 3 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmAgentGraph"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the graph surface's own share of it: the partial is imported by the package entry stylesheet,
/// every class the markup emits has a rule, the state tone is a knob a modifier SETS and the base rule READS (so
/// source order cannot decide a colour), the drawing scrolls inside the component instead of scrolling the page, a
/// coarse pointer grows every target to 44 px, the forced-colors block hands the palette back to the system, the eight
/// agent slots are real palette tokens, and the ONE animated subject (the call pulse) has a reduced-motion block that
/// removes the movement while keeping the highlight.
/// </para>
/// </summary>
public sealed class TmAgentGraphCssTests
{
    /// <summary>Every <c>tm-ai-agent-graph…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-agent-graph",
        "tm-ai-agent-graph__header",
        "tm-ai-agent-graph__title",
        "tm-ai-agent-graph__toolbar",
        "tm-ai-agent-graph__view",
        "tm-ai-agent-graph__zoom",
        "tm-ai-agent-graph__zoom-button",
        "tm-ai-agent-graph__zoom-button--fit",
        "tm-ai-agent-graph__zoom-value",
        "tm-ai-agent-graph__empty",
        "tm-ai-agent-graph__tables",
        "tm-ai-agent-graph__table",
        "tm-ai-agent-graph__table-node",
        "tm-ai-agent-graph__table-select",
        "tm-ai-agent-graph__detail",
        "tm-ai-agent-graph__viewport",
        "tm-ai-agent-graph__canvas",
        "tm-ai-agent-graph__groups",
        "tm-ai-agent-graph__group",
        "tm-ai-agent-graph__group-box",
        "tm-ai-agent-graph__group-label",
        "tm-ai-agent-graph__edges",
        "tm-ai-agent-graph__edge",
        "tm-ai-agent-graph__edge--back",
        "tm-ai-agent-graph__edge--active",
        "tm-ai-agent-graph__edge--pulse",
        "tm-ai-agent-graph__edge--highlighted",
        "tm-ai-agent-graph__edge--dimmed",
        "tm-ai-agent-graph__arrow-head",
        "tm-ai-agent-graph__node",
        "tm-ai-agent-graph__node--idle",
        "tm-ai-agent-graph__node--queued",
        "tm-ai-agent-graph__node--running",
        "tm-ai-agent-graph__node--approval",
        "tm-ai-agent-graph__node--succeeded",
        "tm-ai-agent-graph__node--failed",
        "tm-ai-agent-graph__node--cancelled",
        "tm-ai-agent-graph__node--pinned",
        "tm-ai-agent-graph__node--selected",
        "tm-ai-agent-graph__node--neighbor",
        "tm-ai-agent-graph__node--dimmed",
        "tm-ai-agent-graph__node-body",
        "tm-ai-agent-graph__node-avatar",
        "tm-ai-agent-graph__node-initials",
        "tm-ai-agent-graph__node-kind-icon",
        "tm-ai-agent-graph__node-text",
        "tm-ai-agent-graph__node-label",
        "tm-ai-agent-graph__node-state",
        "tm-ai-agent-graph__node-state-icon",
        "tm-ai-agent-graph__node-state-text",
    ];

    private static string GraphCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-agent-graph.css");
        File.Exists(path).Should().BeTrue(
            "the graph surface ships its own partial next to the components that read it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheGraphPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-agent-graph.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = GraphCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheStateTones_AreKnobsAModifierSets_AndTheBaseRuleReads()
    {
        var css = GraphCss();

        css.Should().Contain("--tm-agent-graph-node-tone:",
            "the tone is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("border-inline-start: 4px solid var(--tm-agent-graph-node-tone)",
            "the node reads the knob with the tone the modifier set");

        foreach (var tone in new[] { "idle", "queued", "running", "approval", "succeeded", "failed", "cancelled" })
        {
            css.Should().Contain($".tm-ai-agent-graph__node--{tone}",
                "the {0} state must paint its own tone", tone);
        }

        // The base rule must not repeat the default AFTER the modifiers: the knob's own default lives in the base
        // block, so a modifier declared later always wins.
        var baseBlock = css[..css.IndexOf(".tm-ai-agent-graph__node--idle", StringComparison.Ordinal)];
        baseBlock.Should().Contain("--tm-agent-graph-node-tone: var(--tm-border-color-strong)");
    }

    [Fact]
    public void TheDrawingScrollsInsideTheComponent()
    {
        var css = GraphCss();

        css.Should().Contain("overflow: auto",
            "the drawing is the ONE horizontal scroll owner, so the page never scrolls sideways");
        css.Should().Contain("min-inline-size: 0",
            "without it the card's automatic minimum width grows to the drawing's content");
        css.Should().Contain("max-block-size: var(--tm-agent-graph-max-block-size)",
            "a tall graph scrolls inside the card instead of pushing the page down");
    }

    [Fact]
    public void TheBoxesAndTheCurves_NeverStealAClick()
    {
        var css = GraphCss();

        css.Should().Contain("pointer-events: none",
            "the group boxes and the curves are decoration; the nodes carry the interaction");
    }

    [Fact]
    public void ACoarsePointer_GrowsEveryTargetToTheTouchTarget()
    {
        var css = GraphCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("inline-size: var(--tm-touch-target)",
            "the zoom buttons reach the 44 px target");
        css.Should().Contain("min-block-size: var(--tm-touch-target)",
            "and so do the table's row actions");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = GraphCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline-color: Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "the connection and group strokes collapse under high contrast, so they keep a system colour");
        css.Should().Contain(".tm-ai-agent-graph__node[aria-pressed=\"true\"]",
            "the picked node keeps a Highlight mark, not only a fill that high contrast removes");
        css.Should().Contain("border: 2px solid Highlight",
            "and so does the node that needs the reader; the mark is a border, so the outline stays the focus ring");
    }

    [Fact]
    public void TheCallPulse_IsTheOnlyAnimatedSubject_AndReducedMotionKeepsTheHighlight()
    {
        var css = GraphCss();

        css.Should().Contain("@keyframes tm-agent-graph-pulse");
        css.Should().Contain("animation: tm-agent-graph-pulse var(--tm-agent-graph-pulse-duration)",
            "the movement is bound to the same duration the component's pulse window uses");
        css.Should().Contain("--tm-agent-graph-pulse-duration:",
            "the knob is declared as the stylesheet's fallback; the component writes PulseDuration's value inline");

        var reduce = css[css.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal)..];

        reduce.Should().Contain(".tm-ai-agent-graph__edge--pulse", "the pulse is the subject of the reduce block");
        reduce.Should().Contain("animation: none", "the movement is what a reduce reader asked to be spared");
        reduce.Should().Contain("stroke-dasharray", "and the highlight stays: a still dashed overlay still marks it");
        reduce.Should().NotContain("stroke-opacity: 0", "an invisible pulse would tell the reader nothing happened");
    }

    [Fact]
    public void ForcedColors_SeparateFocusFromSelection_AndKeepTheArrowDirection()
    {
        var css = GraphCss();
        var block = css[css.IndexOf("@media (forced-colors: active)", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal)];

        block.Should().Contain(".tm-ai-agent-graph__node:focus-visible",
            "a node's own focus ring has to be handed to the system palette like every other ring");
        block.Should().Contain("outline: 3px solid Highlight",
            "focus must look DIFFERENT from selection, which wears the Highlight border");
        block.Should().Contain("outline: none",
            "a selected or approval node must never wear the focus ring on top of its Highlight border");
        block.Should().Contain(".tm-ai-agent-graph__arrow-head",
            "an SVG fill is not remapped by the system palette, so the arrow head would lose its direction cue");
        block.Should().Contain("fill: CanvasText", "the arrow head keeps a system colour next to the CanvasText curve");
        block.Should().Contain(".tm-ai-agent-graph__edge--highlighted",
            "the highlighted connection keeps a system colour too");
        block.Should().Contain(".tm-ai-agent-graph__edge--dimmed",
            "opacity over system colours lowers contrast unpredictably, so the dim is dropped here");
    }

    [Fact]
    public void ADimmedNode_StaysAReadableButton()
    {
        var css = GraphCss();
        var block = css[css.IndexOf(".tm-ai-agent-graph__node--dimmed", StringComparison.Ordinal)..];
        block = block[..block.IndexOf('}', StringComparison.Ordinal)];

        block.Should().Contain("background-color: var(--tm-bg-surface-secondary)",
            "the dim is a token change, not an opacity that pushes the label below 4.5:1");
        block.Should().Contain("border-color: var(--tm-border-color)",
            "the resting border is the neutral token of the surface");
        block.Should().NotContain("opacity",
            "a dimmed node is still a focusable, active button, so the inactive-component exemption does not apply");
    }

    [Fact]
    public void TheEightAgentSlots_AreRealPaletteTokens()
    {
        var css = GraphCss();

        for (var slot = 0; slot < 8; slot++)
        {
            css.Should().Contain($".tm-ai-agent-graph__node[data-color=\"{slot}\"]",
                "slot {0} paints the agent's own palette colour", slot);
            css.Should().Contain($"var(--tm-agent-color-{slot})");
        }

        css.Should().Contain("var(--tm-agent-color-ink)",
            "the initials sit on the palette colour, so the ink comes from the shared palette");
    }

    [Fact]
    public void TheGroupLabel_IsHtmlNotSvgText()
    {
        var css = GraphCss();
        var block = css[css.IndexOf(".tm-ai-agent-graph__group-label", StringComparison.Ordinal)..];
        block = block[..block.IndexOf('}', StringComparison.Ordinal)];

        block.Should().Contain("color:", "the label lives in a foreignObject, where colour is a CSS colour");
        block.Should().NotContain("fill:", "an SVG presentation attribute would not apply to HTML text");
    }

    [Fact]
    public void TheNodeIsStyledAsANativeControl()
    {
        var css = GraphCss();
        var block = css[css.IndexOf(".tm-ai-agent-graph__node {", StringComparison.Ordinal)..];
        block = block[..block.IndexOf('}', StringComparison.Ordinal)];

        block.Should().Contain("cursor: pointer");
        block.Should().Contain("font-family: var(--tm-font-sans)", "an HTML button inherits no SVG font");
        block.Should().Contain("box-sizing: border-box",
            "the inline size the component writes must be the box the layout computed");
    }
}
