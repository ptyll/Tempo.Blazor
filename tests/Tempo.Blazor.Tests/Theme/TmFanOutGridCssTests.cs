using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 4 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmFanOutGrid"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the fan-out surface's own share of it: the partial is imported by the package entry stylesheet,
/// every class the markup emits has a rule, the state tone is a knob a modifier SETS and the base rule READS, the grid
/// is the ONE scroll owner (so a narrow card never scrolls the page), every row's cells are exactly as wide as every
/// other row's, a coarse pointer grows every target to 44 px, the forced-colors block hands the palette back to the
/// system, and the partial animates nothing at all (so it cannot ship movement without a reduced-motion block).
/// </para>
/// </summary>
public sealed class TmFanOutGridCssTests
{
    /// <summary>Every <c>tm-ai-fan-out…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-fan-out",
        "tm-ai-fan-out--compact",
        "tm-ai-fan-out__header",
        "tm-ai-fan-out__title",
        "tm-ai-fan-out__summary",
        "tm-ai-fan-out__stats",
        "tm-ai-fan-out__stat",
        "tm-ai-fan-out__stat-label",
        "tm-ai-fan-out__stat-value",
        "tm-ai-fan-out__filters",
        "tm-ai-fan-out__filter",
        "tm-ai-fan-out__filter--active",
        "tm-ai-fan-out__filter-label",
        "tm-ai-fan-out__filter-count",
        "tm-ai-fan-out__empty",
        "tm-ai-fan-out__grid",
        "tm-ai-fan-out__row",
        "tm-ai-fan-out__cell",
        "tm-ai-fan-out__tile",
        "tm-ai-fan-out__tile--idle",
        "tm-ai-fan-out__tile--queued",
        "tm-ai-fan-out__tile--running",
        "tm-ai-fan-out__tile--approval",
        "tm-ai-fan-out__tile--succeeded",
        "tm-ai-fan-out__tile--failed",
        "tm-ai-fan-out__tile--cancelled",
        "tm-ai-fan-out__tile-avatar",
        "tm-ai-fan-out__tile-initials",
        "tm-ai-fan-out__tile-text",
        "tm-ai-fan-out__tile-label",
        "tm-ai-fan-out__tile-meta",
        "tm-ai-fan-out__tile-state",
        "tm-ai-fan-out__tile-state-icon",
        "tm-ai-fan-out__tile-state-text",
        "tm-ai-fan-out__tile-eta",
        "tm-ai-fan-out__tile-worker",
        "tm-ai-fan-out__badge",
        "tm-ai-fan-out__detail",
        "tm-ai-fan-out__detail-head",
        "tm-ai-fan-out__detail-title",
        "tm-ai-fan-out__detail-state",
        "tm-ai-fan-out__detail-state-icon",
        "tm-ai-fan-out__detail-state-text",
        "tm-ai-fan-out__detail-facts",
        "tm-ai-fan-out__detail-error",
        "tm-ai-fan-out__detail-actions",
        "tm-ai-fan-out__action",
        "tm-ai-fan-out__action--danger",
    ];

    private static string FanOutCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-fan-out-grid.css");
        File.Exists(path).Should().BeTrue(
            "the fan-out surface ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheFanOutPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-fan-out-grid.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = FanOutCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheStateTones_AreKnobsAModifierSets_AndTheBaseRuleReads()
    {
        var css = FanOutCss();

        css.Should().Contain("--tm-fan-out-tile-tone:",
            "the tone is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("border-inline-start: 3px solid var(--tm-fan-out-tile-tone)",
            "the tile reads the knob with the tone the modifier set");

        foreach (var tone in new[] { "idle", "queued", "running", "approval", "succeeded", "failed", "cancelled" })
        {
            css.Should().Contain($".tm-ai-fan-out__tile--{tone}", "the {0} state must paint its own tone", tone);
        }

        var baseBlock = css[..css.IndexOf(".tm-ai-fan-out__tile--idle", StringComparison.Ordinal)];
        baseBlock.Should().Contain("--tm-fan-out-tile-tone: var(--tm-border-color-strong)");
    }

    [Fact]
    public void TheGridIsTheOneScrollOwner_AndEveryRowLinesUp()
    {
        var css = FanOutCss();

        css.Should().Contain("overflow-x: auto",
            "the grid scrolls its tiles inside the card, so the page never scrolls sideways");
        css.Should().Contain("min-inline-size: 0",
            "without it the card's automatic minimum width grows to the grid's content");
        css.Should().Contain("--tm-fan-out-columns",
            "the column count is a knob the component writes inline, so the C# window and the CSS agree");
        css.Should().Contain("max(",
            "a cell keeps a usable minimum on a narrow container instead of squeezing its label to nothing");
    }

    [Fact]
    public void ACoarsePointer_GrowsEveryTargetToTheTouchTarget()
    {
        var css = FanOutCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)",
            "the filter chips, the actions and the tiles reach the 44 px target");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = FanOutCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 3px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "the resting borders collapse under high contrast, so they keep a system colour");
        css.Should().Contain(".tm-ai-fan-out__filter--active",
            "the pressed filter keeps a Highlight mark, not only a fill that high contrast removes");
    }

    [Fact]
    public void TheFocusRing_SurvivesEveryInteractiveSurface()
    {
        var css = FanOutCss();

        css.Should().Contain(".tm-ai-fan-out__tile:focus-visible");
        css.Should().Contain(".tm-ai-fan-out__filter:focus-visible");
        css.Should().Contain(".tm-ai-fan-out__action:focus-visible");
    }

    [Fact]
    public void TheFilterChipPaintsItsOwnFocusRing_NotTheBrowsersDefault()
    {
        var css = FanOutCss();
        var start = css.IndexOf(".tm-ai-fan-out__filter:focus-visible {", StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1,
            "the filter chip is a real button, so it owes the same ring the tiles and the actions paint");

        var block = css[start..css.IndexOf('}', start)];

        block.Should().Contain("outline: 2px solid var(--tm-border-color-focus)",
            "the ring is the surface's token, never the browser's default outline");
        block.Should().Contain("outline-offset: 2px");
    }

    [Fact]
    public void ThePartialAnimatesNothing_BecauseTheCountdownIsText()
    {
        var css = FanOutCss();

        css.Should().NotContain("@keyframes",
            "the live countdown is TEXT the component repaints on its clock, so the stylesheet has no animated subject");
        css.Should().NotContain("animation:",
            "an animation without a reduced-motion block is exactly what this guard exists to prevent");
        css.Should().NotContain("transition:",
            "a transition is movement too: either it is honest and reduced, or it is not there");
    }

    [Fact]
    public void TheAvatarColourComesFromTheSharedAgentPalette()
    {
        var css = FanOutCss();

        css.Should().Contain("--tm-fan-out-tile-avatar",
            "the avatar fill is a knob the component writes from the worker's own token");
        css.Should().NotContain("--tm-agent-color-0",
            "the eight slots are written INLINE from the shared palette, not duplicated as eight selectors");
    }
}
