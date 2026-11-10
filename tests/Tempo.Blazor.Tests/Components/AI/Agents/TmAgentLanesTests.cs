using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 2 — <c>TmAgentLanes</c> (Tempo.Blazor.AI): a multi-agent run as one lane per agent on a shared
/// time axis.
/// <para>
/// The tests read behaviour, not markup: a lane header carries the shared <c>TmAgentBadge</c>, every block is a native
/// button named by its agent, activity, kind, time, state and marker, a marker is an icon AND a word, the "now" line
/// follows the injected <see cref="TimeProvider"/>, zoom widens the blocks and announces itself, a selection is two-way
/// bound and opens the host's detail, the incremental events are applied through the shared stream (idempotently, with
/// a hole reported instead of swallowed) and the table view carries the SAME blocks and handoffs.
/// </para>
/// </summary>
public class TmAgentLanesTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock;
    private string? _selectedId;
    private AgentLanesViewMode? _viewMode;
    private double? _pixelsPerHour;
    private long? _gapExpected;
    private int _gapCount;

    /// <summary>
    /// The pinned clock every render reads: without it the "now" line would describe the test run instead of the run
    /// it paints, and the paint gate could not be advanced deterministically.
    /// </summary>
    public TmAgentLanesTests()
    {
        _clock = new FakeTimeProvider(Nine.AddMinutes(30));
        Services.AddSingleton<TimeProvider>(_clock);
    }

    private static AgentRef Planner => new("Planner", "planner") { ColorIndex = 0 };

    private static AgentRef Writer => new("Writer", "writer") { ColorIndex = 1 };

    private static AgentLaneBlock Block(
        string id,
        string agentId = "planner",
        int startMinutes = 0,
        int? endMinutes = 10,
        AgentLaneBlockKind kind = AgentLaneBlockKind.Task,
        AgentActivityStatus status = AgentActivityStatus.Succeeded,
        AgentLaneBlockMarker? marker = null,
        string? label = null)
        => new(id, agentId, Nine.AddMinutes(startMinutes), label ?? "Work " + id)
        {
            End = endMinutes is { } end ? Nine.AddMinutes(end) : null,
            Kind = kind,
            Status = status,
            Marker = marker,
        };

    private static AgentLanesSnapshot Snapshot(params AgentLaneBlock[] blocks) => new()
    {
        Sequence = 100,
        Agents = [Planner, Writer],
        Blocks = blocks.Length == 0
            ? [Block("a", startMinutes: 0, endMinutes: 10), Block("b", agentId: "writer", startMinutes: 12, endMinutes: 25)]
            : blocks,
        Handoffs = [],
    };

    private static AgentLanesSnapshot WithHandoff(params AgentLaneBlock[] blocks) => Snapshot(blocks) with
    {
        Handoffs = [new AgentHandoffLink("writer", Nine.AddMinutes(11)) { FromBlockId = "a", Label = "Plan ready" }],
    };

    private IRenderedComponent<TmAgentLanes> RenderLanes(
        AgentLanesSnapshot? snapshot = null,
        IReadOnlyList<AgentLaneEvent>? events = null,
        AgentLanesViewMode? viewMode = null,
        string? selectedBlockId = null,
        RenderFragment<AgentLaneBlock>? detailTemplate = null,
        bool showHandoffs = true,
        bool showTitle = true,
        int headingLevel = 3,
        double pixelsPerHour = 600,
        TimeSpan? renderThrottle = null)
    {
        _selectedId = null;
        _viewMode = null;
        _gapCount = 0;

        return Render<TmAgentLanes>(parameters =>
        {
            parameters.Add(component => component.Snapshot, snapshot ?? Snapshot());
            parameters.Add(component => component.Culture, CultureInfo.InvariantCulture);
            parameters.Add(component => component.TimeProvider, _clock);
            parameters.Add(component => component.PixelsPerHour, pixelsPerHour);
            parameters.Add(component => component.ShowHandoffs, showHandoffs);
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.SelectedBlockIdChanged, (string? id) => _selectedId = id);
            parameters.Add(component => component.ViewModeChanged, (AgentLanesViewMode mode) => _viewMode = mode);
            parameters.Add(component => component.PixelsPerHourChanged, (double value) => _pixelsPerHour = value);
            parameters.Add(component => component.OnGapDetected, (long expected) =>
            {
                _gapExpected = expected;
                _gapCount++;
            });

            if (events is not null)
            {
                parameters.Add(component => component.Events, events);
            }

            if (viewMode is not null)
            {
                parameters.Add(component => component.ViewMode, viewMode.Value);
            }

            if (selectedBlockId is not null)
            {
                parameters.Add(component => component.SelectedBlockId, selectedBlockId);
            }

            if (detailTemplate is not null)
            {
                parameters.Add(component => component.DetailTemplate, detailTemplate);
            }

            if (renderThrottle is not null)
            {
                parameters.Add(component => component.RenderThrottle, renderThrottle.Value);
            }
        });
    }

    /// <summary>
    /// Re-renders the SAME instance with the given parameters, the way a host's parent component does (bUnit 2
    /// removed <c>SetParametersAndRender</c>; a partial view leaves every other parameter alone).
    /// </summary>
    private static Task UpdateAsync(IRenderedComponent<TmAgentLanes> cut, params (string Name, object? Value)[] values)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            values.ToDictionary(value => value.Name, value => value.Value))));

    private static IReadOnlyList<AngleSharp.Dom.IElement> Blocks(IRenderedComponent<TmAgentLanes> cut)
        => cut.FindAll("[data-testid='agent-lanes-block']");

    private static string Announcement(IRenderedComponent<TmAgentLanes> cut)
        => cut.Find("[data-testid='agent-lanes-announcement']").TextContent;

    [Fact]
    public void RendersOneLanePerAgent_WithTheSharedBadge()
    {
        var cut = RenderLanes();

        var lanes = cut.FindAll("[data-testid='agent-lanes-lane']");

        lanes.Should().HaveCount(2);
        lanes[0].GetAttribute("data-agent-id").Should().Be("planner");
        lanes[1].GetAttribute("data-agent-id").Should().Be("writer");
        foreach (var lane in lanes)
        {
            lane.QuerySelector(".tm-agent-badge").Should().NotBeNull(
                "an agent looks the same on a lane header as anywhere else in the package");
        }
        lanes[0].TextContent.Should().Contain("Planner");
    }

    [Fact]
    public void TheLaneHeaderNamesTheStateThatMattersNow()
    {
        var cut = RenderLanes(Snapshot(
            Block("a", startMinutes: 0, endMinutes: 10),
            Block("b", startMinutes: 40, endMinutes: 50, status: AgentActivityStatus.Queued),
            Block("c", agentId: "writer", startMinutes: 20, endMinutes: 30,
                status: AgentActivityStatus.WaitingForApproval)));

        var statuses = cut.FindAll("[data-testid='agent-lanes-lane-status']");

        statuses.Should().HaveCount(2);
        statuses[0].GetAttribute("data-status").Should().Be("succeeded",
            "a block that has not started yet must not label a finished agent as queued");
        statuses[1].GetAttribute("data-status").Should().Be("approval",
            "nothing moves until the reader acts, so the waiting approval wins");
        statuses[1].TextContent.Should().Contain("Waiting for approval", "the state is a word, not a colour");
    }

    [Fact]
    public void RendersEveryBlockAsANativeButton_WithALocalizedName()
    {
        var cut = RenderLanes(WithHandoff());

        var blocks = Blocks(cut);

        blocks.Should().HaveCount(2);
        blocks.Should().OnlyContain(block => block.TagName == "BUTTON" && block.GetAttribute("type") == "button");

        var label = blocks[0].GetAttribute("aria-label")!;
        label.Should().StartWith("Work a", "the name starts with the activity the reader SEES (WCAG 2.5.3 label in name)")
            .And.Contain("Planner", "and names the agent")
            .And.Contain("Task", "the kind as a word")
            .And.Contain("09:00", "the start in the reader's culture")
            .And.Contain("09:10", "the end")
            .And.Contain("Succeeded", "and the state as a word, never as colour alone");

        blocks[0].GetAttribute("aria-pressed").Should().Be("false");
        blocks[0].GetAttribute("data-status").Should().Be("succeeded");
        blocks[0].GetAttribute("data-kind").Should().Be("task");
    }

    [Fact]
    public void AMarkerIsAnIconAndAWord()
    {
        var cut = RenderLanes(Snapshot(
            Block("a", status: AgentActivityStatus.WaitingForApproval, marker: AgentLaneBlockMarker.Approval)));

        var marker = cut.Find("[data-testid='agent-lanes-block-marker']");

        marker.TextContent.Should().Contain("Approved");
        marker.QuerySelector("svg").Should().NotBeNull("the verdict is never colour-only");
        marker.ClassList.Should().Contain("tm-ai-agent-lanes__marker--approval");
        Blocks(cut)[0].GetAttribute("data-marker").Should().Be("approval");
        Blocks(cut)[0].GetAttribute("aria-label").Should().Contain("Approved");
    }

    [Fact]
    public void ABlockWithoutAMarker_RendersNoMarker()
    {
        var cut = RenderLanes();

        cut.FindAll("[data-testid='agent-lanes-block-marker']").Should().BeEmpty();
        Blocks(cut)[0].HasAttribute("data-marker").Should().BeFalse();
    }

    [Fact]
    public void AnUnknownStateRendersNoStateHook_AndTheUnknownWord()
    {
        var cut = RenderLanes(Snapshot(Block("a", status: (AgentActivityStatus)99)));

        Blocks(cut)[0].HasAttribute("data-status").Should().BeFalse();
        Blocks(cut)[0].GetAttribute("aria-label").Should().Contain("Unknown state");
    }

    [Fact]
    public void AnOpenBlockRunsToNow_AndTheNowLineIsPainted()
    {
        var cut = RenderLanes(Snapshot(
            Block("a", startMinutes: 0, endMinutes: null, status: AgentActivityStatus.Running)));

        Blocks(cut)[0].GetAttribute("data-open").Should().Be("true");
        Blocks(cut)[0].GetAttribute("aria-label").Should().Contain("Now", "the open end is named as the current moment");

        var now = cut.Find("[data-testid='agent-lanes-now']");
        now.TextContent.Should().Contain("Now");
        now.GetAttribute("style").Should().Contain("px");
    }

    [Fact]
    public void TheNowLineFollowsTheInjectedClock()
    {
        var cut = RenderLanes(Snapshot(
            Block("a", startMinutes: 0, endMinutes: null, status: AgentActivityStatus.Running)));

        var before = cut.Find("[data-testid='agent-lanes-now']").GetAttribute("style");

        _clock.Advance(TimeSpan.FromMinutes(20));

        cut.WaitForAssertion(() =>
        {
            var after = cut.Find("[data-testid='agent-lanes-now']").GetAttribute("style");
            after.Should().NotBe(before, "the live line moves with the host's clock, never with DateTimeOffset.UtcNow");
        });
    }

    [Fact]
    public void SelectingABlock_RaisesTheCallback_AndSaysSoOnce()
    {
        var cut = RenderLanes(Snapshot(Block("a"), Block("b", startMinutes: 20, endMinutes: 30), Block("c", startMinutes: 40, endMinutes: 50)));

        Blocks(cut)[1].Click();

        _selectedId.Should().Be("b");
        Blocks(cut)[1].GetAttribute("aria-pressed").Should().Be("true");
        Announcement(cut).Should().Contain("Selected Work b");
        cut.FindAll("[data-testid='agent-lanes-block'][aria-pressed='true']").Should().ContainSingle(
            "exactly one block is selected");
    }

    [Fact]
    public void AHostPinnedSelection_RendersTheDetailRegion()
    {
        var cut = RenderLanes(
            selectedBlockId: "b",
            detailTemplate: block => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "data-detail-id", block.Id);
                builder.AddContent(2, block.Label);
                builder.CloseElement();
            });

        Blocks(cut)[1].GetAttribute("aria-pressed").Should().Be("true");

        var detail = cut.Find("[data-testid='agent-lanes-detail']");
        detail.GetAttribute("role").Should().Be("region");
        detail.QuerySelector("[data-detail-id='b']").Should().NotBeNull();
    }

    [Fact]
    public void TheViewSwitcher_RaisesTheNewMode()
    {
        var cut = RenderLanes();

        var tableOption = cut.Find("[data-testid='agent-lanes-view']")
            .QuerySelectorAll("button")
            .Single(button => button.TextContent.Trim() == "Table");
        tableOption.Click();

        _viewMode.Should().Be(AgentLanesViewMode.Table);
    }

    [Fact]
    public void TheTableMode_CarriesTheSameBlocksAndHandoffs()
    {
        var cut = RenderLanes(WithHandoff(), viewMode: AgentLanesViewMode.Table);

        cut.Find("[data-testid='agent-lanes-table']").Should().NotBeNull();
        cut.FindAll("[data-testid='agent-lanes-table-row']").Should().HaveCount(2);
        cut.FindAll("[data-testid='agent-lanes-handoff-row']").Should().ContainSingle();
        cut.FindAll("[data-testid='agent-lanes-viewport']").Should().BeEmpty(
            "the table replaces the drawing, it does not duplicate it");

        var header = cut.Find("[data-testid='agent-lanes-table'] thead").TextContent;
        header.Should().Contain("Agent").And.Contain("Start").And.Contain("Marker");
        cut.Find("[data-testid='agent-lanes-handoffs-table']").TextContent.Should().Contain("Plan ready");
    }

    [Fact]
    public void TheTableRows_CanSelectABlockToo()
    {
        var cut = RenderLanes(viewMode: AgentLanesViewMode.Table);

        cut.FindAll("[data-testid='agent-lanes-table-select']")[1].Click();

        _selectedId.Should().Be("b", "the text alternative must not lose the surface's interaction");
    }

    [Fact]
    public void ZoomingIn_WidensTheBlocks_AndAnnouncesTheLevel()
    {
        var cut = RenderLanes(Snapshot(Block("a", startMinutes: 0, endMinutes: 30)));

        var before = Blocks(cut)[0].GetAttribute("style");

        cut.Find("[data-testid='agent-lanes-zoom-in']").Click();

        Blocks(cut)[0].GetAttribute("style").Should().NotBe(before, "the block is painted from the new scale");
        _pixelsPerHour.Should().BeApproximately(900, 0.001);
        cut.Find("[data-testid='agent-lanes-zoom-value']").TextContent.Trim().Should().Be("150 %");
        Announcement(cut).Should().Contain("Zoom").And.Contain("150");
    }

    [Fact]
    public void ZoomingOut_ClampsAtTheFloor()
    {
        var cut = RenderLanes(pixelsPerHour: 30);

        for (var press = 0; press < 6; press++)
        {
            cut.Find("[data-testid='agent-lanes-zoom-out']").Click();
        }

        _pixelsPerHour.Should().Be(24);
        cut.Find("[data-testid='agent-lanes-zoom-value']").TextContent.Trim().Should().Be("4 %");
    }

    [Fact]
    public void FitToWidth_MakesTheRunOccupyTheMeasuredRoom()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.AI/js/tm-agent-lanes.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<double>("measureWidth", _ => true).SetResult(1000);

        var cut = RenderLanes(pixelsPerHour: 600);

        cut.Find("[data-testid='agent-lanes-zoom-fit']").Click();

        var usable = 1000 - 176;
        cut.Instance.Layout.WidthPx.Should().BeApproximately(usable, 1,
            "fit puts the whole run inside the room the reader has");
        _pixelsPerHour.Should().NotBeNull("the reader's new scale is reported to the host once");
    }

    /// <summary>
    /// Plan 7ba95ed6, Fáze 10 — the measured half of "fit to width" in STRICT interop mode, so a call the component makes
    /// that the test did not declare fails the render instead of passing silently. The package's own module path is
    /// asserted (never a core helper), the scrollport is handed over as an <see cref="ElementReference"/> exactly once,
    /// and the measured room is PROJECTED onto the run: the block width ends up equal to the room minus the sticky agent
    /// column. That projection is what the Server and InteractiveAuto browser lanes later read back from the DOM, and
    /// the reason it is pinned here is that a circuit round trip cannot prove which number the component used.
    /// </summary>
    [Fact]
    public void FitToWidth_MeasuresTheScrollportThroughThePackageModule_AndProjectsTheRoom()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.AI/js/tm-agent-lanes.js");
        // The gesture handle is a JS OBJECT, so it gets its own module handler (bUnit refuses a bare
        // Setup<IJSObjectReference>) and its detach is declared or strict mode fails the assertion at dispose.
        var handle = module.SetupModule("attachLanesZoom", _ => true);
        handle.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<double>("measureWidth", _ => true).SetResult(1000);

        // The Visual | Table switch is the core segmented control, which measures its own tray through its module.
        var segmented = JSInterop.SetupModule("./_content/Tempo.Blazor/Components/Inputs/TmSegmentedControl.razor.js");
        segmented.Mode = JSRuntimeMode.Loose;
        segmented.SetupVoid("place", _ => true).SetVoidResult();
        segmented.SetupVoid("focusOption", _ => true).SetVoidResult();
        segmented.SetupVoid("release", _ => true).SetVoidResult();

        var cut = RenderLanes(pixelsPerHour: 600);

        cut.Find("[data-testid='agent-lanes-zoom-fit']").Click();

        var measure = module.Invocations.Single(invocation => invocation.Identifier == "measureWidth");
        measure.Arguments.Should().ContainSingle("the module needs the scrollport and nothing else");
        measure.Arguments[0].Should().BeOfType<ElementReference>("the scrollport is measured where the reader reads");
        ((ElementReference)measure.Arguments[0]!).Id.Should().NotBeNullOrEmpty("the reference must point at a real element");

        cut.Instance.Layout.WidthPx.Should().BeApproximately(1000 - 176, 1,
            "the projection is the measured room minus the sticky agent column");
        cut.Find("[data-testid='agent-lanes-zoom-value']").TextContent.Trim()
            .Should().NotBe("100 %", "the fit really changed the scale the reader sees");
        _pixelsPerHour.Should().NotBeNull("and the one final scale is reported to the host");
    }

    [Fact]
    public void FitToWidth_WithoutAMeasurableViewport_KeepsTheZoom()
    {
        var cut = RenderLanes(pixelsPerHour: 600);

        cut.Find("[data-testid='agent-lanes-zoom-fit']").Click();

        _pixelsPerHour.Should().BeNull("nothing was measured, so the component keeps the reader's zoom");
        cut.Find("[data-testid='agent-lanes-zoom-value']").TextContent.Trim().Should().Be("100 %");
    }

    [Fact]
    public async Task Events_AreAppliedOnTopOfTheSnapshot_AndStayIdempotent()
    {
        IReadOnlyList<AgentLaneEvent> batch =
        [
            new AgentLaneEvent(101) { Block = Block("c", startMinutes: 30, endMinutes: 40) },
            new AgentLaneEvent(102) { Kind = AgentLaneEventKind.HandoffAdded, Handoff = new AgentHandoffLink("writer", Nine.AddMinutes(30)) { FromBlockId = "c" } },
        ];

        var cut = RenderLanes(events: batch);

        Blocks(cut).Should().HaveCount(3);
        cut.Instance.View.Blocks.Should().HaveCount(3);
        cut.Instance.Layout.Handoffs.Should().ContainSingle();

        await UpdateAsync(cut, (nameof(TmAgentLanes.Events), batch));

        Blocks(cut).Should().HaveCount(3, "the shared stream drops an event that was already applied");
        cut.Instance.View.Handoffs.Should().ContainSingle();
    }

    [Fact]
    public async Task ARepeatedParameterSet_DoesNotReplayTheAcceptedTail()
    {
        var cut = RenderLanes(events: [new AgentLaneEvent(101) { Block = Block("c", startMinutes: 30, endMinutes: 40) }]);
        var view = cut.Instance.View;

        await UpdateAsync(cut, (nameof(TmAgentLanes.PixelsPerHour), 900d));
        await UpdateAsync(cut, (nameof(TmAgentLanes.PixelsPerHour), 600d));

        cut.Instance.View.Should().BeSameAs(view,
            "an accepted event is folded into the view once; a later parameter set must not replay the whole tail");
        Blocks(cut).Should().HaveCount(3);
    }

    [Fact]
    public void AStreamHole_IsReportedToTheHost_AndAnnounced()
    {
        var cut = RenderLanes(events: [new AgentLaneEvent(102) { Block = Block("c") }]);

        _gapExpected.Should().Be(101, "the host is told the first sequence the stream does not have");
        Blocks(cut).Should().HaveCount(2, "a batch with a hole is rejected whole instead of papered over");
        Announcement(cut).Should().Contain("behind");
    }

    [Fact]
    public async Task TheSameStreamHole_IsReportedToTheHostOnce()
    {
        var batch = new[] { new AgentLaneEvent(102) { Block = Block("c") } };
        var cut = RenderLanes(events: batch);

        _gapCount.Should().Be(1);
        _gapExpected.Should().Be(101);

        await UpdateAsync(cut, (nameof(TmAgentLanes.Events), batch));
        await UpdateAsync(cut, (nameof(TmAgentLanes.Events), batch));

        _gapCount.Should().Be(1,
            "a render that keeps handing the same stale batch must not ask the host to reload again and again");
    }

    [Fact]
    public async Task TheEventPaintIsThrottled_WhileTheStateIsAbsorbed()
    {
        var cut = RenderLanes(renderThrottle: TimeSpan.FromMilliseconds(100));

        await UpdateAsync(cut, (nameof(TmAgentLanes.Events),
            new[] { new AgentLaneEvent(101) { Block = Block("c", startMinutes: 30, endMinutes: 40) } }));

        cut.Instance.View.Blocks.Should().HaveCount(3, "every event is absorbed, only the PAINT is throttled");
        Blocks(cut).Should().HaveCount(2, "the first batch after a paint falls inside the gate's window");

        _clock.Advance(TimeSpan.FromMilliseconds(150));

        cut.WaitForAssertion(() => Blocks(cut).Should().HaveCount(3, "the trailing paint catches the surface up"));
    }

    [Fact]
    public async Task ANewSnapshot_ReplaysTheEventsOnTopOfIt()
    {
        var cut = RenderLanes(events: [new AgentLaneEvent(101) { Block = Block("c", startMinutes: 30, endMinutes: 40) }]);

        var reloaded = Snapshot(Block("a"), Block("b"), Block("c", startMinutes: 30, endMinutes: 40)) with
        {
            Sequence = 101,
        };

        await UpdateAsync(cut, (nameof(TmAgentLanes.Snapshot), reloaded));

        cut.Instance.View.Sequence.Should().Be(101);
        Blocks(cut).Should().HaveCount(3);
    }

    [Fact]
    public void HandoffsAreDrawn_AndCanBeSwitchedOff()
    {
        var cut = RenderLanes(WithHandoff());

        cut.FindAll("[data-testid='agent-lanes-handoffs'] polyline").Should().ContainSingle()
            .Which.GetAttribute("points").Should().NotBeNullOrWhiteSpace();

        var without = RenderLanes(WithHandoff(), showHandoffs: false);
        without.FindAll("[data-testid='agent-lanes-handoffs']").Should().BeEmpty();
    }

    [Fact]
    public void AnEmptySnapshot_RendersTheEmptyState()
    {
        var cut = RenderLanes(new AgentLanesSnapshot { Agents = [Planner] });

        cut.Find("[data-testid='agent-lanes-empty']").TextContent.Should().Contain("No agent activity");
        cut.FindAll("[data-testid='agent-lanes-viewport']").Should().BeEmpty();
    }

    [Fact]
    public void TheLastTickLabelIsFlipped_SoTheAxisAddsNoOverflow()
    {
        var cut = RenderLanes();

        var ticks = cut.FindAll("[data-testid='agent-lanes-tick']");

        ticks.Should().NotBeEmpty();
        ticks[^1].ClassList.Should().Contain("tm-ai-agent-lanes__tick--end",
            "a label hanging off the aligned end would create a scrollbar the surface never wants");
        ticks[0].ClassList.Should().NotContain("tm-ai-agent-lanes__tick--end");
    }

    [Fact]
    public void TheInstructionsDescribeTheBlocks_AndAreWiredToTheScrollport()
    {
        var cut = RenderLanes();

        var instructions = cut.Find("[data-testid='agent-lanes-instructions']");
        instructions.TextContent.Should().Contain("Tab").And.Contain("Enter");

        var viewport = cut.Find("[data-testid='agent-lanes-viewport']");
        viewport.GetAttribute("aria-describedby").Should().Be(instructions.Id);
        viewport.GetAttribute("tabindex").Should().Be("0", "the scroller itself is reachable by keyboard");
    }

    [Fact]
    public void TheTitleFollowsTheHeadingParameters()
    {
        var cut = RenderLanes(showTitle: false, headingLevel: 2);
        cut.FindAll(".tm-ai-agent-lanes__title").Should().BeEmpty("the host hides the heading");

        var titled = RenderLanes(headingLevel: 2);
        titled.Find(".tm-ai-agent-lanes__title").TagName.Should().Be("H2");
        titled.Find(".tm-ai-agent-lanes__title").TextContent.Should().Contain("Agent lanes");
    }

    [Fact]
    public void TheRootExposesTheViewAndTheZoomLevel()
    {
        var cut = RenderLanes();

        var root = cut.Find("[data-testid='agent-lanes']");
        root.GetAttribute("data-view").Should().Be("timeline");
        root.GetAttribute("data-zoom").Should().Be("100");

        var table = RenderLanes(viewMode: AgentLanesViewMode.Table);
        table.Find("[data-testid='agent-lanes']").GetAttribute("data-view").Should().Be("table");
        table.FindAll("[data-testid='agent-lanes-zoom']").Should().BeEmpty("the table has no scale to zoom");
    }

    [Fact]
    public void ABlockNamingAnUndeclaredAgent_StillRendersInItsOwnLane()
    {
        var cut = RenderLanes(new AgentLanesSnapshot
        {
            Agents = [Planner],
            Blocks = [Block("a"), Block("q", agentId: "qa", startMinutes: 20, endMinutes: 30)],
        });

        cut.FindAll("[data-testid='agent-lanes-lane']").Should().HaveCount(2);
        Blocks(cut).Should().HaveCount(2, "work is never dropped silently");
        cut.FindAll("[data-testid='agent-lanes-lane']")[1].GetAttribute("data-agent-id").Should().Be("qa");
    }
}
