namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The COMPLETE state of an agent graph at one moment: the nodes (agents, tools, resources, the human), the
/// connections between them and the call counts each connection carried — plus the <see cref="Sequence"/> the snapshot
/// includes.
/// <para>
/// A snapshot is what a graph surface renders; it never polls and never mutates. A deep link, a reload and the repair
/// after a gap all produce a new snapshot and the surface simply draws it, which is why
/// <see cref="Apply(IEnumerable{AgentGraphCall})"/> returns a NEW value instead of editing this one. The sequence is
/// the number the event stream re-bases on: everything up to it is already contained here.
/// </para>
/// <para>
/// Both lists may be empty and a null list is normalised to an empty one, so a component never has to null-check the
/// host's data.
/// </para>
/// </summary>
public sealed record AgentGraphSnapshot
{
    /// <summary>
    /// The sequence this snapshot includes — the baseline an event stream starts from. Zero (the default) means the
    /// host does not track sequences for snapshots, which is legal: the stream then starts without a baseline and
    /// accepts the run's first event (see <see cref="AgentEventStream{TEvent}"/>).
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>The nodes of the graph, in the order the layout starts from. Empty is legal (an empty graph).</summary>
    public IReadOnlyList<AgentGraphNode> Nodes
    {
        get => _nodes;
        init => _nodes = value ?? [];
    }

    /// <summary>
    /// The connections between nodes, in any order. An edge naming a node the snapshot does not contain is COUNTED by
    /// the layout and never drawn, because its geometry would be a guess.
    /// </summary>
    public IReadOnlyList<AgentGraphEdge> Edges
    {
        get => _edges;
        init => _edges = value ?? [];
    }

    private readonly IReadOnlyList<AgentGraphNode> _nodes = [];
    private readonly IReadOnlyList<AgentGraphEdge> _edges = [];

    /// <summary>
    /// Folds an incremental batch of calls into this snapshot and returns the result as a NEW value; this instance is
    /// never modified. The batch is expected to be the tail the shared <see cref="AgentEventStream{TEvent}"/> accepted,
    /// so no ordering or de-duplication happens here — that is the stream's job, once, for every surface.
    /// <para>
    /// Each call ADDS its <see cref="AgentGraphCall.Count"/> to the addressed edge, marks it active and moves its
    /// <see cref="AgentGraphEdge.LastCallAt"/> forward (never back). A call naming an edge the snapshot does not have is
    /// ignored, because an edge cannot be invented from one endpoint pair. A batch that changes nothing returns THIS
    /// instance, so a host can compare by reference.
    /// </para>
    /// </summary>
    /// <param name="calls">The applied calls, in sequence order. Null or empty returns this instance.</param>
    /// <returns>A snapshot with the calls folded in, or this instance when nothing changed.</returns>
    public AgentGraphSnapshot Apply(IEnumerable<AgentGraphCall>? calls)
    {
        if (calls is null)
        {
            return this;
        }

        List<AgentGraphEdge>? edges = null;

        foreach (var call in calls)
        {
            if (call is null || string.IsNullOrWhiteSpace(call.EdgeId))
            {
                continue;
            }

            var source = edges ?? _edges;
            var index = -1;
            for (var candidate = 0; candidate < source.Count; candidate++)
            {
                if (string.Equals(source[candidate].Id, call.EdgeId, StringComparison.Ordinal))
                {
                    index = candidate;
                    break;
                }
            }

            if (index < 0)
            {
                continue;
            }

            edges ??= new List<AgentGraphEdge>(_edges);
            var edge = edges[index];
            var lastCall = call.At is { } at && (edge.LastCallAt is not { } recorded || at > recorded)
                ? at
                : edge.LastCallAt;

            edges[index] = edge with
            {
                CallCount = edge.CallCount + call.Count,
                LastCallAt = lastCall,
                IsActive = true,
            };
        }

        return edges is null ? this : this with { Edges = edges };
    }
}
