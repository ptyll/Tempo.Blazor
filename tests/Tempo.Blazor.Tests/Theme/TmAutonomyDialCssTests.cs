using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 6 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmAutonomyDial"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the dial's own share of it: the partial is imported by the package entry stylesheet, every class
/// the markup emits has a rule, the track's fill is one knob the component writes and the rule reads, an unavailable
/// stop is dimmed WITHOUT being the only carrier of the ceiling, the slider and the confirmation carry real focus rings,
/// a coarse pointer reaches the 44 px target, the forced-colors block hands the palette back to the system, and the only
/// movement in the file is reduced away.
/// </para>
/// </summary>
public sealed class TmAutonomyDialCssTests
{
    /// <summary>Every <c>tm-ai-autonomy…</c> class the markup emits statically.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-autonomy",
        "tm-ai-autonomy__header",
        "tm-ai-autonomy__title",
        "tm-ai-autonomy__readout",
        "tm-ai-autonomy__current",
        "tm-ai-autonomy__position",
        "tm-ai-autonomy__description",
        "tm-ai-autonomy__dial",
        "tm-ai-autonomy__control",
        "tm-ai-autonomy__rail",
        "tm-ai-autonomy__fill",
        "tm-ai-autonomy__stop",
        "tm-ai-autonomy__stop-dot",
        "tm-ai-autonomy__step",
        "tm-ai-autonomy__segmented",
        "tm-ai-autonomy__limit",
        "tm-ai-autonomy__limit-reason",
        "tm-ai-autonomy__locks",
        "tm-ai-autonomy__locks-title",
        "tm-ai-autonomy__lock",
        "tm-ai-autonomy__lock-icon",
        "tm-ai-autonomy__lock-action",
        "tm-ai-autonomy__lock-source",
        "tm-ai-autonomy__lock-reason",
        "tm-ai-autonomy__confirm",
        "tm-ai-autonomy__confirm-message",
        "tm-ai-autonomy__confirm-actions",
        "tm-ai-autonomy__confirm-accept",
        "tm-ai-autonomy__confirm-cancel",
    ];

    private static string DialCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-autonomy-dial.css");
        File.Exists(path).Should().BeTrue(
            "the autonomy surface ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheAutonomyPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-autonomy-dial.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = DialCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheTrackFill_IsOneKnobTheComponentWrites_AndTheRuleReads()
    {
        var css = DialCss();

        css.Should().Contain("--tm-autonomy-progress:",
            "the fill's extent is the reader's value, so it is a custom property instead of a second width rule");
        css.Should().Contain("inline-size: var(--tm-autonomy-progress)",
            "the rule reads the value the component wrote, never a duplicated number");
    }

    [Fact]
    public void AnUnavailableStop_IsDimmed_AndTheReachedOneIsMarked()
    {
        var css = DialCss();

        css.Should().Contain(".tm-ai-autonomy__stop[data-available=\"false\"]",
            "a stop above the ceiling must LOOK unavailable, not merely do nothing");
        css.Should().Contain(".tm-ai-autonomy__stop[data-active=\"true\"]",
            "the reached stop is the one the track's fill ends at");
        css.Should().Contain("--tm-autonomy-tone:",
            "the tone is one knob, so a modifier cannot lose to a base rule declared later");
    }

    [Fact]
    public void TheSliderAndTheConfirmation_CarryRealFocusRings()
    {
        var css = DialCss();

        css.Should().Contain(".tm-ai-autonomy__control:focus-visible");
        css.Should().Contain(".tm-ai-autonomy__step:focus-visible");
        css.Should().Contain(".tm-ai-autonomy__confirm-accept:focus-visible");
        css.Should().Contain(".tm-ai-autonomy__confirm-cancel:focus-visible");
    }

    [Fact]
    public void ACoarsePointer_GrowsTheStepButtonsToTheTouchTarget()
    {
        var css = DialCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
        css.Should().Contain("min-inline-size: var(--tm-touch-target)",
            "a step button is a square the reader presses with a thumb");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = DialCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 3px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText",
            "the resting track collapses under high contrast, so it keeps a system colour");
    }

    [Fact]
    public void TheOnlyMovement_IsTheTrackFill_AndReducedMotionRemovesIt()
    {
        var css = DialCss();

        css.Should().NotContain("@keyframes",
            "the dial is a value, not a decoration: it pulses nothing");
        css.Should().Contain("@media (prefers-reduced-motion: reduce)");
        css.Should().Contain("transition: none",
            "the fill's travel is removed for a reader who asked for less motion");
    }

    [Fact]
    public void TheLockAndTheCeiling_AreStatedAsText()
    {
        var css = DialCss();

        css.Should().Contain(".tm-ai-autonomy__lock-source");
        css.Should().Contain(".tm-ai-autonomy__lock-reason");
        css.Should().Contain("overflow-wrap: anywhere",
            "an explanation wraps instead of being clipped by a narrow card");
    }
}
