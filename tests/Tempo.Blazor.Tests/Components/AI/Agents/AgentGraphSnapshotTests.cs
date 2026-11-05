using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 3 — the models behind <c>TmAgentGraph</c>: a node with its kind and state, an edge with the
/// number of calls it carried, and the <see cref="AgentGraphSnapshot.Apply"/> fold that turns a stream of
/// <see cref="AgentGraphCall"/> events into a thicker, freshly touched edge.
/// <para>
/// The snapshot is a VALUE: <c>Apply</c> returns a new one and never edits the instance the host still holds, so a
/// host may compare a rendered snapshot with the previous one. The tests pin the exact fold the component depends on —
/// calls accumulate, the last call is the newest one recorded, an unknown edge is ignored rather than invented, and a
/// snapshot with no calls in it is returned unchanged.
/// </para>
/// </summary>
public class AgentGraphSnapshotTests
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static AgentGraphSnapshot Snapshot() => new()
    {
        Sequence = 3,
        Nodes =
        [
            new AgentGraphNode("planner", "Planner") { Kind = AgentGraphNodeKind.Agent },
            new AgentGraphNode("search", "Web search") { Kind = AgentGraphNodeKind.Tool },
        ],
        Edges =
        [
            new AgentGraphEdge("e1", "planner", "search") { CallCount = 2, LastCallAt = Ten },
            new AgentGraphEdge("e2", "search", "planner"),
        ],
    };

    [Fact]
    public void TheSnapshotNormalisesItsLists_SoAComponentNeverNullChecks()
    {
        var snapshot = new AgentGraphSnapshot { Nodes = null!, Edges = null! };

        snapshot.Nodes.Should().BeEmpty();
        snapshot.Edges.Should().BeEmpty();
    }

    [Fact]
    public void ANodeWithoutAnId_GetsOne_AndKeepsItsLabel()
    {
        var node = new AgentGraphNode("  ", "Web search") { Kind = AgentGraphNodeKind.Tool };

        node.Id.Should().NotBeNullOrWhiteSpace();
        node.Label.Should().Be("Web search");
        node.HasLabel.Should().BeTrue();
        node.Kind.Should().Be(AgentGraphNodeKind.Tool);
        node.Status.Should().Be(AgentActivityStatus.Idle);
    }

    [Fact]
    public void AnAgentNode_NamesItsAgent_AndBorrowsItsInitialsAndPaletteSlot()
    {
        var node = new AgentGraphNode("planner", "Planner")
        {
            Agent = new AgentRef("Data analyst", "planner") { ColorIndex = 3 },
        };

        node.Initials.Should().Be("DA", "an agent node paints the avatar the badge paints");
        node.ColorToken.Should().Be("--tm-agent-color-3");
    }

    [Fact]
    public void AToolNode_DerivesItsInitialsFromItsOwnLabel()
    {
        var tool = new AgentGraphNode("search", "Web search");

        tool.Initials.Should().Be("WS");
        tool.ColorToken.Should().BeEmpty("a tool is not in the agent palette");
    }

    [Fact]
    public void ANodeWithNoNameAtAll_StillPaintsAnAvatar()
    {
        new AgentGraphNode("x", "").Initials.Should().Be("?");
    }

    [Fact]
    public void AnEdgeWithoutAnId_GetsOne_AndANegativeCountIsClamped()
    {
        var edge = new AgentGraphEdge("", "planner", "search") { CallCount = -4 };

        edge.Id.Should().NotBeNullOrWhiteSpace();
        edge.CallCount.Should().Be(0);
        edge.From.Should().Be("planner");
        edge.To.Should().Be("search");
    }

    [Fact]
    public void ACall_ThickensItsEdge_AndRecordsWhenItHappened()
    {
        var snapshot = Snapshot();

        var next = snapshot.Apply([new AgentGraphCall(4) { EdgeId = "e1", At = Ten.AddMinutes(2) }]);

        var edge = next.Edges.Single(candidate => candidate.Id == "e1");
        edge.CallCount.Should().Be(3);
        edge.LastCallAt.Should().Be(Ten.AddMinutes(2));
        edge.IsActive.Should().BeTrue();
        next.Sequence.Should().Be(3, "a snapshot describes the state it was LOADED at");
    }

    [Fact]
    public void ABatchedCall_AddsEveryCount_ItReports()
    {
        var snapshot = Snapshot();

        var next = snapshot.Apply([new AgentGraphCall(4) { EdgeId = "e1", Count = 5 }]);

        next.Edges.Single(edge => edge.Id == "e1").CallCount.Should().Be(7);
    }

    [Fact]
    public void AnOlderCall_NeverMovesTheLastCallBackwards()
    {
        var snapshot = Snapshot();

        var next = snapshot.Apply([new AgentGraphCall(4) { EdgeId = "e1", At = Ten.AddMinutes(-30) }]);

        next.Edges.Single(edge => edge.Id == "e1").LastCallAt.Should().Be(Ten,
            "the newest recorded call is what the surface prints");
    }

    [Fact]
    public void ACallWithoutATimestamp_KeepsTheLastOneItKnows()
    {
        var next = Snapshot().Apply([new AgentGraphCall(4) { EdgeId = "e1" }]);

        next.Edges.Single(edge => edge.Id == "e1").LastCallAt.Should().Be(Ten);
    }

    [Fact]
    public void AnUnknownOrEmptyEdgeId_IsIgnored_InsteadOfInventingAnEdge()
    {
        var snapshot = Snapshot();

        var next = snapshot.Apply(
        [
            new AgentGraphCall(4) { EdgeId = "ghost" },
            new AgentGraphCall(5) { EdgeId = "  " },
            new AgentGraphCall(6),
        ]);

        next.Edges.Should().BeEquivalentTo(snapshot.Edges);
        next.Should().BeSameAs(snapshot, "nothing changed, so the host's snapshot is not replaced by a copy");
    }

    [Fact]
    public void AnEmptyBatch_ReturnsTheSameSnapshot()
    {
        var snapshot = Snapshot();

        snapshot.Apply([]).Should().BeSameAs(snapshot);
        snapshot.Apply(null).Should().BeSameAs(snapshot);
    }

    [Fact]
    public void EveryCallOfABatch_IsFolded_SoABurstNeverLosesCounts()
    {
        var next = Snapshot().Apply(
        [
            new AgentGraphCall(4) { EdgeId = "e1" },
            new AgentGraphCall(5) { EdgeId = "e1" },
            new AgentGraphCall(6) { EdgeId = "e2" },
        ]);

        next.Edges.Single(edge => edge.Id == "e1").CallCount.Should().Be(4);
        next.Edges.Single(edge => edge.Id == "e2").CallCount.Should().Be(1);
    }

    [Fact]
    public void TheCallClampsANegativeCount_SoAnEdgeOnlyEverGrows()
    {
        var next = Snapshot().Apply([new AgentGraphCall(4) { EdgeId = "e1", Count = -10 }]);

        next.Edges.Single(edge => edge.Id == "e1").CallCount.Should().Be(2);
    }
}
