namespace Tempo.Blazor.AI.Models;

/// <summary>The human decision on a memory the agent proposed.</summary>
public enum MemoryReviewDecision
{
    /// <summary>The memory is accepted and becomes active.</summary>
    Approve = 0,

    /// <summary>The memory is rejected; it is no longer used (and is reported as forgotten).</summary>
    Reject = 1,
}
