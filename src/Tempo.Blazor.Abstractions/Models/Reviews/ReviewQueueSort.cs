namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// The order <c>TmReviewQueue&lt;TItem&gt;</c> presents its items in. Sorting is stable: items with
/// an equal rank keep the order the host supplied them in, so a re-render never shuffles rows.
/// </summary>
public enum ReviewQueueSort
{
    /// <summary>
    /// Risk first (Critical before High before Medium before Low), then the highest
    /// <c>PrioritySelector</c> value. The default — the item most likely to hurt comes first.
    /// </summary>
    RiskThenPriority = 0,

    /// <summary>
    /// The highest <c>PrioritySelector</c> value first, then risk. Use it when the host's own
    /// ordering already ranks urgency and risk is only a tie-breaker.
    /// </summary>
    PriorityThenRisk = 1,

    /// <summary>The order of <c>Items</c>, untouched.</summary>
    InputOrder = 2,
}
