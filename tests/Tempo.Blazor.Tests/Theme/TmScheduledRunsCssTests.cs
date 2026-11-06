using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 7 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmScheduledRuns"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens only;
/// this guard proves the runs board's own share of it: the partial is imported by the package entry stylesheet, every
/// class the markup emits has a rule, a run's status and the window tones are knobs a modifier SETS and the base rule
/// READS, the capacity bar's runtime knobs have declared defaults, the day buttons and the move menu carry real focus
/// rings, a coarse pointer reaches the 44 px target, the forced-colors block hands the palette back to the system, and
/// the partial animates nothing at all.
/// </para>
/// </summary>
public sealed class TmScheduledRunsCssTests
{
    /// <summary>Every <c>tm-ai-runs…</c> class the markup emits.</summary>
    private static readonly string[] Emitted =
    [
        "tm-ai-runs",
        "tm-ai-runs--week",
        "tm-ai-runs--day",
        "tm-ai-runs--table",
        "tm-ai-runs__header",
        "tm-ai-runs__title",
        "tm-ai-runs__summary",
        "tm-ai-runs__view",
        "tm-ai-runs__day-nav",
        "tm-ai-runs__day-step",
        "tm-ai-runs__current-day",
        "tm-ai-runs__instructions",
        "tm-ai-runs__notice",
        "tm-ai-runs__empty",
        "tm-ai-runs__board",
        "tm-ai-runs__day",
        "tm-ai-runs__day-title",
        "tm-ai-runs__windows",
        "tm-ai-runs__window",
        "tm-ai-runs__window--overloaded",
        "tm-ai-runs__window--full",
        "tm-ai-runs__window-head",
        "tm-ai-runs__window-title",
        "tm-ai-runs__window-meta",
        "tm-ai-runs__bar",
        "tm-ai-runs__bar-committed",
        "tm-ai-runs__bar-slice",
        "tm-ai-runs__overload",
        "tm-ai-runs__overload-icon",
        "tm-ai-runs__overload-text",
        "tm-ai-runs__full",
        "tm-ai-runs__full-icon",
        "tm-ai-runs__full-text",
        "tm-ai-runs__free",
        "tm-ai-runs__run",
        "tm-ai-runs__run-dot",
        "tm-ai-runs__run-body",
        "tm-ai-runs__run-title",
        "tm-ai-runs__run-meta",
        "tm-ai-runs__status",
        "tm-ai-runs__status--planned",
        "tm-ai-runs__status--running",
        "tm-ai-runs__status--succeeded",
        "tm-ai-runs__status--failed",
        "tm-ai-runs__status--paused",
        "tm-ai-runs__status-icon",
        "tm-ai-runs__status-text",
        "tm-ai-runs__units",
        "tm-ai-runs__part",
        "tm-ai-runs__run-actions",
        "tm-ai-runs__move",
        "tm-ai-runs__move-trigger",
        "tm-ai-runs__table-wrap",
        "tm-ai-runs__table",
        "tm-ai-runs__caption",
        "tm-ai-runs__row",
        "tm-ai-runs__row--unplaced",
        "tm-ai-runs__cell",
        "tm-ai-runs__cell--run",
        "tm-ai-runs__cell--actions",
    ];

    private static string RunsCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-scheduled-runs.css");
        File.Exists(path).Should().BeTrue(
            "the runs board ships its own partial next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheRunsPartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-scheduled-runs.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = RunsCss();

        foreach (var name in Emitted)
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void AStatusIsOneKnobAModifierSets_AndTheBaseRuleReads()
    {
        var css = RunsCss();

        css.Should().Contain("--tm-runs-status-ink:",
            "the status colour is one knob, so a modifier cannot lose to a base rule declared later");
        css.Should().Contain("color: var(--tm-runs-status-ink)");

        foreach (var status in new[] { "planned", "running", "succeeded", "failed", "paused" })
        {
            css.Should().Contain($".tm-ai-runs__status--{status}",
                "the {0} status must paint its own tone", status);
        }
    }

    [Fact]
    public void TheRuntimeBarKnobs_HaveDeclaredDefaults()
    {
        var css = RunsCss();

        css.Should().Contain("--tm-runs-slice-start: 0%");
        css.Should().Contain("--tm-runs-slice-size: 0%");
        css.Should().Contain("--tm-runs-run-color: var(--tm-color-primary)",
            "a run's colour comes from the shared agent palette, written inline per run");
    }

    [Fact]
    public void AnOverloadedWindow_ReadsAsAWarningInItsOwnFrame()
    {
        var css = RunsCss();

        css.Should().Contain(".tm-ai-runs__window--overloaded");
        css.Should().Contain(".tm-ai-runs__overload");
        css.Should().Contain(".tm-ai-runs__overload-text",
            "the overload is a word plus an icon, never a tint alone");
    }

    [Fact]
    public void TheDayButtonsAndTheMoveMenu_CarryRealFocusRings()
    {
        var css = RunsCss();

        css.Should().Contain(".tm-ai-runs__day-step:focus-visible");
        css.Should().Contain(".tm-ai-runs__move:focus-within",
            "the wrapper the menu renders is the tab stop, so it needs the ring too");
        css.Should().Contain(".tm-ai-runs__move-trigger:focus-visible");
        css.Should().Contain("outline: var(--tm-border-width) solid var(--tm-border-color-focus)");
    }

    [Fact]
    public void AnUnavailableDayButton_LooksUnavailable_WithoutLosingItsPlace()
    {
        var css = RunsCss();

        css.Should().Contain(".tm-ai-runs__day-step[aria-disabled='true']",
            "the button stays focusable (never the native disabled attribute) and still reads as unavailable");
    }

    [Fact]
    public void ACoarsePointer_GrowsTheDayStepAndTheMoveMenuToTheTouchTarget()
    {
        var css = RunsCss();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void ForcedColors_KeepTheSystemPaletteInCharge()
    {
        var css = RunsCss();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline: 2px solid Highlight",
            "every focus ring is handed back to the system Highlight colour");
        css.Should().Contain("CanvasText");
    }

    [Fact]
    public void ThePartialAnimatesNothing_BecauseEveryStateIsAlsoText()
    {
        var css = RunsCss();

        css.Should().NotContain("@keyframes");
        css.Should().NotContain("animation:");
        css.Should().NotContain("transition:",
            "a bar that slid into place would be motion without information");
    }

    [Fact]
    public void TheLongTexts_Wrap_AndTheTableScrollsInsteadOfThePage()
    {
        var css = RunsCss();

        css.Should().Contain("overflow-wrap: anywhere");
        css.Should().Contain("min-inline-size: 0");
        css.Should().Contain("overflow-x: auto",
            "a table with many windows scrolls inside its own wrapper, never the page");
    }
}
