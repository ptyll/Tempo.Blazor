using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Overlay;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The non-drag half of <see cref="TmKanbanBoard{TItem}"/> (plan a98e4a71, phase 4 / WCAG 2.5.7):
/// the focusable card with one roving tab stop per cell, the per-card "Move…" menu (a
/// <see cref="TmContextMenu"/> that offers every column × swimlane target, collapsed zones included,
/// plus Move up / Move down for the cell's own order), the single polite live region and the focus
/// hand-back after a move.
/// <para>
/// The card is a NON-NATIVE focusable, so it emulates its own activation per
/// <c>docs/keyboard-activation-convention.md</c>: Enter fires <c>OnCardClick</c> on keydown (with
/// <c>e.Repeat</c> guarded), Space on keyup — exactly where a native <c>&lt;button&gt;</c> fires its
/// click. Everything inside <c>CardTemplate</c> and the move menu is fenced from those handlers
/// (keydown AND keyup), so a control the host renders in a card keeps its own keys.
/// </para>
/// <para>
/// This file is a partial of the component (not a child component): the card markup, the drop
/// handlers and the collapse state all already live in the razor part, and a child component would
/// have had to re-plumb every one of them.
/// </para>
/// </summary>
/// <typeparam name="TItem">Card item type.</typeparam>
public partial class TmKanbanBoard<TItem>
{
    /// <summary>The bUnit/JS observable of an <c>ElementReference.FocusAsync</c> call.</summary>
    private sealed class ElementHandle
    {
        /// <summary>The captured reference.</summary>
        public ElementReference Ref;
    }

    /// <summary>The component reference of one card's move menu, so a key can open it.</summary>
    private sealed class MoveMenuHandle
    {
        /// <summary>The menu of the card this handle belongs to.</summary>
        public TmContextMenu? Menu;
    }

    private readonly Dictionary<string, ElementHandle> _zoneHandles = new(StringComparer.Ordinal);
#pragma warning disable CS8714 // the board accepts any card type, including a nullable-annotated one
    private readonly Dictionary<TItem, ElementHandle> _cardHandles = [];
    private readonly Dictionary<TItem, MoveMenuHandle> _moveMenus = [];
#pragma warning restore CS8714
    private readonly string _instanceId = Guid.NewGuid().ToString("n")[..8];

    private TItem? _rovingItem;
    private readonly ElementHandle _pendingFocusCard = new();
    private TItem? _pendingFocusItem;
    private string? _pendingFocusColumn;
    private string? _pendingFocusLane;
    private int _pendingFocusIndex = -1;
    private bool _focusHandback;
    private bool _focusBatchReady;
    private string _focusTarget = string.Empty;
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    /// <summary>
    /// Whether a card offers the non-drag "Move…" action and the focusable card that carries it.
    /// Defaults to <see langword="true"/>: a drag-only card is a WCAG 2.5.7 failure, so the accessible
    /// path is on unless a host switches it off. <see langword="false"/> restores the pre-2.11 card
    /// markup exactly — no tab stop, no menu, no wrapper, the host template rendered straight into the
    /// draggable card — for a board whose host moves cards elsewhere. A <see cref="Disabled"/> board
    /// renders the same inert card.
    /// </summary>
    [Parameter] public bool ShowMoveMenu { get; set; } = true;

    /// <summary>
    /// Optional function that names a card for assistive technology: the "Move …" trigger, the move
    /// menu's sheet title, the "(current)" entry of the card's own cell and the live region all read it.
    /// <para>
    /// Prefer setting it. When it is <see langword="null"/> the board falls back to the item's
    /// <c>ToString()</c> only when the item's type actually overrides it (a record's generated
    /// <c>ToString()</c> names its fields); a plain class or struct would only print its type name, so
    /// the board uses the localized "card {n}" instead — <c>n</c> being the card's 1-based position in
    /// its cell. That is readable, but anonymous: a label selector is what names the card itself.
    /// </para>
    /// </summary>
    [Parameter] public Func<TItem, string>? CardLabelSelector { get; set; }

    // The accessible path is offered by an enabled board that has not switched the menu off (D6/D8).
    private bool IsMoveMenuEnabled => ShowMoveMenu && !Disabled;

    private string InstructionsId => "tm-kanban-instructions-" + _instanceId;

    // The activation sentence is only true while the host actually handles a card activation, so a
    // board with no OnCardClick does not tell the reader to press a key that does nothing.
    private string CardInstructions
    {
        get
        {
            var move = Loc["TmKanban_CardInstructions"];
            return OnCardClick.HasDelegate ? Loc["TmKanban_CardInstructionsOpen"] + " " + move : move;
        }
    }

    /// <summary>
    /// The name assistive technology reads for a card: the host's <see cref="CardLabelSelector"/>, else
    /// the item's own <c>ToString()</c> when the type overrides it, else the localized "card {n}" with
    /// the card's 1-based position in its cell — never a bare type name.
    /// </summary>
    /// <param name="item">The card item.</param>
    /// <param name="ordinal">The card's 1-based position in its cell, when it is known.</param>
    private string CardLabel(TItem item, int ordinal = 0)
    {
        if (CardLabelSelector?.Invoke(item) is { Length: > 0 } explicitLabel) return explicitLabel;
        if (item is not null && OverridesToString(item.GetType()) && item.ToString() is { Length: > 0 } fallback)
        {
            return fallback;
        }

        return Loc["TmKanban_CardNumber", ordinal > 0 ? ordinal : 1];
    }

    // A record's generated ToString() names its fields, so it is a usable card label; object.ToString()
    // only prints the type name, which names nothing a reader can recognise. Cached per runtime type:
    // the check runs for every card of every render of a board that has no label selector.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> ToStringOverrides = new();

    private static bool OverridesToString(Type type)
        => ToStringOverrides.GetOrAdd(type, static candidate =>
            candidate.GetMethod(nameof(ToString), Type.EmptyTypes) is { } method
            && method.DeclaringType != typeof(object));

    // The "no value" lane is addressed by the internal NUL-prefixed id; a test id must never carry it.
    private static string LaneToken(string? laneId)
        => laneId is null || laneId == NoLaneId ? "none" : laneId;

    // ══════════════════════════════════════════════════════════════════════
    // Roving tab stop
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The index of the cell's single tab stop: the card the reader last focused when it is still in
    /// this cell, otherwise the first card. One stop per cell is what keeps a board of fifty cards
    /// navigable — the arrow keys walk the cell's cards, Tab walks the cells.
    /// </summary>
    /// <param name="items">The cards of one cell, in render order.</param>
    /// <returns>The roving index, or <c>-1</c> for an empty cell.</returns>
    private int RovingIndex(string columnId, string? laneId, IReadOnlyList<TItem> items)
    {
        // A move in flight pins the stop to the position the host was told to put the card at, so the
        // tab order agrees with the focus the hand-back is about to place.
        if (_focusHandback && _pendingFocusIndex >= 0 && _pendingFocusIndex < items.Count
            && _pendingFocusColumn == columnId && _pendingFocusLane == laneId)
        {
            return _pendingFocusIndex;
        }

        if (_rovingItem is not null)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (ItemsEqual(items[index], _rovingItem)) return index;
            }
        }

        return items.Count > 0 ? 0 : -1;
    }

    private static bool ItemsEqual(TItem left, TItem right)
        => EqualityComparer<TItem>.Default.Equals(left, right);

    // ══════════════════════════════════════════════════════════════════════
    // The tab stop of a VIRTUALIZED cell
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The window of one cell's cards that <c>&lt;Virtualize&gt;</c> asked the board to render: the
    /// indices seen since the run restarted, and the last one (which proves whether the next call
    /// continues the same window).
    /// </summary>
    private sealed class VirtualWindow
    {
        /// <summary>Indices rendered in the current window.</summary>
        public readonly HashSet<int> Indices = [];

        /// <summary>The last index the fragment was invoked for.</summary>
        public int LastIndex = int.MinValue;
    }

    private readonly Dictionary<string, VirtualWindow> _virtualWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _virtualFallbackStops = new(StringComparer.Ordinal);

    private static string CellKey(string columnId, string? laneId) => columnId + "\0" + (laneId ?? string.Empty);

    private static (string ColumnId, string? LaneId) SplitCellKey(string key)
    {
        var separator = key.IndexOf('\0');
        var lane = key[(separator + 1)..];
        return (key[..separator], lane.Length == 0 ? null : lane);
    }

    /// <summary>
    /// Whether a rendered card carries its cell's single tab stop.
    /// <para>
    /// A classic or swimlane cell renders every card, so the roving index is always in the DOM and the
    /// answer is simply <c>index == rovingIndex</c>. A <c>&lt;Virtualize&gt;</c>d cell renders only the
    /// WINDOW Virtualize asks for: the roving card can be outside it (the reader scrolled away, or a
    /// move handed the focus to a position the window does not show), and a cell whose cards all carry
    /// <c>tabindex="-1"</c> silently leaves the tab order. The first card the window renders is then
    /// the stop — decided immediately when the window provably starts AFTER the roving card, otherwise
    /// one render later by <see cref="RefreshVirtualTabStops"/>.
    /// </para>
    /// </summary>
    /// <param name="columnId">The column the card is rendered in.</param>
    /// <param name="laneId">The swimlane of the cell, or <see langword="null"/>.</param>
    /// <param name="index">The card's index in its cell.</param>
    /// <param name="rovingIndex">The index of the cell's roving stop.</param>
    /// <param name="keyed">Whether the list renders every card (classic / swimlane) or a window.</param>
    private bool IsCardTabStop(string columnId, string? laneId, int index, int rovingIndex, bool keyed)
    {
        if (keyed) return index == rovingIndex;

        var cell = CellKey(columnId, laneId);
        if (!_virtualWindows.TryGetValue(cell, out var window))
        {
            window = new VirtualWindow();
            _virtualWindows[cell] = window;
        }

        // Cards arrive in ascending index order inside one window, so a call that does not continue
        // that run is the first card of a NEW window (a scroll, or a fresh data set): the tracked
        // window restarts with it.
        if (index != window.LastIndex + 1) window.Indices.Clear();
        window.LastIndex = index;
        window.Indices.Add(index);

        if (index == rovingIndex) return true;

        // The window starts after the roving card, so it cannot contain it: its first card is the stop.
        if (window.Indices.Count == 1 && index > rovingIndex) return true;

        return _virtualFallbackStops.TryGetValue(cell, out var fallback) && fallback == index;
    }

    /// <summary>
    /// Re-points every virtualized cell's tab stop after a render: when the window the board was asked
    /// to render does not contain the roving card, the FIRST rendered card becomes the cell's stop.
    /// </summary>
    /// <returns>Whether the board must render once more to apply the new stop.</returns>
    private bool RefreshVirtualTabStops()
    {
        var changed = false;

        foreach (var (cell, window) in _virtualWindows)
        {
            if (window.Indices.Count == 0) continue;

            var (column, lane) = SplitCellKey(cell);
            var items = GetDropCellItems(column, lane);
            var roving = RovingIndex(column, lane, items);

            if (window.Indices.Contains(roving))
            {
                // The roving card is rendered, so the fallback has nothing to stand in for.
                changed |= _virtualFallbackStops.Remove(cell);
                continue;
            }

            var first = int.MaxValue;
            foreach (var rendered in window.Indices) first = Math.Min(first, rendered);

            if (!_virtualFallbackStops.TryGetValue(cell, out var current) || current != first)
            {
                _virtualFallbackStops[cell] = first;
                changed = true;
            }
        }

        // The next render re-tracks its own window from scratch.
        foreach (var window in _virtualWindows.Values) window.Indices.Clear();

        return changed;
    }

    private void HandleCardFocus(TItem item)
    {
        if (_rovingItem is not null && ItemsEqual(_rovingItem, item)) return;
        _rovingItem = item;
        StateHasChanged();
    }

    private async Task FocusCardAsync(TItem item)
    {
        _rovingItem = item;
        StateHasChanged();

        if (item is not null && _cardHandles.TryGetValue(item, out var handle) && handle.Ref.Id is not null)
        {
            await FocusSafelyAsync(handle.Ref);
        }
    }

    /// <summary>
    /// Moves the focus to the card at <paramref name="index"/> of the cell, returning whether the
    /// element was reachable. A detached or not-yet-captured element is a no-op: the reader can
    /// still walk the cell with the arrow keys.
    /// </summary>
    /// <param name="items">The cell's cards.</param>
    /// <param name="index">Target index within <paramref name="items"/>.</param>
    /// <returns>A task that completes with whether a focus call was made.</returns>
    private async ValueTask<bool> FocusCardAtAsync(IReadOnlyList<TItem> items, int index)
    {
        if (index < 0 || index >= items.Count) return false;

        var target = items[index];
        _rovingItem = target;
        StateHasChanged();

        if (_cardHandles.TryGetValue(target, out var handle) && handle.Ref.Id is not null)
        {
            return await FocusSafelyAsync(handle.Ref);
        }

        return false;
    }

    private static async ValueTask<bool> FocusSafelyAsync(ElementReference element)
    {
        try
        {
            await element.FocusAsync();
            return true;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException
            or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // No JavaScript, or the element left the DOM: focus is a progressive enhancement here.
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Keyboard on the card
    // ══════════════════════════════════════════════════════════════════════

    private async Task HandleCardKeyDownAsync(TItem item, KeyboardEventArgs args)
    {
        // Enter fires on keydown like a native button, and a held key must not re-open the card.
        if (args.Key == "Enter" && !args.Repeat)
        {
            await OnCardClick.InvokeAsync(item);
            return;
        }

        // The move menu answers M and Alt+ArrowDown — the chord the hidden instructions advertise
        // and the same pair TmDataTable uses for its column menu. A Ctrl/⌘ chord belongs to the
        // browser (⌘+M minimizes the window on macOS, Ctrl+M is a platform shortcut), never here.
        if (IsMoveMenuEnabled && !args.CtrlKey && !args.MetaKey
            && (args.Key is "m" or "M" || (args.AltKey && args.Key == "ArrowDown")))
        {
            OpenMoveMenu(item);
            return;
        }

        // Any other Alt chord belongs to the browser/OS, not to the board.
        if (args.AltKey) return;

        var items = GetCardCellItems(item);
        if (items.Count == 0) return;

        var index = IndexOfItem(items, item);
        if (index < 0) return;

        var target = args.Key switch
        {
            "ArrowDown" => index + 1,
            "ArrowUp" => index - 1,
            "Home" => 0,
            "End" => items.Count - 1,
            _ => -1,
        };

        if (target == index) return;
        await FocusCardAtAsync(items, target);
    }

    private Task HandleCardKeyUpAsync(TItem item, KeyboardEventArgs args)
        => args.Key == " " ? OnCardClick.InvokeAsync(item) : Task.CompletedTask;

    /// <summary>
    /// The cards of the item's own cell. The arrow keys walk one cell only: leaving it is a Tab
    /// (each cell owns one stop), so a board never becomes a two-dimensional grid trap.
    /// </summary>
    private List<TItem> GetCardCellItems(TItem item)
    {
        if (ColumnSelector is null) return [];
        var column = ColumnSelector(item);
        if (SwimlanesEnabled && SwimlaneSelector is not null)
        {
            var known = KnownLaneIds();
            var value = SwimlaneSelector(item);
            var lane = value is null || !known.Contains(value) ? NoLaneId : value;
            return GetDropCellItems(column, lane);
        }

        return GetDropCellItems(column, null);
    }

    private void OpenMoveMenu(TItem item)
    {
        if (!IsMoveMenuEnabled) return;
        if (_moveMenus.TryGetValue(item, out var handle)) handle.Menu?.Open();
    }

    // ══════════════════════════════════════════════════════════════════════
    // The move itself — the same contract a drop reports
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Moves the card to the END of the chosen cell (the same append a drop on a cell background
    /// reports) and announces it. <c>TargetBeforeItem</c> is always <see langword="default"/>: an
    /// append has no anchor.
    /// </summary>
    private async Task MoveToCellAsync(TItem item, string fromColumn, string? fromLane, string toColumn, string? toLane)
    {
        var targetItems = GetDropCellItems(toColumn, toLane);
        var targetIndex = targetItems.Count;
        var moveEvent = new KanbanMoveEvent<TItem>(
            item, fromColumn, toColumn, targetIndex, default,
            LaneEventValue(fromLane), LaneEventValue(toLane));

        await CommitMoveAsync(moveEvent, toColumn, toLane, targetIndex);
    }

    /// <summary>
    /// Reorders the card by one place inside its own cell. <paramref name="direction"/> is <c>-1</c>
    /// (up) or <c>1</c> (down); the reported index follows the drop contract — the card ends up
    /// immediately before whatever currently occupies <c>TargetIndex</c>.
    /// </summary>
    private async Task NudgeAsync(TItem item, string column, string? lane, int direction)
    {
        var items = GetDropCellItems(column, lane);
        var index = IndexOfItem(items, item);
        if (index < 0 || index + direction < 0 || index + direction >= items.Count) return;

        // Moving down past the next card means inserting before the card AFTER it (the dragged card
        // is still in the list the consumer receives); moving up means inserting before the previous.
        var targetIndex = Math.Min(direction < 0 ? index - 1 : index + 2, items.Count);
        var beforeItem = targetIndex < items.Count ? items[targetIndex] : default;

        var moveEvent = new KanbanMoveEvent<TItem>(
            item, column, column, targetIndex, beforeItem,
            LaneEventValue(lane), LaneEventValue(lane));

        await CommitMoveAsync(moveEvent, column, lane, index + direction);
    }

    private async Task CommitMoveAsync(KanbanMoveEvent<TItem> moveEvent, string toColumn, string? toLane, int focusIndex)
    {
        // The announcement names the card it moved, so the localized "card {n}" fallback uses the
        // position the host was told to put it at (1-based).
        Announce(Loc["TmKanban_CardMoved", CardLabel(moveEvent.Item, focusIndex + 1), TargetLabel(toColumn, toLane)]);
        RequestMoveFocus(moveEvent.Item, toColumn, toLane, focusIndex);

        var sameCell = moveEvent.FromColumn == moveEvent.ToColumn && moveEvent.FromSwimlane == moveEvent.ToSwimlane;
        if (sameCell)
        {
            if (OnItemReordered.HasDelegate) await OnItemReordered.InvokeAsync(moveEvent);
        }
        else if (OnItemMoved.HasDelegate)
        {
            await OnItemMoved.InvokeAsync(moveEvent);
        }

        // Render once more so the refs of a host that applied the move synchronously are the NEW
        // elements before the pending focus runs in OnAfterRenderAsync.
        StateHasChanged();
    }

    private string TargetLabel(string columnId, string? laneId)
    {
        var column = Columns?.FirstOrDefault(candidate => candidate.Id == columnId)?.Title ?? columnId;
        if (!SwimlanesEnabled || laneId is null) return column;

        var lane = GetEffectiveLanes().FirstOrDefault(candidate => candidate.Id == laneId)?.Title
                   ?? Loc["TmKanban_NoSwimlane"];
        return column + " / " + lane;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Focus hand-back
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Queues the focus hand-back for a move that reported <paramref name="focusIndex"/> as the card's
    /// position in the target cell (the position the host is asked to place it at). The reference is
    /// resolved from the POSITION rather than the item value, because a host usually applies a move by
    /// replacing the item (<c>item with { Status = … }</c>) — an identity lookup would keep pointing at
    /// the element that just left the DOM.
    /// </summary>
    /// <param name="item">The item that moved.</param>
    /// <param name="toColumn">The column the host was told to move it to.</param>
    /// <param name="toLane">The lane the host was told to move it to, if swimlanes are enabled.</param>
    /// <param name="focusIndex">The card's expected index inside the target cell.</param>
    private void RequestMoveFocus(TItem item, string toColumn, string? toLane, int focusIndex)
    {
        _pendingFocusItem = item;
        _pendingFocusColumn = toColumn;
        _pendingFocusLane = toLane;
        _pendingFocusIndex = focusIndex;
        _pendingFocusCard.Ref = default;
        _focusHandback = true;
        _focusBatchReady = false;

        // The root's data-focus-target records WHERE the hand-back went — the same state attribute
        // convention as the zone markup (and what an end-to-end test asserts about the real focus).
        _focusTarget = IsColumnCollapsed(toColumn) ? "column:" + toColumn
            : SwimlanesEnabled && toLane is not null && IsLaneCollapsed(toLane) ? "lane:" + toColumn + ":" + toLane
            : "card";
    }

    // Whether the card being rendered is the position the pending hand-back is waiting for.
    private bool IsPendingFocusPosition(string columnId, string? laneId, int index)
        => _focusHandback
           && _pendingFocusColumn == columnId
           && _pendingFocusLane == laneId
           && _pendingFocusIndex == index;

    /// <summary>
    /// Hands the focus back after a move (WCAG 2.4.3): the moved card when it is rendered, otherwise
    /// the zone it landed in — a collapsed column strip, a collapsed lane's slot, or the target cell.
    /// <para>
    /// The focus is applied one render batch LATER than the move. The move menu closes before the
    /// action runs and hands the focus back to its own trigger from ITS <c>OnAfterRenderAsync</c>;
    /// a child's after-render callback can run after its parent's, so focusing in the same batch
    /// could be undone by the menu restoring the trigger — the reader would land on the card it came
    /// FROM even when the card moved to a collapsed zone. Waiting one batch makes our focus the last
    /// one deterministically, and costs a single extra render per move.
    /// </para>
    /// </summary>
    /// <param name="firstRender">Whether this is the first render.</param>
    /// <returns>A task that completes when the pending focus has been applied.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A virtualized cell keeps exactly one tab stop: re-point it at the window's first card when
        // the window does not render the roving one (one extra render, and only in that case).
        if (RefreshVirtualTabStops()) StateHasChanged();

        if (_focusHandback && !_focusBatchReady)
        {
            _focusBatchReady = true;
            StateHasChanged();
        }
        else if (_focusHandback)
        {
            _focusHandback = false;
            var column = _pendingFocusColumn;
            var lane = _pendingFocusLane;
            var item = _pendingFocusItem;
            var card = _pendingFocusCard.Ref;
            _pendingFocusItem = default;
            _pendingFocusColumn = null;
            _pendingFocusLane = null;
            _pendingFocusIndex = -1;
            _pendingFocusCard.Ref = default;

            if (column is not null)
            {
                var columnCollapsed = IsColumnCollapsed(column);
                var laneCollapsed = !columnCollapsed && SwimlanesEnabled && lane is not null && IsLaneCollapsed(lane);

                if (!columnCollapsed && !laneCollapsed)
                {
                    if (card.Id is not null)
                    {
                        _rovingItem = item;
                        await FocusSafelyAsync(card);
                    }
                    else
                    {
                        // A host that has not applied the move yet still renders the card in its OLD
                        // cell: its identity handle is the card the reader was on, so the focus stays
                        // on it.
                        if (item is not null && _cardHandles.TryGetValue(item, out var unchanged) && unchanged.Ref.Id is not null)
                        {
                            _rovingItem = item;
                            await FocusSafelyAsync(unchanged.Ref);
                        }
                    }
                }
                else
                {
                    var zoneLane = laneCollapsed ? lane : null;
                    if (GetZoneHandle(column, zoneLane) is { Ref.Id: not null } zone)
                    {
                        await FocusSafelyAsync(zone.Ref);
                    }
                }
            }
        }

        // The touch drag attaches to an enabled board and detaches from a Disabled one; a re-render
        // with an unchanged state must not re-attach (the module's registry would drop the live
        // session). It runs AFTER the focus hand-back: attachment is idempotent per state and must
        // never delay (nor be skipped by) the focus logic's early batch. Best-effort: a host without
        // JS keeps every other path.
        await UpdateTouchAttachmentAsync();
    }

    // ══════════════════════════════════════════════════════════════════════
    // The live region
    // ══════════════════════════════════════════════════════════════════════

    private void Announce(string message)
    {
        // A repeated sentence would not re-announce: a zero-width space makes the second one a new
        // string without changing what the reader hears.
        if (string.Equals(message, _announcement, StringComparison.Ordinal))
        {
            _announcementMarker = !_announcementMarker;
            _announcement = _announcementMarker ? message + "\u200B" : message;
            return;
        }

        _announcementMarker = false;
        _announcement = message;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Element references
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>The reference box of a drop zone, addressed by (column, lane). The razor markup uses
    /// it as <c>@ref="ZoneHandle(…).Ref"</c>, exactly like <c>TmSortableList</c>'s row handles.</summary>
    private ElementHandle ZoneHandle(string columnId, string? laneId)
    {
        var key = columnId + "\0" + (laneId ?? string.Empty);
        if (!_zoneHandles.TryGetValue(key, out var handle))
        {
            handle = new ElementHandle();
            _zoneHandles[key] = handle;
        }

        return handle;
    }

    private ElementHandle? GetZoneHandle(string columnId, string? laneId)
        => _zoneHandles.TryGetValue(columnId + "\0" + (laneId ?? string.Empty), out var handle) ? handle : null;

    private ElementHandle CardHandle(TItem item)
    {
        if (!_cardHandles.TryGetValue(item, out var handle))
        {
            handle = new ElementHandle();
            _cardHandles[item] = handle;
        }

        return handle;
    }

    private MoveMenuHandle MoveMenuHandleFor(TItem item)
    {
        if (!_moveMenus.TryGetValue(item, out var handle))
        {
            handle = new MoveMenuHandle();
            _moveMenus[item] = handle;
        }

        return handle;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Card markup (shared by the classic, the swimlane and the virtualized list)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Appends one card to an existing render tree: the focusable, clickable, draggable element with
    /// its fenced content and (when enabled) its move menu. The template content and the menu are
    /// fenced from the card's own key handlers, so a control the host puts in a card keeps every key
    /// it owns.
    /// </summary>
    /// <param name="builder">The render tree the card belongs to.</param>
    /// <param name="columnId">The column the card is rendered in.</param>
    /// <param name="laneId">The swimlane the card is rendered in, or <see langword="null"/>.</param>
    /// <param name="item">The card item.</param>
    /// <param name="index">The card's index in its cell.</param>
    /// <param name="rovingIndex">The index of the cell's single tab stop.</param>
    /// <param name="keyed">
    /// Whether to key the element by its index. The classic / swimlane lists key their cards so a
    /// transient drop indicator cannot recreate them mid-drag; <c>&lt;Virtualize&gt;</c> owns the keys
    /// of its own window and must not be given a second one.
    /// </param>
    private void AddCard(
        RenderTreeBuilder builder,
        string columnId,
        string? laneId,
        TItem item,
        int index,
        int rovingIndex,
        bool keyed)
    {
        builder.OpenElement(8, "div");
        if (keyed) builder.SetKey(index);
        builder.AddAttribute(9, "class", IsMoveMenuEnabled ? "tm-kanban__card tm-kanban__card--movable" : "tm-kanban__card");
        builder.AddAttribute(10, "data-testid", TestId($"card-{columnId}-{LaneToken(laneId)}-{index}"));
        // The touch-drag identity the tm-kanban module reads at press time: the card flag makes the
        // card a drag grip, the index addresses it inside its cell, the key is its index in the
        // host's FULL list (stable across a host refresh — the board verifies the source identity
        // at release against this key). Both indexes are stable for the whole gesture: Blazor never
        // re-renders mid-drag.
        builder.AddAttribute(28, "data-tm-kanban-card", "true");
        builder.AddAttribute(29, "data-tm-kanban-index", index);
        builder.AddAttribute(30, "data-tm-kanban-key", KeyIndex(item));
        if (IsMoveMenuEnabled)
        {
            builder.AddAttribute(11, "role", "group");
            builder.AddAttribute(12, "aria-roledescription", Loc["TmKanban_CardRoleDescription"]);
            builder.AddAttribute(13, "aria-describedby", InstructionsId);
            if (CardLabelSelector is not null)
            {
                builder.AddAttribute(14, "aria-label", CardLabel(item, index + 1));
            }

            builder.AddAttribute(15, "tabindex", IsCardTabStop(columnId, laneId, index, rovingIndex, keyed) ? "0" : "-1");
        }

        builder.AddAttribute(16, "draggable", "true");
        builder.AddAttribute(17, "ondragstart", EventCallback.Factory.Create(this, () => HandleDragStart(item, columnId, laneId)));
        builder.AddAttribute(18, "ondragover", EventCallback.Factory.Create<DragEventArgs>(this, _ => HandleCardDragOver(columnId, laneId, index)));
        builder.AddEventPreventDefaultAttribute(19, "ondragover", !Disabled);
        builder.AddEventStopPropagationAttribute(20, "ondragover", true);
        builder.AddAttribute(21, "ondragend", EventCallback.Factory.Create(this, HandleDragEnd));
        builder.AddAttribute(22, "onclick", EventCallback.Factory.Create(this, () => HandleCardClick(item)));
        if (IsMoveMenuEnabled)
        {
            builder.AddAttribute(23, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, args => HandleCardKeyDownAsync(item, args)));
            builder.AddAttribute(24, "onkeyup", EventCallback.Factory.Create<KeyboardEventArgs>(this, args => HandleCardKeyUpAsync(item, args)));
            builder.AddAttribute(25, "onfocus", EventCallback.Factory.Create<FocusEventArgs>(this, _ => HandleCardFocus(item)));
        }

        builder.AddElementReferenceCapture(26, value =>
        {
            CardHandle(item).Ref = value;
            if (IsPendingFocusPosition(columnId, laneId, index))
            {
                _pendingFocusCard.Ref = value;
                _pendingFocusItem = item;
            }
        });
        builder.AddContent(27, CardContent(item, columnId, laneId, index));
        builder.CloseElement();
    }

    private RenderFragment CardContent(TItem item, string columnId, string? laneId, int index) => builder =>
    {
        if (!IsMoveMenuEnabled)
        {
            // Without the non-drag path the card is exactly the pre-2.11 card: the host's template
            // renders straight into it, with no wrapper and no key handlers to fence.
            builder.AddContent(0, CardTemplate?.Invoke(item));
            return;
        }

        // The keydown/keyup barrier is what keeps a link, input or button inside CardTemplate from
        // double-activating on top of the card's own Enter/Space emulation.
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "tm-kanban__card-content");
        builder.AddEventStopPropagationAttribute(2, "onkeydown", true);
        builder.AddEventStopPropagationAttribute(3, "onkeyup", true);
        builder.AddContent(4, CardTemplate?.Invoke(item));
        builder.CloseElement();

        builder.AddContent(5, MoveMenu(item, columnId, laneId, index));
    };

    private RenderFragment MoveMenu(TItem item, string columnId, string? laneId, int index) => builder =>
    {
        var menu = MoveMenuHandleFor(item);

        // The wrapper owns the pointer AND the keys of the menu: a click must never open the card,
        // and the menu's own Enter/Space/arrows/Escape must never reach the card's handler.
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "tm-kanban__card-actions");
        builder.AddEventStopPropagationAttribute(2, "onclick", true);
        builder.AddEventStopPropagationAttribute(3, "onkeydown", true);
        builder.AddEventStopPropagationAttribute(4, "onkeyup", true);

        builder.OpenComponent<TmContextMenu>(5);
        builder.AddAttribute(6, "Class", "tm-kanban__card-move");
        // The card already owns the cell's single tab stop and opens this menu with M /
        // Alt+ArrowDown, so the trigger must not add a SECOND stop to the same cell.
        builder.AddAttribute(7, "TriggerTabIndex", -1);
        // A phone gets the shared bottom sheet instead of a popover anchored to a card corner.
        builder.AddAttribute(8, "MobilePresentation", PanelPresentation.Auto);
        builder.AddAttribute(9, "SheetTitle", Loc["TmKanban_MoveCard", CardLabel(item, index + 1)]);
        builder.AddAttribute(10, "Trigger", (RenderFragment)(trigger =>
        {
            trigger.OpenElement(0, "span");
            trigger.AddAttribute(1, "class", "tm-kanban__card-move-trigger");
            trigger.AddAttribute(2, "data-testid", TestId($"card-move-{columnId}-{LaneToken(laneId)}-{index}"));
            trigger.OpenComponent<TmIcon>(3);
            trigger.AddAttribute(4, "Name", IconNames.MoreVertical);
            trigger.AddAttribute(5, "Size", IconSize.Sm);
            trigger.CloseComponent();
            trigger.OpenElement(6, "span");
            trigger.AddAttribute(7, "class", "tm-sr-only");
            trigger.AddContent(8, Loc["TmKanban_MoveCard", CardLabel(item, index + 1)]);
            trigger.CloseElement();
            trigger.CloseElement();
        }));
        builder.AddAttribute(11, "ChildContent", MoveMenuItems(item, columnId, laneId, index));
        builder.AddComponentReferenceCapture(12, component => menu.Menu = (TmContextMenu)component);
        builder.CloseComponent();

        builder.CloseElement();
    };

    private RenderFragment MoveMenuItems(TItem item, string columnId, string? laneId, int index) => builder =>
    {
        var sequence = 0;
        var items = GetDropCellItems(columnId, laneId);

        // The cell's own order first: the same OnItemReordered a drop inside the cell reports.
        builder.OpenComponent<TmContextMenuItem>(sequence++);
        builder.AddAttribute(sequence++, "Label", Loc["TmKanban_MoveUp"]);
        builder.AddAttribute(sequence++, "Icon", IconNames.ArrowUp);
        builder.AddAttribute(sequence++, "Disabled", index <= 0);
        builder.AddAttribute(sequence++, "data-testid", TestId("move-up"));
        builder.AddAttribute(sequence++, "OnClick", EventCallback.Factory.Create(this, () => NudgeAsync(item, columnId, laneId, -1)));
        builder.CloseComponent();

        builder.OpenComponent<TmContextMenuItem>(sequence++);
        builder.AddAttribute(sequence++, "Label", Loc["TmKanban_MoveDown"]);
        builder.AddAttribute(sequence++, "Icon", IconNames.ArrowDown);
        builder.AddAttribute(sequence++, "Disabled", index >= items.Count - 1);
        builder.AddAttribute(sequence++, "data-testid", TestId("move-down"));
        builder.AddAttribute(sequence++, "OnClick", EventCallback.Factory.Create(this, () => NudgeAsync(item, columnId, laneId, 1)));
        builder.CloseComponent();

        builder.OpenComponent<TmContextMenuItem>(sequence++);
        builder.AddAttribute(sequence++, "IsDivider", true);
        builder.CloseComponent();

        foreach (var target in MoveTargets())
        {
            var toColumn = target.ColumnId;
            var toLane = target.LaneId;

            // The cell the card already sits in is not a destination: it is marked as the current one
            // (disabled so it can never be activated, aria-current so assistive technology can tell it
            // apart from the real targets) instead of silently re-appending the card to itself.
            var isCurrent = toColumn == columnId && toLane == laneId;
            var label = isCurrent ? target.Label + " " + Loc["TmKanban_CurrentTarget"] : target.Label;

            builder.OpenComponent<TmContextMenuItem>(sequence++);
            builder.AddAttribute(sequence++, "Label", label);
            builder.AddAttribute(sequence++, "data-testid",
                TestId(toLane is null ? $"move-to-{toColumn}" : $"move-to-{toColumn}-{LaneToken(toLane)}"));
            if (isCurrent)
            {
                builder.AddAttribute(sequence++, "Disabled", true);
                builder.AddAttribute(sequence++, "aria-current", "true");
            }
            else
            {
                builder.AddAttribute(sequence++, "OnClick",
                    EventCallback.Factory.Create(this, () => MoveToCellAsync(item, columnId, laneId, toColumn, toLane)));
            }

            builder.CloseComponent();
        }
    };

    /// <summary>
    /// Every place a card can be moved to: one target per column, or one per column × lane when the
    /// board groups cards into swimlanes. Collapsed columns and lanes are NOT filtered out — a
    /// collapsed zone is a legal destination, which is the whole point of the collapse feature.
    /// </summary>
    private IReadOnlyList<(string ColumnId, string? LaneId, string Label)> MoveTargets()
    {
        var targets = new List<(string ColumnId, string? LaneId, string Label)>();
        if (Columns is null) return targets;

        foreach (var column in Columns)
        {
            if (!SwimlanesEnabled)
            {
                targets.Add((column.Id, null, column.Title));
                continue;
            }

            foreach (var lane in GetEffectiveLanes())
            {
                targets.Add((column.Id, lane.Id, column.Title + " / " + lane.Title));
            }
        }

        return targets;
    }
}
