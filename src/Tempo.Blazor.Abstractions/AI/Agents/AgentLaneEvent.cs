namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One applied change to a lane surface, with the monotonic <see cref="Sequence"/> the shared live contract needs.
/// <para>
/// The shape is deliberately concrete rather than generic: <c>TmAgentLanes</c> applies these events to the snapshot
/// it renders through <see cref="AgentEventStream{TEvent}"/>, so a host only has to build one small record per
/// change regardless of its transport (a SignalR hub, a one-second poll, a server-sent event stream). A batch may
/// arrive more than once — the stream drops what was already applied — so the host never de-duplicates itself.
/// </para>
/// </summary>
public sealed record AgentLaneEvent
{
    /// <summary>Creates an event.</summary>
    /// <param name="sequence">
    /// The event's position in the run's change log. It must grow by ONE per applied change, because the shared
    /// stream treats "the last applied sequence plus one" as its definition of contiguity and rejects a batch with a
    /// hole rather than papering over it.
    /// </param>
    public AgentLaneEvent(long sequence) => Sequence = sequence;

    /// <summary>The event's position in the run's change log; strictly increasing per applied change.</summary>
    public long Sequence { get; init; }

    /// <summary>What the event does. Default <see cref="AgentLaneEventKind.BlockUpserted"/>.</summary>
    public AgentLaneEventKind Kind { get; init; }

    /// <summary>The block an upsert carries. Null makes <see cref="AgentLaneEventKind.BlockUpserted"/> a no-op.</summary>
    public AgentLaneBlock? Block { get; init; }

    /// <summary>The id a removal addresses. Null makes <see cref="AgentLaneEventKind.BlockRemoved"/> a no-op.</summary>
    public string? BlockId { get; init; }

    /// <summary>The link a handoff adds. Null makes <see cref="AgentLaneEventKind.HandoffAdded"/> a no-op.</summary>
    public AgentHandoffLink? Handoff { get; init; }
}
