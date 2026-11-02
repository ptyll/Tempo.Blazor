namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// Which button produced a <c>ReviewQueueActionArgs&lt;TItem&gt;</c>.
/// </summary>
public enum ReviewQueueAction
{
    /// <summary>The Approve action (<see cref="ReviewQueueActions.Approve"/>).</summary>
    Approve = 0,

    /// <summary>The Edit action (<see cref="ReviewQueueActions.Edit"/>).</summary>
    Edit = 1,

    /// <summary>The Reject action (<see cref="ReviewQueueActions.Reject"/>).</summary>
    Reject = 2,

    /// <summary>The Skip action (<see cref="ReviewQueueActions.Skip"/>). Skip never resolves the
    /// item — it keeps it pending and only moves the queue on.</summary>
    Skip = 3,

    /// <summary>A host-defined <c>ReviewQueueQuickAction</c>. The args carry the action's
    /// <see cref="ReviewQueueActionArgs{TItem}.QuickActionId"/> and the row resolves like any other
    /// decision.</summary>
    QuickAction = 4,
}
