using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 3 — <c>TmAgentGraph</c> (Tempo.Blazor.AI): agents and the tools they call as one drawn network,
/// where an edge's thickness is how often it was called and a call is a short pulse.
/// <para>
/// The tests read behaviour, not markup: the canvas is an SVG <c>role="group"</c> whose nodes are focusable buttons
/// named by their kind, state and call count, the ARROW keys walk the actual CONNECTIONS (not the DOM order), Enter and
/// Space select, a selection highlights the neighbours, a waiting approval is an icon AND a word, a new
/// <see cref="AgentGraphCall"/> pulses its edge for a bounded time with a bounded number of concurrent pulses, the
/// incremental events are applied through the shared stream (idempotently, with a hole reported instead of swallowed)
/// and the table view carries the SAME nodes and connections.
/// </para>
/// </summary>
public class TmAgentGraphTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";
    private const string ModulePath = "./_content/Tempo.Blazor.AI/js/tm-agent-graph.js";

    private static readonly DateTimeOffset Ten = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock;
    private string? _selectedId;
    private AgentGraphViewMode? _viewMode;
    private double? _zoom;
    private long? _gapExpected;
    private int _gapCount;

    /// <summary>
    /// The pinned clock every render reads: the pulse window is a duration, so a test has to be able to close it without
    /// sleeping, and the paint gate has to advance deterministically.
    /// </summary>
    public TmAgentGraphTests()
    {
        _clock = new FakeTimeProvider(Ten);
        Services.AddSingleton<TimeProvider>(_clock);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static AgentGraphNode Agent(string id, string label, AgentActivityStatus status = AgentActivityStatus.Running,
        string? group = null, int color = 0)
        => new(id, label)
        {
            Kind = AgentGraphNodeKind.Agent,
            Status = status,
            Group = group,
            Agent = new AgentRef(label, id) { ColorIndex = color },
        };

    private static AgentGraphNode Tool(string id, string label, AgentActivityStatus status = AgentActivityStatus.Idle)
        => new(id, label) { Kind = AgentGraphNodeKind.Tool, Status = status };

    private static AgentGraphNode Resource(string id, string label)
        => new(id, label) { Kind = AgentGraphNodeKind.Resource };

    private static AgentGraphNode Human(string id, string label)
        => new(id, label) { Kind = AgentGraphNodeKind.Human };

    private static AgentGraphEdge Edge(string id, string from, string to, int calls = 1, DateTimeOffset? last = null)
        => new(id, from, to) { CallCount = calls, LastCallAt = last ?? Ten.AddMinutes(-5), IsActive = true };

    /// <summary>The base graph of these tests: a human, two agents, a resource and two tools.</summary>
    private static AgentGraphSnapshot Snapshot() => new()
    {
        Sequence = 10,
        Nodes =
        [
            Human("human", "Dana"),
            Agent("planner", "Planner", group: "Core"),
            Agent("writer", "Writer", AgentActivityStatus.WaitingForApproval, group: "Core", color: 1),
            Resource("ledger", "Ledger"),
            Tool("search", "Web search", AgentActivityStatus.Succeeded),
            Tool("code", "Code interpreter"),
        ],
        Edges =
        [
            Edge("e-plan", "human", "planner"),
            Edge("e-search", "planner", "search", calls: 3),
            Edge("e-ledger", "writer", "ledger", calls: 12),
            Edge("e-code", "planner", "code", calls: 1),
            Edge("e-delegate", "planner", "writer"),
        ],
    };

    private IRenderedComponent<TmAgentGraph> RenderGraph(
        AgentGraphSnapshot? snapshot = null,
        IReadOnlyList<AgentGraphCall>? events = null,
        AgentGraphViewMode? viewMode = null,
        string? selectedNodeId = null,
        RenderFragment<AgentGraphNode>? detailTemplate = null,
        bool showTitle = true,
        int headingLevel = 3,
        int maxPulses = 6,
        TimeSpan? pulseDuration = null,
        TimeSpan? renderThrottle = null,
        IReadOnlyDictionary<string, AgentGraphPosition>? nodePositions = null,
        double zoom = 1)
    {
        _selectedId = null;
        _viewMode = null;
        _zoom = null;
        _gapCount = 0;

        return Render<TmAgentGraph>(parameters =>
        {
            parameters.Add(component => component.Snapshot, snapshot ?? Snapshot());
            parameters.Add(component => component.Culture, CultureInfo.InvariantCulture);
            parameters.Add(component => component.TimeProvider, _clock);
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.MaxPulses, maxPulses);
            parameters.Add(component => component.Zoom, zoom);
            parameters.Add(component => component.SelectedNodeIdChanged, (string? id) => _selectedId = id);
            parameters.Add(component => component.ViewModeChanged, (AgentGraphViewMode mode) => _viewMode = mode);
            parameters.Add(component => component.ZoomChanged, (double value) => _zoom = value);
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

            if (selectedNodeId is not null)
            {
                parameters.Add(component => component.SelectedNodeId, selectedNodeId);
            }

            if (detailTemplate is not null)
            {
                parameters.Add(component => component.DetailTemplate, detailTemplate);
            }

            if (pulseDuration is not null)
            {
                parameters.Add(component => component.PulseDuration, pulseDuration.Value);
            }

            if (renderThrottle is not null)
            {
                parameters.Add(component => component.RenderThrottle, renderThrottle.Value);
            }

            if (nodePositions is not null)
            {
                parameters.Add(component => component.NodePositions, nodePositions);
            }
        });
    }

    /// <summary>Re-renders the SAME instance with the given parameters, the way a host's parent component does.</summary>
    private static Task UpdateAsync(IRenderedComponent<TmAgentGraph> cut, params (string Name, object? Value)[] values)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            values.ToDictionary(value => value.Name, value => value.Value))));

    private static IReadOnlyList<AngleSharp.Dom.IElement> Nodes(IRenderedComponent<TmAgentGraph> cut)
        => cut.FindAll("[data-testid='agent-graph-node']");

    private static AngleSharp.Dom.IElement Node(IRenderedComponent<TmAgentGraph> cut, string nodeId)
        => cut.Find($"[data-testid='agent-graph-node'][data-node-id='{nodeId}']");

    private static string Announcement(IRenderedComponent<TmAgentGraph> cut)
        => cut.Find("[data-testid='agent-graph-announcement']").TextContent;

    private static string ZoomValue(IRenderedComponent<TmAgentGraph> cut)
        => cut.Find("[data-testid='agent-graph-zoom-value']").TextContent.Trim();

    /// <summary>The focus invocations the component made, newest last.</summary>
    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (invocation.Arguments[0] as ElementReference?)?.Id)
            .ToList();

    // ── The empty graph ──────────────────────────────────────────────────────

    [Fact]
    public void AnEmptySnapshot_RendersTheLocalizedEmptyState_AndNoCanvas()
    {
        var cut = RenderGraph(new AgentGraphSnapshot());

        cut.Find("[data-testid='agent-graph-empty']").TextContent.Should().Contain("No agent graph");
        cut.FindAll("[data-testid='agent-graph-canvas']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-graph-viewport']").Should().BeEmpty();
    }

    // ── The canvas ───────────────────────────────────────────────────────────

    [Fact]
    public void TheViewport_IsTheOneLabelledGroup_AndTheCanvasAddsNoSecondStop()
    {
        var cut = RenderGraph();

        var viewport = cut.Find("[data-testid='agent-graph-viewport']");
        viewport.GetAttribute("role").Should().Be("group", "the graph is a labelled group, not a picture");
        viewport.GetAttribute("aria-label").Should().Be("Agent graph");

        var instructions = cut.Find("[data-testid='agent-graph-instructions']").Id;
        var described = cut.FindAll($"[aria-describedby='{instructions}']");
        described.Should().ContainSingle(
            "a screen reader hears the surface named and its instructions read ONCE, not once per nesting level");
        described[0].GetAttribute("data-testid").Should().Be("agent-graph-viewport",
            "the scrollport is the one labelled surface; the drawing inside it is an unlabelled container");

        viewport.GetAttribute("tabindex").Should().Be("-1",
            "the nodes carry the graph's ONE tab stop, so the viewport is focusable only programmatically");
        viewport.QuerySelectorAll("[tabindex='0']").Should().ContainSingle(
            "the whole drawing adds exactly ONE tab stop: the node that owns the roving tabindex");

        var canvas = cut.Find("[data-testid='agent-graph-canvas']");
        canvas.TagName.Should().Be("svg", "the drawing is one SVG scene");
        canvas.GetAttribute("role").Should().BeNull(
            "a second role=group would repeat the name and the instructions and add a tab stop in front of the nodes");
        canvas.GetAttribute("aria-label").Should().BeNull();
        canvas.GetAttribute("aria-describedby").Should().BeNull();
    }

    [Fact]
    public void EveryNode_IsAFocusableButton_WithAComposedAccessibleName()
    {
        var cut = RenderGraph();

        var nodes = Nodes(cut);
        nodes.Should().HaveCount(6);
        foreach (var node in nodes)
        {
            node.TagName.Should().Be("BUTTON", "a node is a NATIVE button: Enter/Space, the focus ring and the tap "
                + "target come from the platform instead of an emulation");
            node.GetAttribute("tabindex").Should().NotBeNull("a node is in the tab order or parked just outside it");
        }

        var search = Node(cut, "search");
        var label = search.GetAttribute("aria-label")!;
        label.Should().StartWith("Web search", "a voice-control user says what they see");
        label.Should().Contain("Tool").And.Contain("Succeeded").And.Contain("Calls: 3");
    }

    [Fact]
    public void ExactlyOneNode_IsInTheTabOrder_SoTheGraphIsOneTabStop()
    {
        var cut = RenderGraph();

        var tabbable = Nodes(cut).Where(node => node.GetAttribute("tabindex") == "0").ToList();

        tabbable.Should().ContainSingle("a graph is a roving-tabindex group, not six tab stops");
        tabbable[0].GetAttribute("data-node-id").Should().Be("human", "the first node by painting order starts it");
    }

    // ── Keyboard navigation along the edges ──────────────────────────────────

    [Fact]
    public void TheRightArrow_MovesAlongAConnection_ToTheNodeOnTheRight()
    {
        var cut = RenderGraph();

        var planner = Node(cut, "planner");
        var plannerReference = planner.GetAttribute("blazor:elementreference");
        planner.Focus();
        planner.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Node(cut, "search").GetAttribute("tabindex").Should().Be("0",
            "the arrow moves the roving tab stop with the focus");
        Node(cut, "planner").GetAttribute("tabindex").Should().Be("-1");

        var focusTargets = FocusTargets();
        focusTargets.Should().ContainSingle("the reader's focus has to follow the arrow");
        focusTargets[^1].Should().NotBeNullOrEmpty();
        focusTargets[^1].Should().NotBe(plannerReference, "the focus left the node the reader pressed the key on");
    }

    [Fact]
    public void TheLeftArrow_MovesBack_AlongTheSameConnection()
    {
        var cut = RenderGraph(selectedNodeId: null);

        Node(cut, "search").Focus();
        Node(cut, "search").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Node(cut, "planner").GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void AnArrowWithoutAConnectionInThatDirection_LeavesTheFocusWhereItIs()
    {
        var cut = RenderGraph();

        Node(cut, "human").Focus();
        Node(cut, "human").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Node(cut, "human").GetAttribute("tabindex").Should().Be("0");
        FocusTargets().Should().BeEmpty("nothing moved, so the component must not steal the focus");
    }

    [Fact]
    public void TheArrowKeysNeverFollowTheDeclarationOrder_OnlyTheConnections()
    {
        // "code" is declared after "search" but only the planner calls it; the human is connected to the planner alone,
        // so ArrowRight must never reach the tool the human does not talk to.
        var cut = RenderGraph(new AgentGraphSnapshot
        {
            Nodes = [Human("human", "Dana"), Agent("planner", "Planner"), Tool("search", "Web search")],
            Edges = [Edge("e1", "human", "planner")],
        });

        Node(cut, "human").Focus();
        Node(cut, "human").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Node(cut, "planner").GetAttribute("tabindex").Should().Be("0");
        Node(cut, "search").GetAttribute("tabindex").Should().Be("-1",
            "the human has no connection to the tool");
    }

    [Fact]
    public void HomeAndEnd_JumpToTheFirstAndTheLastNode()
    {
        var cut = RenderGraph();

        Node(cut, "human").Focus();
        Node(cut, "human").KeyDown(new KeyboardEventArgs { Key = "End" });
        Node(cut, "code").GetAttribute("tabindex").Should().Be("0");

        Node(cut, "code").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Node(cut, "human").GetAttribute("tabindex").Should().Be("0");
    }

    // ── Selection ────────────────────────────────────────────────────────────

    [Fact]
    public void ANativeButtonNode_IsNeverActivatedTwice()
    {
        var cut = RenderGraph();

        // The button's own activation (Enter/Space -> click) selects exactly once, so the component must NOT emulate
        // those keys: a keydown alone changes nothing here, which is the proof it does not.
        Node(cut, "planner").Focus();
        Node(cut, "planner").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Node(cut, "planner").KeyDown(new KeyboardEventArgs { Key = " " });

        _selectedId.Should().BeNull("the component never emulates Enter or Space on a native button");

        Node(cut, "planner").Click();

        _selectedId.Should().Be("planner");
        Node(cut, "planner").GetAttribute("aria-pressed").Should().Be("true");
        Announcement(cut).Should().Contain("Planner", "one live region says what was selected");
    }

    [Fact]
    public void ClickingANode_SelectsIt()
    {
        var cut = RenderGraph();

        Node(cut, "ledger").Click();

        _selectedId.Should().Be("ledger");
        Node(cut, "ledger").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void TheSelectionHighlightsItsNeighbours_AndDimsEverythingElse()
    {
        var cut = RenderGraph(selectedNodeId: "planner");

        Node(cut, "planner").ClassList.Should().Contain("tm-ai-agent-graph__node--selected");
        Node(cut, "search").ClassList.Should().Contain("tm-ai-agent-graph__node--neighbor");
        Node(cut, "writer").ClassList.Should().Contain("tm-ai-agent-graph__node--neighbor");
        Node(cut, "human").ClassList.Should().Contain("tm-ai-agent-graph__node--neighbor",
            "the edge points INTO the planner, so the human is a neighbour too");
        Node(cut, "code").ClassList.Should().Contain("tm-ai-agent-graph__node--neighbor");
        Node(cut, "ledger").ClassList.Should().Contain("tm-ai-agent-graph__node--dimmed",
            "the ledger is two hops away and is dimmed, not hidden");
    }

    [Fact]
    public void TheSelectedNode_KeepsTheHighlightOnItsEdges()
    {
        var cut = RenderGraph(selectedNodeId: "planner");

        var highlighted = cut.FindAll("[data-testid='agent-graph-edge']")
            .Where(edge => edge.ClassList.Contains("tm-ai-agent-graph__edge--highlighted"))
            .Select(edge => edge.GetAttribute("data-edge-id"))
            .ToList();

        highlighted.Should().BeEquivalentTo(["e-search", "e-code", "e-delegate", "e-plan"]);
    }

    [Fact]
    public void TheDetailTemplate_RendersTheSelectedNode_InANamedRegion()
    {
        var cut = RenderGraph(
            selectedNodeId: "search",
            detailTemplate: node => builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "data-testid", "detail-body");
                builder.AddContent(2, node.Label + " detail");
                builder.CloseElement();
            });

        cut.Find("[data-testid='agent-graph-detail']").GetAttribute("role").Should().Be("region");
        cut.Find("[data-testid='agent-graph-detail']").GetAttribute("aria-label").Should().Be("Node detail");
        cut.Find("[data-testid='detail-body']").TextContent.Should().Be("Web search detail");
    }

    [Fact]
    public void WithoutADetailTemplate_NothingIsRenderedBelowTheCanvas()
    {
        var cut = RenderGraph(selectedNodeId: "search");

        cut.FindAll("[data-testid='agent-graph-detail']").Should().BeEmpty();
    }

    // ── States, kinds and labels ─────────────────────────────────────────────

    [Fact]
    public void AWaitingForApprovalNode_IsHighlighted_WithAnIconAndAWord()
    {
        var cut = RenderGraph();

        var writer = Node(cut, "writer");

        writer.ClassList.Should().Contain("tm-ai-agent-graph__node--approval");
        writer.GetAttribute("data-status").Should().Be("approval");
        writer.QuerySelector(".tm-ai-agent-graph__node-state-icon").Should().NotBeNull(
            "a state is never a colour alone");
        writer.QuerySelector(".tm-ai-agent-graph__node-state-text")!.TextContent
            .Should().Be("Waiting for approval");
        writer.GetAttribute("aria-label").Should().Contain("Waiting for approval");
    }

    [Fact]
    public void EveryNodeKind_CarriesItsOwnStableHook_AndItsWord()
    {
        var cut = RenderGraph();

        Node(cut, "human").GetAttribute("data-kind").Should().Be("human");
        Node(cut, "planner").GetAttribute("data-kind").Should().Be("agent");
        Node(cut, "ledger").GetAttribute("data-kind").Should().Be("resource");
        Node(cut, "search").GetAttribute("data-kind").Should().Be("tool");

        Node(cut, "human").GetAttribute("aria-label").Should().Contain("Human");
        Node(cut, "ledger").GetAttribute("aria-label").Should().Contain("Resource");
    }

    [Fact]
    public void AnAgentNode_PaintsTheAgentsOwnInitials_AndAToolPaintsItsGlyph()
    {
        var cut = RenderGraph();

        Node(cut, "planner").QuerySelector(".tm-ai-agent-graph__node-initials")!.TextContent.Should().Be("P");
        Node(cut, "search").QuerySelector(".tm-ai-agent-graph__node-initials").Should().BeNull(
            "a tool has no name initials — it paints its kind glyph instead");
        Node(cut, "search").QuerySelector(".tm-ai-agent-graph__node-kind-icon").Should().NotBeNull();
    }

    [Fact]
    public void ALongLabel_IsWrappedIntoTwoLines_AndTheFullLabelStaysReachable()
    {
        var cut = RenderGraph(new AgentGraphSnapshot
        {
            Nodes = [Agent("planner", "Review the reconciliation report of the whole ledger")],
        });

        var node = Node(cut, "planner");
        node.QuerySelectorAll(".tm-ai-agent-graph__node-label").Should().HaveCount(2);
        node.GetAttribute("title").Should().Contain("the whole ledger",
            "the tooltip carries what the box had to ellipsize");
        node.GetAttribute("aria-label").Should().Contain("the whole ledger");
    }

    [Fact]
    public void ANodeWithoutALabel_FallsBackToTheLocalizedKindWord()
    {
        var cut = RenderGraph(new AgentGraphSnapshot { Nodes = [new AgentGraphNode("ghost", "  ") { Kind = AgentGraphNodeKind.Tool }] });

        Node(cut, "ghost").GetAttribute("aria-label").Should().StartWith("Tool");
    }

    [Fact]
    public void TheGroupedNodes_ArePaintedInsideALabelledBox()
    {
        var cut = RenderGraph();

        var group = cut.FindAll("[data-testid='agent-graph-group']").Should().ContainSingle().Subject;
        group.GetAttribute("data-group").Should().Be("Core");
        group.QuerySelector(".tm-ai-agent-graph__group-label")!.TextContent.Trim().Should().Be("Core");
    }

    // ── Pinned positions ─────────────────────────────────────────────────────

    [Fact]
    public void APinnedPosition_IsHonouredExactly()
    {
        var cut = RenderGraph(nodePositions: new Dictionary<string, AgentGraphPosition>(StringComparer.Ordinal)
        {
            ["search"] = new AgentGraphPosition(420, 300),
        });

        cut.Find("[data-node-frame='search']").GetAttribute("transform").Should().Be("translate(420 300)");
        Node(cut, "search").GetAttribute("data-pinned").Should().Be("true");
        Node(cut, "planner").GetAttribute("data-pinned").Should().BeNull();
    }

    // ── Pulses ───────────────────────────────────────────────────────────────

    [Fact]
    public void ACall_PulsesItsEdge_AndThePulseEndsWithItsWindow()
    {
        var cut = RenderGraph(
            events: [new AgentGraphCall(11) { EdgeId = "e-search", At = Ten }],
            pulseDuration: TimeSpan.FromMilliseconds(400));

        var edge = cut.Find("[data-testid='agent-graph-edge'][data-edge-id='e-search']");
        edge.GetAttribute("data-pulse").Should().Be("true");
        edge.ClassList.Should().Contain("tm-ai-agent-graph__edge--pulse");

        _clock.Advance(TimeSpan.FromMilliseconds(500));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='agent-graph-edge'][data-edge-id='e-search']")
                .GetAttribute("data-pulse").Should().BeNull("the highlight is short, not permanent"));
    }

    [Fact]
    public void ThePulseWindow_ReachesTheStylesheet_FromTheSameParameter()
    {
        var cut = RenderGraph(pulseDuration: TimeSpan.FromMilliseconds(250));

        cut.Find("[data-testid='agent-graph']").GetAttribute("style").Should()
            .Contain("--tm-agent-graph-pulse-duration: 250ms",
                "the CSS animation length is written from PulseDuration, so ONE value decides how long the pulse lasts");

        var defaultCut = RenderGraph();

        defaultCut.Find("[data-testid='agent-graph']").GetAttribute("style").Should()
            .Contain("--tm-agent-graph-pulse-duration: 600ms",
                "even the default window reaches the stylesheet, so a host never has to restyle the knob");
    }

    [Fact]
    public void WithoutACall_NothingPulses()
    {
        var cut = RenderGraph();

        foreach (var edge in cut.FindAll("[data-testid='agent-graph-edge']"))
        {
            edge.GetAttribute("data-pulse").Should().BeNull("no call arrived, so nothing pulses");
        }
    }

    [Fact]
    public void ConcurrentPulses_AreCapped_SoABurstCannotLightUpTheWholeGraph()
    {
        var snapshot = new AgentGraphSnapshot
        {
            Sequence = 10,
            Nodes =
            [
                Agent("planner", "Planner"),
                Tool("t1", "One"),
                Tool("t2", "Two"),
                Tool("t3", "Three"),
                Tool("t4", "Four"),
            ],
            Edges =
            [
                Edge("e1", "planner", "t1"),
                Edge("e2", "planner", "t2"),
                Edge("e3", "planner", "t3"),
                Edge("e4", "planner", "t4"),
            ],
        };

        var cut = RenderGraph(
            snapshot,
            events:
            [
                new AgentGraphCall(11) { EdgeId = "e1" },
                new AgentGraphCall(12) { EdgeId = "e2" },
                new AgentGraphCall(13) { EdgeId = "e3" },
                new AgentGraphCall(14) { EdgeId = "e4" },
            ],
            maxPulses: 2);

        var pulsing = cut.FindAll("[data-testid='agent-graph-edge']")
            .Where(edge => edge.GetAttribute("data-pulse") == "true")
            .Select(edge => edge.GetAttribute("data-edge-id"))
            .ToList();

        pulsing.Should().Equal(["e3", "e4"], "only the newest calls pulse, and only up to the cap");
    }

    [Fact]
    public void ACallThickensItsEdge_SoTheStrokeGrows()
    {
        var cut = RenderGraph(events: [new AgentGraphCall(11) { EdgeId = "e-code", Count = 40 }]);

        var stroke = double.Parse(
            cut.Find("[data-testid='agent-graph-edge'][data-edge-id='e-code']").GetAttribute("stroke-width")!,
            CultureInfo.InvariantCulture);

        stroke.Should().BeGreaterThan(AgentGraphLayout.MaxEdgeWidthPx - 0.01,
            "forty calls make the busiest connection of the graph");
    }

    // ── The live contract ────────────────────────────────────────────────────

    [Fact]
    public async Task Events_AreAppliedOnTopOfTheSnapshot_AndStayIdempotent()
    {
        IReadOnlyList<AgentGraphCall> batch = [new AgentGraphCall(11) { EdgeId = "e-search", At = Ten }];

        var cut = RenderGraph(events: batch);

        cut.Instance.View.Edges.Single(edge => edge.Id == "e-search").CallCount.Should().Be(4);

        await UpdateAsync(cut, (nameof(TmAgentGraph.Events), batch));

        cut.Instance.View.Edges.Single(edge => edge.Id == "e-search").CallCount
            .Should().Be(4, "a redelivered batch changes nothing");
    }

    [Fact]
    public async Task AGap_IsReportedOnce_WithTheFirstMissingSequence()
    {
        var cut = RenderGraph(events: [new AgentGraphCall(11) { EdgeId = "e-search" }]);

        await UpdateAsync(cut, (nameof(TmAgentGraph.Events), new[] { new AgentGraphCall(14) { EdgeId = "e-search" } }));

        _gapCount.Should().Be(1);
        _gapExpected.Should().Be(12, "the host is told the first sequence the stream does NOT have");

        // The announcement is a PAINT, and an event-only update is gated: the trailing paint closes the window.
        _clock.Advance(TimeSpan.FromMilliseconds(150));
        cut.WaitForAssertion(() => Announcement(cut).Should().Contain("Live updates are behind"));
    }

    [Fact]
    public async Task ANewSnapshot_RebasesTheStream()
    {
        var cut = RenderGraph(events: [new AgentGraphCall(11) { EdgeId = "e-search" }]);

        var reloaded = Snapshot() with
        {
            Sequence = 11,
            Edges =
            [
                Edge("e-plan", "human", "planner"),
                Edge("e-search", "planner", "search", calls: 9),
                Edge("e-ledger", "writer", "ledger", calls: 12),
                Edge("e-code", "planner", "code"),
                Edge("e-delegate", "planner", "writer"),
            ],
        };

        await UpdateAsync(cut, (nameof(TmAgentGraph.Snapshot), reloaded));

        cut.Instance.View.Sequence.Should().Be(11);
        Node(cut, "search").GetAttribute("aria-label").Should().Contain("Calls: 9");
    }

    [Fact]
    public async Task TheEventPaintIsThrottled_WhileTheStateIsAbsorbed()
    {
        var cut = RenderGraph(renderThrottle: TimeSpan.FromMilliseconds(100));

        await UpdateAsync(cut, (nameof(TmAgentGraph.Events),
            new[] { new AgentGraphCall(11) { EdgeId = "e-search" } }));

        cut.Instance.View.Edges.Single(edge => edge.Id == "e-search").CallCount.Should().Be(4,
            "every event is absorbed; only the PAINT is gated");

        _clock.Advance(TimeSpan.FromMilliseconds(150));

        cut.WaitForAssertion(() => Node(cut, "search").GetAttribute("aria-label").Should().Contain("Calls: 4"));
    }

    // ── The text alternative ─────────────────────────────────────────────────

    [Fact]
    public void TheTableMode_CarriesEveryConnection_WithItsCallCount()
    {
        var cut = RenderGraph(viewMode: AgentGraphViewMode.Table);

        cut.FindAll("[data-testid='agent-graph-viewport']").Should().BeEmpty(
            "the table replaces the drawing instead of duplicating it");

        var rows = cut.FindAll("[data-testid='agent-graph-table-row']");
        rows.Should().HaveCount(5);
        cut.Find("[data-testid='agent-graph-table'] thead").TextContent
            .Should().Contain("From").And.Contain("To").And.Contain("Calls").And.Contain("Last call");

        var ledgerRow = rows.Single(row => row.GetAttribute("data-edge-id") == "e-ledger");
        ledgerRow.TextContent.Should().Contain("Writer").And.Contain("Ledger").And.Contain("12");
    }

    [Fact]
    public void TheTableMode_ListsEveryNode_WithItsKindStateAndCalls()
    {
        var cut = RenderGraph(viewMode: AgentGraphViewMode.Table);

        var rows = cut.FindAll("[data-testid='agent-graph-node-row']");

        rows.Should().HaveCount(6, "a node with no connection is data a reader must not lose");
        var writer = rows.Single(row => row.GetAttribute("data-node-id") == "writer");
        writer.TextContent.Should().Contain("Writer").And.Contain("Agent").And.Contain("Waiting for approval");
    }

    [Fact]
    public void TheTableRows_CanSelectANodeToo()
    {
        var cut = RenderGraph(viewMode: AgentGraphViewMode.Table);

        cut.FindAll("[data-testid='agent-graph-node-select']")
            .Single(button => button.TextContent.Trim() == "Web search")
            .Click();

        _selectedId.Should().Be("search", "the text alternative is not a dead end");
    }

    [Fact]
    public void TheViewSwitcher_RaisesTheNewMode_AndKeepsTheData()
    {
        var cut = RenderGraph();

        cut.Find("[data-testid='agent-graph-view']")
            .QuerySelectorAll("button")
            .Single(button => button.TextContent.Trim() == "Table")
            .Click();

        _viewMode.Should().Be(AgentGraphViewMode.Table);
        cut.Instance.View.Nodes.Should().HaveCount(6, "switching the presentation never changes the data");
    }

    // ── Zoom and pan ─────────────────────────────────────────────────────────

    [Fact]
    public void TheZoomButtons_ChangeTheScale_AndAnnounceIt()
    {
        var cut = RenderGraph();

        cut.Find("[data-testid='agent-graph-zoom-in']").Click();

        cut.Find("[data-testid='agent-graph']").GetAttribute("data-zoom").Should().Be("125");
        ZoomValue(cut).Should().Be("125 %");
        Announcement(cut).Should().Contain("125");
        _zoom.Should().BeApproximately(1.25, 0.001, "the reader's scale is reported to the host");

        cut.Find("[data-testid='agent-graph-zoom-out']").Click();
        cut.Find("[data-testid='agent-graph']").GetAttribute("data-zoom").Should().Be("100");
    }

    [Fact]
    public void TheZoomIsBounded_AtBothEnds()
    {
        var cut = RenderGraph();

        for (var press = 0; press < 12; press++)
        {
            cut.Find("[data-testid='agent-graph-zoom-in']").Click();
        }

        cut.Find("[data-testid='agent-graph']").GetAttribute("data-zoom").Should().Be("250",
            "the ceiling is where the canvas stops being readable");

        for (var press = 0; press < 20; press++)
        {
            cut.Find("[data-testid='agent-graph-zoom-out']").Click();
        }

        cut.Find("[data-testid='agent-graph']").GetAttribute("data-zoom").Should().Be("50");
    }

    [Fact]
    public void FitToView_UsesTheMeasuredRoom_AndNeverStretchesBeyondTheWindow()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<double[]>("measureViewport", _ => true).SetResult([280d, 900d]);

        var cut = RenderGraph();

        cut.Find("[data-testid='agent-graph-zoom-fit']").Click();

        cut.Find("[data-testid='agent-graph']").GetAttribute("data-zoom").Should().Be("50",
            "the graph is wider than the room, so fit lands on the floor instead of a guess");
    }

    [Fact]
    public void FitToView_WithoutAMeasurableViewport_KeepsTheZoom()
    {
        var cut = RenderGraph();

        cut.Find("[data-testid='agent-graph-zoom-fit']").Click();

        _zoom.Should().BeNull("nothing was measured, so the component keeps the reader's scale");
        cut.Find("[data-testid='agent-graph']").GetAttribute("data-zoom").Should().Be("100");
    }

    [Fact]
    public async Task ThePinchGesture_ChangesTheScale_AndIsReportedOnceAtTheEnd()
    {
        var cut = RenderGraph();

        await cut.InvokeAsync(() => cut.Instance.OnGraphZoomGestureStartAsync());
        await cut.InvokeAsync(() => cut.Instance.OnGraphZoomGestureChangeAsync(1.5));
        await cut.InvokeAsync(() => cut.Instance.OnGraphZoomGestureChangeAsync(1.8));

        cut.Find("[data-testid='agent-graph']").GetAttribute("data-zoom").Should().Be("180",
            "the helper reports a CUMULATIVE scale, so the second tick replaces the first instead of multiplying it");
        _zoom.Should().BeNull("nothing is reported while the fingers are still on the canvas");

        await cut.InvokeAsync(() => cut.Instance.OnGraphZoomGestureEndAsync());

        _zoom.Should().BeApproximately(1.8, 0.001, "the gesture is committed once, when it ends");
        Announcement(cut).Should().Contain("180");
    }

    // ── Chrome ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheTitle_IsAHearing_AtTheRequestedLevel_AndFallsBackToTheLocalizedOne()
    {
        var cut = RenderGraph(headingLevel: 2);

        var heading = cut.Find("[data-testid='agent-graph-title']");
        heading.TagName.Should().Be("H2");
        heading.TextContent.Should().Be("Agent graph");

        var withoutTitle = RenderGraph(showTitle: false);
        withoutTitle.FindAll("[data-testid='agent-graph-title']").Should().BeEmpty();
    }

    [Fact]
    public void TheHeadingLevelIsClampedIntoTheSectioningRange()
    {
        RenderGraph(headingLevel: 9).Find("[data-testid='agent-graph-title']").TagName.Should().Be("H6");
        RenderGraph(headingLevel: 1).Find("[data-testid='agent-graph-title']").TagName.Should().Be("H2");
    }

    [Fact]
    public void TheSurfaceOwnsExactlyOneLiveRegion_AndTheGraphOwnsNoSecondOne()
    {
        var cut = RenderGraph();

        cut.FindAll("[aria-live]").Should().ContainSingle(
            "a badge, an icon and a chip announce nothing, so a change is never spoken twice");
        cut.Find("[aria-live]").GetAttribute("role").Should().Be("status");
        cut.Find("[aria-live]").GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public void TheZoomControls_AreAnAlternativeToTheGesture_NotAnExtra()
    {
        var cut = RenderGraph();

        var group = cut.Find("[data-testid='agent-graph-zoom']");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be("Graph zoom");
        foreach (var id in new[] { "agent-graph-zoom-in", "agent-graph-zoom-out", "agent-graph-zoom-fit" })
        {
            cut.Find($"[data-testid='{id}']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace(
                "an icon-only control needs a name (WCAG 2.5.7 ships the alternative, 4.1.2 the name)");
        }
    }

    [Fact]
    public void TheRootCarriesTheTestIdContract_AndTheHostClasses()
    {
        var cut = Render<TmAgentGraph>(parameters => parameters
            .Add(component => component.Snapshot, Snapshot())
            .Add(component => component.Class, "my-graph")
            .Add(component => component.DataTestId, "graph-here"));

        cut.Find("[data-testid='graph-here']").ClassList.Should().Contain("tm-ai-agent-graph");
        cut.Find("[data-testid='graph-here']").ClassList.Should().Contain("my-graph");
    }

    // ── Focus management ─────────────────────────────────────────────────────

    [Fact]
    public async Task ASelectedNodeThatDisappears_ClearsTheSelection_AndReturnsTheFocusToTheViewport()
    {
        var cut = RenderGraph(selectedNodeId: "search");
        var viewport = cut.Find("[data-testid='agent-graph-viewport']").GetAttribute("blazor:elementreference");

        Node(cut, "search").Focus();
        await UpdateAsync(cut, (nameof(TmAgentGraph.Snapshot), new AgentGraphSnapshot
        {
            Sequence = 11,
            Nodes = [Human("human", "Dana"), Agent("planner", "Planner")],
            Edges = [Edge("e-plan", "human", "planner")],
        }));

        cut.Instance.SelectedNode.Should().BeNull("a selection cannot survive its node");
        FocusTargets()[^1].Should().Be(viewport,
            "the focus returns to the surface instead of dropping the reader onto the document body");
    }
}
