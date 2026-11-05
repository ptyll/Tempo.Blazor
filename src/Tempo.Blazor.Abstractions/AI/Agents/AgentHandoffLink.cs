namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One handover between two agents, as a lane surface draws it: an arrow from the work that was handed over (or from
/// the source agent's lane at that moment) to the target agent's lane.
/// <para>
/// <see cref="FromBlockId"/> and <see cref="FromAgentId"/> are two ways to name the same source: a block id anchors
/// the arrow at the end of the finished block (the accurate picture), while an agent id anchors it at
/// <see cref="At"/> on that agent's lane (enough when the host does not model which block was handed over). A link
/// names at least one of them; a link that names neither cannot be drawn and a layout skips it instead of guessing.
/// </para>
/// </summary>
public sealed record AgentHandoffLink
{
    /// <summary>Creates a handoff link.</summary>
    /// <param name="toAgentId">The agent the work was handed TO. Always required — an arrow needs a target.</param>
    /// <param name="at">When the handoff happened, on the host's clock.</param>
    public AgentHandoffLink(string toAgentId, DateTimeOffset at)
    {
        ToAgentId = toAgentId ?? string.Empty;
        At = at;
    }

    /// <summary>
    /// The id of the block that was handed over, or null when the host only knows the source agent. Takes
    /// precedence over <see cref="FromAgentId"/> for the arrow's anchor.
    /// </summary>
    public string? FromBlockId { get; init; }

    /// <summary>
    /// The id of the agent that handed the work over, or null when only <see cref="FromBlockId"/> is known. When
    /// both are supplied the block wins and the agent is the fallback for a block the snapshot does not contain.
    /// </summary>
    public string? FromAgentId { get; init; }

    /// <summary>The agent the work was handed to.</summary>
    public string ToAgentId { get; init; }

    /// <summary>When the handoff happened.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>What was handed over, in the host's own words. Null renders no label.</summary>
    public string? Label { get; init; }

    /// <summary>Whether the link names a source at all. A link that does not is skipped by every layout.</summary>
    public bool HasSource => !string.IsNullOrWhiteSpace(FromBlockId) || !string.IsNullOrWhiteSpace(FromAgentId);
}
