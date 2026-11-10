namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Describes where a drag that is STILL RUNNING would land: the row at <see cref="FromIndex"/> of the list
/// identified by <see cref="FromListId"/> is over the list identified by <see cref="ToListId"/>.
/// <para>
/// Raised on the SOURCE list through <c>OnDragOver</c> only when the list opted in with
/// <c>TrackDragOver="true"</c>, and throttled by the module: the first change of the gesture arrives at once, a
/// change inside the throttle window arrives once when the window closes (the latest list wins), and an unchanged
/// list is never repeated. <see cref="ToListId"/> equals <see cref="FromListId"/> while the row hangs over its own
/// list (which is how a host learns WHICH row is being dragged), and is <see langword="null"/> once the gesture ends
/// or is cancelled — that is the resting state, so a projection painted during the drag can be taken back.
/// </para>
/// <para>
/// This is a PREVIEW, not a move: the component never changes its own list here, and the ONE committed move still
/// arrives through <c>OnTransfer</c> (or <c>OnReorder</c>) on release.
/// </para>
/// </summary>
public sealed class SortableDragOverArgs
{
    /// <summary>Creates the args of one throttled drag-over report.</summary>
    /// <param name="fromListId">The <c>ListId</c> of the list the dragged row belongs to.</param>
    /// <param name="fromIndex">The index of the dragged row in the source list's items.</param>
    /// <param name="toListId">The <c>ListId</c> of the list the row is over, or null for the resting state.</param>
    public SortableDragOverArgs(string? fromListId, int fromIndex, string? toListId)
    {
        FromListId = fromListId;
        FromIndex = fromIndex;
        ToListId = toListId;
    }

    /// <summary>The <c>ListId</c> of the list the dragged row belongs to.</summary>
    public string? FromListId { get; init; }

    /// <summary>The zero-based index of the dragged row in the source list's items.</summary>
    public int FromIndex { get; init; }

    /// <summary>
    /// The <c>ListId</c> of the list the row is currently over. Null when the gesture ended or was cancelled (the
    /// resting state), and equal to <see cref="FromListId"/> while the row is back over its own list.
    /// </summary>
    public string? ToListId { get; init; }
}
