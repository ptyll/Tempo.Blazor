namespace Tempo.Blazor.Abstractions.Models.Approvals;

/// <summary>
/// One human decision on an approval request: the outcome, the optional note the decider wrote and
/// who decided when.
/// <para>
/// <c>TmApprovalCard</c> hands this to its <c>OnDecision</c> callback and only leaves
/// <see cref="ApprovalState.Pending"/> when the callback completes without throwing, so a failed
/// save never looks like a decision.
/// </para>
/// </summary>
public sealed record ApprovalDecision
{
    /// <summary>Creates a decision. Only <see cref="ApprovalState.Approved"/> and
    /// <see cref="ApprovalState.Rejected"/> are decisions — any other state means the request was
    /// never decided by a human and is rejected here rather than silently accepted.</summary>
    /// <param name="state">The decision outcome.</param>
    /// <param name="note">Free-text note from the decider, when the host asked for one.</param>
    /// <param name="decidedBy">Display name of the decider; the host supplies it because the
    /// library does not know the current user.</param>
    /// <param name="decidedAt">Moment of the decision; the host supplies it, so a stamp is the
    /// server's, not the browser's.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is neither
    /// <see cref="ApprovalState.Approved"/> nor <see cref="ApprovalState.Rejected"/>.</exception>
    public ApprovalDecision(
        ApprovalState state,
        string? note = null,
        string? decidedBy = null,
        DateTimeOffset? decidedAt = null)
    {
        if (state is not (ApprovalState.Approved or ApprovalState.Rejected))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state), state, "An approval decision is either Approved or Rejected.");
        }

        State = state;
        Note = note;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
    }

    /// <summary>Outcome of the decision: <see cref="ApprovalState.Approved"/> or
    /// <see cref="ApprovalState.Rejected"/>.</summary>
    public ApprovalState State { get; }

    /// <summary>Note the decider wrote, or null when none was asked for or given.</summary>
    public string? Note { get; }

    /// <summary>Display name of the person who decided, when the host knows it.</summary>
    public string? DecidedBy { get; }

    /// <summary>Moment of the decision, when the host knows it.</summary>
    public DateTimeOffset? DecidedAt { get; }

    /// <summary>Whether the request was approved (the shorthand hosts branch on most often).</summary>
    public bool IsApproved => State == ApprovalState.Approved;

    /// <summary>Creates an approval decision.</summary>
    /// <param name="decidedBy">Display name of the decider.</param>
    /// <param name="decidedAt">Moment of the decision.</param>
    /// <param name="note">Optional note.</param>
    /// <returns>A decision with <see cref="State"/> set to
    /// <see cref="ApprovalState.Approved"/>.</returns>
    public static ApprovalDecision Approve(
        string? decidedBy = null, DateTimeOffset? decidedAt = null, string? note = null)
        => new(ApprovalState.Approved, note, decidedBy, decidedAt);

    /// <summary>Creates a rejection decision.</summary>
    /// <param name="note">Optional note explaining the rejection.</param>
    /// <param name="decidedBy">Display name of the decider.</param>
    /// <param name="decidedAt">Moment of the decision.</param>
    /// <returns>A decision with <see cref="State"/> set to
    /// <see cref="ApprovalState.Rejected"/>.</returns>
    public static ApprovalDecision Reject(
        string? note = null, string? decidedBy = null, DateTimeOffset? decidedAt = null)
        => new(ApprovalState.Rejected, note, decidedBy, decidedAt);
}
