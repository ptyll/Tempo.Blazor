using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Demo.SharedUI.Components;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// The <c>/ai-agent-visualizations</c> demo page: one simulated run across every agent surface. These facts walk what
/// a reader actually does — start and pause the generator on a fake clock, change its speed, reset it, read the
/// counters, switch each graphical surface to its table presentation, forget a fact and undo it, settle every conflict
/// and merge — so a page that only renders the components cannot pass.
/// </summary>
public class AiAgentVisualizationsPageTests : DemoPageLocalizationTestBase
{
    public AiAgentVisualizationsPageTests()
    {
        // The page asks for the demo clock the way every host does; a test registers its own before rendering.
        Services.AddSingleton(TimeProvider.System);
    }

    private FakeTimeProvider RegisterClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        return clock;
    }

    private IRenderedComponent<AiAgentVisualizationsPage> RenderPage() => Render<AiAgentVisualizationsPage>();

    private static IElement Section(IRenderedComponent<AiAgentVisualizationsPage> cut, string testId)
        => cut.Find($"[data-testid='{testId}']");

    private static string Text(IRenderedComponent<AiAgentVisualizationsPage> cut, string testId)
        => Section(cut, testId).TextContent.Trim();

    /// <summary>Presses a control inside the renderer's dispatcher, so the event handler cannot go stale.</summary>
    private static void Press(IRenderedComponent<AiAgentVisualizationsPage> cut, string selector, int index = 0)
        => cut.InvokeAsync(() => cut.FindAll(selector)[index].Click());

    /// <summary>
    /// Walks a fake clock in single intervals. The fake clock fires a periodic timer once per
    /// <see cref="FakeTimeProvider.Advance(TimeSpan)"/> call, so a one-second jump would look like a single tick.
    /// </summary>
    private static void Advance(IRenderedComponent<AiAgentVisualizationsPage> cut, FakeTimeProvider clock,
        int ticks, int milliseconds)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            cut.InvokeAsync(() => clock.Advance(TimeSpan.FromMilliseconds(milliseconds)));
        }
    }

    /// <summary>The event count the page currently PAINTS (which may lag the applied count by one paint window).</summary>
    private static int Painted(IRenderedComponent<AiAgentVisualizationsPage> cut)
        => int.Parse(Text(cut, "ai-viz-events"), CultureInfo.InvariantCulture);

    [Fact]
    public void ThePageRendersEverySurfaceAndTheSimulatorWithoutRawKeys()
    {
        RegisterClock();
        var cut = RenderPage();

        foreach (var section in new[]
                 {
                     "ai-viz-simulator", "ai-viz-lanes-section", "ai-viz-graph-section", "ai-viz-fan-out-section",
                     "ai-viz-attempts-section", "ai-viz-handoff-section", "ai-viz-fallback-section",
                     "ai-viz-autonomy-section", "ai-viz-memory-section", "ai-viz-delegate-section",
                     "ai-viz-runs-section", "ai-viz-conflict-section", "ai-viz-presence-section",
                 })
        {
            Section(cut, section).TextContent.Should().NotContain("[Tm", "no raw resource key may reach the page");
        }

        Text(cut, "ai-viz-demo-notice").Should().Contain("Demo data", "the page must label its invented data");
        Text(cut, "ai-viz-state").Should().Be("Paused", "a timer never starts before the reader asks for one");
        Text(cut, "ai-viz-events").Should().Be("0");
        Text(cut, "ai-viz-last-event").Should().Contain("Nothing has happened yet");
        cut.Find("[data-testid='ai-viz-announcer']").GetAttribute("aria-live").Should().Be("polite");
        cut.FindAll("[data-testid='ai-viz-announcer']").Should().HaveCount(1,
            "the page owns exactly one live region");
    }

    [Fact]
    public void PlayingTheSimulatorAdvancesTheCounters_AndPausingStopsThem()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        Press(cut, "[data-testid='ai-viz-play']");
        Advance(cut, clock, 10, 100);

        Text(cut, "ai-viz-state").Should().Be("Running");
        Text(cut, "ai-viz-rate").Should().Be("10");
        Painted(cut).Should().BeInRange(9, 10,
            "ten events per second were APPLIED, and the paint gate may still hold the newest one");
        Text(cut, "ai-viz-last-event").Should().Contain("Event ");
        cut.FindAll("[data-testid='ai-viz-lanes-section'] [data-testid='agent-lanes-block']").Should().NotBeEmpty(
            "the lanes render the reduced snapshot of the simulator");

        Press(cut, "[data-testid='ai-viz-play']");
        Advance(cut, clock, 20, 100);

        Text(cut, "ai-viz-state").Should().Be("Paused");
        Painted(cut).Should().BeInRange(9, 10, "a paused generator applies nothing");
    }

    [Fact]
    public void TheSpeedChoiceSetsTheRate_AndSaysSoInTheLiveRegion()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        Press(cut, "[data-testid='ai-viz-speed'] [data-testid='segmented-option']", 2);

        Text(cut, "ai-viz-rate").Should().Be("20", "the fastest choice runs at twice the base rate");
        cut.Find("[data-testid='ai-viz-announcer']").TextContent.Should().Contain("20");

        Press(cut, "[data-testid='ai-viz-play']");
        Advance(cut, clock, 20, 50);

        Painted(cut).Should().BeInRange(10, 20,
            "twenty events per second were APPLIED; a 200 ms paint window may show any state inside one window");
    }

    [Fact]
    public void ResettingReturnsTheRunToItsOpeningSnapshot()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        Press(cut, "[data-testid='ai-viz-play']");
        Advance(cut, clock, 10, 100);
        Painted(cut).Should().BeInRange(9, 10);

        Press(cut, "[data-testid='ai-viz-reset']");

        Text(cut, "ai-viz-state").Should().Be("Paused");
        Text(cut, "ai-viz-events").Should().Be("0");
        Text(cut, "ai-viz-sequence").Should().Be("0");
        Text(cut, "ai-viz-last-event").Should().Contain("Nothing has happened yet");
        cut.Find("[data-testid='ai-viz-announcer']").TextContent.Should()
            .Contain("opening snapshot", "the live region says what the reset did");
    }

    [Fact]
    public void TheFanOutTableIsTheTextAlternative_WithAllFortyUnits()
    {
        RegisterClock();
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-viz-fan-out-table']").Should().BeEmpty();
        cut.FindAll("[data-testid='ai-viz-fan-out-table'] [data-testid='ai-viz-fan-out-row']").Should().BeEmpty();

        Press(cut, "[data-testid='ai-viz-fan-out-view']");

        cut.FindAll("[data-testid='ai-viz-fan-out-table'] [data-testid='ai-viz-fan-out-row']").Should().HaveCount(40);
        cut.FindAll("[data-testid='ai-viz-fan-out']").Should().BeEmpty(
            "the graphical surface is replaced, not duplicated, so nothing is announced twice");
        cut.Find("[data-testid='ai-viz-fan-out-view']").HasAttribute("aria-pressed").Should().BeFalse(
            "the label names the presentation a press shows, so an aria-pressed state would contradict it");
        Text(cut, "ai-viz-fan-out-view").Should().Be("Show the visual view");

        Press(cut, "[data-testid='ai-viz-fan-out-view']");
        cut.FindAll("[data-testid='ai-viz-fan-out-table']").Should().BeEmpty();
        cut.FindAll("[data-testid='ai-viz-fan-out'] [data-testid='fan-out-tile']").Should().NotBeEmpty();
    }

    /// <summary>
    /// The six graphic/table switches of the page name the presentation a press SHOWS. That changing label is the
    /// button's accessible name, so the button must not also carry an <c>aria-pressed</c> state: a reader would hear
    /// "Show the visual view, pressed" while the table is on screen (WCAG 4.1.2; the APG keeps a toggle button's name
    /// fixed). One fact per button, so a regression names the surface it broke.
    /// </summary>
    [Theory]
    [InlineData("ai-viz-fan-out-view")]
    [InlineData("ai-viz-attempts-view")]
    [InlineData("ai-viz-memory-view")]
    [InlineData("ai-viz-delegate-view")]
    [InlineData("ai-viz-conflict-view")]
    [InlineData("ai-viz-presence-view")]
    public void TheGraphicTableSwitchNamesItsActionInsteadOfBeingAToggleButton(string testId)
    {
        RegisterClock();
        var cut = RenderPage();

        var toggle = cut.Find($"[data-testid='{testId}']");
        toggle.HasAttribute("aria-pressed").Should().BeFalse(
            "a changing label and a pressed state would say two contradictory things");
        toggle.TextContent.Trim().Should().Be("Show the table view");

        Press(cut, $"[data-testid='{testId}']");

        var switched = cut.Find($"[data-testid='{testId}']");
        switched.HasAttribute("aria-pressed").Should().BeFalse(
            "the button is a plain action button, not a toggle");
        switched.TextContent.Trim().Should().Be("Show the visual view",
            "the label keeps naming the presentation the next press shows");
    }

    [Fact]
    public void TheAttemptsTableListsTheThreeDrafts()
    {
        RegisterClock();
        var cut = RenderPage();

        Press(cut, "[data-testid='ai-viz-attempts-view']");

        var rows = cut.FindAll("[data-testid='ai-viz-attempts-table'] [data-testid='ai-viz-attempts-row']");
        rows.Should().HaveCount(3);
        rows[1].TextContent.Should().Contain("Draft B").And.Contain("0.93");
    }

    [Fact]
    public void TheMemoryPanelForgetsAndUndoes_AndTheTableListsAllTwentyFacts()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        var items = cut.FindAll("[data-testid='ai-viz-memory'] [data-testid='memory-item']");
        items.Should().HaveCount(20);

        Press(cut, "[data-testid='ai-viz-memory'] [data-testid='memory-item-forget']");

        cut.FindAll("[data-testid='ai-viz-memory'] [data-testid='memory-item']").Should().HaveCount(19,
            "the row leaves at once and the undo window holds it");

        Press(cut, "[data-testid='ai-viz-memory'] [data-testid='memory-undo-action']");
        cut.FindAll("[data-testid='ai-viz-memory'] [data-testid='memory-item']").Should().HaveCount(20,
            "Undo brings the row back, and the page never heard about the removal");
        Text(cut, "ai-viz-memory-log").Should().Contain("Nothing has happened yet",
            "an undone removal is never reported to the page");

        // A second removal whose undo window CLOSES is the one the page really applies.
        Press(cut, "[data-testid='ai-viz-memory'] [data-testid='memory-item-forget']");
        cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(11)));
        cut.FindAll("[data-testid='ai-viz-memory'] [data-testid='memory-item']").Should().HaveCount(19);
        Text(cut, "ai-viz-memory-log").Should().Contain("Forgotten");

        Press(cut, "[data-testid='ai-viz-memory-view']");
        cut.FindAll("[data-testid='ai-viz-memory-table'] [data-testid='ai-viz-memory-row']").Should().HaveCount(19);
    }

    [Fact]
    public void TheDelegateTableListsEveryTask_AndTheBoardKeepsItsKeyboardAlternative()
    {
        RegisterClock();
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-viz-delegate'] .tm-ai-delegate__column").Should().HaveCount(4,
            "the backlog plus the three agents");

        Press(cut, "[data-testid='ai-viz-delegate-view']");
        cut.FindAll("[data-testid='ai-viz-delegate-table'] [data-testid='ai-viz-delegate-row']").Should().HaveCount(8);
        cut.FindAll("[data-testid='ai-viz-delegate']").Should().BeEmpty();
    }

    [Fact]
    public void TheConflictTableListsEveryChangeAndEveryDecision()
    {
        RegisterClock();
        var cut = RenderPage();

        var radar = cut.Find("[data-testid='ai-viz-conflict']");
        radar.QuerySelectorAll("[data-testid='radar-row'][data-conflict='true']").Should().HaveCount(4,
            "the demo carries three agents over twenty-five files with four real conflicts");

        Press(cut, "[data-testid='ai-viz-conflict-view']");
        cut.FindAll("[data-testid='ai-viz-conflict-table'] [data-testid='ai-viz-conflict-row']").Should().HaveCount(29,
            "every change of every branch is listed");
        cut.FindAll("[data-testid='ai-viz-conflict-decisions'] [data-testid='ai-viz-conflict-decision']")
            .Should().HaveCount(4);
        Text(cut, "ai-viz-conflict-decisions").Should().Contain("Not decided");
    }

    [Fact]
    public void TheConflictRadarSettlesEveryConflictAndMerges()
    {
        RegisterClock();
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-viz-conflict'] [role='radiogroup']").Should().HaveCount(4);

        // One decision per renderer turn: a decision re-keys the radio group, so a held element carries a stale handler.
        for (var index = 0; index < 4; index++)
        {
            cut.InvokeAsync(() => cut.FindAll("[data-testid='ai-viz-conflict'] [role='radiogroup']")[index]
                .QuerySelectorAll("input[type='radio']")[1].Change(true));
        }

        Text(cut, "ai-viz-conflict-log").Should().Contain("4 conflict(s) settled",
            "the page stores every decision the radar reported");

        Press(cut, "[data-testid='ai-viz-conflict'] [data-testid='radar-merge']");
        Text(cut, "ai-viz-conflict-log").Should().Contain("Merge requested");
    }

    [Fact]
    public void ThePresenceTableNamesEveryonePresent()
    {
        RegisterClock();
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-viz-presence'] [data-presence-marker]").Should().HaveCount(4);
        cut.FindAll("[data-testid='ai-viz-presence'] [data-testid='presence-roster-item']").Should().HaveCount(4);

        Press(cut, "[data-testid='ai-viz-presence-view']");
        cut.FindAll("[data-testid='ai-viz-presence-table'] [data-testid='ai-viz-presence-row']").Should().HaveCount(4);
        cut.FindAll("[data-testid='ai-viz-presence']").Should().BeEmpty();
    }

    [Fact]
    public void TheLanesSelectionReachesThePageAndTheLanesOwnTableViewIsReachable()
    {
        RegisterClock();
        var cut = RenderPage();

        var blocks = cut.FindAll("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-block']");
        blocks.Should().NotBeEmpty();

        cut.InvokeAsync(() => cut.FindAll("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-block']")[0].Click());
        Text(cut, "ai-viz-lanes-selected").Should().NotContain("No block selected");

        cut.InvokeAsync(() => cut
            .FindAll("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-view'] button")[1].Click());
        cut.FindAll("[data-testid='ai-viz-lanes'] [data-testid='agent-lanes-table-row']").Should().NotBeEmpty(
            "the lanes' own switch is its text alternative");
    }

    [Fact]
    public void TheGraphSelectionReachesThePage_AndTheGraphOwnTableViewIsReachable()
    {
        RegisterClock();
        var cut = RenderPage();

        var nodes = cut.FindAll("[data-testid='ai-viz-graph'] [data-testid='agent-graph-node']");
        nodes.Should().NotBeEmpty();

        cut.InvokeAsync(() => cut.FindAll("[data-testid='ai-viz-graph'] [data-testid='agent-graph-node']")[1].Click());
        Text(cut, "ai-viz-graph-selected").Should().NotContain("No node is selected");

        cut.InvokeAsync(() => cut
            .FindAll("[data-testid='ai-viz-graph'] [data-testid='agent-graph-view'] button")[1].Click());
        cut.FindAll("[data-testid='ai-viz-graph'] [data-testid='agent-graph-table-row']").Should().NotBeEmpty();
    }

    [Fact]
    public void TheAutonomyDialKeepsItsLocksAndReportsAGradeToThePage()
    {
        RegisterClock();
        var cut = RenderPage();

        var dial = cut.Find("[data-testid='ai-viz-autonomy']");
        dial.QuerySelectorAll("[data-testid='autonomy-lock']").Should().HaveCount(2);
        cut.Find("[data-testid='ai-viz-autonomy'] [data-testid='autonomy-dial-track']")
            .GetAttribute("aria-valuenow").Should().Be("1");

        cut.InvokeAsync(() =>
        {
            var track = cut.Find("[data-testid='ai-viz-autonomy'] [data-testid='autonomy-dial-track']");
            track.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Home" });
        });

        cut.Find("[data-testid='ai-viz-autonomy'] [data-testid='autonomy-dial-track']")
            .GetAttribute("aria-valuenow").Should().Be("0", "Home walks the dial to its first grade");
    }

    /// <summary>
    /// The paint gate keeps a trailing timer between ticks. That timer must never outlive the component: a paint that
    /// arrives after <c>Dispose</c> would call <c>InvokeAsync</c> on a circuit that is already gone. The race itself is
    /// not deterministic, so this fact pins the observable contract — disposing mid-window and then advancing the
    /// clock is silent, and the surface never paints again.
    /// </summary>
    [Fact]
    public void ADisposedLiveSurfaceDropsItsTrailingPaintInsteadOfCrashing()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        Press(cut, "[data-testid='ai-viz-play']");
        Advance(cut, clock, 1, 100);

        var painted = Text(cut, "ai-viz-events");

        cut.FindComponents<AiAgentRunLiveSurfaces>()[0].Instance.Dispose();
        cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(2)));

        Text(cut, "ai-viz-events").Should().Be(painted, "a disposed live surface never paints again");
    }
}
