using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.DataTable;

public partial class TmDataTable<TItem>
{
    private int _activeRow;
    private int _activeCol;
    private bool _gridCellSeeded;
    private bool _gridFocusPending;
    private bool _gridKeysAttached;

    /// <summary>
    /// Columns that actually render as cells. Card layout omits <see cref="DataTableCardRole.Hidden"/>.
    /// Rebuilt when the column set or the layout changes; every cell loop reads this list.
    /// </summary>
    private List<TmDataTableColumn<TItem>> _renderedCellColumns = [];

    /// <summary>
    /// Display index of each rendered item. Virtualize's context is the item, and
    /// <see cref="List{T}.IndexOf(T)"/> both scans the whole list and returns the wrong index
    /// for a value-equal duplicate. Rebuilt whenever <see cref="_displayedItems"/> is replaced.
    /// </summary>
    private readonly Dictionary<object, int> _displayedItemIndex = new(ReferenceEqualityComparer.Instance);

    private void RebuildRenderedCellColumns() =>
        _renderedCellColumns = _visibleColumns
            .Where(c => !IsMobileCards || c.CardRole != DataTableCardRole.Hidden)
            .ToList();

    /// <summary>
    /// Everything the row loop used to recompute per cell. A 1000-row table called
    /// <c>IsMobilePinned</c> and <c>GetCellClass</c> — each of them walking the visible columns —
    /// once per cell per render. The answers depend only on the column set, the pins and the
    /// layout, so they are rebuilt with <see cref="RebuildRenderedCellColumns"/>. The header class
    /// is NOT cached: it carries the sort state, which changes without a column-set rebuild.
    /// </summary>
    private void RebuildRowLoopCaches()
    {
        _mobilePinnedKeys.Clear();
        _cellClassByKey.Clear();
        _hasFilterRow = false;
        _lastFilterableColumnKey = null;

        foreach (var col in _visibleColumns)
        {
            if (IsMobilePinned(col))
                _mobilePinnedKeys.Add(col.Key);
            if (col.Filterable)
            {
                _hasFilterRow = true;
                _lastFilterableColumnKey = col.Key;
            }
        }

        foreach (var col in _renderedCellColumns)
            _cellClassByKey[col.Key] = BuildCellClass(col);
    }

    private readonly HashSet<string> _mobilePinnedKeys = [];
    private readonly Dictionary<string, string> _cellClassByKey = [];
    private bool _hasFilterRow;
    private string? _lastFilterableColumnKey;

    /// <summary>Whether the column carries the mobile pin marker. Reads the cache the row loop needs.</summary>
    private bool IsColumnMobilePinned(TmDataTableColumn<TItem> col) => _mobilePinnedKeys.Contains(col.Key);

    private bool IsGrid => KeyboardNavigation == DataTableKeyboardNavigation.Grid;

    /// <summary>Expander + selection checkbox + rendered data columns + the edit-action column when it renders.</summary>
    private int GridColumnCount =>
        _renderedCellColumns.Count + (Selectable ? 1 : 0) + (HasDetail ? 1 : 0) + (HasEditActions ? 1 : 0);

    /// <summary>The first data column, skipping the expander and the selection checkbox.</summary>
    private int GridDefaultColumn => (HasDetail ? 1 : 0) + (Selectable ? 1 : 0);

    private bool GridHeaderIsActive => IsGrid && _displayedItems.Count == 0;

    /// <summary>Header row is aria-rowindex 1; a rendered filter row takes 2 and shifts every body row.</summary>
    private int GridBodyRowOffset => 1 + (HasGridFilterRow ? 1 : 0);

    private bool HasGridFilterRow => _visibleColumns.Any(c => c.Filterable);

    private string GridCellKey(int row, int col) => $"{row}:{col}";

    private string CellTabIndex(int row, int col) =>
        IsGrid && !GridHeaderIsActive && row == _activeRow && col == _activeCol ? "0" : IsGrid ? "-1" : "0";

    private string HeaderTabIndex(int col) =>
        GridHeaderIsActive && col == _activeCol ? "0" : GridHeaderIsActive ? "-1" : "0";

    private string RowTabIndex() => IsGrid ? "-1" : "0";

    /// <summary>
    /// Rows the grid announces: the total plus every header row (the column header, and the filter
    /// row when one renders). Body rows start their <c>aria-rowindex</c> at <see cref="GridBodyRowOffset"/>,
    /// so the last index would otherwise sit past <c>aria-rowcount</c>. Grouping counts only the
    /// rows that are actually rendered. An unknown server total is announced as -1.
    /// </summary>
    private int GridAriaRowCount
    {
        get
        {
            if (DataProvider is not null && _totalCount < 0)
                return -1;
            var total = _groupByColumns.Count > 0
                ? CountGroupRows(_groupedData)
                : Math.Max(_totalCount, _displayedItems.Count);
            return total + GridBodyRowOffset;
        }
    }

    /// <summary>
    /// Visible group rows: the header always counts, and sub-groups or items only while the group
    /// is expanded. Collapsed content is not in the DOM, so counting it would let the cursor land
    /// on a row that has no cell.
    /// </summary>
    private int CountGroupRows(IReadOnlyList<DataGroup<TItem>>? groups)
    {
        if (groups is null) return 0;
        var count = 0;
        foreach (var group in groups)
        {
            count++;
            if (!IsGroupExpanded(group))
                continue;
            count += group.SubGroups.Count > 0 ? CountGroupRows(group.SubGroups) : group.Items.Count;
        }
        return count;
    }

    /// <summary>True when the rendered group row at <paramref name="row"/> is a group header.</summary>
    private bool IsGroupHeaderRow(int row) =>
        _groupByColumns.Count > 0 && TryGetGroupRow(row)?.IsHeader == true;

    private GroupRowLocation? TryGetGroupRow(int row)
    {
        if (_groupedData is null) return null;
        var cursor = 0;
        return FindGroupRow(_groupedData, row, ref cursor);
    }

    private GroupRowLocation? FindGroupRow(IReadOnlyList<DataGroup<TItem>> groups, int row, ref int cursor)
    {
        foreach (var group in groups)
        {
            if (cursor == row)
                return new GroupRowLocation(group, IsHeader: true);
            cursor++;
            if (!IsGroupExpanded(group))
                continue;
            if (group.SubGroups.Count > 0)
            {
                var nested = FindGroupRow(group.SubGroups, row, ref cursor);
                if (nested is not null)
                    return nested;
            }
            else
            {
                foreach (var _ in group.Items)
                {
                    if (cursor == row)
                        return new GroupRowLocation(group, IsHeader: false);
                    cursor++;
                }
            }
        }

        return null;
    }

    private readonly record struct GroupRowLocation(DataGroup<TItem> Group, bool IsHeader);

    /// <summary>
    /// Keeps the single roving stop inside the rendered grid after a page, filter or grouping change.
    /// </summary>
    private void ClampGridCursor()
    {
        if (!IsGrid) return;

        var columns = Math.Max(0, GridColumnCount - 1);
        // Columns register during the first render, after the initial data refresh. Seeding before
        // that clamps the cursor onto the checkbox (the last column that exists yet) and never moves.
        if (!_gridCellSeeded && _displayedItems.Count > 0 && _columns.Count > 0)
        {
            _activeCol = Math.Min(GridDefaultColumn, columns);
            _gridCellSeeded = true;
        }

        _activeCol = Math.Clamp(_activeCol, 0, columns);
        var rowCount = _groupByColumns.Count > 0 ? CountGroupRows(_groupedData) : _displayedItems.Count;
        var rows = Math.Max(0, rowCount - 1);
        _activeRow = Math.Clamp(_activeRow, 0, rows);
    }

    private Task HandleGridKeyDownAsync(KeyboardEventArgs e, int row, int col, TItem? item)
    {
        // An open editor owns the keys: arrows move inside the input, Escape returns to the cell.
        if (!IsGrid || _editContext is not null)
            return Task.CompletedTask;

        if (e.Key is "ArrowRight" or "ArrowLeft" or "ArrowDown" or "ArrowUp"
            or "PageDown" or "PageUp" or "Home" or "End")
        {
            MoveGridCursor(e, row, col);
            return Task.CompletedTask;
        }

        return ActivateGridCellAsync(e, col, item);
    }

    private Task HandleGridHeaderKeyDownAsync(KeyboardEventArgs e, int col)
    {
        if (!GridHeaderIsActive)
            return Task.CompletedTask;

        if (e.Key is "ArrowRight" or "ArrowLeft" or "Home" or "End")
        {
            MoveGridCursor(e, 0, col);
            return Task.CompletedTask;
        }

        // M opens the active data column's menu without moving the cursor. Utility columns
        // (expander, checkbox) have no menu, so M there is a no-op.
        if (ShowColumnMenu && ColumnMenuMode == ColumnMenuMode.Menu
            && (e.Key is "m" or "M" || (e.AltKey && e.Key == "ArrowDown")))
        {
            var dataColumn = GridHeaderColumn(col);
            if (dataColumn is not null)
                return ToggleColumnMenuAsync(dataColumn);
        }

        return Task.CompletedTask;
    }

    /// <summary>The data column behind a grid header index, or null for a utility column.</summary>
    private TmDataTableColumn<TItem>? GridHeaderColumn(int col)
    {
        var index = col - GridDefaultColumn;
        return index >= 0 && index < _renderedCellColumns.Count ? _renderedCellColumns[index] : null;
    }

    private void MoveGridCursor(KeyboardEventArgs e, int row, int col)
    {
        var lastCol = Math.Max(0, GridColumnCount - 1);
        var rowCount = _groupByColumns.Count > 0 ? CountGroupRows(_groupedData) : _displayedItems.Count;
        var lastRow = Math.Max(0, rowCount - 1);
        switch (e.Key)
        {
            case "ArrowRight":
                _activeCol = Math.Min(lastCol, col + 1);
                _activeRow = row;
                break;
            case "ArrowLeft":
                _activeCol = Math.Max(0, col - 1);
                _activeRow = row;
                break;
            case "ArrowDown":
                _activeRow = Math.Min(lastRow, row + 1);
                _activeCol = GridColumnForRow(_activeRow, col);
                break;
            case "ArrowUp":
                _activeRow = Math.Max(0, row - 1);
                _activeCol = GridColumnForRow(_activeRow, col);
                break;
            case "PageDown":
                _activeRow = Math.Min(lastRow, row + 10);
                _activeCol = GridColumnForRow(_activeRow, col);
                break;
            case "PageUp":
                _activeRow = Math.Max(0, row - 10);
                _activeCol = GridColumnForRow(_activeRow, col);
                break;
            case "Home":
                _activeCol = 0;
                _activeRow = e.CtrlKey ? 0 : row;
                break;
            case "End":
                _activeCol = lastCol;
                _activeRow = e.CtrlKey ? lastRow : row;
                break;
            default:
                return;
        }

        _gridCellSeeded = true;
        _gridFocusPending = true;
        if (ScrollMode == DataTableScrollMode.Virtualized)
            _ = ScrollVirtualRowIntoViewAsync(_activeRow);
        StateHasChanged();
    }

    /// <summary>
    /// A group header spans every column and only renders <c>data-grid-cell="r:0"</c>. Keeping the
    /// previous column would make <c>focusGridCell</c> look for a cell that does not exist.
    /// </summary>
    private int GridColumnForRow(int row, int col) => IsGroupHeaderRow(row) ? 0 : col;

    /// <summary>
    /// Replaces the displayed page and rebuilds the reference-equality index virtualized rows read.
    /// Call this instead of assigning <see cref="_displayedItems"/> directly.
    /// </summary>
    private void SetDisplayedItems(List<TItem> items)
    {
        _displayedItems = items;
        RebuildDisplayedItemIndex();
    }

    private void RebuildDisplayedItemIndex()
    {
        _displayedItemIndex.Clear();
        for (var i = 0; i < _displayedItems.Count; i++)
        {
            if (_displayedItems[i] is { } item)
                _displayedItemIndex.TryAdd(item, i);
        }
    }

    private int DisplayedIndexOf(TItem item) =>
        item is not null && _displayedItemIndex.TryGetValue(item, out var index) ? index : -1;

    /// <summary>
    /// A virtualized row past the rendered window is not in the DOM, so focus has nothing to land
    /// on. Scroll the estimate first; the focus call after the next render finds the cell.
    /// </summary>
    private async Task ScrollVirtualRowIntoViewAsync(int row)
    {
        try
        {
            await JS.InvokeVoidAsync("tmDataTable.scrollGridToIndex", _tableRef, row, VirtualItemSize);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or InvalidOperationException or TaskCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Enter activates the row once. Space toggles selection and never activates. On the expander
    /// or checkbox column the key is handed to that control instead.
    /// </summary>
    private Task ActivateGridCellAsync(KeyboardEventArgs e, int col, TItem? item)
    {
        if (item is null || e.Repeat)
            return Task.CompletedTask;

        var expanderCol = HasDetail ? 0 : -1;
        var checkCol = HasDetail ? 1 : 0;
        if (HasDetail && col == expanderCol && e.Key is "Enter" or " ")
            return ToggleRowDetailAsync(item);
        if (Selectable && col == checkCol && e.Key is "Enter" or " ")
            return ToggleRowSelectionAsync(item, new ChangeEventArgs { Value = !IsSelected(item) });

        return e.Key switch
        {
            "Enter" when Editable && AnyEditableColumn && IsEditableColumn(col) => StartEditAtAsync(RowIndexOf(item), item),
            "Enter" => HandleRowClickAsync(item),
            " " when Selectable => ToggleRowSelectionAsync(item, new ChangeEventArgs { Value = !IsSelected(item) }),
            "F2" when Editable && AnyEditableColumn => StartEditAtAsync(RowIndexOf(item), item),
            _ => Task.CompletedTask
        };
    }

    private bool IsEditableColumn(int col)
    {
        var index = col - GridDefaultColumn;
        return index >= 0 && index < _renderedCellColumns.Count && _renderedCellColumns[index].Editable;
    }

    private int RowIndexOf(TItem item)
    {
        var index = _displayedItems.FindIndex(x => EqualityComparer<TItem>.Default.Equals(x, item));
        return index >= 0 ? index : _activeRow;
    }

    /// <summary>
    /// Attaches the key listener that cancels the browser's scroll default for grid navigation keys.
    /// Blazor cannot make <c>preventDefault</c> depend on which key was pressed, so the decision has
    /// to live in JavaScript (<c>tmDataTable.attachGridKeys</c>).
    /// </summary>
    private async Task SyncGridKeysAsync()
    {
        if (IsGrid == _gridKeysAttached)
            return;
        try
        {
            if (IsGrid)
                await JS.InvokeVoidAsync("tmDataTable.attachGridKeys", _tableRef);
            else
                await JS.InvokeVoidAsync("tmDataTable.detachGridKeys", _tableRef);
            _gridKeysAttached = IsGrid;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or InvalidOperationException or TaskCanceledException or ObjectDisposedException)
        {
            // JS unavailable (tests, prerender). Navigation still works; only the scroll default remains.
        }
    }

    private async Task DetachGridKeysAsync()
    {
        _gridKeysAttached = false;
        try
        {
            await JS.InvokeVoidAsync("tmDataTable.detachGridKeys", _tableRef);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or InvalidOperationException or TaskCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task FocusActiveGridCellAsync()
    {
        if (!_gridFocusPending || !IsGrid)
            return;
        _gridFocusPending = false;
        try
        {
            await JS.InvokeVoidAsync("tmDataTable.focusGridCell", _tableRef, _activeRow, _activeCol);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or InvalidOperationException or TaskCanceledException or ObjectDisposedException)
        {
            // JS unavailable (tests, prerender). The tabindex move is the contract; focus is best-effort.
        }
    }
}
