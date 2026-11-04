namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// How a committed reorder of a <c>TmSortableList</c> was performed. A host that has to announce or
/// log the change differently per input method (or that wants to treat a menu move as a deliberate
/// command) reads this instead of guessing.
/// </summary>
public enum SortableReorderSource
{
    /// <summary>The shared pointer drag: mouse, pen, or a touch long-press.</summary>
    Pointer = 0,

    /// <summary>A keyboard move: the grab/arrow/drop sequence or the Alt+arrow shortcut.</summary>
    Keyboard = 1,

    /// <summary>The move menu — the tap alternative to dragging (WCAG 2.5.7).</summary>
    Menu = 2,
}

/// <summary>
/// Describes one committed reorder of a <c>TmSortableList&lt;TItem&gt;</c>: the item at
/// <see cref="FromIndex"/> of the host's own <c>Items</c> list belongs at <see cref="ToIndex"/>.
/// <para>
/// The component never mutates the host's list. It raises exactly one of these per committed gesture
/// and then renders the order the host's list reports, so the host applies the move (and re-renders)
/// or the row snaps back — a half-applied order can never be displayed.
/// </para>
/// <para>
/// Every property is <c>init</c>-only and the three-argument constructor stays: a later phase can add
/// fields (the moved item, its key) without a second constructor or a breaking change.
/// </para>
/// </summary>
public sealed class SortableReorderArgs
{
    /// <summary>Creates the args of one committed reorder.</summary>
    /// <param name="fromIndex">The index of the moved item in the host's list.</param>
    /// <param name="toIndex">The index the item belongs at after the move.</param>
    /// <param name="source">Which input method committed the move.</param>
    public SortableReorderArgs(int fromIndex, int toIndex, SortableReorderSource source)
    {
        FromIndex = fromIndex;
        ToIndex = toIndex;
        Source = source;
    }

    /// <summary>The zero-based index of the moved item in the host's <c>Items</c> list.</summary>
    public int FromIndex { get; init; }

    /// <summary>The zero-based index the item belongs at after the move.</summary>
    public int ToIndex { get; init; }

    /// <summary>Which input method committed the move.</summary>
    public SortableReorderSource Source { get; init; }
}
