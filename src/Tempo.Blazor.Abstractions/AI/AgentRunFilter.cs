namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The "show only" filter of an agent run tree: which nodes are worth looking at right now.
/// <para>
/// The filter never breaks the tree: a row survives when it matches the filter <b>or</b> when a surviving
/// descendant needs it as its ancestor. Filtering to <see cref="Failed"/> therefore still shows the chain
/// that led to the failure, which is the only context that makes the failure readable.
/// </para>
/// </summary>
public enum AgentRunFilter
{
    /// <summary>Every node of the run.</summary>
    All = 0,

    /// <summary>Running nodes, plus their ancestors.</summary>
    Running = 1,

    /// <summary>Failed nodes, plus their ancestors.</summary>
    Failed = 2,
}
