using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 3 — <c>AgentGraphLayout</c>, the pure C# half of <c>TmAgentGraph</c>: the layered columns a
/// node kind belongs to, the barycenter ordering that keeps crossings down, the pixel box of every node, the curve
/// every edge is painted as, the group boxes and the log-scaled edge thickness a busy connection gets.
/// <para>
/// The tests drive the layout directly (no renderer, no clock, no stylesheet): every case pins concrete pixel sizes,
/// so a geometry regression is a number in the failure message rather than a picture nobody diffs. The layout must be
/// DETERMINISTIC — the same snapshot twice has to produce the same numbers, because a live surface re-runs it on
/// every paint.
/// </para>
/// </summary>
public class AgentGraphLayoutTests
{
    private static AgentGraphNode Node(
        string id,
        AgentGraphNodeKind kind,
        AgentActivityStatus status = AgentActivityStatus.Idle,
        string? group = null)
        => new(id, "Node " + id) { Kind = kind, Status = status, Group = group };

    private static AgentGraphEdge Edge(string id, string from, string to, int calls = 1, DateTimeOffset? last = null)
        => new(id, from, to) { CallCount = calls, LastCallAt = last, IsActive = true };

    private static AgentGraphSnapshot Snapshot(
        IEnumerable<AgentGraphNode> nodes,
        IEnumerable<AgentGraphEdge>? edges = null)
        => new() { Nodes = nodes.ToList(), Edges = edges?.ToList() ?? [] };

    private static AgentGraphLayoutResult Build(
        AgentGraphSnapshot? snapshot,
        IReadOnlyDictionary<string, AgentGraphPosition>? positions = null)
        => AgentGraphLayout.Build(snapshot, new AgentGraphLayoutOptions { NodePositions = positions });

    private static AgentGraphNodePlacement Placement(AgentGraphLayoutResult layout, string nodeId)
        => layout.Nodes.Single(placement => placement.Node.Id == nodeId);

    private static string Text(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The points of a path: <c>M x y C x1 y1 x2 y2 x y</c>.</summary>
    private static (double X, double Y)[] Points(AgentGraphEdgePath edge)
    {
        var numbers = edge.Path
            .Replace("M", " ", StringComparison.Ordinal)
            .Replace("C", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => double.Parse(token, CultureInfo.InvariantCulture))
            .ToArray();

        numbers.Length.Should().Be(8, "an edge paints one move and one cubic segment, got: " + edge.Path);
        return [(numbers[0], numbers[1]), (numbers[2], numbers[3]), (numbers[4], numbers[5]), (numbers[6], numbers[7])];
    }

    // ── The empty graph ──────────────────────────────────────────────────────

    [Fact]
    public void AnEmptySnapshot_PaintsNothing_AndSaysSo()
    {
        var layout = Build(new AgentGraphSnapshot());

        layout.Nodes.Should().BeEmpty();
        layout.Edges.Should().BeEmpty();
        layout.IsEmpty.Should().BeTrue();
        layout.LayerCount.Should().Be(0);
        layout.WidthPx.Should().Be(0);
        layout.HeightPx.Should().Be(0);
    }

    [Fact]
    public void ANullSnapshot_IsTreatedAsAnEmptyGraph()
    {
        var layout = Build(null);

        layout.IsEmpty.Should().BeTrue();
        layout.UnresolvedEdgeCount.Should().Be(0);
    }

    // ── Columns ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheKindDecidesTheColumn_HumanAgentsResourcesTools_LeftToRight()
    {
        var layout = Build(Snapshot(
        [
            Node("human", AgentGraphNodeKind.Human),
            Node("planner", AgentGraphNodeKind.Agent),
            Node("ledger", AgentGraphNodeKind.Resource),
            Node("search", AgentGraphNodeKind.Tool),
        ]));

        var human = Placement(layout, "human");
        var agent = Placement(layout, "planner");
        var resource = Placement(layout, "ledger");
        var tool = Placement(layout, "search");

        human.Layer.Should().Be(0, "the person asking stands to the left");
        agent.Layer.Should().Be(1, "the agent answers from the next column");
        resource.Layer.Should().Be(2, "a resource it reads comes after it");
        tool.Layer.Should().Be(3, "and a tool is the right-most column");

        human.X.Should().BeLessThan(agent.X).And.BeLessThan(resource.X).And.BeLessThan(tool.X);
        agent.X.Should().BeLessThan(tool.X);
        layout.LayerCount.Should().Be(4);
    }

    [Fact]
    public void TwoAgents_ShareOneColumn_AndAreStackedWithoutOverlapping()
    {
        var layout = Build(Snapshot(
        [
            Node("a", AgentGraphNodeKind.Agent),
            Node("b", AgentGraphNodeKind.Agent),
        ]));

        var a = Placement(layout, "a");
        var b = Placement(layout, "b");

        a.Layer.Should().Be(b.Layer);
        a.X.Should().Be(b.X);
        b.Y.Should().BeGreaterThan(a.Y);
        b.Y.Should().BeGreaterThanOrEqualTo(a.Y + a.HeightPx + AgentGraphLayout.VerticalGapPx - 0.01);
    }

    [Fact]
    public void AnEmptyKindColumn_IsCollapsed_InsteadOfLeavingAGap()
    {
        // No human, no resource: the agents are the first column and the tools follow immediately.
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent),
            Node("search", AgentGraphNodeKind.Tool),
        ]));

        Placement(layout, "planner").Layer.Should().Be(0);
        Placement(layout, "search").Layer.Should().Be(1);
        layout.LayerCount.Should().Be(2);
    }

    [Fact]
    public void AnAgentToAgentEdge_PushesTheTargetIntoALaterColumn()
    {
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent),
            Node("writer", AgentGraphNodeKind.Agent),
        ],
        [Edge("e1", "planner", "writer")]));

        var planner = Placement(layout, "planner");
        var writer = Placement(layout, "writer");

        writer.Layer.Should().BeGreaterThan(planner.Layer,
            "an edge must flow forward, so a delegated agent stands to the right of the delegating one");
        writer.X.Should().BeGreaterThan(planner.X);
    }

    [Fact]
    public void AToolResultingIntoAnAgent_PushesTheAgentRightOfTheTool()
    {
        var layout = Build(Snapshot(
        [
            Node("search", AgentGraphNodeKind.Tool),
            Node("planner", AgentGraphNodeKind.Agent),
        ],
        [Edge("e1", "search", "planner")]));

        Placement(layout, "planner").Layer.Should().BeGreaterThan(Placement(layout, "search").Layer,
            "a tool feeding an agent is a data flow, so the agent sits downstream of it");
    }

    // ── Crossing minimization ────────────────────────────────────────────────

    [Fact]
    public void TheBarycenterOrdering_RemovesACrossingTheDeclarationOrderWouldPaint()
    {
        // Declaration order puts the tool of the BOTTOM agent first, which crosses both edges; the barycenter pass
        // has to order the tool column to follow the agent column instead.
        var layout = Build(Snapshot(
        [
            Node("a-top", AgentGraphNodeKind.Agent),
            Node("a-bottom", AgentGraphNodeKind.Agent),
            Node("tool-of-bottom", AgentGraphNodeKind.Tool),
            Node("tool-of-top", AgentGraphNodeKind.Tool),
        ],
        [
            Edge("e1", "a-top", "tool-of-top"),
            Edge("e2", "a-bottom", "tool-of-bottom"),
        ]));

        var topAgentY = Placement(layout, "a-top").Y;
        var bottomAgentY = Placement(layout, "a-bottom").Y;
        var topToolY = Placement(layout, "tool-of-top").Y;
        var bottomToolY = Placement(layout, "tool-of-bottom").Y;

        topAgentY.Should().BeLessThan(bottomAgentY, "agents keep the declaration order");
        topToolY.Should().BeLessThan(bottomToolY, "the tool of the top agent is painted above the other one");
    }

    [Fact]
    public void TheBarycenterOrdering_AlsoRunsUpwards_SoASharedToolStaysCentred()
    {
        // Both agents call ONE tool; a second, unrelated tool must not be pushed between them and their own column.
        var layout = Build(Snapshot(
        [
            Node("first", AgentGraphNodeKind.Agent),
            Node("second", AgentGraphNodeKind.Agent),
            Node("shared", AgentGraphNodeKind.Tool),
            Node("other", AgentGraphNodeKind.Tool, group: "g"),
        ],
        [
            Edge("e1", "first", "shared"),
            Edge("e2", "second", "shared"),
        ]));

        Placement(layout, "shared").Layer.Should().Be(1);
        Placement(layout, "other").Layer.Should().Be(1);
        layout.Edges.Select(edge => edge.Edge.Id).Should().BeEquivalentTo(["e1", "e2"]);
    }

    // ── Edge thickness ───────────────────────────────────────────────────────

    [Fact]
    public void TheEdgeThickness_GrowsWithTheCallCount_OnALogScale()
    {
        var quiet = AgentGraphLayout.EdgeWidth(1, 100);
        var busy = AgentGraphLayout.EdgeWidth(10, 100);
        var busiest = AgentGraphLayout.EdgeWidth(100, 100);

        quiet.Should().BeGreaterThan(AgentGraphLayout.MinEdgeWidthPx,
            "a logarithm already lifts one call out of a hundred off the floor");
        busiest.Should().BeApproximately(AgentGraphLayout.MaxEdgeWidthPx, 0.001);
        busy.Should().BeGreaterThan(quiet).And.BeLessThan(busiest);
    }

    [Fact]
    public void TheLogScale_KeepsAQuietEdgeVisible_InsteadOfMakingItLinear()
    {
        // Ten calls out of a hundred is 10 % of the count range but more than half of the WIDTH range on a log scale:
        // a linear scale would paint it at a tenth of the way, indistinguishable from one call.
        var width = AgentGraphLayout.EdgeWidth(10, 100);
        var linearShare = (width - AgentGraphLayout.MinEdgeWidthPx)
                          / (AgentGraphLayout.MaxEdgeWidthPx - AgentGraphLayout.MinEdgeWidthPx);

        linearShare.Should().BeGreaterThan(0.4, "a logarithm spends most of the width range on the quiet end");
        width.Should().BeLessThan(AgentGraphLayout.MaxEdgeWidthPx);
    }

    [Fact]
    public void AnEdgeWithoutCalls_IsPaintedWithTheThinnestWidth_AndNeverZero()
    {
        AgentGraphLayout.EdgeWidth(0, 0).Should().Be(AgentGraphLayout.MinEdgeWidthPx);
        AgentGraphLayout.EdgeWidth(-5, 10).Should().Be(AgentGraphLayout.MinEdgeWidthPx);
        AgentGraphLayout.EdgeWidth(0, 10).Should().Be(AgentGraphLayout.MinEdgeWidthPx);
        AgentGraphLayout.EdgeWidth(int.MaxValue, int.MaxValue)
            .Should().BeApproximately(AgentGraphLayout.MaxEdgeWidthPx, 0.001);
    }

    [Fact]
    public void TheLayoutReportsTheBusiestEdge_SoASurfaceCanScaleConsistently()
    {
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent),
            Node("search", AgentGraphNodeKind.Tool),
            Node("ledger", AgentGraphNodeKind.Resource),
        ],
        [Edge("e1", "planner", "search", calls: 4), Edge("e2", "planner", "ledger", calls: 40)]));

        layout.MaxCallCount.Should().Be(40);
        layout.Edges.Single(edge => edge.Edge.Id == "e2").StrokeWidthPx
            .Should().BeGreaterThan(layout.Edges.Single(edge => edge.Edge.Id == "e1").StrokeWidthPx);
    }

    // ── Pinned positions ─────────────────────────────────────────────────────

    [Fact]
    public void APinnedNode_KeepsItsExactPosition_AndSaysSo()
    {
        var layout = Build(
            Snapshot(
            [
                Node("planner", AgentGraphNodeKind.Agent),
                Node("search", AgentGraphNodeKind.Tool),
            ]),
            new Dictionary<string, AgentGraphPosition>(StringComparer.Ordinal)
            {
                ["search"] = new AgentGraphPosition(640, 220),
            });

        var search = Placement(layout, "search");

        search.X.Should().Be(640);
        search.Y.Should().Be(220);
        search.IsPinned.Should().BeTrue();
        Placement(layout, "planner").IsPinned.Should().BeFalse("only the host pinned node moves");
    }

    [Fact]
    public void APinnedNode_GrowsTheCanvas_SoItsBoxIsNotCutOff()
    {
        var layout = Build(
            Snapshot([Node("planner", AgentGraphNodeKind.Agent), Node("search", AgentGraphNodeKind.Tool)]),
            new Dictionary<string, AgentGraphPosition>(StringComparer.Ordinal)
            {
                ["search"] = new AgentGraphPosition(900, 40),
            });

        layout.WidthPx.Should().BeGreaterThanOrEqualTo(900 + AgentGraphLayout.NodeWidthPx);
        layout.HeightPx.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AnUnknownPinnedId_IsIgnored_AndTheLayoutStaysStable()
    {
        var layout = Build(
            Snapshot([Node("planner", AgentGraphNodeKind.Agent)]),
            new Dictionary<string, AgentGraphPosition>(StringComparer.Ordinal)
            {
                ["nobody"] = new AgentGraphPosition(10, 10),
            });

        layout.Nodes.Should().HaveCount(1);
        layout.Nodes[0].IsPinned.Should().BeFalse();
    }

    // ── Edge geometry ────────────────────────────────────────────────────────

    [Fact]
    public void AForwardEdge_LeavesTheRightEdgeOfItsSource_AndEntersTheLeftEdgeOfItsTarget()
    {
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent),
            Node("search", AgentGraphNodeKind.Tool),
        ],
        [Edge("e1", "planner", "search")]));

        var edge = layout.Edges.Single();
        var from = Placement(layout, "planner");
        var to = Placement(layout, "search");
        var points = Points(edge);

        points[0].X.Should().BeApproximately(from.X + from.WidthPx, 0.01);
        points[0].Y.Should().BeApproximately(from.Y + (from.HeightPx / 2), 0.01);
        points[^1].X.Should().BeApproximately(to.X, 0.01);
        points[^1].Y.Should().BeApproximately(to.Y + (to.HeightPx / 2), 0.01);
        points[1].X.Should().BeGreaterThan(points[0].X, "the curve leaves horizontally");
        points[2].X.Should().BeLessThan(points[^1].X, "and arrives horizontally");
        edge.IsBackEdge.Should().BeFalse();
    }

    [Fact]
    public void ABackEdge_IsRoutedBelowBothBoxes_AndCounted()
    {
        // A cycle: the layout has to break it, and the edge that closes it is drawn UNDER the boxes it connects.
        var layout = Build(Snapshot(
        [
            Node("first", AgentGraphNodeKind.Agent),
            Node("second", AgentGraphNodeKind.Agent),
        ],
        [Edge("e1", "first", "second"), Edge("e2", "second", "first")]));

        layout.Edges.Should().HaveCount(2);
        layout.BackEdgeCount.Should().Be(1);

        var back = layout.Edges.Single(edge => edge.IsBackEdge);
        var points = Points(back);
        var lowestBox = layout.Nodes.Max(placement => placement.Y + placement.HeightPx);

        points.Skip(1).Take(2).Select(point => point.Y).Should().OnlyContain(y => y > lowestBox,
            "a back edge loops below the boxes instead of crossing them");
    }

    [Fact]
    public void ASelfLoop_IsDrawnBesideTheNode_AndNeverBreaksTheLayout()
    {
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent),
            Node("search", AgentGraphNodeKind.Tool),
        ],
        [Edge("loop", "planner", "planner"), Edge("e1", "planner", "search")]));

        var loop = layout.Edges.Single(edge => edge.Edge.Id == "loop");

        loop.IsBackEdge.Should().BeTrue("a node calling itself is not a forward connection");
        loop.Path.Should().NotBeNullOrWhiteSpace();
        loop.From.Node.Id.Should().Be(loop.To.Node.Id);
        Placement(layout, "search").Layer.Should().Be(1, "the self loop does not move the tool column");
    }

    [Fact]
    public void AnEdgeWithAMissingEndpoint_IsCounted_InsteadOfGuessed()
    {
        var layout = Build(Snapshot(
            [Node("planner", AgentGraphNodeKind.Agent)],
            [Edge("e1", "planner", "ghost"), Edge("e2", "ghost", "planner")]));

        layout.Edges.Should().BeEmpty();
        layout.UnresolvedEdgeCount.Should().Be(2);
        layout.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public void ARepeatedNodeOrEdgeId_IsReadOnce()
    {
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent),
            Node("planner", AgentGraphNodeKind.Agent),
            Node("search", AgentGraphNodeKind.Tool),
        ],
        [Edge("e1", "planner", "search"), Edge("e1", "planner", "search")]));

        layout.Nodes.Should().HaveCount(2);
        layout.Edges.Should().HaveCount(1);
    }

    // ── Groups ───────────────────────────────────────────────────────────────

    [Fact]
    public void AGroup_GetsABoxAroundEveryMember_WithPadding()
    {
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent, group: "Backend"),
            Node("writer", AgentGraphNodeKind.Agent, group: "Backend"),
            Node("search", AgentGraphNodeKind.Tool),
        ]));

        var group = layout.Groups.Should().ContainSingle().Subject;
        var members = layout.Nodes.Where(placement => placement.Node.Group == "Backend").ToList();

        group.Group.Should().Be("Backend");
        group.Nodes.Should().HaveCount(2);
        group.X.Should().BeLessThanOrEqualTo(members.Min(placement => placement.X) - AgentGraphLayout.GroupPaddingPx + 0.01);
        group.Y.Should().BeLessThanOrEqualTo(members.Min(placement => placement.Y) - AgentGraphLayout.GroupPaddingPx + 0.01);
        (group.X + group.WidthPx).Should()
            .BeGreaterThanOrEqualTo(members.Max(placement => placement.X + placement.WidthPx) + AgentGraphLayout.GroupPaddingPx - 0.01);
        (group.Y + group.HeightPx).Should()
            .BeGreaterThanOrEqualTo(members.Max(placement => placement.Y + placement.HeightPx) + AgentGraphLayout.GroupPaddingPx - 0.01);
    }

    [Fact]
    public void GroupedNodes_ArePartitionedIntoRunsInsteadOfBeingScattered()
    {
        var layout = Build(Snapshot(
        [
            Node("one", AgentGraphNodeKind.Agent, group: "B"),
            Node("two", AgentGraphNodeKind.Agent, group: "A"),
            Node("three", AgentGraphNodeKind.Agent, group: "B"),
        ]));

        var y = layout.Nodes.OrderBy(placement => placement.Y).Select(placement => placement.Node.Group).ToList();

        y.Should().Equal(["B", "B", "A"], "the two grouped agents are painted as one run");
        layout.Groups.Select(box => box.Group).Should().Equal(["B", "A"]);
    }

    [Fact]
    public void AnUngroupedNode_IsNeverPutInsideAGroupBox()
    {
        var layout = Build(Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent, group: "Backend"),
            Node("writer", AgentGraphNodeKind.Agent),
        ]));

        var box = layout.Groups.Single();
        box.Nodes.Select(placement => placement.Node.Id).Should().Equal(["planner"]);
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void TheSameSnapshot_ProducesTheSameGeometry_EveryTime()
    {
        var snapshot = Snapshot(
        [
            Node("planner", AgentGraphNodeKind.Agent, group: "Core"),
            Node("writer", AgentGraphNodeKind.Agent, group: "Core"),
            Node("search", AgentGraphNodeKind.Tool),
            Node("ledger", AgentGraphNodeKind.Resource),
        ],
        [
            Edge("e1", "planner", "search", calls: 3),
            Edge("e2", "writer", "ledger", calls: 9),
            Edge("e3", "planner", "writer"),
        ]);

        var first = Build(snapshot);
        var second = Build(snapshot);

        second.Nodes.Select(placement => (placement.Node.Id, placement.Layer, placement.X, placement.Y))
            .Should().Equal(first.Nodes.Select(placement => (placement.Node.Id, placement.Layer, placement.X, placement.Y)));
        second.Edges.Select(edge => edge.Path).Should().Equal(first.Edges.Select(edge => edge.Path));
        second.WidthPx.Should().Be(first.WidthPx);
        second.HeightPx.Should().Be(first.HeightPx);
    }

    [Fact]
    public void NodesAreLaidOutInsideTheCanvas_AndNothingOverlaps()
    {
        var layout = Build(Snapshot(
        [
            Node("h", AgentGraphNodeKind.Human),
            Node("a", AgentGraphNodeKind.Agent),
            Node("b", AgentGraphNodeKind.Agent),
            Node("t", AgentGraphNodeKind.Tool),
            Node("r", AgentGraphNodeKind.Resource),
        ],
        [
            Edge("e1", "h", "a"),
            Edge("e2", "a", "t"),
            Edge("e3", "b", "r"),
        ]));

        foreach (var placement in layout.Nodes)
        {
            placement.X.Should().BeGreaterThanOrEqualTo(0);
            placement.Y.Should().BeGreaterThanOrEqualTo(0);
            (placement.X + placement.WidthPx).Should().BeLessThanOrEqualTo(layout.WidthPx);
            (placement.Y + placement.HeightPx).Should().BeLessThanOrEqualTo(layout.HeightPx);
        }

        for (var index = 0; index < layout.Nodes.Count; index++)
        {
            for (var other = index + 1; other < layout.Nodes.Count; other++)
            {
                var left = layout.Nodes[index];
                var right = layout.Nodes[other];
                var overlaps = left.X < right.X + right.WidthPx && right.X < left.X + left.WidthPx
                    && left.Y < right.Y + right.HeightPx && right.Y < left.Y + left.HeightPx;
                overlaps.Should().BeFalse($"{left.Node.Id} and {right.Node.Id} must not cover each other");
            }
        }
    }

    [Fact]
    public void TheCanvasCarriesAMargin_SoABorderRingIsNeverClipped()
    {
        var layout = Build(Snapshot([Node("planner", AgentGraphNodeKind.Agent)]));

        var placement = Placement(layout, "planner");

        placement.X.Should().Be(AgentGraphLayout.MarginPx);
        placement.Y.Should().Be(AgentGraphLayout.MarginPx);
        layout.WidthPx.Should().Be(AgentGraphLayout.MarginPx + placement.WidthPx + AgentGraphLayout.MarginPx);
        layout.HeightPx.Should().Be(AgentGraphLayout.MarginPx + placement.HeightPx + AgentGraphLayout.MarginPx);
    }

    [Fact]
    public void AShortLabel_KeepsOneLine_ALongOneWrapsToTwo_AndTheRestIsEllipsized()
    {
        AgentGraphPresentation.LabelLines("Plan", 20).Should().Equal(["Plan"]);

        var wrapped = AgentGraphPresentation.LabelLines("Review the reconciliation report", 20);
        wrapped.Should().HaveCount(2);
        wrapped[0].Should().Be("Review the");

        var long1 = AgentGraphPresentation.LabelLines(
            "Review the reconciliation report of the whole ledger export and everything else", 20);
        long1.Should().HaveCount(2);
        long1[1].Should().EndWith("…", "the second line never grows past the box");
        long1[1].Length.Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public void AnEmptyLabel_StaysEmpty_SoTheSurfaceCanPaintItsFallback()
    {
        AgentGraphPresentation.LabelLines("", 20).Should().BeEmpty();
        AgentGraphPresentation.LabelLines("   ", 20).Should().BeEmpty();
    }

    [Fact]
    public void AFullySpecifiedPlacement_IsAddressableById()
    {
        var layout = Build(Snapshot([Node("planner", AgentGraphNodeKind.Agent)]));

        layout.Find("planner").Should().NotBeNull();
        layout.Find("nobody").Should().BeNull();
        Text(layout.Find("planner")!.CenterX).Should().Be(
            Text(Placement(layout, "planner").X + (AgentGraphLayout.NodeWidthPx / 2)));
    }
}
