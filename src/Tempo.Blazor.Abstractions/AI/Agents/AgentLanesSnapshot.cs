namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The COMPLETE state of a multi-agent run at one moment: the agents taking part, the work each of them did and the
/// handoffs between them — plus the <see cref="Sequence"/> the snapshot includes.
/// <para>
/// A snapshot is what a lane surface renders; it never polls and never mutates. A deep link, a reload and the repair
/// after a gap all produce a new snapshot and the surface simply draws it, which is why
/// <see cref="Apply(IEnumerable{AgentLaneEvent})"/> returns a NEW value instead of editing this one. The sequence is
/// the number the event stream re-bases on: everything up to it is already contained here.
/// </para>
/// <para>
/// All three lists may be empty; a null list is normalised to an empty one, so a component never has to null-check
/// the host's data.
/// </para>
/// </summary>
public sealed record AgentLanesSnapshot
{
    /// <summary>
    /// The sequence this snapshot includes — the baseline an event stream starts from. Zero (the default) means the
    /// host does not track sequences for snapshots, which is legal: the stream then starts without a baseline and
    /// accepts the run's first event (see <see cref="AgentEventStream{TEvent}"/>).
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>The agents taking part, in the order the lanes are drawn. Empty is legal (an empty run).</summary>
    public IReadOnlyList<AgentRef> Agents
    {
        get => _agents;
        init => _agents = value ?? [];
    }

    /// <summary>
    /// The work blocks of the run, in any order — a layout orders them by time. A block whose agent is not declared
    /// in <see cref="Agents"/> still renders (the lane is appended), so a run is never silently incomplete.
    /// </summary>
    public IReadOnlyList<AgentLaneBlock> Blocks
    {
        get => _blocks;
        init => _blocks = value ?? [];
    }

    /// <summary>The handoffs between agents. A link that cannot be resolved to two lanes is skipped by a layout.</summary>
    public IReadOnlyList<AgentHandoffLink> Handoffs
    {
        get => _handoffs;
        init => _handoffs = value ?? [];
    }

    private readonly IReadOnlyList<AgentRef> _agents = [];
    private readonly IReadOnlyList<AgentLaneBlock> _blocks = [];
    private readonly IReadOnlyList<AgentHandoffLink> _handoffs = [];

    /// <summary>
    /// Folds an incremental batch into this snapshot and returns the result as a NEW value; this instance is never
    /// modified. The batch is expected to be the tail the shared <see cref="AgentEventStream{TEvent}"/> accepted, so
    /// no ordering or de-duplication happens here — that is the stream's job, once, for every surface.
    /// </summary>
    /// <param name="events">The applied events, in sequence order. Null is treated as empty.</param>
    /// <returns>
    /// A snapshot with every upserted block in place (a new block is appended, an existing one keeps its position),
    /// every removed block gone and every new handoff appended once. <see cref="Sequence"/> is NOT changed: it
    /// describes the snapshot that was loaded, not the events applied on top of it.
    /// </returns>
    public AgentLanesSnapshot Apply(IEnumerable<AgentLaneEvent>? events)
    {
        if (events is null)
        {
            return this;
        }

        var blocks = new List<AgentLaneBlock>(_blocks);
        var handoffs = new List<AgentHandoffLink>(_handoffs);
        var changed = false;

        foreach (var change in events)
        {
            if (change is null)
            {
                continue;
            }

            switch (change.Kind)
            {
                case AgentLaneEventKind.BlockUpserted when change.Block is { } block:
                    var index = blocks.FindIndex(candidate =>
                        string.Equals(candidate.Id, block.Id, StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        blocks[index] = block;
                    }
                    else
                    {
                        blocks.Add(block);
                    }

                    changed = true;
                    break;

                case AgentLaneEventKind.BlockRemoved when !string.IsNullOrWhiteSpace(change.BlockId):
                    changed = blocks.RemoveAll(candidate =>
                        string.Equals(candidate.Id, change.BlockId, StringComparison.Ordinal)) > 0 || changed;
                    break;

                case AgentLaneEventKind.HandoffAdded when change.Handoff is { } handoff:
                    if (!handoffs.Contains(handoff))
                    {
                        handoffs.Add(handoff);
                        changed = true;
                    }

                    break;
            }
        }

        return changed ? this with { Blocks = blocks, Handoffs = handoffs } : this;
    }
}
