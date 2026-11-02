namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// How <c>TmReviewQueue&lt;TItem&gt;</c> presents the items it was given.
/// </summary>
public enum ReviewQueueMode
{
    /// <summary>
    /// Every item renders as a selectable row (roles <c>listbox</c>/<c>grid</c> depending on whether
    /// the rows carry action buttons) and the row's own buttons act on that row. The default for a
    /// triage screen where the reviewer works down a list.
    /// </summary>
    List = 0,

    /// <summary>
    /// One item at a time: the active item's <c>DetailTemplate</c>, a localized "Item 3 of 12"
    /// position, previous/next navigation and one shared action group. The default for a "one by one"
    /// review session, and the only mode a mobile layout renders with an action bar.
    /// </summary>
    Focus = 1,
}
