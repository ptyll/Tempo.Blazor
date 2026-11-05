using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// A generic list whose rows the reader can reorder — by pointer (through the shared
/// <c>pointer-drag.js</c> helper), by keyboard, or from a per-row move menu that is the tap
/// alternative required by WCAG 2.5.7.
/// <para>
/// The component NEVER mutates <see cref="Items"/>. Every committed move raises
/// <see cref="OnReorder"/> with the indexes of the host's own list; the host applies the move and the
/// next render shows the new order. While a keyboard move is in progress the rows are painted in the
/// pending order, so the reader sees the row travel before committing it; cancelling (Escape) or
/// releasing without a change paints the host's order again.
/// </para>
/// <para>
/// Accessibility: the list is a <c>role="list"</c> of <c>role="listitem"</c> rows, each of which
/// names itself a localized "sortable item" and points at the hidden keyboard instructions. A row is
/// the focusable grip in both handle modes (one tab stop per row, never two): Enter/Space grabs the
/// row, the up/down arrows move it, Enter/Space drops it, Escape cancels and Tab cancels and leaves the
/// list (a grabbed row is never a keyboard trap); Alt+arrow moves it one position without grabbing. One
/// polite live region narrates every step of a move.
/// </para>
/// </summary>
/// <typeparam name="TItem">The row model the host supplies.</typeparam>
public partial class TmSortableList<TItem> : TmComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The rows, in display order. The component renders exactly this order plus any pending keyboard
    /// move; it never writes back to the list, so <see cref="OnReorder"/> is the only way a reorder
    /// becomes permanent. A null list renders the empty state.
    /// </summary>
    [Parameter] public IReadOnlyList<TItem>? Items { get; set; }

    /// <summary>
    /// The content of one row, rendered inside the row's labelled content element. Null renders
    /// <c>ToString()</c> of the item. Focusable content of its own is allowed: the content element
    /// fences the row's emulating key handlers off, so a link, input or button in the template keeps its
    /// own clicks and keys, and in <see cref="SortableHandleMode.Handle"/> the pointer drag starts on the
    /// handle.
    /// </summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>
    /// Rendered instead of the localized default ("No items to reorder.") when <see cref="Items"/> is
    /// null or empty. The hidden instructions and the live region stay rendered either way, so the
    /// list never loses its explanation.
    /// </summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>
    /// The stable identity of one row, used both for Blazor's <c>@key</c> (so a reordered row keeps
    /// its DOM node and any child component instance instead of being re-created in place) and for
    /// the row's <c>data-sortable-key</c> attribute. Null falls back to the row's index — stable only
    /// while the list order is stable, so a host that reorders should always supply this.
    /// <para>
    /// Keys must be UNIQUE across <see cref="Items"/>: Blazor's keyed diff throws on a duplicate
    /// sibling key. When a selector does return a duplicate, every row falls back to its index, so a
    /// mistaken selector degrades to an unkeyed list instead of failing the render.
    /// </para>
    /// </summary>
    [Parameter] public Func<TItem, object>? KeySelector { get; set; }

    /// <summary>
    /// The word the live region and the move menu use to name a row ("Approve the plan moved to
    /// position 2 of 5"). Null falls back to the item itself when <typeparamref name="TItem"/> is a
    /// <see cref="string"/>, then to the localized "Item {n}" with the row's 1-based position in the
    /// host's list. <see cref="KeySelector"/> is deliberately NOT a fallback: a database key read aloud
    /// ("3f2a… picked up") names nothing a person can recognise, and the menu trigger would carry it too.
    /// </summary>
    [Parameter] public Func<TItem, string>? ItemLabelSelector { get; set; }

    /// <summary>
    /// Raised once per committed move with the indexes of the host's <see cref="Items"/> list. Not
    /// raised when nothing actually moved (a grab that returned to its start, a boundary Alt+arrow,
    /// a disabled list). The component does not reorder the list itself.
    /// </summary>
    [Parameter] public EventCallback<SortableReorderArgs> OnReorder { get; set; }

    /// <summary>
    /// Disables every reorder path at once: the rows are no longer focusable, the pointer drag is not
    /// attached, no move menu is rendered and nothing is announced. The list still renders its rows,
    /// so a read-only view keeps its content and its accessible structure.
    /// </summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Which part of a row starts a pointer drag. Default
    /// <see cref="SortableHandleMode.Handle"/>: only the decorative grip starts the drag, so the rest
    /// of the row stays interactive. <see cref="SortableHandleMode.WholeItem"/> makes the row itself
    /// the drag surface and requires a 400 ms touch long-press, so a tap and a page scroll still work.
    /// </summary>
    [Parameter] public SortableHandleMode HandleMode { get; set; } = SortableHandleMode.Handle;

    /// <summary>
    /// Renders the per-row move menu (Move up / Move down / Move to top / Move to bottom) that is the
    /// tap and mouse alternative to dragging. Default <c>true</c>; set <c>false</c> only when the rows
    /// already offer their own reorder commands — a list without the menu and without a pointer has
    /// no way to reorder at all.
    /// </summary>
    [Parameter] public bool ShowMoveMenu { get; set; } = true;

    /// <summary>
    /// The accessible name of the list. Null uses the localized default ("Sortable list").
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private ElementReference _listRef;
    private SortableListInterop? _interop;
    private DotNetObjectReference<TmSortableList<TItem>>? _dotnetRef;
    // A bare unique value on purpose: the markup-class sweep counts quoted, component-prefixed stems.
    private readonly string _registrationId = Guid.NewGuid().ToString("N");
    // One element reference per ROW KEY, never per position: a host that reorders its own list keeps
    // both the DOM node and the reference, so a focus hand-back still names the row the caller meant.
    private readonly Dictionary<string, RowHandle> _rowHandles = [];
    private string? _attachedSignature;
    private string _announcement = string.Empty;
    private bool _announcementMarker;
    private IReadOnlyList<SortableEntry> _entries = [];
    private List<int>? _order;
    private int? _grabbedSourceIndex;
    private int _itemCount;

    private bool IsWholeItem => HandleMode == SortableHandleMode.WholeItem;

    private bool IsGrabbed => _grabbedSourceIndex is not null;

    private bool IsEmpty => _itemCount == 0;

    private string? EffectiveLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Loc["TmSortableList_Label"] : AriaLabel;

    private string RootClass
    {
        get
        {
            var classes = new System.Text.StringBuilder("tm-sortable-list");
            if (Disabled) classes.Append(" tm-sortable-list--disabled");
            if (IsWholeItem) classes.Append(" tm-sortable-list--whole-item");
            if (!string.IsNullOrEmpty(Class)) classes.Append(' ').Append(Class);
            return classes.ToString();
        }
    }

    /// <summary>The rows in the order the list currently shows (the pending order while grabbed).</summary>
    internal IReadOnlyList<SortableEntry> Entries => _entries;

    /// <summary>The source index of the grabbed row, or null when nothing is grabbed.</summary>
    internal int? GrabbedIndex => _grabbedSourceIndex;

    /// <summary>One row as the pending order presents it.</summary>
    /// <param name="SourceIndex">The index of the row in the host's list.</param>
    /// <param name="Item">The host's row model.</param>
    /// <param name="Key">The <c>@key</c> value.</param>
    /// <param name="KeyText">The textual form of <paramref name="Key"/>.</param>
    /// <param name="Label">The name the live region and the menu use.</param>
    /// <param name="Position">The zero-based position the row is currently painted at.</param>
    /// <param name="Count">The number of rows.</param>
    internal sealed record SortableEntry(
        int SourceIndex,
        TItem Item,
        object Key,
        string KeyText,
        string Label,
        int Position,
        int Count)
    {
        /// <summary>Whether this row is the first one.</summary>
        public bool IsFirst => Position <= 0;

        /// <summary>Whether this row is the last one.</summary>
        public bool IsLast => Position >= Count - 1;
    }

    /// <summary>
    /// The captured element of one row. The capture target is this box, resolved per row KEY, so the
    /// reference survives a host reorder (the DOM node is moved, not re-created) and a re-render that
    /// does not re-run the capture cannot bind the reference to the wrong row.
    /// </summary>
    private sealed class RowHandle
    {
        internal ElementReference Ref;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _itemCount = Items?.Count ?? 0;
        // A host that replaces or shrinks the list abandons any pending move: painting an order that
        // addresses rows which no longer exist would throw at render time.
        if (_order is not null && (_order.Count != _itemCount || _order.Any(index => index >= _itemCount)))
        {
            ResetGrab();
        }

        if (_grabbedSourceIndex is { } grabbed && grabbed >= _itemCount)
        {
            ResetGrab();
        }

        // A host that turns the list read-only mid-move must not strand the pending order on screen: the
        // rows would keep the order of a grab no key can drop any more.
        if (Disabled && _grabbedSourceIndex is not null)
        {
            ResetGrab();
        }

        Rebuild();
        PruneHandles();
    }

    // ---------- focus (the host's hand-back after a row it was on disappears) ----------

    /// <summary>
    /// Moves keyboard focus to the row at <paramref name="index"/> of <see cref="Items"/> — the same
    /// index <see cref="OnReorder"/> reports, not a painted position. A host uses it to keep a
    /// keyboard or screen-reader reader in place when the control they were on is removed with its
    /// row (WCAG 2.4.3): the row that took the removed one's place receives the focus instead of the
    /// document body.
    /// <para>
    /// Returns whether a focus call was made. <see langword="false"/> means there was nothing to focus
    /// — the index is outside the list, the list is <see cref="Disabled"/> (a read-only row is not a
    /// tab stop, so moving the focus there would strand it), or no element was captured for that row
    /// yet (before the first render).
    /// </para>
    /// </summary>
    /// <param name="index">The row's index in <see cref="Items"/>.</param>
    /// <returns>A task that completes with whether the row was focused.</returns>
    public ValueTask<bool> FocusItemAsync(int index)
    {
        if (Disabled)
        {
            return ValueTask.FromResult(false);
        }

        var key = KeyAt(index);
        if (key is null || !_rowHandles.TryGetValue(key, out var handle) || handle.Ref.Id is null)
        {
            return ValueTask.FromResult(false);
        }

        return FocusRowAsync(handle.Ref);
    }

    /// <summary>
    /// The reference box of one row, created on first use and kept for as long as the row is in the
    /// list. The capture writes into the box, so a re-render that reuses the DOM node cannot bind the
    /// element to another row.
    /// </summary>
    /// <param name="entry">The row being rendered.</param>
    /// <returns>The row's reference box.</returns>
    private RowHandle HandleFor(SortableEntry entry)
    {
        if (!_rowHandles.TryGetValue(entry.KeyText, out var handle))
        {
            handle = new RowHandle();
            _rowHandles[entry.KeyText] = handle;
        }

        return handle;
    }

    /// <summary>Drops the reference boxes of rows the host removed, so a long-lived list cannot grow.</summary>
    private void PruneHandles()
    {
        if (_rowHandles.Count == 0)
        {
            return;
        }

        var live = new HashSet<string>(_entries.Select(entry => entry.KeyText), StringComparer.Ordinal);
        List<string>? stale = null;
        foreach (var key in _rowHandles.Keys)
        {
            if (!live.Contains(key))
            {
                (stale ??= []).Add(key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var key in stale)
        {
            _rowHandles.Remove(key);
        }
    }

    /// <summary>The <c>@key</c> text of the row the host addresses by its <see cref="Items"/> index.</summary>
    /// <param name="index">The row's index in <see cref="Items"/>.</param>
    /// <returns>The row's key text, or null when no rendered row has that index.</returns>
    private string? KeyAt(int index)
    {
        foreach (var entry in _entries)
        {
            if (entry.SourceIndex == index)
            {
                return entry.KeyText;
            }
        }

        return null;
    }

    private static async ValueTask<bool> FocusRowAsync(ElementReference row)
    {
        try
        {
            await row.FocusAsync();
            return true;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException
            or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // No JavaScript, or the row is no longer in the DOM: the reader can still Tab to it, so a
            // failed hand-back must never surface as an unhandled exception.
            return false;
        }
    }

    // ---------- keyboard (the non-drag alternative, WCAG 2.5.7) ----------

    private async Task OnItemKeyDownAsync(SortableEntry entry, KeyboardEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        // Alt+arrow is the one-step shortcut: it commits immediately and never enters the grab mode.
        if (args.AltKey && !args.CtrlKey && !args.MetaKey && args.Key is "ArrowUp" or "ArrowDown")
        {
            if (!IsGrabbed)
            {
                await MoveDirectAsync(entry, args.Key == "ArrowUp" ? -1 : 1);
            }

            return;
        }

        if (!IsGrabbed)
        {
            // Enter on a non-native focusable is emulated on keydown (and its auto-repeat guarded),
            // so a held key cannot re-toggle the grab on every repeat event.
            if (args.Key == "Enter" && !args.Repeat)
            {
                Grab(entry.SourceIndex);
            }

            return;
        }

        switch (args.Key)
        {
            case "Enter" when !args.Repeat:
                await DropAsync();
                break;
            case "ArrowUp":
                MovePending(-1);
                break;
            case "ArrowDown":
                MovePending(1);
                break;
            case "Home":
                MovePendingTo(0);
                break;
            case "End":
                MovePendingTo(int.MaxValue);
                break;
            case "Escape":
                CancelGrab();
                break;
            // Tab is never swallowed: it cancels the pending move (so nothing is left half-applied) and
            // then moves the focus out of the row like any other Tab. Holding the grab past a Tab would
            // be the keyboard trap WCAG 2.1.2 forbids.
            case "Tab":
                CancelGrab();
                break;
        }
    }

    private async Task OnItemKeyUpAsync(SortableEntry entry, KeyboardEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        // Space is emulated on KEYUP, exactly where a native button fires its click. The residual page
        // scroll of the Space keydown that GRABS a row is accepted for the same reason the shared
        // convention accepts it: the module's page-scroll guard only covers a row that is already
        // grabbed (so it can exclude Tab at event time), not the key that starts the grab.
        if (args.Key is " " or "Spacebar")
        {
            if (!IsGrabbed)
            {
                Grab(entry.SourceIndex);
            }
            else
            {
                await DropAsync();
            }
        }
    }

    private void Grab(int sourceIndex)
    {
        _grabbedSourceIndex = sourceIndex;
        _order = Enumerable.Range(0, _itemCount).ToList();
        Rebuild();
        Announce(Loc["TmSortableList_PickedUp", Label(sourceIndex), (PositionOf(sourceIndex) + 1).ToString(CultureInfo.CurrentCulture), CountText]);
    }

    private void MovePending(int delta)
    {
        if (_grabbedSourceIndex is not { } grabbed || _order is null)
        {
            return;
        }

        var position = _order.IndexOf(grabbed);
        var target = Math.Clamp(position + delta, 0, _order.Count - 1);
        if (target == position)
        {
            AnnounceBoundary(grabbed, delta);
            return;
        }

        _order.RemoveAt(position);
        _order.Insert(target, grabbed);
        Rebuild();
        Announce(Loc["TmSortableList_Moved", Label(grabbed), (target + 1).ToString(CultureInfo.CurrentCulture), CountText]);
    }

    private void MovePendingTo(int target)
    {
        if (_grabbedSourceIndex is not { } grabbed || _order is null)
        {
            return;
        }

        var position = _order.IndexOf(grabbed);
        var bounded = Math.Clamp(target, 0, _order.Count - 1);
        if (bounded == position)
        {
            return;
        }

        _order.RemoveAt(position);
        _order.Insert(bounded, grabbed);
        Rebuild();
        Announce(Loc["TmSortableList_Moved", Label(grabbed), (bounded + 1).ToString(CultureInfo.CurrentCulture), CountText]);
    }

    private async Task DropAsync()
    {
        if (_grabbedSourceIndex is not { } grabbed)
        {
            return;
        }

        var to = PositionOf(grabbed);
        var label = Label(grabbed);
        ResetGrab();
        Rebuild();
        Announce(Loc["TmSortableList_Dropped", label, (to + 1).ToString(CultureInfo.CurrentCulture), CountText]);

        if (to != grabbed)
        {
            await OnReorder.InvokeAsync(new SortableReorderArgs(grabbed, to, SortableReorderSource.Keyboard));
        }
    }

    private void CancelGrab()
    {
        if (_grabbedSourceIndex is not { } grabbed)
        {
            return;
        }

        var label = Label(grabbed);
        ResetGrab();
        Rebuild();
        Announce(Loc["TmSortableList_Cancelled", label, (grabbed + 1).ToString(CultureInfo.CurrentCulture), CountText]);
    }

    private async Task MoveDirectAsync(SortableEntry entry, int delta)
    {
        var current = PositionOf(entry.SourceIndex);
        var target = current + delta;
        if (target < 0 || target >= _itemCount)
        {
            AnnounceBoundary(entry.SourceIndex, delta);
            return;
        }

        await CommitAsync(entry.SourceIndex, target, SortableReorderSource.Keyboard, entry.Label);
    }

    // ---------- the move menu (tap alternative) ----------

    /// <summary>The four moves the per-row menu offers. A named command beats a bare <c>int</c>.</summary>
    private enum MoveCommand
    {
        Up,
        Down,
        Top,
        Bottom,
    }

    private async Task MoveFromMenuAsync(SortableEntry entry, MoveCommand command)
    {
        var current = PositionOf(entry.SourceIndex);
        var target = command switch
        {
            MoveCommand.Up => current - 1,
            MoveCommand.Down => current + 1,
            MoveCommand.Top => 0,
            _ => _itemCount - 1,
        };

        if (target == current || target < 0 || target >= _itemCount)
        {
            return;
        }

        await CommitAsync(entry.SourceIndex, target, SortableReorderSource.Menu, entry.Label);
    }

    private async Task CommitAsync(int from, int to, SortableReorderSource source, string label)
    {
        ResetGrab();
        Rebuild();
        Announce(Loc["TmSortableList_Dropped", label, (to + 1).ToString(CultureInfo.CurrentCulture), CountText]);

        if (from != to)
        {
            await OnReorder.InvokeAsync(new SortableReorderArgs(from, to, source));
        }
    }

    // ---------- the pointer commit (the sortable module, over the shared pointer-drag helper) ----------

    /// <summary>
    /// The single commit path of the pointer drag: the module reports the indexes ONCE on release and
    /// never re-renders while the pointer moves. Called from JS; not meant to be called by hosts.
    /// </summary>
    /// <param name="fromIndex">The index the row started at.</param>
    /// <param name="toIndex">The index the row was dropped at.</param>
    [JSInvokable]
    public async Task OnSortableReorderCommitted(int fromIndex, int toIndex)
    {
        if (Disabled || _itemCount == 0)
        {
            return;
        }

        if (fromIndex < 0 || fromIndex >= _itemCount || toIndex < 0 || toIndex >= _itemCount || fromIndex == toIndex)
        {
            return;
        }

        await CommitAsync(fromIndex, toIndex, SortableReorderSource.Pointer, Label(fromIndex));

        // A JS invocation is not a Blazor event: nothing marks this component for a render, so a host that
        // ignores OnReorder would never see the announcement (nor the pending-order reset).
        await InvokeAsync(StateHasChanged);
    }

    // ---------- rendering helpers ----------

    private void Rebuild()
    {
        var items = Items;
        if (items is null || items.Count == 0)
        {
            _entries = [];
            _itemCount = 0;
            return;
        }

        _itemCount = items.Count;
        var order = _order ?? Enumerable.Range(0, items.Count).ToList();
        var keys = ResolveKeys(items);
        var entries = new List<SortableEntry>(order.Count);
        for (var position = 0; position < order.Count; position++)
        {
            var sourceIndex = order[position];
            var item = items[sourceIndex];
            var key = keys is not null ? keys[sourceIndex] ?? sourceIndex : sourceIndex;
            entries.Add(new SortableEntry(
                sourceIndex,
                item,
                key,
                key.ToString() ?? sourceIndex.ToString(CultureInfo.InvariantCulture),
                Label(sourceIndex),
                position,
                order.Count));
        }

        _entries = entries;
    }

    /// <summary>
    /// The <c>@key</c> value of every row, resolved once per render, or null when the rows fall back to
    /// their index. A selector that repeats a key would make Blazor's keyed diff throw with no hint at
    /// the cause, so a duplicate (from ANY row) drops the whole list to index keys — a still-correct
    /// list that merely loses instance reuse, never a failed render.
    /// </summary>
    private object?[]? ResolveKeys(IReadOnlyList<TItem> items)
    {
        if (KeySelector is null)
        {
            return null;
        }

        var keys = new object?[items.Count];
        var seen = new HashSet<object?>();
        for (var index = 0; index < items.Count; index++)
        {
            keys[index] = KeySelector(items[index]);
            if (!seen.Add(keys[index]))
            {
                return null;
            }
        }

        return keys;
    }

    private int PositionOf(int sourceIndex)
        => _order?.IndexOf(sourceIndex) is { } position && position >= 0 ? position : sourceIndex;

    private string Label(int sourceIndex)
    {
        if (Items is not null && sourceIndex >= 0 && sourceIndex < Items.Count)
        {
            if (ItemLabelSelector?.Invoke(Items[sourceIndex]) is { Length: > 0 } explicitLabel)
            {
                return explicitLabel;
            }

            // A string IS its own name. Anything else falls through to the localized position label:
            // ToString() of an entity is a type name at best and a database key at worst, and reading a
            // GUID aloud ("3f2a… picked up") names nothing a person can recognise.
            if (Items[sourceIndex] is string { Length: > 0 } text)
            {
                return text;
            }
        }

        return Loc["TmSortableList_ItemLabel", (sourceIndex + 1).ToString(CultureInfo.CurrentCulture)];
    }

    private void AnnounceBoundary(int sourceIndex, int delta)
        => Announce(delta < 0
            ? Loc["TmSortableList_AtFirst", Label(sourceIndex)]
            : Loc["TmSortableList_AtLast", Label(sourceIndex)]);

    /// <summary>
    /// Replaces the live region's text. Kept sticky (never cleared on a re-render, which could wipe an
    /// announcement before assistive technology read it); when two consecutive transitions would write
    /// the same sentence, one invisible marker character is toggled so the region still sees a
    /// mutation instead of staying silent.
    /// </summary>
    private void Announce(string message)
    {
        if (string.Equals(message, _announcement, StringComparison.Ordinal))
        {
            _announcementMarker = !_announcementMarker;
            _announcement = _announcementMarker ? message + "\u200B" : message;
            return;
        }

        _announcementMarker = false;
        _announcement = message;
    }

    private void ResetGrab()
    {
        _order = null;
        _grabbedSourceIndex = null;
    }

    private string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The row count as the current culture writes it — the live region's "of {n}".</summary>
    private string CountText => _itemCount.ToString(CultureInfo.CurrentCulture);

    private string ItemClass(SortableEntry entry)
    {
        var classes = new System.Text.StringBuilder("tm-sortable-list__item");
        if (GrabbedIndex == entry.SourceIndex) classes.Append(" tm-sortable-list__item--grabbed");
        return classes.ToString();
    }

    // ---------- JS lifecycle ----------

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Attach when the list can be dragged, detach when it cannot (Disabled) — a re-render with an
        // unchanged signature must not re-attach (the module's registry would drop the live session).
        string? signature = Disabled || IsEmpty ? null : IsWholeItem ? "whole-item" : "handle";
        if (signature == _attachedSignature)
        {
            return;
        }

        _attachedSignature = signature;

        if (signature is null)
        {
            if (_interop is not null)
            {
                await _interop.DetachAsync(_registrationId);
            }

            return;
        }

        _dotnetRef ??= DotNetObjectReference.Create(this);
        _interop ??= new SortableListInterop(JSRuntime);
        await _interop.AttachAsync(_registrationId, _listRef, _dotnetRef, signature);
    }

    /// <summary>Releases the JS registration and the .NET reference of this list.</summary>
    public async ValueTask DisposeAsync()
    {
        _attachedSignature = null;
        if (_interop is not null)
        {
            await _interop.DisposeAsync();
            _interop = null;
        }

        _dotnetRef?.Dispose();
        _dotnetRef = null;
    }
}
