namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// The built-in decisions <c>TmReviewQueue&lt;TItem&gt;</c> offers for an item. A flagged set, so a
/// host enables exactly the verbs its workflow has: a document queue usually offers
/// <see cref="Approve"/> and <see cref="Reject"/>, a content queue adds <see cref="Edit"/> and
/// <see cref="Skip"/>.
/// </summary>
[Flags]
public enum ReviewQueueActions
{
    /// <summary>No built-in action button. Use it with <c>QuickActions</c> for a decision inbox
    /// whose two buttons are host-defined, or for a read-only queue.</summary>
    None = 0,

    /// <summary>Approves the item. Shortcut <c>A</c>.</summary>
    Approve = 1,

    /// <summary>Marks the item as reviewed with changes. Shortcut <c>E</c>.</summary>
    Edit = 2,

    /// <summary>Rejects the item. Shortcut <c>R</c>.</summary>
    Reject = 4,

    /// <summary>Defers the item: the queue moves on but the item stays pending. Shortcut <c>S</c>.</summary>
    Skip = 8,

    /// <summary>The two decisions of a classic approval: <see cref="Approve"/> and
    /// <see cref="Reject"/>.</summary>
    Decisions = Approve | Reject,

    /// <summary>Every built-in action. The default.</summary>
    All = Approve | Edit | Reject | Skip,
}
