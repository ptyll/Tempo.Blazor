namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One review decision the reader made about a memory the agent proposed: WHICH memory and WHAT was decided.
/// <para>
/// The surface reports the decision and never applies it to the host's data — <c>OnReview</c> is the host's contract,
/// and the panel keeps its own copy of the outcome only so the reader sees the row change before the store answers.
/// </para>
/// </summary>
public sealed record MemoryReview
{
    /// <summary>Creates a review decision.</summary>
    /// <param name="item">The memory under review.</param>
    /// <param name="decision">What the reader decided.</param>
    public MemoryReview(MemoryItem item, MemoryReviewDecision decision)
    {
        Item = item;
        Decision = decision;
    }

    /// <summary>The memory under review.</summary>
    public MemoryItem Item { get; init; }

    /// <summary>What the reader decided about it.</summary>
    public MemoryReviewDecision Decision { get; init; }
}
