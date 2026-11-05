namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One node of an agent graph: an agent, a tool, a resource or a human, with the state it is in right now and the
/// optional group it belongs to.
/// <para>
/// The record is a VALUE — a host recomposes it with <c>node with { … }</c> when a state or a label changes, and no
/// component mutates it. <see cref="Id"/> is what an <see cref="AgentGraphEdge"/> addresses and what a selection
/// reports; a blank id is generated at construction, so a graph built in a loop still renders.
/// </para>
/// <para>
/// <see cref="Agent"/> is how an AGENT node borrows the shared identity contract: the palette slot and the initials
/// an agent paints in every other visualization. A tool, a resource and a human carry no palette slot — painting them
/// in an agent's colour would claim an identity they do not have.
/// </para>
/// </summary>
public sealed record AgentGraphNode
{
    /// <summary>Creates a node.</summary>
    /// <param name="id">
    /// Stable identity of the node — the key an edge addresses and the value a selection reports. Null or whitespace
    /// generates one, so a list built in a loop still renders; a host that wants a stable selection supplies its own
    /// id (the same rule <see cref="AgentRef"/> follows for agents).
    /// </param>
    /// <param name="label">What the node is called, in the host's own words. Empty renders the surface's own fallback.</param>
    public AgentGraphNode(string id, string label)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        Label = label ?? string.Empty;
    }

    /// <summary>Stable identity of the node. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>The display name, as the host wants it read. Empty when the host supplied none.</summary>
    public string Label { get; init; }

    /// <summary>What the node is. Default <see cref="AgentGraphNodeKind.Agent"/>; the kind decides its column.</summary>
    public AgentGraphNodeKind Kind { get; init; }

    /// <summary>
    /// What the node is doing right now. Default <see cref="AgentActivityStatus.Idle"/>. Painted as a tone, an icon AND
    /// a localized word, never as a colour alone; <see cref="AgentActivityStatus.WaitingForApproval"/> is the state the
    /// surface highlights, because nothing moves until the reader acts.
    /// </summary>
    public AgentActivityStatus Status { get; init; }

    /// <summary>
    /// The group the node belongs to, or <c>null</c> for an ungrouped node. Members of one group are laid out as one
    /// contiguous run inside their column and get ONE labelled box around them; a null group never joins a box.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// The identity of the agent an AGENT node stands for, or <c>null</c>. Setting it makes the node paint the agent's
    /// palette slot and initials (and its name, when <see cref="Label"/> is empty); a tool, a resource and a human
    /// leave it null, because they are not in the agent palette.
    /// </summary>
    public AgentRef? Agent { get; init; }

    /// <summary>Whether the host supplied a label. A surface paints its own localized fallback when it is false.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>
    /// The one or two letters the node's avatar paints: the agent's own initials when <see cref="Agent"/> is set, the
    /// initials of <see cref="Label"/> otherwise, and <c>"?"</c> when there is no name at all (an avatar must never be
    /// an empty circle). Diacritics and surrogate pairs are kept as whole text elements.
    /// </summary>
    public string Initials => Agent is { } agent ? agent.Initials : AgentRef.InitialsOf(Label);

    /// <summary>
    /// The agent palette token the node paints its avatar with, or an empty string when the node is not an agent.
    /// Always a declared <c>--tm-agent-color-{0..7}</c> token when it is not empty.
    /// </summary>
    public string ColorToken => Agent is { } agent ? agent.ColorToken : string.Empty;

    /// <summary>The name a surface prints: <see cref="Label"/>, or the agent's name when the label is empty.</summary>
    public string DisplayName => HasLabel ? Label : Agent is { } agent && agent.HasName ? agent.Name : string.Empty;
}
