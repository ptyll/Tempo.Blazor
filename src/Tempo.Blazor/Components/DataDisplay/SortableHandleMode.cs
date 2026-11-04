namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Which part of a <c>TmSortableList</c> row starts a pointer drag.
/// </summary>
public enum SortableHandleMode
{
    /// <summary>
    /// Only a dedicated drag handle starts the drag (the default), so the rest of the row keeps its
    /// own clicks, links and inputs. The handle is a decorative pointer affordance: the row itself
    /// stays the focusable keyboard grip.
    /// </summary>
    Handle = 0,

    /// <summary>
    /// The whole row is the drag surface. Use it for plain rows that hold no interactive content of
    /// their own; a touch press has to be held (400 ms) before the drag starts, so the row can still
    /// be tapped and the page still scrolls.
    /// </summary>
    WholeItem = 1,
}
