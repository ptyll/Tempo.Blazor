using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 2 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmAgentLanes"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the lanes surface's own share of it: the partial is imported by the package entry stylesheet,
/// every class the markup emits has a rule, the state tones are knobs a modifier SETS and the base rule READS (so
/// source order cannot decide a colour), the timeline scrolls sideways inside the component instead of scrolling the
/// page, the coarse pointer grows every target to 44 px, the forced-colors block keeps the system palette in charge
/// and the surface declares NO motion — a live run repaints while it streams.
/// </para>
/// </summary>
public sealed class TmAgentLanesCssTests
{
    /// <summary>Every <c>tm-ai-agent-lanes…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-agent-lanes",
        "tm-ai-agent-lanes__header",
        "tm-ai-agent-lanes__title",
        "tm-ai-agent-lanes__toolbar",
        "tm-ai-agent-lanes__view",
        "tm-ai-agent-lanes__zoom",
        "tm-ai-agent-lanes__zoom-button",
        "tm-ai-agent-lanes__zoom-button--fit",
        "tm-ai-agent-lanes__zoom-value",
        "tm-ai-agent-lanes__empty",
        "tm-ai-agent-lanes__viewport",
        "tm-ai-agent-lanes__canvas",
        "tm-ai-agent-lanes__axis",
        "tm-ai-agent-lanes__axis-label",
        "tm-ai-agent-lanes__axis-track",
        "tm-ai-agent-lanes__tick",
        "tm-ai-agent-lanes__tick--end",
        "tm-ai-agent-lanes__tick-label",
        "tm-ai-agent-lanes__lanes",
        "tm-ai-agent-lanes__handoffs",
        "tm-ai-agent-lanes__handoff",
        "tm-ai-agent-lanes__arrow-head",
        "tm-ai-agent-lanes__lane",
        "tm-ai-agent-lanes__lane-head",
        "tm-ai-agent-lanes__lane-status",
        "tm-ai-agent-lanes__lane-status--idle",
        "tm-ai-agent-lanes__lane-status--queued",
        "tm-ai-agent-lanes__lane-status--running",
        "tm-ai-agent-lanes__lane-status--approval",
        "tm-ai-agent-lanes__lane-status--succeeded",
        "tm-ai-agent-lanes__lane-status--failed",
        "tm-ai-agent-lanes__lane-status--cancelled",
        "tm-ai-agent-lanes__lane-status-icon",
        "tm-ai-agent-lanes__lane-status-text",
        "tm-ai-agent-lanes__lane-track",
        "tm-ai-agent-lanes__block",
        "tm-ai-agent-lanes__block--idle",
        "tm-ai-agent-lanes__block--queued",
        "tm-ai-agent-lanes__block--running",
        "tm-ai-agent-lanes__block--approval",
        "tm-ai-agent-lanes__block--succeeded",
        "tm-ai-agent-lanes__block--failed",
        "tm-ai-agent-lanes__block--cancelled",
        "tm-ai-agent-lanes__block--open",
        "tm-ai-agent-lanes__block--selected",
        "tm-ai-agent-lanes__block-kind",
        "tm-ai-agent-lanes__block-label",
        "tm-ai-agent-lanes__block-status",
        "tm-ai-agent-lanes__block-marker",
        "tm-ai-agent-lanes__block-marker-icon",
        "tm-ai-agent-lanes__block-marker-text",
        "tm-ai-agent-lanes__marker",
        "tm-ai-agent-lanes__marker-icon",
        "tm-ai-agent-lanes__marker-text",
        "tm-ai-agent-lanes__marker--approval",
        "tm-ai-agent-lanes__marker--changes-requested",
        "tm-ai-agent-lanes__marker--review",
        "tm-ai-agent-lanes__now",
        "tm-ai-agent-lanes__now--end",
        "tm-ai-agent-lanes__now-label",
        "tm-ai-agent-lanes__tables",
        "tm-ai-agent-lanes__table",
        "tm-ai-agent-lanes__table-activity",
        "tm-ai-agent-lanes__table-select",
        "tm-ai-agent-lanes__detail",
    ];

    private static string LanesCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-agent-lanes.css");
        File.Exists(path).Should().BeTrue(
            "the lanes surface ships its own partial next to the components that read it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheLanesPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-agent-lanes.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = LanesCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheStateTones_AreKnobsAModifierSets_AndTheBaseRuleReads()
    {
        var css = LanesCss();

        css.Should().Contain("--tm-agent-lanes-block-tone:",
            "the tone is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("border-inline-start: 3px solid var(--tm-agent-lanes-block-tone)",
            "the block reads the knob with the tone the modifier set");

        foreach (var tone in new[] { "idle", "queued", "running", "approval", "succeeded", "failed", "cancelled" })
        {
            css.Should().Contain($".tm-ai-agent-lanes__block--{tone}",
                "the {0} state must paint its own tone", tone);
        }
    }

    [Fact]
    public void TheTimelineScrollsInsideTheComponent()
    {
        var css = LanesCss();

        css.Should().Contain("overflow-x: auto",
            "the timeline is the ONE horizontal scroll owner, so the page never scrolls sideways");
        css.Should().Contain("min-inline-size: 0",
            "without it the card's automatic minimum width grows to the timeline's content");
        css.Should().Contain("position: sticky",
            "the agent column and the time axis stay readable while the timeline scrolls");
    }

    [Fact]
    public void ACoarsePointer_GrowsEveryTargetToTheTouchTarget()
    {
        var css = LanesCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-inline-size: var(--tm-touch-target)",
            "a very short block stays a 44 px tap target");
        css.Should().Contain("inline-size: var(--tm-touch-target)",
            "the zoom buttons reach the 44 px target too");
        css.Should().Contain("min-block-size: var(--tm-touch-target)",
            "and so does the table's row action");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = LanesCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline-color: Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "a tone border collapses under high contrast, so the block keeps a system-colour edge");
        css.Should().Contain(".tm-ai-agent-lanes__block[aria-pressed=\"true\"]",
            "the picked block keeps a Highlight ring, not only a fill that high contrast removes");
    }

    [Fact]
    public void TheSurface_NeverAnimates()
    {
        var css = LanesCss();

        css.Should().NotContain("transition",
            "a live run repaints while it streams; a moving surface would animate for the whole run");
        css.Should().NotContain("animation");
    }

    [Fact]
    public void TheGeometryKnobs_AreDeclaredOnTheRoot()
    {
        var css = LanesCss();

        css.Should().Contain("--tm-agent-lanes-label-width:",
            "the component writes the knob inline from its parameter, so the partial declares its fallback");
        css.Should().Contain("--tm-agent-lanes-row-height:");
    }
}
