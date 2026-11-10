using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 7ba95ed6, Fáze 7 — the LIVE presence section of <c>/ai-agent-visualizations</c>.
/// <para>
/// The section's cursors are fed by a marker stream the SECTION owns (<c>AiAgentPresenceLiveSurface</c> and its own
/// simulator), not by the page's run: a reader can watch the cursors move while the page's generator stays paused and
/// its counters stay at zero, and the paint gate keeps a ten-per-second stream down to one paint per window. That
/// isolation is the whole point — the page used to render four SAMPLE markers because a live one would have repainted
/// the page for every tick of every other visualization.
/// </para>
/// </summary>
public class AiAgentPresenceLiveSurfaceTests : DemoPageLocalizationTestBase
{
    public AiAgentPresenceLiveSurfaceTests() => Services.AddSingleton(TimeProvider.System);

    private FakeTimeProvider RegisterClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        return clock;
    }

    private IRenderedComponent<AiAgentVisualizationsPage> RenderPage() => Render<AiAgentVisualizationsPage>();

    private static void Press(IRenderedComponent<AiAgentVisualizationsPage> cut, string selector, int index = 0)
        => cut.InvokeAsync(() => cut.FindAll(selector)[index].Click());

    /// <summary>
    /// Walks the fake clock in single intervals; the fake provider fires a periodic timer once per advance call, so a
    /// one-second jump would look like a single tick.
    /// </summary>
    private static void Advance(IRenderedComponent<AiAgentVisualizationsPage> cut, FakeTimeProvider clock,
        int ticks, int milliseconds)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            cut.InvokeAsync(() => clock.Advance(TimeSpan.FromMilliseconds(milliseconds)));
        }
    }

    private static string[] Anchors(IRenderedComponent<AiAgentVisualizationsPage> cut)
        =>
        [
            .. cut.FindAll("[data-testid='ai-viz-presence'] [data-presence-marker]")
                .Select(marker => marker.GetAttribute("data-presence-anchor") ?? string.Empty),
        ];

    private static string[] Activities(IRenderedComponent<AiAgentVisualizationsPage> cut)
        =>
        [
            .. cut.FindAll("[data-testid='ai-viz-presence'] [data-presence-marker]")
                .Select(marker => marker.GetAttribute("data-activity") ?? string.Empty),
        ];

    [Fact]
    public void TheSectionOwnsItsOwnMarkerStream_AndThePageStartsPaused()
    {
        RegisterClock();
        var cut = RenderPage();

        cut.Find("[data-testid='ai-viz-state']").TextContent.Trim().Should().Be("Paused",
            "the presence section must not start the page's run");
        cut.Find("[data-testid='ai-viz-events']").TextContent.Trim().Should().Be("0");
        cut.FindAll("[data-testid='ai-viz-presence'] [data-presence-marker]").Should().HaveCount(4);
        string.Join("|", Anchors(cut)).Should().Be("plan-lead|plan-goal|plan-risk|plan-close",
            "the opening snapshot anchors one cursor per paragraph, in reading order");
    }

    [Fact]
    public void PlayingTheCursorsMovesThem_WithoutAdvancingThePagesRun()
    {
        var clock = RegisterClock();
        var cut = RenderPage();
        var before = Anchors(cut);

        Press(cut, "[data-testid='ai-viz-presence-play']");
        Advance(cut, clock, 12, 100);

        Anchors(cut).Should().NotEqual(before,
            "the section's own stream moved the cursors to the paragraphs the run is working on");
        cut.Find("[data-testid='ai-viz-state']").TextContent.Trim().Should().Be("Paused",
            "a live presence cursor is not the page's run");
        cut.Find("[data-testid='ai-viz-events']").TextContent.Trim().Should().Be("0",
            "the page's counters are untouched, so a tick repaints the section only");

        Press(cut, "[data-testid='ai-viz-presence-play']");

        // A pending trailing paint still lands after the pause (the gate folds a burst into one paint), so the clock
        // is walked once more BEFORE the paused snapshot is read.
        Advance(cut, clock, 3, 100);
        var parked = Anchors(cut);

        Advance(cut, clock, 12, 100);
        string.Join("|", Anchors(cut)).Should().Be(string.Join("|", parked),
            "a paused stream applies nothing");
    }

    [Fact]
    public void TheLiveStreamChangesActivity_AndTheRosterCarriesTheSamePresenceAsText()
    {
        var clock = RegisterClock();
        var cut = RenderPage();
        var before = Activities(cut);

        Press(cut, "[data-testid='ai-viz-presence-play']");
        Advance(cut, clock, 12, 100);

        Activities(cut).Should().NotEqual(before, "an agent starts, finishes or switches its activity");
        cut.FindAll("[data-testid='ai-viz-presence'] [data-testid='presence-roster-item']").Should().HaveCount(4,
            "the roster is the text alternative of the cursor layer, and it follows the same snapshot");
    }

    [Fact]
    public void TheLiveLayerAddsNoSecondLiveRegion()
    {
        RegisterClock();
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-viz-presence'] [aria-live]").Should().BeEmpty(
            "markers change on every tick; a live region reading them around would drown the page's one channel");
        cut.FindAll("[data-testid='ai-viz-announcer']").Should().HaveCount(1,
            "the page still owns exactly ONE polite live region");
    }

    [Fact]
    public void TheTablePresentationRendersTheLiveSnapshotOneRowPerAgent()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        Press(cut, "[data-testid='ai-viz-presence-play']");
        Advance(cut, clock, 12, 100);

        Press(cut, "[data-testid='ai-viz-presence-view']");

        cut.FindAll("[data-testid='ai-viz-presence']").Should().BeEmpty(
            "the two presentations are alternatives, never duplicates");
        var rows = cut.FindAll("[data-testid='ai-viz-presence-table'] [data-testid='ai-viz-presence-row']");
        rows.Should().HaveCount(4);
        rows.Select(row => row.GetAttribute("data-agent-id")).Should().Equal("planner", "retriever", "reconciler",
            "reviewer");

        Press(cut, "[data-testid='ai-viz-presence-view']");
        cut.FindAll("[data-testid='ai-viz-presence'] [data-presence-marker]").Should().HaveCount(4,
            "the toggle brings the layer back with the same snapshot");
    }

    [Fact]
    public void TheToggleNamesThePresentationTheNextPressShows()
    {
        RegisterClock();
        var cut = RenderPage();
        var toggle = cut.Find("[data-testid='ai-viz-presence-view']");

        toggle.TextContent.Should().Contain("Show the table view");
        Press(cut, "[data-testid='ai-viz-presence-view']");
        cut.Find("[data-testid='ai-viz-presence-view']").TextContent.Should().Contain("Show the visual view");
    }

    [Fact]
    public void TheCursorsArePaintedFromTheSectionsOwnSnapshot()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        Press(cut, "[data-testid='ai-viz-presence-play']");
        Advance(cut, clock, 12, 100);
        Press(cut, "[data-testid='ai-viz-presence-play']");

        // A paused stream still renders every cursor of the LAST snapshot: activity, colour and label are text.
        foreach (var marker in cut.FindAll("[data-testid='ai-viz-presence'] [data-presence-marker]"))
        {
            marker.TextContent.Should().NotBeNullOrWhiteSpace("a cursor label names the agent and its activity");
            marker.GetAttribute("data-presence-anchor").Should().NotBeNullOrEmpty();
        }

        cut.FindAll("[data-testid='ai-viz-presence'] [data-presence-marker]")
            .Select(marker => marker.GetAttribute("data-agent-id"))
            .Should().Equal("planner", "retriever", "reconciler", "reviewer");
    }
}
