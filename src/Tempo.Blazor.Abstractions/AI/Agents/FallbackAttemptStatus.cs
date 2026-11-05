namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What became of one model in a fallback chain — the outcome a fallback surface paints per attempt.
/// <para>
/// <see cref="NotTried"/> is the default (0) on purpose: an attempt a host has not classified yet must never read as
/// skipped or succeeded, because both are claims about a run that cost money. The values are ordered from the least to
/// the most consequential so a caller can compare them.
/// </para>
/// </summary>
public enum FallbackAttemptStatus
{
    /// <summary>The attempt never ran (the chain was already answered or the run stopped).</summary>
    NotTried = 0,

    /// <summary>The attempt was deliberately not made (a policy, a budget, a missing capability).</summary>
    Skipped = 1,

    /// <summary>The attempt ran and failed; <see cref="FallbackAttempt.FailureReason"/> says why.</summary>
    Failed = 2,

    /// <summary>The attempt ran and produced the answer; it ends the chain.</summary>
    Succeeded = 3,
}
