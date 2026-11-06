namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Describes one committed CROSS-LIST move of a <c>TmSortableList&lt;TItem&gt;</c>: the row at
/// <see cref="FromIndex"/> of the SOURCE list (the component that raised the callback) was dropped
/// into the list identified by <see cref="ToListId"/> at position <see cref="ToIndex"/>.
/// <para>
/// Raised on the SOURCE list through <c>OnTransfer</c>, because that is the component that owns the
/// moved row and can name it. The component never moves anything itself: the host removes the row from
/// the source collection, inserts it into the target collection and re-renders — and it owns whatever
/// announcement the move deserves, since only the host knows whether the drop was accepted (a full
/// capacity, a read-only board) or refused.
/// </para>
/// <para>
/// Two lists join the same drag group through the same non-empty <c>Group</c> value; <c>ListId</c> is
/// how a host recognises the target inside its handler. <see cref="ToIndex"/> is the insertion slot
/// the pointer resolved over the target's CURRENT rows, so <c>rows.Count</c> means "after the last
/// row" — a host with no meaningful order inside the target (a board column) simply ignores it.
/// </para>
/// </summary>
public sealed class SortableTransferArgs
{
    /// <summary>Creates the args of one committed cross-list move.</summary>
    /// <param name="fromListId">The <c>ListId</c> of the list the row came from.</param>
    /// <param name="fromIndex">The index of the moved row in the source list's items.</param>
    /// <param name="toListId">The <c>ListId</c> of the list the row was dropped into.</param>
    /// <param name="toIndex">The insertion slot the pointer resolved over the target's rows.</param>
    /// <param name="source">Which input method committed the move.</param>
    public SortableTransferArgs(string? fromListId, int fromIndex, string? toListId, int toIndex,
        SortableReorderSource source)
    {
        FromListId = fromListId;
        FromIndex = fromIndex;
        ToListId = toListId;
        ToIndex = toIndex;
        Source = source;
    }

    /// <summary>The <c>ListId</c> of the list the row came from (the raising component's own id).</summary>
    public string? FromListId { get; init; }

    /// <summary>The zero-based index of the moved row in the source list's items.</summary>
    public int FromIndex { get; init; }

    /// <summary>The <c>ListId</c> of the list the row was dropped into.</summary>
    public string? ToListId { get; init; }

    /// <summary>
    /// The insertion slot over the target list's rows at the moment of the release. Equal to the target
    /// row count when the row was dropped below the last one, so a host that cares about order can
    /// insert without an extra clamp.
    /// </summary>
    public int ToIndex { get; init; }

    /// <summary>Which input method committed the move. A transfer only ever comes from the pointer.</summary>
    public SortableReorderSource Source { get; init; }
}
