using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The touch half of <see cref="TmKanbanBoard{TItem}"/> (plan 92e96b25, phase 5 / TG-005, user
/// decision O7): the board's touch drag rides the SHARED <c>pointer-drag.js</c> /
/// <c>pointer-gesture.js</c> helpers through the <c>tm-kanban.js</c> module — never a second drag
/// implementation. A long-press starts the drag (so a tap and a page scroll keep working; moving
/// beyond the tolerance before the hold completes is a scroll, not a drag), the module paints the
/// dragged card, the hovered zone and the insertion line with inline styles and CSS classes only —
/// Blazor NEVER re-renders mid-gesture, which is what keeps the captured card's DOM node alive for
/// the whole drag — and the ONE commit arrives here on release. A cancel
/// (<c>pointercancel</c> / Escape / a second finger, all owned by the shared helper) rolls back
/// every inline style in the module and is announced through the board's one polite live region.
/// HTML5 drag and drop stays the mouse path; the "Move…" menu stays the WCAG 2.5.7 alternative.
/// <para>
/// The board is stateless for the gesture: the module reports the source
/// <c>(column, lane, index)</c> triple plus the card's STABLE key (its index in the host's full
/// list — verified at release, so a host refresh under the gesture cancels instead of mis-moving),
/// and the target <c>(column, lane)</c> with the FULL-LIST index of the card before which the drop
/// lands (<c>-1</c> = append), so the item is resolved from the host's unchanged list and a touch
/// drop reports exactly the same <see cref="KanbanMoveEvent{TItem}"/> contract as an HTML5 drop
/// (the insertion midpoint rule runs against the OTHER cards of the cell).
/// </para>
/// </summary>
public partial class TmKanbanBoard<TItem> : IAsyncDisposable
{
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private readonly string _touchRegistrationId = Guid.NewGuid().ToString("N");
    private ElementReference _rootElement;
    private KanbanBoardInterop? _touchInterop;
    private DotNetObjectReference<TmKanbanBoard<TItem>>? _touchDotnetRef;
    // null = detached; the attachment follows !Disabled exactly (a disabled board has no gesture).
    private bool? _touchAttached;

    // ══════════════════════════════════════════════════════════════════
    // Attachment (called from OnAfterRenderAsync, which lives in Move.cs)
    // ══════════════════════════════════════════════════════════════════

    private async Task UpdateTouchAttachmentAsync()
    {
        var want = !Disabled;
        if (want == _touchAttached)
        {
            return;
        }

        _touchAttached = want;

        if (!want)
        {
            if (_touchInterop is not null)
            {
                await _touchInterop.DetachAsync(_touchRegistrationId);
            }

            return;
        }

        _touchDotnetRef ??= DotNetObjectReference.Create(this);
        _touchInterop ??= new KanbanBoardInterop(JSRuntime);
        await _touchInterop.AttachAsync(_touchRegistrationId, _rootElement, _touchDotnetRef);
    }

    // ══════════════════════════════════════════════════════════════════
    // The commit paths (called from tm-kanban.js; not meant for hosts)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// The single commit of a touch drag. Resolves the dragged card from the host's unchanged list
    /// (the module reports <c>(column, lane, index)</c> triples from the press-time DOM, which no
    /// re-render touched during the gesture, plus the card's STABLE key — its index in the host's
    /// full <see cref="Items"/> list — which is double-checked here so a host refresh that moved the
    /// list under the gesture cancels the drop instead of moving the wrong card), builds the same
    /// <see cref="KanbanMoveEvent{TItem}"/> an HTML5 drop reports and announces it through the one
    /// polite live region. Called from JS; not meant to be called by hosts.
    /// </summary>
    /// <param name="fromColumn">The column the drag started in.</param>
    /// <param name="fromLane">The source lane (the public <see cref="NoSwimlaneId"/> sentinel for the
    /// "no value" lane, <see langword="null"/> for the classic layout).</param>
    /// <param name="fromIndex">The card's index in its source cell at press time.</param>
    /// <param name="fromKey">The card's index in the host's full <see cref="Items"/> list at press
    /// time (the module's <c>data-tm-kanban-key</c>). <c>-1</c> when the card carried no key.</param>
    /// <param name="toColumn">The column under the release point.</param>
    /// <param name="toLane">The target lane, same encoding as <paramref name="fromLane"/>.</param>
    /// <param name="beforeIndex">The FULL-LIST index (inside the target cell) of the card before
    /// which the drop lands — the hovered card's own <c>data-tm-kanban-index</c> — or <c>-1</c> to
    /// append. Reading the anchor by its full-list index keeps a <see cref="VirtualizeCards"/> cell
    /// honest: its DOM holds only the visible window, so a window-relative index would misread the
    /// slot.</param>
    [JSInvokable]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task OnKanbanTouchDrop(string fromColumn, string? fromLane, int fromIndex, int fromKey,
        string toColumn, string? toLane, int beforeIndex)
    {
        if (Disabled)
        {
            return;
        }

        var sourceLane = InternalLane(fromLane);
        var targetLane = InternalLane(toLane);
        var sourceItems = GetDropCellItems(fromColumn, sourceLane);
        if (fromIndex < 0 || fromIndex >= sourceItems.Count)
        {
            return;
        }

        var item = sourceItems[fromIndex];
        if (!TouchSourceKeyMatches(item, fromKey))
        {
            // The host list changed under the gesture: the key no longer addresses the card the
            // index names. Roll back like a cancel — nothing moves, and the reader hears it.
            var named = TryTouchSourceByKey(fromKey, out var keyed) ? keyed! : item;
            var cell = CellOf(named);
            Announce(Loc["TmKanban_DragCancelled", CardLabel(named), TargetLabel(cell.Column, cell.Lane)]);
            await InvokeAsync(StateHasChanged);
            return;
        }

        var targetItems = GetDropCellItems(toColumn, targetLane);

        // The anchor is the target cell's card at the FULL-LIST index the module read from the
        // hovered card (the dragged one excluded — a stale report pointing at itself appends).
        var beforeItem = beforeIndex >= 0 && beforeIndex < targetItems.Count
            && !ItemsEqual(targetItems[beforeIndex], item)
            ? targetItems[beforeIndex]
            : default;
        var targetIndex = beforeItem is not null
            ? Math.Max(0, targetItems.FindIndex(candidate => ItemsEqual(candidate, beforeItem)))
            : targetItems.Count;

        var moveEvent = new KanbanMoveEvent<TItem>(
            item, fromColumn, toColumn, targetIndex, beforeItem,
            LaneEventValue(sourceLane), LaneEventValue(targetLane));

        Announce(Loc["TmKanban_CardMoved", CardLabel(item, targetIndex + 1), TargetLabel(toColumn, targetLane)]);

        var sameCell = fromColumn == toColumn && sourceLane == targetLane;
        if (sameCell)
        {
            if (OnItemReordered.HasDelegate)
            {
                await OnItemReordered.InvokeAsync(moveEvent);
            }
        }
        else if (OnItemMoved.HasDelegate)
        {
            await OnItemMoved.InvokeAsync(moveEvent);
        }

        // A JS invocation is not a Blazor event: nothing marks this component for a render, so a host
        // that ignores the callback would never see the announcement.
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// A touch gesture cancelled by the shared helper (<c>pointercancel</c>, Escape, a second
    /// finger, a lost capture): nothing moves and the rollback is announced, naming the card and the
    /// cell it stayed in. Called from JS; not meant to be called by hosts.
    /// </summary>
    /// <param name="fromColumn">The column the drag started in.</param>
    /// <param name="fromLane">The source lane (same encoding as the drop).</param>
    /// <param name="fromIndex">The card's index in its source cell at press time.</param>
    /// <param name="fromKey">The card's index in the host's full <see cref="Items"/> list at press
    /// time. When the key no longer agrees with the index (the host list changed under the
    /// gesture), the key wins for NAMING the card, and the cell is the card's CURRENT one.</param>
    [JSInvokable]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task OnKanbanTouchDragCancelled(string fromColumn, string? fromLane, int fromIndex, int fromKey)
    {
        if (Disabled)
        {
            return;
        }

        var sourceLane = InternalLane(fromLane);
        var sourceItems = GetDropCellItems(fromColumn, sourceLane);
        if (fromIndex < 0 || fromIndex >= sourceItems.Count)
        {
            return;
        }

        var item = TryTouchSourceByKey(fromKey, out var keyed) ? keyed! : sourceItems[fromIndex];
        var cell = TouchSourceKeyMatches(sourceItems[fromIndex], fromKey)
            ? (Column: fromColumn, Lane: sourceLane)
            : CellOf(item);
        Announce(Loc["TmKanban_DragCancelled", CardLabel(item), TargetLabel(cell.Column, cell.Lane)]);
        await InvokeAsync(StateHasChanged);
    }

    // The module speaks the PUBLIC lane encoding (NoSwimlaneId addresses the no-value lane, null the
    // classic layout); the board's internals use the NUL-prefixed sentinel.
    private static string? InternalLane(string? lane) => lane == NoSwimlaneId ? NoLaneId : lane;

    // The key the module reports (the card's index in the host's full list) still addresses the same
    // card the (column, lane, index) triple names. A negative key means the card carried no key
    // attribute — an older markup — and skips the verification.
    private bool TouchSourceKeyMatches(TItem item, int fromKey)
        => !TryTouchSourceByKey(fromKey, out var keyed) ? fromKey < 0
            : ItemsEqual(keyed!, item);

    // The item the module's stable key addresses, or false when the key is absent / out of range
    // (the host list shrank under the gesture).
    private bool TryTouchSourceByKey(int fromKey, out TItem? item)
    {
        if (Items is not null && fromKey >= 0 && fromKey < Items.Count)
        {
            item = Items[fromKey];
            return true;
        }

        item = default;
        return false;
    }

    // The (column, lane) cell an item belongs to RIGHT NOW — the cancel announcement names the cell
    // the card actually stayed in when the list changed under the gesture.
    private (string Column, string? Lane) CellOf(TItem item)
    {
        if (ColumnSelector is null)
        {
            return (string.Empty, null);
        }

        var column = ColumnSelector(item);
        if (!SwimlanesEnabled || SwimlaneSelector is null)
        {
            return (column, null);
        }

        var known = KnownLaneIds();
        var value = SwimlaneSelector(item);
        var lane = value is null || !known.Contains(value) ? NoLaneId : value;
        return (column, lane);
    }

    // The markup writes the same PUBLIC encoding into the zone's data attribute (the internal NUL id
    // can never leave the component, not even into an attribute).
    private static string? LaneDragValue(string? laneId)
        => laneId is null ? null : laneId == NoLaneId ? NoSwimlaneId : laneId;

    /// <summary>Releases the touch-drag registration and the .NET reference of this board.</summary>
    public async ValueTask DisposeAsync()
    {
        _touchAttached = null;
        if (_touchInterop is not null)
        {
            await _touchInterop.DisposeAsync();
            _touchInterop = null;
        }

        _touchDotnetRef?.Dispose();
        _touchDotnetRef = null;
    }
}
