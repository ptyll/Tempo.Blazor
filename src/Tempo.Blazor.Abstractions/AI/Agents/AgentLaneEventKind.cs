namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What one incremental <see cref="AgentLaneEvent"/> does to a lane snapshot. Three verbs are enough to describe a
/// live run: a block appears or changes (an upsert, so a start and a later completion are the same operation), a
/// block disappears, or a handoff is recorded.
/// </summary>
public enum AgentLaneEventKind
{
    /// <summary>Adds the block, or replaces the block with the same <see cref="AgentLaneBlock.Id"/> in place.</summary>
    BlockUpserted = 0,

    /// <summary>Removes the block named by <see cref="AgentLaneEvent.BlockId"/>; an unknown id is a no-op.</summary>
    BlockRemoved = 1,

    /// <summary>Records the handoff, unless the same link is already present.</summary>
    HandoffAdded = 2,
}
