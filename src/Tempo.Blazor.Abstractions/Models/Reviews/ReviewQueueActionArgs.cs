namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// One raised decision of a review queue: which item, which verb, and — for a host-defined
/// <see cref="ReviewQueueAction.QuickAction"/> — which quick action fired.
/// <para>
/// <c>TmReviewQueue&lt;TItem&gt;</c> awaits the host's <c>OnAction</c> callback with these args and
/// only resolves the item when it completes without throwing, so a failed save keeps the item
/// pending and the queue usable.
/// </para>
/// </summary>
/// <typeparam name="TItem">The item type the queue was given.</typeparam>
public sealed class ReviewQueueActionArgs<TItem>
{
    /// <summary>Creates the args of one raised action.</summary>
    /// <param name="item">The item the action belongs to.</param>
    /// <param name="action">Which verb fired.</param>
    /// <param name="availableActions">The verb set the host had enabled, so the callback can tell a
    /// programmed raise from a user one.</param>
    /// <param name="quickActionId">The <see cref="ReviewQueueQuickAction.Id"/> when
    /// <paramref name="action"/> is <see cref="ReviewQueueAction.QuickAction"/>.</param>
    public ReviewQueueActionArgs(
        TItem item,
        ReviewQueueAction action,
        ReviewQueueActions availableActions = ReviewQueueActions.All,
        string? quickActionId = null)
    {
        Item = item;
        Action = action;
        AvailableActions = availableActions;
        QuickActionId = quickActionId;
    }

    /// <summary>The item the decision belongs to.</summary>
    public TItem Item { get; }

    /// <summary>Which verb fired.</summary>
    public ReviewQueueAction Action { get; }

    /// <summary>The verb set the queue had enabled for this interaction.</summary>
    public ReviewQueueActions AvailableActions { get; }

    /// <summary>The quick action's id when <see cref="Action"/> is
    /// <see cref="ReviewQueueAction.QuickAction"/>; otherwise null.</summary>
    public string? QuickActionId { get; }

    /// <summary>Whether the item was approved.</summary>
    public bool IsApprove => Action == ReviewQueueAction.Approve;

    /// <summary>Whether the item was marked as reviewed with changes.</summary>
    public bool IsEdit => Action == ReviewQueueAction.Edit;

    /// <summary>Whether the item was rejected.</summary>
    public bool IsReject => Action == ReviewQueueAction.Reject;

    /// <summary>Whether the item was deferred (still pending after the callback).</summary>
    public bool IsSkip => Action == ReviewQueueAction.Skip;

    /// <summary>Whether a host-defined quick action fired.</summary>
    public bool IsQuickAction => Action == ReviewQueueAction.QuickAction;
}
