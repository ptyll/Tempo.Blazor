namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What a node of an agent graph IS, in the one vocabulary every agent visualization shares.
/// <para>
/// The kind decides the node's column in the layered layout (a human asks on the left, an agent answers, a resource
/// it reads follows and a tool it calls stands on the right), its glyph, its stable CSS hook and its localized word.
/// The order of the members is therefore the LEFT-TO-RIGHT order of the columns, and it is part of the contract.
/// </para>
/// <para>
/// The set is deliberately small and transport-neutral: a host maps its own backend vocabulary (a participant role, a
/// tool-server key, a document store, the human in the loop) onto these four values.
/// </para>
/// </summary>
public enum AgentGraphNodeKind
{
    /// <summary>
    /// The agent that acts — a participant, a role, a runtime agent. Painted in the middle-left column, with the
    /// agent palette slot of <see cref="AgentGraphNode.Agent"/> when the host supplied one.
    /// </summary>
    Agent = 0,

    /// <summary>
    /// A tool the agent called (a tool server, a function, an MCP server). Painted to the RIGHT of the agent, because
    /// an edge from an agent to a tool is what a call looks like.
    /// </summary>
    Tool = 1,

    /// <summary>
    /// A resource an agent read or wrote (a document, a table, a ledger, a memory store). Painted between the agent
    /// column and the tool column.
    /// </summary>
    Resource = 2,

    /// <summary>
    /// The human in the loop — the person who asked, approved or is being waited for. Painted to the LEFT of the
    /// agents, because the request comes from there.
    /// </summary>
    Human = 3,
}
