using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 2 — <see cref="AgentLanesSnapshot.Apply"/>: the reducer a lane surface folds the accepted
/// event tail into its snapshot with. It is pure and it never edits the instance it was given, so a host can keep
/// its last snapshot and rebuild from it at any moment (which is exactly what the repair after a stream gap does).
/// </summary>
public class AgentLanesSnapshotTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static AgentLaneBlock Block(string id, int startMinutes = 0, int? endMinutes = 10)
        => new(id, "planner", Nine.AddMinutes(startMinutes), "Block " + id)
        {
            End = endMinutes is { } end ? Nine.AddMinutes(end) : null,
        };

    private static AgentLanesSnapshot Snapshot() => new()
    {
        Sequence = 42,
        Agents = [new AgentRef("Planner", "planner")],
        Blocks = [Block("a"), Block("b", 20, 30)],
        Handoffs = [],
    };

    [Fact]
    public void Apply_AddsAnUpsertedBlock_AtTheEnd()
    {
        var snapshot = Snapshot();

        var next = snapshot.Apply([new AgentLaneEvent(43) { Block = Block("c", 40, 50) }]);

        next.Blocks.Select(block => block.Id).Should().Equal("a", "b", "c");
        snapshot.Blocks.Should().HaveCount(2, "the reducer returns a new value and never edits the snapshot");
        next.Sequence.Should().Be(42, "the sequence describes the loaded snapshot, not the events on top of it");
    }

    [Fact]
    public void Apply_ReplacesAnExistingBlockInPlace()
    {
        var finished = Block("a", 0, 25);

        var next = Snapshot().Apply([new AgentLaneEvent(43) { Block = finished }]);

        next.Blocks[0].Should().BeSameAs(finished);
        next.Blocks.Select(block => block.Id).Should().Equal("a", "b");
        next.Blocks.Should().HaveCount(2, "an upsert replaces, it never appends a second copy");
    }

    [Fact]
    public void Apply_RemovesTheNamedBlock_AndIgnoresAnUnknownId()
    {
        var next = Snapshot().Apply(
        [
            new AgentLaneEvent(43) { Kind = AgentLaneEventKind.BlockRemoved, BlockId = "b" },
            new AgentLaneEvent(44) { Kind = AgentLaneEventKind.BlockRemoved, BlockId = "nope" },
            new AgentLaneEvent(45) { Kind = AgentLaneEventKind.BlockRemoved },
        ]);

        next.Blocks.Select(block => block.Id).Should().Equal("a");
    }

    [Fact]
    public void Apply_AppendsANewHandoffOnce()
    {
        var link = new AgentHandoffLink("writer", Nine.AddMinutes(20)) { FromBlockId = "a" };

        var next = Snapshot().Apply(
        [
            new AgentLaneEvent(43) { Kind = AgentLaneEventKind.HandoffAdded, Handoff = link },
            new AgentLaneEvent(44) { Kind = AgentLaneEventKind.HandoffAdded, Handoff = link },
        ]);

        next.Handoffs.Should().ContainSingle("the same link delivered twice is still one handoff");
        next.Handoffs[0].Label.Should().BeNull();
    }

    [Fact]
    public void Apply_IdentifiesAHandoffByAllItsFields()
    {
        var at = Nine.AddMinutes(20);
        var link = new AgentHandoffLink("writer", at) { FromBlockId = "a", Label = "Plan ready" };

        var next = Snapshot().Apply(
        [
            new AgentLaneEvent(43) { Kind = AgentLaneEventKind.HandoffAdded, Handoff = link },
            new AgentLaneEvent(44) { Kind = AgentLaneEventKind.HandoffAdded, Handoff = link with { Label = "Plan revised" } },
        ]);

        next.Handoffs.Should().HaveCount(2,
            "handoffs are append-only and identified by ALL their fields, so an edited label is a NEW handoff");
    }

    [Fact]
    public void Apply_IgnoresEventsWithoutAPayload()
    {
        var snapshot = Snapshot();

        var next = snapshot.Apply(
        [
            new AgentLaneEvent(43),
            new AgentLaneEvent(44) { Kind = AgentLaneEventKind.BlockRemoved },
            new AgentLaneEvent(45) { Kind = AgentLaneEventKind.HandoffAdded },
        ]);

        next.Should().BeSameAs(snapshot, "nothing changed, so the same value comes back and no render is needed");
    }

    [Fact]
    public void Apply_WithNoEvents_ReturnsTheSameValue()
    {
        var snapshot = Snapshot();

        snapshot.Apply([]).Should().BeSameAs(snapshot);
        snapshot.Apply(null).Should().BeSameAs(snapshot);
    }

    [Fact]
    public void TheListsAreNeverNull()
    {
        var snapshot = new AgentLanesSnapshot
        {
            Agents = null!,
            Blocks = null!,
            Handoffs = null!,
        };

        snapshot.Agents.Should().BeEmpty();
        snapshot.Blocks.Should().BeEmpty();
        snapshot.Handoffs.Should().BeEmpty();
    }
}
