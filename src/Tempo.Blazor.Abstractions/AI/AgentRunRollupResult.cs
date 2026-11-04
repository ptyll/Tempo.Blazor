namespace Tempo.Blazor.AI.Models;

/// <summary>
/// A number of an agent run, summed over one node's whole SUBTREE (the node itself included) — what a parent
/// row of <c>TmAgentRunTree</c> shows next to its own row, and what a host needs to render a summary
/// elsewhere without re-deriving it from a private implementation.
/// <para>
/// Every value keeps the "not reported" contract of the model: a value no node of the subtree reports is
/// <see langword="null"/>, never zero, so "this run reports no cost" and "this run cost nothing" stay
/// distinguishable.
/// </para>
/// </summary>
public sealed record AgentRunRollupResult
{
    /// <summary>Sum of the reported costs, in micro-units. Null when no node reports a cost.</summary>
    public long? CostMicroUnits { get; init; }

    /// <summary>Sum of the reported token counts. Null when no node reports tokens.</summary>
    public long? Tokens { get; init; }

    /// <summary>
    /// Sum of the subtree's elapsed times: the recorded duration of a finished node and the time from its
    /// start to the caller's "now" for a running one. Null when no node carries a start.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>How many nodes the roll-up covers, the node itself included.</summary>
    public int NodeCount { get; init; }

    /// <summary>How many nodes hang BELOW the node (<see cref="NodeCount"/> minus the node itself).</summary>
    public int DescendantCount => NodeCount > 0 ? NodeCount - 1 : 0;

    /// <summary>Whether the roll-up covers at least one descendant, i.e. whether the window shows a
    /// summary rather than the node's own numbers.</summary>
    public bool IsSummary => DescendantCount > 0;

    /// <summary>An empty roll-up: no node, nothing reported.</summary>
    public static AgentRunRollupResult Empty { get; } = new();
}
