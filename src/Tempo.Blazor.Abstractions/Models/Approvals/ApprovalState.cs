namespace Tempo.Blazor.Abstractions.Models.Approvals;

/// <summary>
/// Lifecycle state of an approval request rendered by <c>TmApprovalCard</c>.
/// <para>
/// The host owns the state: it passes <c>State</c> and receives every transition through
/// <c>StateChanged</c>. <see cref="Pending"/> is the only state in which the card offers its
/// actions; every other state collapses the card into a single result row.
/// </para>
/// </summary>
public enum ApprovalState
{
    /// <summary>Waiting for a decision; the card shows its actions.</summary>
    Pending,

    /// <summary>A human approved the request.</summary>
    Approved,

    /// <summary>A human rejected the request.</summary>
    Rejected,

    /// <summary>The deadline passed before anyone decided.</summary>
    Expired,

    /// <summary>The request was withdrawn, so there is nothing to decide.</summary>
    Cancelled,
}
