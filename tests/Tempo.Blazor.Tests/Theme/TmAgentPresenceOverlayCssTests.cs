using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 8 — the stylesheet contract of
/// <see cref="Tempo.Blazor.Components.AI.TmAgentPresenceOverlay"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the overlay's own share of it: the partial is imported by the package entry stylesheet, every class
/// the markup emits has a rule, the layer is a non-interactive picture above the content, the runtime knobs the component
/// and module write have declared defaults, an anchored marker stays invisible until it has been placed, a coarse
/// pointer reaches the 44 px target, forced colors hand the palette back to the system, and the ONE animation (a moving
/// cursor) is suppressed by a prefers-reduced-motion block.
/// </para>
/// </summary>
public sealed class TmAgentPresenceOverlayCssTests
{
    /// <summary>Every <c>tm-ai-presence…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-presence",
        "tm-ai-presence__content",
        "tm-ai-presence__layer",
        "tm-ai-presence__marker",
        "tm-ai-presence__cursor",
        "tm-ai-presence__pointer",
        "tm-ai-presence__label",
        "tm-ai-presence__name",
        "tm-ai-presence__activity",
        "tm-ai-presence__roster",
        "tm-ai-presence__roster-title",
        "tm-ai-presence__roster-empty",
        "tm-ai-presence__roster-list",
        "tm-ai-presence__roster-item",
        "tm-ai-presence__roster-activity",
    ];

    private static string PresenceCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-agent-presence.css");
        File.Exists(path).Should().BeTrue(
            "the overlay ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsThePresencePartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-agent-presence.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = PresenceCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheLayerIsAPictureAboveTheContent_AndNeverEatsThePointer()
    {
        var css = PresenceCss();

        css.Should().Contain(".tm-ai-presence__layer");
        css.Should().Contain("position: absolute");
        css.Should().Contain("pointer-events: none",
            "a cursor layer must never intercept a press meant for the editor underneath");
    }

    [Fact]
    public void TheRuntimeKnobsHaveDeclaredDefaults()
    {
        var css = PresenceCss();

        css.Should().Contain("--tm-presence-x: 0%");
        css.Should().Contain("--tm-presence-y: 0%");
        css.Should().Contain("--tm-presence-color: var(--tm-color-primary)",
            "a cursor's colour comes from the shared agent palette, written inline per marker");
        css.Should().Contain("inset-inline-start: var(--tm-presence-x)");
        css.Should().Contain("inset-block-start: var(--tm-presence-y)");
    }

    [Fact]
    public void AnAnchoredMarkerIsInvisibleUntilItHasBeenMeasured()
    {
        var css = PresenceCss();

        css.Should().Contain(".tm-ai-presence__marker[data-presence-anchor]:not([data-presence-placed=\"true\"])");
        css.Should().Contain("opacity: 0",
            "a cursor whose element was not measured must not sit at the content's origin");
    }

    [Fact]
    public void ALabelNearTheInlineEnd_IsMirroredInsteadOfWideningThePage()
    {
        var css = PresenceCss();

        css.Should().Contain(".tm-ai-presence__marker[data-presence-flip=\"true\"]");
        css.Should().Contain("transform: translateX(-100%)",
            "the mirrored marker's inline end sits on the measured point");
        css.Should().Contain("overflow: clip",
            "the layer is the last guard: it can never widen the page, even before the measurement lands");
    }

    [Fact]
    public void TheMovingCursorIsTheOneAnimation_AndReducedMotionTurnsItIntoAJump()
    {
        var css = PresenceCss();

        css.Should().Contain("transition:");
        css.Should().Contain("@media (prefers-reduced-motion: reduce)");
        css.Should().Contain("transition: none",
            "a reader who asked for less motion gets the finished position at once");
        css.Should().NotContain("@keyframes", "a presence cursor travels; it never loops");
    }

    [Fact]
    public void TheActivityToneIsASecondCueNextToTheWord()
    {
        var css = PresenceCss();

        css.Should().Contain(".tm-ai-presence__marker[data-activity=\"editing\"]");
        css.Should().Contain(".tm-ai-presence__marker[data-activity=\"commenting\"]");
        css.Should().Contain(".tm-ai-presence__activity",
            "the label always names the activity; the tone only agrees with it");
    }

    [Fact]
    public void ACoarsePointer_GrowsTheRosterRowToTheTouchTarget()
    {
        var css = PresenceCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
        css.Should().Contain(".tm-ai-presence__roster-item");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = PresenceCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("CanvasText");
        css.Should().Contain("Highlight");
        css.Should().Contain("forced-color-adjust: none",
            "the system palette is the source, so no token colour may survive it");
    }

    [Fact]
    public void TheContentNeverOverflowsItsHost()
    {
        var css = PresenceCss();

        css.Should().Contain("min-inline-size: 0");
        css.Should().Contain("max-inline-size: 14rem",
            "a long agent name is elided inside the label instead of stretching the page");
        css.Should().Contain("text-overflow: ellipsis");
    }
}
