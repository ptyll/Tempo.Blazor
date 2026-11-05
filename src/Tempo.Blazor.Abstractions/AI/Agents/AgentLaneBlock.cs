namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One piece of work an agent did (or is still doing), as a lane surface draws it: the agent it belongs to, when it
/// started, when it ended, what happened and — optionally — the review verdict it carries.
/// <para>
/// The record is a VALUE: a host recomposes it with <c>block with { … }</c> when a block finishes or its state
/// changes, and no component mutates it. <see cref="End"/> is <c>null</c> while the block is still open — a running
/// block has no recorded end, so a surface draws it up to its own "now" instead of inventing a duration. An
/// <see cref="End"/> EARLIER than <see cref="Start"/> is clamped to <see cref="Start"/> by every layout and
/// formatter, because a negative duration is never meaningful.
/// </para>
/// <para>
/// Nothing here carries colour: the lane palette slot belongs to the agent (<see cref="AgentRef.ColorIndex"/>), so
/// two blocks of one agent can never disagree about their identity.
/// </para>
/// </summary>
public sealed record AgentLaneBlock
{
    /// <summary>Creates a block.</summary>
    /// <param name="id">
    /// Stable identity of the block — the value a selection reports and the key an incremental event addresses. Null
    /// or whitespace generates one, so a list built in a loop still renders; a host that wants a stable selection
    /// across renders supplies its own id (the same rule <see cref="AgentRef"/> follows for agents).
    /// </param>
    /// <param name="agentId">The id of the agent that owns the block. A blank value is stored as an empty string.</param>
    /// <param name="start">When the work started, on the host's clock.</param>
    /// <param name="label">What the work was, in the host's own words. Empty renders the block without visible text.</param>
    public AgentLaneBlock(string id, string agentId, DateTimeOffset start, string label)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        AgentId = agentId ?? string.Empty;
        Start = start;
        Label = label ?? string.Empty;
    }

    /// <summary>Stable identity of the block. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>The id of the agent the block belongs to; <see cref="AgentRef.Id"/> names it on the snapshot.</summary>
    public string AgentId { get; init; }

    /// <summary>When the work started.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>
    /// When the work ended, or <c>null</c> while it is still open. A surface draws an open block up to its own
    /// "now" and marks it as running, so a live lane never shows a block that stops before the current moment.
    /// </summary>
    public DateTimeOffset? End { get; init; }

    /// <summary>The block's own state. Default <see cref="AgentActivityStatus.Idle"/>; an unknown value renders no state word.</summary>
    public AgentActivityStatus Status { get; init; }

    /// <summary>What the work was, as the host wants it read. Empty renders no visible text (the accessible name still names the kind).</summary>
    public string Label { get; init; }

    /// <summary>What kind of work the block represents. Default <see cref="AgentLaneBlockKind.Task"/>.</summary>
    public AgentLaneBlockKind Kind { get; init; }

    /// <summary>
    /// The review verdict the block carries, or <c>null</c> when it carries none. A marker is painted as an icon AND
    /// a localized word; <c>null</c> is not the same as a neutral marker, so a component draws nothing.
    /// </summary>
    public AgentLaneBlockMarker? Marker { get; init; }

    /// <summary>Whether the block is still open, i.e. <see cref="End"/> is null.</summary>
    public bool IsOpen => End is null;

    /// <summary>
    /// The end a surface paints for the block: <see cref="End"/> when it is recorded, otherwise
    /// <paramref name="now"/>. An end before <see cref="Start"/> is clamped to <see cref="Start"/>, so a caller
    /// can never derive a negative duration from a block.
    /// </summary>
    /// <param name="now">The current moment, used only for an open block.</param>
    /// <returns>The effective end, never earlier than <see cref="Start"/>.</returns>
    public DateTimeOffset EndAt(DateTimeOffset now)
    {
        var end = End ?? now;
        return end < Start ? Start : end;
    }
}
