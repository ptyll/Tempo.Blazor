namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One node of an agent run: an agent, a delegated sub-agent, a tool call or a step, its live state, and the
/// numbers the run is watched by (model, elapsed time, cost and tokens).
/// <para>
/// The list a host passes to <c>TmAgentRunTree</c> is FLAT and the tree comes from <see cref="ParentId"/>: a
/// node whose parent names no supplied node is an orphan attached under the root (never silently dropped),
/// and a parent cycle is rejected and reported. The record is a VALUE — a host recomposes a node with
/// <c>node with { … }</c> as the run moves on.
/// </para>
/// <para>
/// Timestamps are the only source of a duration: <see cref="Duration"/> needs both ends, and a node that is
/// still <see cref="AgentStepStatus.Running"/> elapses from <see cref="StartedAt"/> to the "now" the caller
/// passes (the tree reads it from its <see cref="System.TimeProvider"/>), never to the wall clock.
/// </para>
/// </summary>
public sealed record AgentRunNode
{
    /// <summary>Creates a pending node.</summary>
    /// <param name="title">What the node does, as the reader should see it. Null is stored as an empty
    /// string, so nothing ever renders the word "null"; a blank title renders the localized "Untitled
    /// node" fallback instead of an empty row.</param>
    /// <param name="id">Stable identity of the node, used as the row's <c>@key</c>, as the value of
    /// <see cref="ParentId"/> and as the value of the tree's <c>SelectedNodeId</c>. Null or whitespace
    /// generates one, so two identical nodes are still two rows.</param>
    public AgentRunNode(string title, string? id = null)
    {
        Title = title ?? string.Empty;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
    }

    /// <summary>Stable identity of the node. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>
    /// The <see cref="Id"/> of the node this one hangs under. Null or whitespace makes the node a ROOT; a
    /// parent id that names no supplied node makes it an orphan root (reported by the layout, never
    /// dropped).
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>What the node does, as the reader should see it.</summary>
    public string Title { get; init; }

    /// <summary>What the node is. Default <see cref="AgentRunNodeKind.Agent"/>; decides the row's
    /// decorative icon, which is always paired with the state in words.</summary>
    public AgentRunNodeKind Kind { get; init; } = AgentRunNodeKind.Agent;

    /// <summary>Current execution state. Default <see cref="AgentStepStatus.Pending"/>.</summary>
    public AgentStepStatus Status { get; init; } = AgentStepStatus.Pending;

    /// <summary>The model the node ran on (a model id or a display name). Null renders an em dash in the
    /// model column, so "not reported" never looks like a model called "".</summary>
    public string? Model { get; init; }

    /// <summary>When the node started, from the host's clock. Null means the tree cannot time it.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the node finished. Null on a node that is still running (or never started).</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// What the node cost, in MICRO-UNITS of the host's currency: one million micro-units is 1.00. The tree
    /// renders it through <see cref="AiCostFormatter"/> (or the host's own formatter) and sums it over the
    /// subtree for a parent row. Null means "not reported" and is never treated as a zero.
    /// </summary>
    public long? CostMicroUnits { get; init; }

    /// <summary>How many tokens the node consumed, summed over the subtree for a parent row. Null means
    /// "not reported" and is never treated as a zero.</summary>
    public long? Tokens { get; init; }

    /// <summary>
    /// The failure reason of a <see cref="AgentStepStatus.Failed"/> node, in the agent's own words. The row
    /// keeps the state word and carries the reason as its status cell's accessible name (so the grid grows no
    /// second text column); a host shows the full text in its own detail panel.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>Whether the node reached a terminal state (<see cref="AgentStepStatus.Done"/>,
    /// <see cref="AgentStepStatus.Failed"/> or <see cref="AgentStepStatus.Skipped"/>).</summary>
    public bool IsFinished =>
        Status is AgentStepStatus.Done or AgentStepStatus.Failed or AgentStepStatus.Skipped;

    /// <summary>
    /// How long the node took, or null while one end of its window is missing. A running node therefore
    /// reports null here (its duration is not a fact yet) — use <see cref="Elapsed"/> for that.
    /// </summary>
    public TimeSpan? Duration
        => StartedAt is { } start && CompletedAt is { } end && end >= start ? end - start : null;

    /// <summary>
    /// How much time the node has consumed at <paramref name="now"/>: its recorded
    /// <see cref="Duration"/> once it finished, otherwise the time since <see cref="StartedAt"/> — never
    /// negative, so a clock that moved backwards cannot paint "-0:03".
    /// </summary>
    /// <param name="now">The current moment, from the caller's <see cref="System.TimeProvider"/>.</param>
    /// <returns>The elapsed time, or null when the node never started.</returns>
    public TimeSpan? Elapsed(DateTimeOffset now)
    {
        if (Duration is { } recorded)
        {
            return recorded;
        }

        if (StartedAt is not { } start)
        {
            return null;
        }

        return now > start ? now - start : TimeSpan.Zero;
    }
}
