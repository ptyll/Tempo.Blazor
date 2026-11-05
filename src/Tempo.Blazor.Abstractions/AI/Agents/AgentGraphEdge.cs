namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One connection of an agent graph: who called whom, how often and when it happened last.
/// <para>
/// The record is a VALUE — an <see cref="AgentGraphCall"/> event folds into it through
/// <see cref="AgentGraphSnapshot.Apply"/>, which returns a new snapshot instead of editing this edge. The
/// <see cref="CallCount"/> is what the surface turns into a THICKNESS (a log scale, so a quiet connection stays
/// visible instead of disappearing next to a busy one), and <see cref="LastCallAt"/> is what it prints.
/// </para>
/// <para>
/// Nothing here carries colour: the direction of the flow is painted by the edge itself and the identity by the two
/// nodes it connects.
/// </para>
/// </summary>
public sealed record AgentGraphEdge
{
    private int _callCount;

    /// <summary>Creates an edge.</summary>
    /// <param name="id">
    /// Stable identity of the edge — the value an <see cref="AgentGraphCall"/> addresses. Null or whitespace generates
    /// one, so a list built in a loop still renders.
    /// </param>
    /// <param name="from">The id of the node the call starts at.</param>
    /// <param name="to">The id of the node the call goes to. Equal to <paramref name="from"/> is a self loop, which is
    /// legal and is painted beside the node.</param>
    public AgentGraphEdge(string id, string from, string to)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        From = from ?? string.Empty;
        To = to ?? string.Empty;
    }

    /// <summary>Stable identity of the edge. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>The id of the node the call starts at; <see cref="AgentGraphNode.Id"/> names it on the snapshot.</summary>
    public string From { get; init; }

    /// <summary>
    /// The id of the node the call goes to. Equal to <see cref="From"/> is a self loop — one agent calling itself —
    /// which the layout paints beside the node instead of dropping.
    /// </summary>
    public string To { get; init; }

    /// <summary>
    /// How many calls this connection carried. Any negative value is clamped to zero on assignment, so an edge only
    /// ever grows and a thickness can never be derived from a negative number. Zero paints the thinnest width.
    /// </summary>
    public int CallCount
    {
        get => _callCount;
        init => _callCount = value < 0 ? 0 : value;
    }

    /// <summary>
    /// When the newest call was recorded, or <c>null</c> when the host does not track it. A surface prints it when it
    /// is set and prints the localized "never" otherwise.
    /// </summary>
    public DateTimeOffset? LastCallAt { get; init; }

    /// <summary>
    /// Whether the connection is active right now (a call is in flight, the tool is being held). Default
    /// <c>false</c>; a surface paints an active edge with its motion, and an inactive one without.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>Whether the edge starts and ends at the same node.</summary>
    public bool IsSelfLoop => string.Equals(From, To, StringComparison.Ordinal);
}
