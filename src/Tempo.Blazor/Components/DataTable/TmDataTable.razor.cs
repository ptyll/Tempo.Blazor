using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Components.DataTable;

/// <summary>
/// A fully-featured data table component with support for sorting, filtering, pagination,
/// selection, grouping, and view management. Supports both client-side and server-side data.
/// </summary>
public partial class TmDataTable<TItem> : IDisposable, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TmColumnPicker? _columnPicker;

    // ── Column menu (ColumnMenuMode.Menu) ─────────────────────────
    // One kebab per data header. The open menu is remembered by column key so exactly one menu is
    // open, and the trigger refs are stable across renders (a fresh ElementReference per render
    // would drop the overlay's anchor out from under an open menu).

    private string? _openColumnMenuKey;
    private string? _columnMenuFocusKey;
    private string? _columnMenuReturnFocusKey;
    // Per-instance prefix so two tables with the same column keys never share menu or filter ids.
    private readonly string _idBase = $"tm-dt-{Guid.NewGuid():N}";
    private readonly Dictionary<string, ColumnMenuState> _columnMenus = new(StringComparer.Ordinal);

    /// <summary>
    /// Per-column menu state. The trigger reference lives in a box the markup binds with
    /// <c>@ref</c>, so each header keeps its own anchor across renders.
    /// </summary>
    private sealed class ColumnMenuState
    {
        public TriggerRef Trigger { get; } = new();
        public bool IsOpen { get; set; }
    }

    /// <summary>A box around an <see cref="ElementReference"/>, so markup can bind <c>@ref</c> to it.</summary>
    private sealed class TriggerRef
    {
        public ElementReference Current { get; set; }
    }

    private ColumnMenuState ColumnMenuOf(string columnKey)
    {
        if (!_columnMenus.TryGetValue(columnKey, out var state))
        {
            state = new ColumnMenuState();
            _columnMenus[columnKey] = state;
        }

        state.IsOpen = IsColumnMenuOpen(columnKey);
        return state;
    }

    /// <summary>
    /// The name a column's menu announces: <c>Title</c> wins, then <c>SortLabel</c>, then the key.
    /// A header may legitimately carry only a <c>HeaderTemplate</c>, and the key always names it.
    /// </summary>
    private string ColumnAccessibleName(TmDataTableColumn<TItem> col)
    {
        if (!string.IsNullOrWhiteSpace(col.Title))
            return col.Title;
        if (!string.IsNullOrWhiteSpace(col.SortLabel))
            return col.SortLabel!;
        return col.Key;
    }

    private bool IsColumnMenuOpen(string columnKey)
        => string.Equals(_openColumnMenuKey, columnKey, StringComparison.Ordinal);

    /// <summary>
    /// Turns a column key into a fragment that is legal in an HTML id. Keys may contain spaces
    /// and other characters; anything outside <c>[A-Za-z0-9_-]</c> becomes an underscore.
    /// </summary>
    private static string ColumnIdFragment(string columnKey)
    {
        if (string.IsNullOrEmpty(columnKey))
            return "_";

        var chars = columnKey.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var legal = (c is >= 'A' and <= 'Z') || (c is >= 'a' and <= 'z')
                || (c is >= '0' and <= '9') || c is '_' or '-';
            if (!legal)
                chars[i] = '_';
        }

        return new string(chars);
    }

    private string ColumnMenuId(string columnKey) => $"{_idBase}-col-menu-{ColumnIdFragment(columnKey)}";

    private string ColumnFilterId(string columnKey) => $"{_idBase}-col-filter-{ColumnIdFragment(columnKey)}";

    /// <summary>
    /// The cards sheet's filter inputs must not share ids with the header filter row: the thead is
    /// only <c>display:none</c> while the sheet is open, so both inputs are in the DOM at once and
    /// a duplicate id is invalid HTML (and could point a label at the hidden header input).
    /// </summary>
    private string CardsFilterId(string columnKey) => $"{_idBase}-cards-filter-{ColumnIdFragment(columnKey)}";

    private Task ToggleColumnMenuAsync(TmDataTableColumn<TItem> col)
    {
        _openColumnMenuKey = IsColumnMenuOpen(col.Key) ? null : col.Key;
        return InvokeAsync(StateHasChanged);
    }

    private Task SetColumnMenuOpenAsync(TmDataTableColumn<TItem> col, bool open)
    {
        if (open)
            _openColumnMenuKey = col.Key;
        else if (IsColumnMenuOpen(col.Key))
        {
            _openColumnMenuKey = null;
            // Overlay.js consumes Escape before this handler's keydown runs, so the return-focus
            // key has to be stored on the close the overlay actually reports. Filter already named
            // its own target; an outside click is ignored by focusHeader when focus went elsewhere.
            if (_columnMenuFocusKey is null)
                _columnMenuReturnFocusKey ??= col.Key;
        }
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The actions a column's menu offers, in display order. Each item exists only when the column
    /// can actually perform it, so a non-sortable column never announces a sort it cannot do.
    /// </summary>
    private IEnumerable<string> ColumnMenuActions(TmDataTableColumn<TItem> col)
    {
        if (col.Sortable)
        {
            yield return "sort-asc";
            yield return "sort-desc";
            if (ColumnSortDirection(col) is not null)
                yield return "clear-sort";
            // The sort group is its own section; the separator only earns its place when something follows it.
            if (ColumnMenuHasActionsAfterSort(col))
                yield return "separator";
        }

        if (ShowColumnMenu)
        {
            if (GetPin(col) == ColumnPin.None)
            {
                yield return "pin-left";
                yield return "pin-right";
            }
            else
            {
                yield return "unpin";
            }
        }

        // Grouping is a table feature, not just a column one: the item exists only while the table
        // shows grouping at all (the same gate the "Add grouping" control uses).
        if (ShowGrouping && col.Groupable && !_groupByColumns.Contains(col.Key))
            yield return "group";
        // Hiding is the column picker's job, so the item exists only while the picker does — and
        // never for the last visible column, which would leave the table with nothing to show.
        if (ShouldRenderColumnPicker() && col.Hideable && _visibleColumns.Count(c => c.Hideable) > 1)
            yield return "hide";
        if (col.Filterable)
            yield return "filter";
    }

    /// <summary>The direction this column is currently sorted in, or null when it is not part of the sort.</summary>
    private DataTableSortDirection? ColumnSortDirection(TmDataTableColumn<TItem> col)
    {
        var descriptor = _sortDescriptors
            .FirstOrDefault(d => string.Equals(d.Column, col.Key, StringComparison.Ordinal));
        return descriptor is null
            ? null
            : descriptor.Direction == DataTableSortDirection.Descending
                ? DataTableSortDirection.Descending
                : DataTableSortDirection.Ascending;
    }

    /// <summary>Whether the menu has anything to show below the sort group, so the separator is not trailing.</summary>
    private bool ColumnMenuHasActionsAfterSort(TmDataTableColumn<TItem> col) =>
        ShowColumnMenu
        || (ShowGrouping && col.Groupable && !_groupByColumns.Contains(col.Key))
        || (ShouldRenderColumnPicker() && col.Hideable && _visibleColumns.Count(c => c.Hideable) > 1)
        || col.Filterable;

    /// <summary>The menu action that states the direction the column is already sorted in, so it can be checked.</summary>
    private string? ColumnMenuCheckedAction(TmDataTableColumn<TItem> col) => ColumnSortDirection(col) switch
    {
        DataTableSortDirection.Ascending => "sort-asc",
        DataTableSortDirection.Descending => "sort-desc",
        _ => null
    };

    private static string ColumnMenuActionKey(string action) => action switch
    {
        "sort-asc" => "TmDataTable_ColumnMenu_SortAsc",
        "sort-desc" => "TmDataTable_ColumnMenu_SortDesc",
        "clear-sort" => "TmDataTable_ColumnMenu_ClearSort",
        "pin-left" => "TmDataTable_PinLeft",
        "pin-right" => "TmDataTable_PinRight",
        "unpin" => "TmDataTable_ColumnMenu_Unpin",
        "group" => "TmDataTable_ColumnMenu_GroupBy",
        "hide" => "TmDataTable_ColumnMenu_Hide",
        "filter" => "TmDataTable_ColumnMenu_Filter",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown column menu action.")
    };

    private async Task RunColumnMenuActionAsync(TmDataTableColumn<TItem> col, string action)
    {
        _openColumnMenuKey = null;
        // Filter moves focus into the filter row. Every other action closes the menu, so focus
        // must return to this column's header — otherwise it drops to <body> (WCAG 2.4.3).
        if (action != "filter")
            _columnMenuReturnFocusKey = col.Key;
        switch (action)
        {
            case "sort-asc":
                await SortColumnAsync(col, DataTableSortDirection.Ascending);
                break;
            case "sort-desc":
                await SortColumnAsync(col, DataTableSortDirection.Descending);
                break;
            case "clear-sort":
                await ClearColumnSortAsync(col);
                break;
            case "pin-left":
                await SetColumnPinAsync(col.Key, ColumnPin.Left);
                break;
            case "pin-right":
                await SetColumnPinAsync(col.Key, ColumnPin.Right);
                break;
            case "unpin":
                await SetColumnPinAsync(col.Key, ColumnPin.None);
                break;
            case "group":
                AddGroupColumn(col.Key);
                break;
            case "hide":
                await OnToggleColumnAsync(col.Key);
                break;
            case "filter":
                // The choice between the header filter and the cards sheet cannot be made from the
                // layout flags alone: a Cards table in a WIDE container still shows the header and
                // its filter row, and only the narrow container hides them. The action names the
                // header filter unconditionally; OnAfterRenderAsync asks the browser whether that
                // input is actually visible and falls back to the cards sheet when it is not.
                _columnMenuFocusKey = col.Key;
                break;
        }
    }

    private async Task FocusColumnMenuAsync()
    {
        if (JS is null || _openColumnMenuKey is null)
            return;
        try
        {
            await JS.InvokeVoidAsync("tmDataTable.focusColumnMenu", ColumnMenuId(_openColumnMenuKey));
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or TaskCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // No JS (tests, prerender): the menu stays in the native Tab order.
        }
    }

    /// <summary>
    /// Escape inside an open column menu closes it. The key is answered here, on the menu, so it
    /// never reaches the header's own handler.
    /// </summary>
    private Task OnColumnMenuKeyDownAsync(TmDataTableColumn<TItem> col, KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _openColumnMenuKey = null;
            _columnMenuReturnFocusKey = col.Key;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the column's sort to exactly <paramref name="direction"/> (the menu states a direction;
    /// it does not walk the header's asc → desc → none cycle) and re-queries.
    /// </summary>
    private async Task SortColumnAsync(TmDataTableColumn<TItem> col, DataTableSortDirection direction)
    {
        if (!col.Sortable)
            return;

        _sortDescriptors.Clear();
        _sortDescriptors.Add(new SortDescriptor(col.Key, direction));
        _currentPage = 1;
        _groupPageRequests.Clear();
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    // ── Cards sort & filter ───────────────────────────────────────
    // The card header is display:none below 640px, so sort and filter need a home a finger can
    // reach. Both live in the toolbar (never in ContentOnly) and the container query reveals them.
    // Sort is a dropdown of column × direction; filter is a sheet of labelled fields.

    private bool _cardsFilterOpen;
    private string? _cardsFilterFocusKey;
    private ElementReference _cardsFilterTrigger;
    private TmOverlayPanel? _cardsSheet;

    /// <summary>The cards sort control exists for Cards alone, and never in a content-only toolbar.</summary>
    private bool ShouldRenderMobileSort() =>
        MobileLayout == DataTableMobileLayout.Cards && !IsContentToolbarMode && CardsSortableColumns.Any();

    /// <summary>The cards filter control exists for Cards alone, and only while a column can be filtered.</summary>
    private bool ShouldRenderMobileFilters() =>
        MobileLayout == DataTableMobileLayout.Cards && !IsContentToolbarMode && CardsFilterableColumns.Any();

    private IEnumerable<TmDataTableColumn<TItem>> CardsSortableColumns =>
        _visibleColumns.Where(c => c.Sortable);

    private IEnumerable<TmDataTableColumn<TItem>> CardsFilterableColumns =>
        _visibleColumns.Where(c => c.Filterable);

    /// <summary>How many columns currently carry a filter, so the button can say "Filters (N)".</summary>
    private int ActiveCardFilterCount =>
        CardsFilterableColumns.Count(col => _activeFilters.ContainsKey(col.Key));

    private Task SetCardsFilterOpenAsync(bool open)
    {
        _cardsFilterOpen = open && CardsFilterableColumns.Any();
        return InvokeAsync(StateHasChanged);
    }

    private Task OpenCardsFilterAsync() => SetCardsFilterOpenAsync(true);

    /// <summary>
    /// A sort choice from the cards dropdown. <c>clear</c> drops every descriptor; anything else is
    /// <c>{columnKey}|asc</c> or <c>{columnKey}|desc</c>.
    /// </summary>
    private Task OnCardSortSelectedAsync(string value)
    {
        if (value == "clear")
            return ClearColumnSortAsync();

        var separator = value.IndexOf('|');
        if (separator <= 0)
            return Task.CompletedTask;

        var key = value[..separator];
        var descending = value[(separator + 1)..] == "desc";
        var col = CardsSortableColumns.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
        return col is null
            ? Task.CompletedTask
            : SortColumnAsync(col, descending ? DataTableSortDirection.Descending : DataTableSortDirection.Ascending);
    }

    /// <summary>True when this column-and-direction pair is the active sort, so the dropdown can check it.</summary>
    private bool IsCardSortChecked(TmDataTableColumn<TItem> col, bool descending)
    {
        var direction = ColumnSortDirection(col);
        return descending
            ? direction == DataTableSortDirection.Descending
            : direction == DataTableSortDirection.Ascending;
    }

    /// <summary>Removes every sort descriptor and re-queries, so the table returns to its unsorted order.</summary>
    private Task ClearColumnSortAsync() => ClearColumnSortAsync(null);

    private async Task ClearColumnSortAsync(TmDataTableColumn<TItem>? col)
    {
        if (col is not null && !_sortDescriptors.Any(d => string.Equals(d.Column, col.Key, StringComparison.Ordinal)))
            return;

        _sortDescriptors.Clear();
        _currentPage = 1;
        _groupPageRequests.Clear();
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    // ── Column layout state (pin + width; runtime overrides of column defaults) ──
    private readonly Dictionary<string, int> _columnWidths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ColumnPin> _columnPins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stickyLeft = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stickyRight = new(StringComparer.Ordinal);
    private bool _layoutLoaded;
    private DotNetObjectReference<TmDataTable<TItem>>? _dotNetRef;
    private const int DefaultPinnedWidth = 160;
    private const int MinColumnWidth = 60;

    /// <summary>Persistence hook for per-user column layout (widths + pin state). Optional.</summary>
    [Parameter] public IDataTableLayoutStore? LayoutStore { get; set; }

    /// <summary>Fires whenever the column layout (widths or pin state) changes.</summary>
    [Parameter] public EventCallback<DataTableLayout> LayoutChanged { get; set; }

    /// <summary>
    /// Whether the inline column-header controls are shown: the pin toggle and the resize handle.
    /// <c>false</c> hides both, and also hides the kebab when <see cref="ColumnMenuMode"/> is
    /// <see cref="ColumnMenuMode.Menu"/>. Default true.
    /// </summary>
    [Parameter] public bool ShowColumnMenu { get; set; } = true;

    /// <summary>
    /// Column-menu affordance. <see cref="ColumnMenuMode.None"/> (the default) keeps the inline pin
    /// toggle in each header. <see cref="ColumnMenuMode.Menu"/> renders a kebab in each data column
    /// header instead — sort, clear sort, pin, group, hide and filter, each only when the column
    /// supports it — and removes the pin button. The menu is header content, so
    /// <see cref="DataToolbarMode.ContentOnly"/> hides the toolbar but keeps the kebab. The toolbar
    /// itself is unchanged either way.
    /// </summary>
    [Parameter] public ColumnMenuMode ColumnMenuMode { get; set; } = ColumnMenuMode.None;

    /// <summary>Optional group header content; null preserves the current group label.</summary>
    [Parameter] public RenderFragment<DataGroup<TItem>>? GroupHeaderTemplate { get; set; }

    /// <summary>Edit action presentation. Defaults to Icons.</summary>
    [Parameter] public EditActionsMode EditActionsMode { get; set; } = EditActionsMode.Icons;

    /// <summary>
    /// When true, and the table is <see cref="Selectable"/>, the export menu offers a scope choice:
    /// every matching row, or only the checked rows. The selected-rows choice is disabled while
    /// nothing is checked. Default <c>false</c> keeps the format-only menu.
    /// </summary>
    [Parameter] public bool AllowExportSelected { get; set; }

    /// <summary>Scope the next menu export uses. The menu resets it to <see cref="DataTableExportScope.All"/> after each export.</summary>
    private DataTableExportScope _exportScope = DataTableExportScope.All;

    /// <summary>
    /// Per-instance id of the empty-selection explanation, so two tables on one page do not share an id
    /// and <c>aria-describedby</c> cannot point at the other table's text.
    /// </summary>
    private readonly string _exportScopeEmptyId = "tm-data-table-export-scope-empty-" + Guid.NewGuid().ToString("N");

    /// <summary>True when the export menu offers the all-rows / selected-rows choice.</summary>
    private bool ShowsExportScope => AllowExportSelected && Selectable;

    private string ExportScopeItemClass(DataTableExportScope scope)
        => _exportScope == scope
            ? "tm-data-table__export-item tm-data-table__export-scope tm-data-table__export-scope--selected"
            : "tm-data-table__export-item tm-data-table__export-scope";

    /// <summary>
    /// Rows an all-rows export would include, after filtering and before paging.
    /// The client path sets <c>_totalCount</c> from the filtered list; a provider reports it on the result.
    /// </summary>
    private int ExportAllCount => _totalCount;

    private string ExportSelectedLabel()
        => Tempo.Blazor.Localization.TmLocalizerPluralExtensions
            .Plural(Loc, "TmDataTable_ExportScopeSelectedCount", _selectedItems.Count);

    private Dictionary<string, object> ExportSelectedAttributes()
    {
        var attributes = new Dictionary<string, object> { ["data-export-scope"] = "selected" };
        if (_selectedItems.Count == 0)
        {
            attributes["aria-describedby"] = _exportScopeEmptyId;
        }

        return attributes;
    }

    private void ResetExportScopeIfSelectionEmpty()
    {
        if (_selectedItems.Count == 0)
        {
            _exportScope = DataTableExportScope.All;
        }
    }

    /// <summary>Include active view name in view toggle. Defaults false.</summary>
    [Parameter] public bool ShowActiveViewName { get; set; }

    /// <summary>Show per-view Set as default actions. Defaults false.</summary>
    [Parameter] public bool AllowSetDefault { get; set; }

    /// <summary>Raised when a view is requested as default.</summary>
    [Parameter] public EventCallback<DataTableView> OnSetDefault { get; set; }

    // ── Inline edit state ─────────────────────────────────────────
    private TItem? _editingItem;
    private EditContext? _editContext;
    private bool _editHasErrors;

    /// <summary>Enables inline row editing (double-click a row). Requires editable columns with EditTemplate.</summary>
    [Parameter] public bool Editable { get; set; }

    /// <summary>Whether to show the row edit action buttons. Defaults to <c>true</c>.</summary>
    [Parameter] public bool ShowEditButtons { get; set; } = true;

    /// <summary>Fires when a row enters inline edit mode.</summary>
    [Parameter] public EventCallback<TItem> OnRowEditStart { get; set; }

    /// <summary>Fires after a valid row edit is saved.</summary>
    [Parameter] public EventCallback<TItem> OnRowSave { get; set; }

    /// <summary>
    /// Fires when a row edit is cancelled. The callback receives the edited item; restoring its
    /// original values is the consuming application's responsibility.
    /// </summary>
    [Parameter] public EventCallback<TItem> OnRowEditCancel { get; set; }

    /// <summary>
    /// Optional factory that creates the <see cref="EditContext"/> cascaded to a row's edit
    /// templates. When omitted, the table creates a context for the edited item.
    /// </summary>
    [Parameter] public Func<TItem, EditContext>? RowValidatorFactory { get; set; }

    /// <summary>
    /// Invoked to persist a committed row edit. Return false to keep the row in edit mode
    /// (for example when a server rejected the change). When null, the edit commits locally.
    /// </summary>
    [Parameter] public Func<TItem, Task<bool>>? OnRowCommit { get; set; }

    /// <summary>
    /// Legacy cancellation callback invoked after <see cref="OnRowEditCancel"/>. It receives the
    /// already-mutated item; restoring original values remains the consuming application's responsibility.
    /// </summary>
    [Parameter] public EventCallback<TItem> OnRowEditCancelled { get; set; }

    /// <summary>
    /// Optional validator content rendered inside the row's cascading <see cref="EditContext"/> while
    /// editing (for example <c>&lt;FluentValidationValidator /&gt;</c> or <c>&lt;DataAnnotationsValidator /&gt;</c>).
    /// Commit is blocked while the edit context is invalid.
    /// </summary>
    [Parameter] public RenderFragment? RowEditValidator { get; set; }

    private int _editingRowIndex = -1;
    private bool _editorFocusPending;
    private bool AnyEditableColumn => _columns.Any(c => c.Editable && c.EditTemplate is not null);
    private bool HasEditActions => Editable
        && ShowEditButtons
        && AnyEditableColumn
        && ScrollMode != DataTableScrollMode.Virtualized
        && _groupByColumns.Count == 0;

    // Editing is tracked by row index (not item value) so value-equal duplicate rows do not all
    // enter edit mode together, and value-type TItem does not falsely match default(TItem).
    private bool IsEditingRow(int rowIndex) => _editingRowIndex == rowIndex && _editContext is not null;

    private void SetEditingRow(int rowIndex, TItem item)
    {
        _editingRowIndex = rowIndex;
        _editingItem = item;
        _editContext = RowValidatorFactory?.Invoke(item) ?? new EditContext(item!);
        _editHasErrors = false;
    }

    private async Task StartEditAtAsync(int rowIndex, TItem item)
    {
        // Defence in depth for Grid mode: the virtualized and grouped branches render no editing
        // row, so an edit started there would lock the keyboard (GridKeyboard owns the same gate
        // on the Enter/F2 arms). Rows mode keeps its double-click/click paths everywhere.
        if (!Editable || !AnyEditableColumn || (IsGrid && !GridCanEdit)) return;
        if (_editContext is not null && _editingRowIndex != rowIndex)
        {
            await CancelEditAsync();
        }

        SetEditingRow(rowIndex, item);
        // The editing row renders no grid cells, so the cell that held focus is gone. Grid mode hands
        // focus to the editor, or Escape/Enter (answered on the editing row) never reach the table.
        // The cursor follows the edited row too, so focus returns to it after commit or cancel
        // instead of the stale cell the roving stop was on before.
        if (IsGrid)
        {
            _activeRow = Math.Max(0, rowIndex);
            _gridCellSeeded = true;
        }
        _editorFocusPending = IsGrid;
        StateHasChanged();
        await OnRowEditStart.InvokeAsync(item);
    }

    /// <summary>Begins inline editing of a row (host-triggered equivalent of a double-click).</summary>
    public void BeginRowEdit(TItem item)
    {
        var index = _displayedItems.FindIndex(x => EqualityComparer<TItem>.Default.Equals(x, item));
        if (index >= 0)
        {
            _ = InvokeAsync(() => StartEditAtAsync(index, item));
        }
    }

    private async Task CommitEditAsync()
    {
        if (_editingItem is null || _editContext is null) return;

        if (!_editContext.Validate())
        {
            _editHasErrors = true;
            StateHasChanged();
            return;
        }

        var item = _editingItem;
        var committed = OnRowCommit is null || await OnRowCommit(item);
        if (committed)
        {
            await OnRowSave.InvokeAsync(item);
            _editingItem = default;
            _editContext = null;
            _editingRowIndex = -1;
            _editHasErrors = false;
            if (IsGrid)
                _gridFocusPending = true;
        }

        StateHasChanged();
    }

    private async Task CancelEditAsync()
    {
        var item = _editingItem;
        _editingItem = default;
        _editContext = null;
        _editingRowIndex = -1;
        _editHasErrors = false;
        StateHasChanged();
        if (item is not null)
        {
            await OnRowEditCancel.InvokeAsync(item);
            await OnRowEditCancelled.InvokeAsync(item);
        }
    }

    private async Task HandleEditKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "Escape":
                await CancelEditAsync();
                if (IsGrid)
                    _gridFocusPending = true;
                break;
            case "Enter":
                await CommitEditAsync();
                break;
        }
    }

    // ── Master-detail expandable rows ─────────────────────────────
    private readonly HashSet<TItem> _expandedRows = new();
    private readonly HashSet<TItem> _detailLoaded = new();

    /// <summary>Template rendered as an expandable detail row beneath a data row.</summary>
    [Parameter] public RenderFragment<TItem>? DetailTemplate { get; set; }

    /// <summary>
    /// Invoked once, the first time a row is expanded, so the host can lazily load detail data
    /// before <see cref="DetailTemplate"/> renders it.
    /// </summary>
    [Parameter] public Func<TItem, Task>? OnLoadDetail { get; set; }

    private bool HasDetail => DetailTemplate is not null;
    private bool IsRowExpanded(TItem item) => _expandedRows.Contains(item);

    /// <summary>Toggles a row's expandable detail, lazily invoking <see cref="OnLoadDetail"/> on first expand.</summary>
    public async Task ToggleRowDetailAsync(TItem item)
    {
        if (!_expandedRows.Add(item))
        {
            _expandedRows.Remove(item);
            StateHasChanged();
            return;
        }

        if (_detailLoaded.Add(item) && OnLoadDetail is not null)
        {
            await OnLoadDetail(item);
        }

        StateHasChanged();
    }

    // ── Export ────────────────────────────────────────────────────

    private bool _isExporting;
    private IDataTableXlsxExporter? XlsxExporter =>
        ServiceProvider.GetService(typeof(IDataTableXlsxExporter)) as IDataTableXlsxExporter;

    /// <summary>Shows the export format dropdown in the table toolbar. Defaults to <c>false</c>.</summary>
    [Parameter] public bool ShowExport { get; set; }

    /// <summary>Delimiter used by the built-in CSV export. Defaults to a comma.</summary>
    [Parameter] public string CsvDelimiter { get; set; } = ",";

    /// <summary>Fires after an export has been generated and handed to the browser download API.</summary>
    [Parameter] public EventCallback<DataTableExportResult> OnExportCompleted { get; set; }

    private async Task<IReadOnlyList<TItem>> GatherAllRowsAsync()
    {
        if (DataProvider is not null)
        {
            var query = new DataTableQuery
            {
                Page = 1,
                PageSize = int.MaxValue,
                SortColumn = _sortColumn,
                SortDescending = _sortDescending,
                SortDescriptors = _sortDescriptors.ToList(),
                Filters = _activeFilters.Values.ToList(),
                SearchText = _searchText
            };
            var result = await DataProvider.GetDataAsync(query);
            return result.Items;
        }

        var items = (Items ?? []).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.Trim();
            items = items.Where(item =>
                _columns.Any(col => col.Field?.Invoke(item)?.ToString()
                    ?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }

        foreach (var filter in _activeFilters.Values)
        {
            var col = _columns.FirstOrDefault(c => c.Key == filter.Column);
            if (col?.Field is not null)
            {
                items = ApplyClientFilter(items, col.Field, filter);
            }
        }

        return ApplySort(items).ToList();
    }

    /// <summary>
    /// Checked rows for a selected export. Rows still in the current view come first, in that view's
    /// order; rows a later search or filter hid are appended in the order they were checked. A provider
    /// is not re-queried: its fresh instances would miss a reference-equality selection, and a checked
    /// row it no longer returns must still be exported.
    /// </summary>
    private Task<IReadOnlyList<TItem>> GatherSelectedRowsAsync()
    {
        if (DataProvider is not null)
        {
            return Task.FromResult(OrderSelectedByView(_displayedItems));
        }

        // No sort: the current view is Items itself, so a checked row hidden by a filter still has an
        // index there and must not be treated as unmatched. A sort reorders only the rows it can see.
        IReadOnlyList<TItem> view = _sortDescriptors.Count == 0
            ? (Items ?? []).ToList()
            : ApplySort(_selectedOrder).ToList();
        return Task.FromResult(OrderSelectedByView(view));
    }

    /// <summary>
    /// Checked rows found in <paramref name="view"/>, in that view's order, then the rest in the order
    /// they were checked. Matching uses <see cref="EqualityComparer{T}"/> so a provider instance that
    /// equals a checked value still counts.
    /// </summary>
    private IReadOnlyList<TItem> OrderSelectedByView(IReadOnlyList<TItem> view)
    {
        var comparer = EqualityComparer<TItem>.Default;
        var matched = new List<(int Index, TItem Item)>();
        var unmatched = new List<TItem>();
        foreach (var item in _selectedOrder)
        {
            var index = -1;
            for (var i = 0; i < view.Count; i++)
            {
                if (comparer.Equals(view[i], item))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                matched.Add((index, item));
            }
            else
            {
                unmatched.Add(item);
            }
        }

        matched.Sort((left, right) => left.Index.CompareTo(right.Index));
        var rows = new List<TItem>(matched.Count + unmatched.Count);
        rows.AddRange(matched.Select(entry => entry.Item));
        rows.AddRange(unmatched);
        return rows;
    }

    /// <summary>
    /// Materializes the full result set for the current filter/sort (all pages, paging bypassed)
    /// as an export snapshot of the visible columns.
    /// </summary>
    public Task<DataTableExportData> BuildExportDataAsync()
        => BuildExportDataAsync(DataTableExportScope.All);

    /// <summary>
    /// Materializes an export snapshot of the visible columns. <see cref="DataTableExportScope.All"/>
    /// is the full result set for the current filter/sort (all pages, paging bypassed);
    /// <see cref="DataTableExportScope.Selected"/> exports the checked rows, sorted like the current
    /// view, with any row no longer in that view appended in the order it was checked.
    /// </summary>
    /// <param name="scope">Which rows to include.</param>
    public async Task<DataTableExportData> BuildExportDataAsync(DataTableExportScope scope)
    {
        var rows = scope == DataTableExportScope.Selected
            ? await GatherSelectedRowsAsync()
            : await GatherAllRowsAsync();

        var headers = _visibleColumns.Select(c => c.Title).ToList();
        var values = rows
            .Select(item => (IReadOnlyList<object?>)_visibleColumns
                .Select(c => c.Field?.Invoke(item))
                .ToList())
            .ToList();
        var data = values
            .Select(row => (IReadOnlyList<string?>)row.Select(value => value?.ToString()).ToList())
            .ToList();

        return new DataTableExportData { Name = ViewContext, Headers = headers, Rows = data, Values = values };
    }

    /// <summary>Exports the current filter/sort result set (all pages) and triggers a browser download.</summary>
    /// <param name="exporter">Format-specific exporter (CSV, XLSX, …).</param>
    /// <param name="fileName">Optional file name; defaults to the view context plus the exporter extension.</param>
    public Task ExportAsync(IDataTableExporter exporter, string? fileName = null)
        => ExportAsync(exporter, DataTableExportScope.All, fileName);

    /// <summary>
    /// Exports rows of the requested scope with a custom exporter (JSON, PDF, …) and triggers a
    /// browser download.
    /// </summary>
    /// <param name="exporter">Format-specific exporter.</param>
    /// <param name="scope">
    /// <see cref="DataTableExportScope.All"/> exports every row matching the current filter and
    /// sort; <see cref="DataTableExportScope.Selected"/> exports the checked rows, sorted like the
    /// current view, with any row no longer in that view appended in the order it was checked.
    /// </param>
    /// <param name="fileName">Optional file name; defaults to the view context plus the exporter extension.</param>
    public async Task ExportAsync(IDataTableExporter exporter, DataTableExportScope scope, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        var data = await BuildExportDataAsync(scope);
        var bytes = exporter.Export(data);
        var name = fileName ?? $"{ViewContext}.{exporter.FileExtension}";
        try
        {
            await JS.InvokeVoidAsync("tmDataTable.downloadFile", name, exporter.ContentType, Convert.ToBase64String(bytes));
        }
        catch { /* JS unavailable (e.g. tests) */ }
    }

    /// <summary>
    /// Exports all rows matching the current filter and sort in a built-in format and triggers a
    /// streamed browser download.
    /// </summary>
    /// <param name="format">CSV, or XLSX when an <see cref="IDataTableXlsxExporter"/> is registered.</param>
    public Task ExportAsync(DataTableExportFormat format)
        => ExportAsync(format, DataTableExportScope.All);

    /// <summary>
    /// Exports rows matching the current filter and sort in a built-in format and triggers a
    /// streamed browser download.
    /// </summary>
    /// <param name="format">CSV, or XLSX when an <see cref="IDataTableXlsxExporter"/> is registered.</param>
    /// <param name="scope">
    /// <see cref="DataTableExportScope.All"/> exports every matching row;
    /// <see cref="DataTableExportScope.Selected"/> exports the checked rows, sorted like the current
    /// view, with any row no longer in that view appended in the order it was checked.
    /// </param>
    public async Task ExportAsync(DataTableExportFormat format, DataTableExportScope scope)
    {
        if (format is not DataTableExportFormat.Csv and not DataTableExportFormat.Xlsx)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }

        if (_isExporting)
        {
            return;
        }

        var xlsxExporter = format == DataTableExportFormat.Xlsx ? XlsxExporter : null;
        if (format == DataTableExportFormat.Xlsx && xlsxExporter is null)
        {
            return;
        }

        _isExporting = true;
        StateHasChanged();
        try
        {
            var data = await BuildExportDataAsync(scope);
            var bytes = format switch
            {
                DataTableExportFormat.Csv => new CsvDataTableExporter(CsvDelimiter, writeBom: true).Export(data),
                DataTableExportFormat.Xlsx => xlsxExporter!.Export(data),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
            var extension = format == DataTableExportFormat.Csv ? "csv" : "xlsx";
            var fileName = $"{ViewContext}.{extension}";

            await using var stream = new MemoryStream(bytes, writable: false);
            using var streamReference = new DotNetStreamReference(stream);
            await JS.InvokeVoidAsync("TempoFileManager.downloadFileFromStream", fileName, streamReference);
            await OnExportCompleted.InvokeAsync(new DataTableExportResult(format, fileName, data.Rows.Count, scope));
        }
        finally
        {
            _isExporting = false;
            StateHasChanged();
        }
    }

    private Task HandleExportSelectionAsync(string value)
    {
        switch (value)
        {
            case "scope-all":
                _exportScope = DataTableExportScope.All;
                return Task.CompletedTask;
            case "scope-selected":
                _exportScope = _selectedItems.Count == 0
                    ? DataTableExportScope.All
                    : DataTableExportScope.Selected;
                return Task.CompletedTask;
            case "csv":
            case "xlsx":
                var scope = _exportScope;
                _exportScope = DataTableExportScope.All;
                return ExportAsync(value == "csv" ? DataTableExportFormat.Csv : DataTableExportFormat.Xlsx, scope);
            default:
                return Task.CompletedTask;
        }
    }
    // ── Column registry ──────────────────────────────────────────
    private readonly List<TmDataTableColumn<TItem>> _columns = [];
    private readonly List<TmDataTableColumn<TItem>> _visibleColumns = [];
    private readonly HashSet<string> _hiddenColumns = new();

    // ── Sort state (multi-column; index 0 = primary) ─────────────
    private readonly List<SortDescriptor> _sortDescriptors = [];
    private string? _sortColumn => _sortDescriptors.Count > 0 ? _sortDescriptors[0].Column : null;
    private bool _sortDescending => _sortDescriptors.Count > 0 && _sortDescriptors[0].Direction == DataTableSortDirection.Descending;

    private SortDescriptor? GetColumnSort(string key) => _sortDescriptors.FirstOrDefault(s => s.Column == key);
    private int GetColumnSortIndex(string key) => _sortDescriptors.FindIndex(s => s.Column == key);

    private IEnumerable<TItem> ApplySort(IEnumerable<TItem> items)
    {
        IOrderedEnumerable<TItem>? ordered = null;
        foreach (var descriptor in _sortDescriptors)
        {
            var col = _columns.FirstOrDefault(c => c.Key == descriptor.Column);
            if (col?.Field is null) continue;

            var field = col.Field;
            var descending = descriptor.Direction == DataTableSortDirection.Descending;
            ordered = ordered is null
                ? (descending ? items.OrderByDescending(field) : items.OrderBy(field))
                : (descending ? ordered.ThenByDescending(field) : ordered.ThenBy(field));
        }

        return ordered ?? items;
    }

    // ── Pagination state ─────────────────────────────────────────
    private int _currentPage = 1;
    private int _pageSize;
    private int _totalCount;
    private int _totalPages;

    /// <summary>
    /// Last value of the <see cref="PageSize"/> parameter the component has already applied — either
    /// because it arrived from the host, or because the table itself reported a change through
    /// <see cref="PageSizeChanged"/>. Keeping it in sync before invoking the callback is what stops a
    /// two-way bound value from coming back in as a "new" parameter and re-querying the provider twice.
    /// </summary>
    private int? _lastPageSizeParam;

    /// <summary>
    /// Page size the host has last been told about. Compared against <c>_pageSize</c> so
    /// <see cref="PageSizeChanged"/> fires on real changes only — every provider load rewrites
    /// <c>_pageSize</c> from the result, which is usually the same value that was asked for.
    /// </summary>
    private int _reportedPageSize;

    /// <summary>
    /// Set when the table changed the page size itself — the built-in dropdown, <see cref="ChangePageSizeAsync"/>
    /// or an applied saved view — while <see cref="PageSize"/> is controlled but no <see cref="PageSizeChanged"/>
    /// handler exists to carry the new value to the host. The host's value is then stale by definition, so the
    /// next parameter set re-syncs to it instead of letting the two drift apart silently.
    /// </summary>
    /// <remarks>
    /// A page size imposed by the <see cref="IDataTableDataProvider{TItem}"/> deliberately does <em>not</em> set
    /// this: the provider would answer the re-synced query with the same imposed size again, so every parent
    /// render would cost one more query and one more jump back to page one.
    /// </remarks>
    private bool _pageSizeOutOfSyncWithHost;

    // ── Data ─────────────────────────────────────────────────────
    private List<TItem> _displayedItems = [];
    private bool _isLoading;

    // ── Selection ────────────────────────────────────────────────
    // The set answers "is this checked?" (EqualityComparer<TItem>.Default, so a provider that returns
    // a new instance of the same value still matches). The list remembers the order rows were checked,
    // which the selected export needs for rows that are no longer in the current view.
    private readonly HashSet<TItem> _selectedItems = new();
    private readonly List<TItem> _selectedOrder = [];

    // ── Filtering / Search ───────────────────────────────────────
    private readonly Dictionary<string, DataTableFilter> _activeFilters = new();
    private string _searchText = string.Empty;
    private string? _lastSearchTextParam;
    private List<ActiveFilter> _externalFilters = [];

    /// <summary>
    /// Bumped whenever an input the filter builder cannot see changes (search text, column filters,
    /// new items, a provider reload). Passed as the builder's <c>ResultCountKey</c> so the preview
    /// recount instead of keeping a stale number.
    /// </summary>
    private int _resultCountVersion;

    /// <summary>The <see cref="Items"/> reference the preview-invalidation counter last saw.</summary>
    private IEnumerable<TItem>? _lastCountedItems;
    private string? _activeViewId;

    // ── Grouping state ─────────────────────────────────────────
    private readonly List<string> _groupByColumns = [];
    private readonly HashSet<string> _expandedGroups = new();
    private IReadOnlyList<DataGroup<TItem>>? _groupedData;
    private string? _draggedColumnKey;
    private int? _draggedChipIndex;
    private bool _isDragOver;
    private readonly Dictionary<string, int> _groupPageRequests = new();

    // ── Parameters: data ─────────────────────────────────────────

    /// <summary>In-memory items. When set without DataProvider, client-side sort/filter/pagination applies.</summary>
    [Parameter] public IEnumerable<TItem>? Items { get; set; }

    /// <summary>Server-side data provider. When set, overrides Items and calls GetDataAsync on query changes.</summary>
    [Parameter] public IDataTableDataProvider<TItem>? DataProvider { get; set; }

    /// <summary>Column definitions (TmDataTableColumn children).</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    // ── Parameters: behaviour ─────────────────────────────────────

    /// <summary>When true, shows a loading spinner instead of rows. Useful for manual loading control.</summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>Heading shown in the empty state when no items are available.</summary>
    [Parameter] public string? EmptyTitle { get; set; }

    /// <summary>Enables row checkboxes for multi-select.</summary>
    [Parameter] public bool Selectable { get; set; }

    /// <summary>When true, shows the global search input. Default: true.</summary>
    [Parameter] public bool ShowSearch { get; set; } = true;

    /// <summary>Controlled search text. When set, the component uses this value instead of its internal state.</summary>
    [Parameter] public string? SearchText { get; set; }

    /// <summary>Fires when the search text changes.</summary>
    [Parameter] public EventCallback<string> SearchTextChanged { get; set; }

    /// <summary>When true, shows the column visibility picker. Default: true.</summary>
    [Parameter] public bool ShowColumnPicker { get; set; } = true;

    /// <summary>
    /// When true, shows the TmPagination bar when more than one page exists. Default: true.
    /// When false, a client-side table (<see cref="Items"/>) renders every item it was handed rather than
    /// the first page — the pager is the only element that reaches pages 2..N, so hiding it and still
    /// slicing would leave the remaining rows in no element at all. Server-side paging through
    /// <see cref="DataProvider"/> is unaffected: there the page is chosen by the provider.
    /// </summary>
    [Parameter] public bool ShowPagination { get; set; } = true;

    /// <summary>
    /// When true, renders the toolbar container with search, column picker, and view manager. Default: true.
    /// The toolbar is not rendered when no visible control would be shown (see <see cref="ToolbarMode"/>).
    /// For a page-owned filter surface, set <see cref="ToolbarMode"/> to <see cref="DataToolbarMode.ContentOnly"/>
    /// and supply filtered data via <see cref="Items"/> or <see cref="DataProvider"/>.
    /// </summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>
    /// Opt-in responsive card mode. When true, the wrapper gains the
    /// <c>tm-data-table-wrapper--card</c> class and every data cell carries a
    /// <c>data-label</c> attribute (the owning column's <see cref="TmDataTableColumn{TItem}.Title"/>),
    /// which the scoped CSS uses on narrow viewports to stack each row into a labeled card.
    /// It aliases Cards only when MobileLayout is None; explicit Scroll wins.
    /// Default: false — the rendered markup is unchanged from a non-card table, so existing
    /// consumers are unaffected. Fully compatible with server-side data (<see cref="DataProvider"/>)
    /// and sorting.
    /// </summary>
    [Parameter] public bool CardMode { get; set; }

    /// <summary>
    /// Base wrapper class. Adds the opt-in <c>tm-data-table-wrapper--card</c> modifier only
    /// when <see cref="CardMode"/> is enabled, so non-card tables render an identical class list.
    /// </summary>
    private string WrapperCssClass =>
        UsesCardWrapper ? "tm-data-table-wrapper tm-data-table-wrapper--card" : "tm-data-table-wrapper";

    private string RootCssClass => string.Join(" ", new[]
    {
        WrapperCssClass,
        IsRedesigned ? "tm-data-table-wrapper--card-surface" : null,
        MobileLayout == DataTableMobileLayout.Scroll ? "tm-data-table-wrapper--responsive" : null,
        MobileLayout != DataTableMobileLayout.None ? "tm-data-table-wrapper--container" : null,
        IsMobileCards ? "tm-data-table-wrapper--cards" : null,
        Class,
    }.Where(part => !string.IsNullOrEmpty(part)));

    private string PaginationContainerClass =>
        MobileLayout == DataTableMobileLayout.None
            ? "tm-pagination-container"
            : "tm-pagination-container tm-data-table-pager--responsive";

    /// <summary>
    /// High-level preset that controls which toolbar chrome elements are rendered.
    /// <list type="bullet">
    ///   <item><see cref="DataToolbarMode.Full"/> — respects the individual <c>Show*</c> booleans.</item>
    ///   <item><see cref="DataToolbarMode.SearchOnly"/> — renders only the global search input.</item>
    ///   <item><see cref="DataToolbarMode.ActionsOnly"/> — renders only the column picker and view manager.</item>
    ///   <item><see cref="DataToolbarMode.ContentOnly"/> — hides all toolbar chrome and the external filter builder; use when the owning page provides its own filters.</item>
    /// </list>
    /// Default is <see cref="DataToolbarMode.Full"/>. Modes other than Full override the <c>Show*</c> booleans for the elements they affect.
    /// </summary>
    [Parameter] public DataToolbarMode ToolbarMode { get; set; } = DataToolbarMode.Full;

    /// <summary>
    /// When true and a <see cref="ViewProvider"/> is set, renders the TmViewManager. Default: true.
    /// </summary>
    [Parameter] public bool ShowViewManager { get; set; } = true;

    /// <summary>
    /// Initial page size, read once during initialization. Default: 25.
    /// Ignored when <see cref="PageSize"/> is supplied, which is the controlled counterpart.
    /// </summary>
    [Parameter] public int DefaultPageSize { get; set; } = 25;

    /// <summary>
    /// Controlled page size. When set, it wins over <see cref="DefaultPageSize"/> — including in the very
    /// first <see cref="IDataTableDataProvider{TItem}"/> query — and changing it after the table is mounted
    /// resizes the table in place. Null (the default) leaves the table uncontrolled: it starts at
    /// <see cref="DefaultPageSize"/> and the built-in page-size dropdown owns the value from then on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pair it with <see cref="PageSizeChanged"/> (<c>@bind-PageSize</c>) when the host renders its own
    /// page-size control, so the built-in dropdown and the host's control cannot drift apart. The
    /// imperative <see cref="ChangePageSizeAsync"/> does the same job for a host that keeps no state of
    /// its own.
    /// </para>
    /// <para>
    /// Changing the size returns the table to page one — page <c>N</c> denotes a different slice of the
    /// data at a different size, and may not exist at all once the size grows. This is the same behaviour
    /// the built-in dropdown has always had.
    /// </para>
    /// <para>
    /// A value of zero or less is rejected with <see cref="ArgumentOutOfRangeException"/>.
    /// </para>
    /// </remarks>
    [Parameter] public int? PageSize { get; set; }

    /// <summary>
    /// Fires whenever the effective page size changes — from the built-in dropdown, from
    /// <see cref="ChangePageSizeAsync"/>, from an applied saved view, or because an
    /// <see cref="IDataTableDataProvider{TItem}"/> answered with a page size different from the one asked
    /// for. Enables <c>@bind-PageSize</c>.
    /// </summary>
    [Parameter] public EventCallback<int> PageSizeChanged { get; set; }

    /// <summary>
    /// Key of the column the table is sorted by on first render, before the user has clicked any header.
    /// The key is <see cref="TmDataTableColumn{TItem}.PropertyName"/>, falling back to
    /// <see cref="TmDataTableColumn{TItem}.Title"/> when <c>PropertyName</c> is not set.
    /// Null (the default) starts the table unsorted, in the order the items were supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is seeded once during initialization, so it also reaches the first
    /// <see cref="IDataTableDataProvider{TItem}"/> query as <c>SortColumn</c>/<c>SortDescending</c>
    /// instead of being applied only to already-fetched rows.
    /// </para>
    /// <para>
    /// It is a starting state, not a floor: clicking the header cycles from it normally
    /// (a default of <see cref="DataTableSortDirection.Ascending"/> means the first click on that same
    /// column sorts descending, and the second clears the sort), applying a saved view replaces it,
    /// and an unknown key simply leaves the table unsorted.
    /// </para>
    /// <para>
    /// The column need not be <see cref="TmDataTableColumn{TItem}.Sortable"/> — a non-sortable column
    /// still orders the data, it just does not advertise <c>aria-sort</c> or react to clicks, which is
    /// how you express "this list has a fixed order the user cannot change".
    /// </para>
    /// </remarks>
    [Parameter] public string? DefaultSortColumn { get; set; }

    /// <summary>
    /// Direction applied to <see cref="DefaultSortColumn"/>. Ignored when that is null.
    /// Default: <see cref="DataTableSortDirection.Ascending"/>.
    /// </summary>
    [Parameter] public DataTableSortDirection DefaultSortDirection { get; set; } = DataTableSortDirection.Ascending;

    /// <summary>Page size options shown in the pagination dropdown.</summary>
    [Parameter] public IReadOnlyList<int> PageSizeOptions { get; set; } = [5, 10, 25, 50, 100];

    /// <summary>
    /// Additional HTML attributes splatted onto the built-in <see cref="TmPagination"/> root element.
    /// Use it to attach host-owned hooks (analytics ids, extra ARIA) to the pagination bar; the
    /// <c>data-testid</c>s of the pagination controls themselves come from <see cref="TmComponentBase.TestIdPrefix"/>,
    /// which this table propagates to the pagination automatically.
    /// </summary>
    [Parameter] public Dictionary<string, object>? PaginationAttributes { get; set; }

    /// <summary>
    /// Replaces the built-in "showing X–Y of Z" summary next to the pagination bar. The context carries the
    /// current paging state (<see cref="DataTablePaginationInfo"/>), so a host can render its own wording
    /// instead of the library's <c>TmDataTable_ShowingItems</c> resource. When null the localized default is used.
    /// Applies to the <see cref="DataTablePaginationInfoPlacement.Summary"/> placement only, which is the
    /// only placement the table itself renders.
    /// </summary>
    [Parameter] public RenderFragment<DataTablePaginationInfo>? PaginationInfoTemplate { get; set; }

    /// <summary>
    /// Which of the two item-range labels in the paging footer is rendered. The table's own summary and the
    /// embedded <see cref="TmPagination"/> both know the range, so exactly one of them shows it.
    /// Default: <see cref="DataTablePaginationInfoPlacement.Summary"/> — the table's summary, which is the
    /// placement that honours <see cref="PaginationInfoTemplate"/> and keeps the range on the footer's left edge.
    /// </summary>
    [Parameter] public DataTablePaginationInfoPlacement PaginationInfoPlacement { get; set; }
        = DataTablePaginationInfoPlacement.Summary;

    /// <summary>
    /// Whether the "showing X–Y of Z" result line renders when the pager does not. The released
    /// component only ever prints the count inside the paging footer, so with
    /// <see cref="ShowPagination"/> off, a non-paging <see cref="ScrollMode"/>, grouping, or a single
    /// page a reader (or a screen reader) gets no way to tell the table ended rather than truncated.
    /// Null (the default) keeps that behaviour exactly; <c>true</c> renders the summary whenever the
    /// pager itself is not already printing it; <c>false</c> suppresses the summary everywhere —
    /// including the footer slot a rendered pager would otherwise give it. Honours
    /// <see cref="PaginationInfoTemplate"/>.
    /// </summary>
    [Parameter] public bool? ShowResultSummary { get; set; }

    /// <summary>Current paging state as handed to <see cref="PaginationInfoTemplate"/>.</summary>
    private DataTablePaginationInfo CurrentPaginationInfo => new(
        CurrentPage: _currentPage,
        TotalPages: _totalPages,
        PageSize: _pageSize,
        TotalCount: _totalCount,
        StartItem: _totalCount == 0 ? 0 : ((_currentPage - 1) * _pageSize) + 1,
        EndItem: Math.Min(_currentPage * _pageSize, _totalCount));

    /// <summary>
    /// The paging state the standalone summary reports. When the pager is not slicing the rows,
    /// everything in <c>_totalCount</c> is on the page, so the honest range is 1–total — reporting
    /// "1–25 of 500" under <c>ShowPagination="false"</c> would describe a slice nobody rendered.
    /// </summary>
    private DataTablePaginationInfo ResultSummaryInfo => PagerRenders
        ? CurrentPaginationInfo
        : new DataTablePaginationInfo(
            CurrentPage: 1,
            TotalPages: 1,
            PageSize: _totalCount,
            TotalCount: _totalCount,
            StartItem: _totalCount == 0 ? 0 : 1,
            EndItem: _totalCount);

    /// <summary>
    /// What the polite live region announces: the same sentence the visible summary prints, so a
    /// screen reader hears the count a sighted reader sees. A custom template has no text to lift,
    /// and the Card appearance prints its own "Total N" sentence instead of the ranged one, so both
    /// fall back to the total.
    /// </summary>
    private string ResultCountAnnouncement => PaginationInfoTemplate is not null || IsRedesigned
        ? Loc.Plural("TmDataTable_TotalItems", _totalCount)
        : Loc["TmDataTable_ShowingItems", ResultSummaryInfo.StartItem, ResultSummaryInfo.EndItem, _totalCount];

    /// <summary>
    /// What the always-present filter-summary live region carries: empty while no filter is active
    /// (so the region exists before the text and the first filter change is announced), the plural
    /// filter count afterwards.
    /// </summary>
    private string FilterSummaryAnnouncement =>
        _activeFilters.Count > 0 ? Loc.Plural("TmDataTable_ActiveFilters", _activeFilters.Count) : string.Empty;

    /// <summary>The single condition the paging footer's visibility is decided by.</summary>
    private bool PagerRenders =>
        ShowPagination && _totalPages > 1
        && ScrollMode == DataTableScrollMode.Pagination
        && _groupByColumns.Count == 0;

    /// <summary>
    /// Optional view persistence provider. When set, enables saved views and (by default) the external filter builder.
    /// Use <see cref="ShowExternalFilterBuilder"/> = false to keep saved views without rendering the inline filter builder,
    /// or <see cref="ToolbarMode"/> = <see cref="DataToolbarMode.ContentOnly"/> when the page owns the filtering UI.
    /// </summary>
    [Parameter] public IDataTableViewProvider? ViewProvider { get; set; }

    /// <summary>Filter definitions used by the view manager filter builder when creating or editing saved views.</summary>
    [Parameter] public List<FilterDefinition> ViewFilterDefinitions { get; set; } = [];

    /// <summary>
    /// When true and a <see cref="ViewProvider"/> is set, shows the inline FilterBuilder for external filtering. Default: true.
    /// Set to false when the surrounding page owns the filtering UI to avoid duplicate filters.
    /// </summary>
    [Parameter] public bool ShowExternalFilterBuilder { get; set; } = true;

    /// <summary>When true, displays a localized clear-filters control while filters are active. Default: false.</summary>
    [Parameter] public bool ShowFilterSummary { get; set; }

    /// <summary>
    /// When true, the inline filter builder shows how many records match the current filters.
    /// A <see cref="DataProvider"/> is asked with <see cref="DataTableQuery.PageSize"/> of 1 and the
    /// preview reads <see cref="PagedResult{T}.TotalCount"/>; without one the in-memory rows are counted
    /// with the same rules as the table. Default: false.
    /// </summary>
    [Parameter] public bool ShowFilterResultPreview { get; set; }

    /// <summary>The count source handed to the inline filter builder, or null when the preview is off.</summary>
    private Func<IReadOnlyList<ActiveFilter>, CancellationToken, Task<int?>>? ResultCountProvider =>
        ShowFilterResultPreview && ShouldRenderExternalFilterBuilder() ? _countDelegate : null;

    private readonly DataTableResultCountProvider _resultCountProvider = new();

    // One delegate for the lifetime of the table. A method group would be a new instance on every
    // render, and the builder would treat that as a new provider and count again.
    private readonly Func<IReadOnlyList<ActiveFilter>, CancellationToken, Task<int?>> _countDelegate;

    /// <summary>Initializes the stable count delegate the inline filter builder receives.</summary>
    public TmDataTable()
    {
        _countDelegate = _resultCountProvider.CountAsync;
    }

    /// <summary>
    /// Filter definitions for the inline external filter builder (shown above the table when <see cref="ViewProvider"/> is set).
    /// When the page owns filtering, leave this empty and set <see cref="ShowExternalFilterBuilder"/> to false
    /// or use <see cref="ToolbarMode"/> = <see cref="DataToolbarMode.ContentOnly"/>.
    /// </summary>
    [Parameter] public List<FilterDefinition> ExternalFilterDefinitions { get; set; } = [];

    /// <summary>
    /// Universal display resolver for field labels and filter values.
    /// <list type="bullet">
    ///   <item><c>DisplayResolver(fieldName, null)</c> → localized field/column label (e.g., "Status" → "Stav")</item>
    ///   <item><c>DisplayResolver(fieldName, rawValue)</c> → localized value (e.g., ("Status","Active") → "Aktivní")</item>
    /// </list>
    /// Return null to use defaults (FilterDefinition.FieldLabel, column Title, or raw value).
    /// Set once on the component — flows to ViewManager, FilterBuilder, and ColumnPicker.
    /// </summary>
    [Parameter] public Func<string, string?, string?>? DisplayResolver { get; set; }

    /// <summary>Whether the user can create tenant-wide views. Default: false.</summary>
    [Parameter] public bool ViewCanCreateTenantViews { get; set; }

    /// <summary>Current user ID for personal view scoping.</summary>
    [Parameter] public string? CurrentUserId { get; set; }

    /// <summary>Current tenant ID for tenant view scoping.</summary>
    [Parameter] public string? CurrentTenantId { get; set; }

    /// <summary>
    /// Unique identifier for this table instance.
    /// Used to scope saved views to specific tables (e.g., "employees", "projects").
    /// </summary>
    [Parameter, EditorRequired] public string ViewContext { get; set; } = default!;

    // ── Parameters: virtualization ──────────────────────────────────

    /// <summary>Scroll/pagination mode. Default: Pagination.</summary>
    [Parameter] public DataTableScrollMode ScrollMode { get; set; } = DataTableScrollMode.Pagination;

    /// <summary>
    /// Height of a single row in pixels, used by Virtualize. The default 48 matches the pre-redesign
    /// row and does not force a CSS row height; set a different value to emit <c>--tm-data-table-row-height</c>.
    /// </summary>
    [Parameter] public float VirtualItemSize { get; set; } = 48f;

    private bool HasExplicitRowHeight => VirtualItemSize != 48f;

    /// <summary>Number of extra items to render above/below viewport. Default: 3.</summary>
    [Parameter] public int VirtualOverscanCount { get; set; } = 3;

    /// <summary>Fixed height for the virtualized scroll container (e.g. "600px", "80vh").</summary>
    [Parameter] public string? VirtualScrollHeight { get; set; }

    // ── Parameters: grouping ────────────────────────────────────────

    /// <summary>
    /// Keyboard model. <see cref="DataTableKeyboardNavigation.Rows"/> (default) keeps one Tab stop per row.
    /// <see cref="DataTableKeyboardNavigation.Grid"/> uses one roving tabindex on cells: arrows, Home, End,
    /// PageUp and PageDown move it; Enter activates the row once; Space toggles selection; F2 starts editing.
    /// Grid becomes the default in the next major;
    /// set <c>KeyboardNavigation="DataTableKeyboardNavigation.Rows"</c> to keep today's behavior after that.
    /// </summary>
    [Parameter] public DataTableKeyboardNavigation KeyboardNavigation { get; set; } = DataTableKeyboardNavigation.Rows;

    /// <summary>
    /// Surface. <see cref="DataTableAppearance.Plain"/> (default) keeps today's markup.
    /// <see cref="DataTableAppearance.Card"/> adds the card class around the table, pager and group bar.
    /// </summary>
    [Parameter] public DataTableAppearance Appearance { get; set; } = DataTableAppearance.Plain;

    /// <summary>Responsive mobile presentation. None (default) preserves today's table markup; Scroll enables horizontal scrolling.</summary>
    [Parameter] public DataTableMobileLayout MobileLayout { get; set; } = DataTableMobileLayout.None;

    /// <summary>True only for an explicit card mobile layout. CardMode keeps the original card class without card cells.</summary>
    private bool IsMobileCards => MobileLayout == DataTableMobileLayout.Cards;

    /// <summary>The card wrapper class from the pre-redesign CardMode, or the explicit Cards layout.</summary>
    private bool UsesCardWrapper => MobileLayout == DataTableMobileLayout.None ? CardMode : IsMobileCards;

    /// <summary>Card labels belong to CardMode and the explicit Cards layout. Card cell chrome is Cards only.</summary>
    private bool IsCardLayout => MobileLayout == DataTableMobileLayout.None ? CardMode : IsMobileCards;
    private EditActionsMode EffectiveEditActionsMode => IsMobileCards ? EditActionsMode.Text : EditActionsMode;

    /// <summary>The visual redesign. Every new default-markup change is gated on this.</summary>
    private bool IsRedesigned => Appearance == DataTableAppearance.Card;
    private bool IsResponsiveScroll => MobileLayout == DataTableMobileLayout.Scroll;
    private bool IsMobilePinned(TmDataTableColumn<TItem> col) => IsResponsiveScroll &&
        (col.MobilePinned || (!_visibleColumns.Any(c => c.MobilePinned) && ReferenceEquals(col, _visibleColumns.FirstOrDefault())));

    /// <summary>Whether to show the grouping drop zone above the table.</summary>
    [Parameter] public bool ShowGrouping { get; set; }

    /// <summary>Whether groups are collapsed by default.</summary>
    [Parameter] public bool GroupsCollapsedByDefault { get; set; } = true;

    // ── Parameters: events ────────────────────────────────────────

    /// <summary>Fires when a data row is clicked.</summary>
    [Parameter] public EventCallback<TItem> OnRowClick { get; set; }

    /// <summary>Fires when the selection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TItem>> OnSelectionChanged { get; set; }

    /// <summary>Fires when grouping configuration changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> OnGroupingChanged { get; set; }

    /// <summary>Additional HTML attributes to apply to each data row.</summary>
    [Parameter] public Func<TItem, IReadOnlyDictionary<string, object>?>? RowAttributes { get; set; }

    // ── Parameters: slots ─────────────────────────────────────────

    /// <summary>Render fragment shown in the selection action bar (bulk actions).</summary>
    [Parameter] public RenderFragment? SelectionActions { get; set; }

    /// <summary>Additional CSS class applied to the wrapper div.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes to apply to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Computed helpers ──────────────────────────────────────────

    private bool IsAllSelected => _displayedItems.Count > 0 && _displayedItems.All(IsSelected);
    private bool IsPartiallySelected => _selectedItems.Count > 0 && !IsAllSelected;

    /// <summary>Last value handed to <c>setIndeterminate</c>. Null until the first successful call.</summary>
    private bool? _lastSelectAllIndeterminate;
    private ElementReference _selectAllRef;
    private bool IsSelected(TItem item) => _selectedItems.Contains(item);
    private int ColSpan => Math.Max(1, (HasDetail ? 1 : 0) + (Selectable ? 1 : 0) + _visibleColumns.Count + (HasEditActions ? 1 : 0));

    /// <summary>
    /// Columns the edit-action footer must cover: the expander, the checkbox, every rendered data
    /// column and the edit-action column itself.
    /// </summary>
    private int EditFooterColSpan => (HasDetail ? 1 : 0) + (Selectable ? 1 : 0) + _renderedCellColumns.Count + 1;
    /// <summary>
    /// Attribute names the table manages itself on each &lt;tr&gt;. A consumer <see cref="RowAttributes"/>
    /// dictionary must not override these, or it would clobber row styling, selection clicks, keyboard
    /// handling, or focus order. They are dropped before the consumer attributes are splatted.
    /// </summary>
    private static readonly HashSet<string> ReservedRowAttributeNames =
        new(StringComparer.OrdinalIgnoreCase) { "class", "onclick", "onkeydown", "tabindex" };

    private IReadOnlyDictionary<string, object>? GetRowAttributes(TItem item)
    {
        var attributes = RowAttributes?.Invoke(item);

        // Selectable rows expose their selection state to assistive tech via aria-selected
        // (the table is role=grid). This requires merging into the consumer attributes.
        if (Selectable)
        {
            var result = new Dictionary<string, object>((attributes?.Count ?? 0) + 1, StringComparer.Ordinal);
            if (attributes is not null)
            {
                foreach (var pair in attributes)
                {
                    if (!ReservedRowAttributeNames.Contains(pair.Key) &&
                        !string.Equals(pair.Key, "aria-selected", StringComparison.Ordinal))
                    {
                        result[pair.Key] = pair.Value;
                    }
                }
            }
            result["aria-selected"] = IsSelected(item) ? "true" : "false";
            return result;
        }

        if (attributes is null || attributes.Count == 0)
        {
            return attributes;
        }

        // Fast path: no reserved names present → return the consumer dictionary untouched (no allocation).
        var hasReserved = false;
        foreach (var key in attributes.Keys)
        {
            if (ReservedRowAttributeNames.Contains(key))
            {
                hasReserved = true;
                break;
            }
        }

        if (!hasReserved)
        {
            return attributes;
        }

        var filtered = new Dictionary<string, object>(attributes.Count, StringComparer.Ordinal);
        foreach (var pair in attributes)
        {
            if (!ReservedRowAttributeNames.Contains(pair.Key))
            {
                filtered[pair.Key] = pair.Value;
            }
        }

        return filtered;
    }

    /// <summary>Determines whether any toolbar control should be rendered.</summary>
    private bool HasVisibleToolbarControls() =>
        ShouldRenderSearch() ||
        ShouldRenderColumnPicker() ||
        ShouldRenderViewManager() ||
        ShouldRenderExport() ||
        ShouldRenderMobileSort() ||
        ShouldRenderMobileFilters();

    private bool IsFullToolbarMode => ToolbarMode == DataToolbarMode.Full;
    private bool IsSearchToolbarMode => ToolbarMode == DataToolbarMode.SearchOnly;
    private bool IsActionsToolbarMode => ToolbarMode == DataToolbarMode.ActionsOnly;
    private bool IsContentToolbarMode => ToolbarMode == DataToolbarMode.ContentOnly;

    /// <summary>True when the global search input should be rendered.</summary>
    private bool ShouldRenderSearch() =>
        !IsContentToolbarMode && (IsSearchToolbarMode || (IsFullToolbarMode && ShowSearch));

    /// <summary>True when the column visibility picker should be rendered.</summary>
    private bool ShouldRenderColumnPicker() =>
        !IsContentToolbarMode &&
        !IsSearchToolbarMode &&
        _columns.Any(c => c.Hideable) &&
        (IsActionsToolbarMode || (IsFullToolbarMode && ShowColumnPicker));

    /// <summary>True when the view manager should be rendered.</summary>
    private bool ShouldRenderViewManager() =>
        !IsContentToolbarMode &&
        !IsSearchToolbarMode &&
        ViewProvider is not null &&
        (IsActionsToolbarMode || (IsFullToolbarMode && ShowViewManager));

    /// <summary>True when the export action should be rendered.</summary>
    private bool ShouldRenderExport() =>
        ShowExport &&
        !IsContentToolbarMode &&
        !IsSearchToolbarMode;

    /// <summary>True when the redesigned "Add grouping" dropdown should be rendered. Content-only and search-only toolbars keep the drop zone only.</summary>
    private bool ShouldRenderAddGrouping() =>
        ShowGrouping && IsRedesigned && !IsContentToolbarMode && !IsSearchToolbarMode;

    /// <summary>
    /// True when the active-filter summary and its clear-all link are allowed to render. The number
    /// of active filters is a data check and stays in the markup, the same way
    /// <c>TmMultiViewList.ShouldRenderItemCount()</c> keeps data checks separate. The summary renders
    /// outside the toolbar <c>div</c>, so it is deliberately not counted by
    /// <see cref="HasVisibleToolbarControls"/>.
    /// </summary>
    private bool ShouldRenderFilterSummary() =>
        ShowFilterSummary && (IsFullToolbarMode || IsActionsToolbarMode);

    private bool ShouldRenderExternalFilterBuilder() =>
        IsFullToolbarMode &&
        ShowExternalFilterBuilder &&
        ViewProvider is not null &&
        ExternalFilterDefinitions?.Any() == true;

    // ── Lifecycle ─────────────────────────────────────────────────

    private bool _dataLoaded;

    /// <summary>Initializes the table with default page size, controlled search text, and loads initial data.</summary>
    protected override async Task OnInitializedAsync()
    {
        _resultCountProvider.Attach(this);
        if (_dataLoaded) return;

        // A controlled PageSize wins over DefaultPageSize here, not later, so a server-side provider is
        // asked for the right page size in its very first query instead of fetching the default first.
        if (PageSize is { } initialPageSize)
            ThrowIfNotPositive(initialPageSize, nameof(PageSize));
        _pageSize = PageSize ?? DefaultPageSize;
        _lastPageSizeParam = PageSize;
        _reportedPageSize = _pageSize;

        _searchText = SearchText ?? string.Empty;
        _lastSearchTextParam = SearchText;

        // Seeded before the first load so a server-side provider receives the default order in its very
        // first query, rather than the table re-sorting page one of an already differently-ordered result.
        // Client-side the columns are not registered yet, so ApplySort finds nothing to sort by here; each
        // AddColumn re-runs RefreshClientItems, which applies it as soon as the column exists.
        if (!string.IsNullOrEmpty(DefaultSortColumn))
            _sortDescriptors.Add(new SortDescriptor(DefaultSortColumn, DefaultSortDirection));

        await LoadLayoutAsync();

        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();

        _dataLoaded = true;
    }

    /// <summary>Re-applies filters when Items collection reference changes in client-side mode and syncs the controlled search text and page size.</summary>
    protected override async Task OnParametersSetAsync()
    {
        // Sync externally supplied PageSize into internal state.
        // When PageSize is null the component is uncontrolled; internal state is preserved.
        var pageSizeChanged = PageSize is not null && (PageSize != _lastPageSizeParam || _pageSizeOutOfSyncWithHost);
        if (pageSizeChanged)
        {
            ThrowIfNotPositive(PageSize!.Value, nameof(PageSize));
            _pageSizeOutOfSyncWithHost = false;
            _lastPageSizeParam = PageSize;
            _pageSize = PageSize.Value;
            _reportedPageSize = _pageSize; // the host set it, so it already knows
            _currentPage = 1;
            _groupPageRequests.Clear();
        }
        else if (PageSize is null && _lastPageSizeParam is not null)
        {
            // Track transition from controlled back to uncontrolled without overwriting internal state.
            _lastPageSizeParam = null;
        }

        // Sync externally supplied SearchText into internal state.
        // When SearchText is null the component is uncontrolled; internal state is preserved.
        var searchTextChanged = SearchText is not null && SearchText != _lastSearchTextParam;
        if (searchTextChanged)
        {
            _lastSearchTextParam = SearchText;
            _searchText = SearchText!;
            _currentPage = 1;
            _groupPageRequests.Clear();
            _resultCountVersion++;
        }
        else if (SearchText is null && _lastSearchTextParam is not null)
        {
            // Track transition from controlled back to uncontrolled without overwriting internal state.
            _lastSearchTextParam = null;
        }

        // One reload covers both, so a host that changes page size and search text in the same render
        // does not issue two provider queries.
        if (pageSizeChanged || searchTextChanged)
        {
            if (DataProvider is not null)
                await LoadFromProviderAsync();
            else
                RefreshClientItems();
            return;
        }

        // Re-apply when Items collection reference changes in client-side mode
        if (DataProvider is null)
        {
            if (!ReferenceEquals(_lastCountedItems, Items))
            {
                _lastCountedItems = Items;
                _resultCountVersion++;
            }
            RefreshClientItems();
        }
    }

    // ── Column registration ───────────────────────────────────────

    /// <summary>Called by TmDataTableColumn.OnInitialized to register itself with the table.</summary>
    public void AddColumn(TmDataTableColumn<TItem> column)
    {
        if (_columns.Contains(column)) return;

        _columns.Add(column);
        if (column.HiddenByDefault)
            _hiddenColumns.Add(column.Key);

        RebuildVisibleColumns();

        if (DataProvider is null)
            RefreshClientItems();

        StateHasChanged();
    }

    private void RebuildVisibleColumns()
    {
        _visibleColumns.Clear();
        _visibleColumns.AddRange(
            _columns.Where(c => !_hiddenColumns.Contains(c.Key))
                    .OrderBy(c => GetPin(c) switch { ColumnPin.Left => 0, ColumnPin.Right => 2, _ => 1 })
                    .ThenBy(c => c.Order));
        RecomputeSticky();
        RebuildRenderedCellColumns();
        RebuildRowLoopCaches();
        // Hiding, reordering or switching layout can drop the column the cursor sits on.
        // Without this the active column stays past the last rendered cell and the grid
        // has no tabindex=0.
        ClampGridCursor();
    }

    // ── Column layout (pin + resize) ──────────────────────────────

    private ColumnPin GetPin(TmDataTableColumn<TItem> col)
        => _columnPins.TryGetValue(col.Key, out var pin) ? pin : col.Pinned;

    private int? GetPixelWidth(TmDataTableColumn<TItem> col)
    {
        if (_columnWidths.TryGetValue(col.Key, out var w)) return w;
        if (!string.IsNullOrEmpty(col.Width) && col.Width.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(col.Width[..^2], out var parsed))
            return parsed;
        return null;
    }

    /// <summary>
    /// The width the keyboard End key jumps to and <c>aria-valuemax</c> announces on the resize
    /// separator. A column has no enforced maximum (the drag is only min-clamped; the table
    /// scrolls), so this is the keyboard bound, not a clamp: mouse users may set any width.
    /// </summary>
    private const int MaxColumnWidth = 1200;

    private void RecomputeSticky()
    {
        _stickyLeft.Clear();
        _stickyRight.Clear();

        // Left-pinned columns stack from the container edge. The selection column's width is not
        // reserved here: the stylesheet shifts the stack by --tm-data-table-mobile-checkbox-width
        // when the responsive checkbox column is pinned, so the offset stays RTL-safe.
        // The pin groups are partitioned once: the old two Where() scans walked every visible
        // column twice, and the row loop used to scan them again per cell.
        _leftPinnedColumns.Clear();
        _rightPinnedColumns.Clear();
        foreach (var col in _visibleColumns)
        {
            switch (GetPin(col))
            {
                case ColumnPin.Left: _leftPinnedColumns.Add(col); break;
                case ColumnPin.Right: _rightPinnedColumns.Add(col); break;
            }
        }

        var leftOffset = 0;
        foreach (var col in _leftPinnedColumns)
        {
            _stickyLeft[col.Key] = leftOffset;
            leftOffset += GetPixelWidth(col) ?? DefaultPinnedWidth;
        }

        var rightOffset = 0;
        for (var i = _rightPinnedColumns.Count - 1; i >= 0; i--)
        {
            var col = _rightPinnedColumns[i];
            _stickyRight[col.Key] = rightOffset;
            rightOffset += GetPixelWidth(col) ?? DefaultPinnedWidth;
        }
    }

    private readonly List<TmDataTableColumn<TItem>> _leftPinnedColumns = [];
    private readonly List<TmDataTableColumn<TItem>> _rightPinnedColumns = [];

    private string GetColumnStyle(TmDataTableColumn<TItem> col)
    {
        var parts = new List<string>();
        var px = GetPixelWidth(col);
        if (px.HasValue)
        {
            parts.Add($"width:{px}px");
            // The table layout is auto: `width` alone is a suggestion the layout algorithm may
            // ignore when the columns compete for space, so a user-set pixel width is repeated
            // as min-width (the column's own MinWidth, if any, keeps priority over it).
            if (string.IsNullOrEmpty(col.MinWidth)) parts.Add($"min-width:{px}px");
        }
        else if (!string.IsNullOrEmpty(col.Width)) parts.Add($"width:{col.Width}");
        if (!string.IsNullOrEmpty(col.MinWidth)) parts.Add($"min-width:{col.MinWidth}");

        switch (GetPin(col))
        {
            case ColumnPin.Left when _stickyLeft.TryGetValue(col.Key, out var l):
                // The offset travels as a custom property: the stylesheet turns it into
                // inset-inline-start, so an RTL table sticks the column to the correct edge
                // without the component knowing the writing direction. The C# value is only the
                // fallback — data-table.js measures the header and writes --tm-pin-left-<index>
                // on the wrapper, and a re-render must keep reading that measured value.
                parts.Add("position:sticky");
                parts.Add(PinOffsetStyle("left", PinColumnIndex(col), l));
                break;
            case ColumnPin.Right when _stickyRight.TryGetValue(col.Key, out var r):
                parts.Add("position:sticky");
                parts.Add(PinOffsetStyle("right", PinColumnIndex(col), r));
                break;
        }

        return string.Join(";", parts);
    }

    /// <summary>
    /// Index of the column inside the first header row, counting the detail and selection columns
    /// that render before the data columns. The pin script keys its measured offset by this index,
    /// and the header renders <see cref="_renderedCellColumns"/> — a Cards layout leaves a
    /// <see cref="DataTableCardRole.Hidden"/> column out, so counting <see cref="_visibleColumns"/>
    /// would read the previous column's <c>--tm-pin-*</c> value.
    /// </summary>
    private int PinColumnIndex(TmDataTableColumn<TItem> col)
    {
        var index = 0;
        if (HasDetail) index++;
        if (Selectable) index++;
        foreach (var rendered in _renderedCellColumns)
        {
            if (ReferenceEquals(rendered, col) || string.Equals(rendered.Key, col.Key, StringComparison.Ordinal))
                return index;
            index++;
        }

        return index;
    }

    /// <summary>
    /// <c>--tm-pin-left: var(--tm-pin-left-N, &lt;fallback&gt;px)</c> (and <c>--tm-pin-right: var(--tm-pin-right-N, &lt;fallback&gt;px)</c>
    /// for a right-pinned column; both are component-local properties). The wrapper owns the measured
    /// value; the inline property is what the stylesheet reads, and the fallback keeps the column
    /// stuck before the first measurement (and in tests, where no script runs).
    /// </summary>
    private static string PinOffsetStyle(string edge, int index, int fallbackPx)
    {
        var i = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var px = fallbackPx.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Literal property names (no {edge} interpolation) so the token audit sees both declarations.
        return edge == "right"
            ? $"--tm-pin-right:var(--tm-pin-right-{i},{px}px)"
            : $"--tm-pin-left:var(--tm-pin-left-{i},{px}px)";
    }

    private string GetPinClass(TmDataTableColumn<TItem> col)
        => GetPin(col) switch
        {
            ColumnPin.Left => "tm-col-pinned tm-col-pinned-left",
            ColumnPin.Right => "tm-col-pinned tm-col-pinned-right",
            _ => string.Empty
        };

    private string GetPinGlyph(TmDataTableColumn<TItem> col)
        => GetPin(col) switch
        {
            ColumnPin.Left => "⇤",   // ⇤ pinned left
            ColumnPin.Right => "⇥",  // ⇥ pinned right
            _ => "⤡"                 // ⤡ unpinned
        };

    /// <summary>Sets a column's pin state (left/right/none), reorders columns, and persists the layout.</summary>
    public async Task SetColumnPinAsync(string columnKey, ColumnPin pin)
    {
        _columnPins[columnKey] = pin;
        RebuildVisibleColumns();
        await PersistLayoutAsync();
        StateHasChanged();
    }

    private Task CyclePinAsync(TmDataTableColumn<TItem> col)
    {
        var next = GetPin(col) switch
        {
            ColumnPin.None => ColumnPin.Left,
            ColumnPin.Left => ColumnPin.Right,
            _ => ColumnPin.None
        };
        return SetColumnPinAsync(col.Key, next);
    }

    /// <summary>Sets a column's pixel width and persists the layout.</summary>
    public async Task SetColumnWidthAsync(string columnKey, int width)
    {
        _columnWidths[columnKey] = Math.Max(MinColumnWidth, width);
        RecomputeSticky();
        await PersistLayoutAsync();
        StateHasChanged();
    }

    // ── Column resize: pointer (data-table.js over pointer-drag.js) + keyboard ──

    private ElementReference _tableRef;
    private ElementReference _wrapperRef;
    private bool _resizeAttached;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // One delegated attachment for the whole table (data-table.js listens on the root and
        // starts only from .tm-col-resize-handle handles), not one per handle press: the gesture
        // must be live before the first pointer move reaches .NET, which the old
        // press-then-startColumnResize round trip could not guarantee on Server.
        if (!_resizeAttached)
        {
            _resizeAttached = true;
            _dotNetRef ??= DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmDataTable.attachColumnResize", _tableRef, _dotNetRef);
                await JS.InvokeVoidAsync("tmDataTable.observePinnedOffsets", _wrapperRef);
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException
                                   or TaskCanceledException or ObjectDisposedException)
            {
                // JS unavailable (e.g. tests, prerender) — the drag is a no-op, the keyboard path
                // and SetColumnWidthAsync still work.
            }
        }

        // "Filter" closes the menu first; the control it names is focused once the menu is gone,
        // so focus never lands inside a panel that is about to unmount. The browser decides which
        // surface the focus goes to: the header filter when it is actually visible (a Cards table
        // in a wide container keeps its header), otherwise the cards filter sheet.
        if (_columnMenuFocusKey is { } focusKey)
        {
            _columnMenuFocusKey = null;
            try
            {
                var headerVisible = await JS.InvokeAsync<bool>(
                    "tmDataTable.focusColumnFilterOrOpenSheet", ColumnFilterId(focusKey));
                if (!headerVisible && CardsFilterableColumns.Any())
                {
                    _cardsFilterOpen = true;
                    _cardsFilterFocusKey = focusKey;
                    StateHasChanged();
                }
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException
                                   or TaskCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // No JS (tests, prerender): the header filter row is already rendered, so the
                // control is reachable by Tab. The id contract is what the tests assert.
            }
        }
        else if (_cardsFilterFocusKey is { } cardsFocusKey)
        {
            _cardsFilterFocusKey = null;
            try
            {
                // The sheet's inputs carry their own id family — the header row is still in the
                // DOM (only display:none), so the header id here would resolve to the wrong input.
                await JS.InvokeVoidAsync("tmDataTable.focusColumnFilter", CardsFilterId(cardsFocusKey));
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException
                                   or TaskCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // No JS (tests, prerender): the sheet is rendered, so the control is reachable.
            }
        }

        // Escape and every menu action except Filter close the menu. Focus would otherwise drop
        // to <body> while the trigger is tabindex=-1, so it returns to this column's header.
        if (_columnMenuReturnFocusKey is { } returnKey)
        {
            _columnMenuReturnFocusKey = null;
            try
            {
                await JS.InvokeVoidAsync("tmDataTable.focusHeader", _tableRef, returnKey);
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException
                                   or TaskCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // No JS (tests, prerender): the header is already in the document.
            }
        }

        if (Selectable)
        {
            // The mixed state is a DOM property JS has to write, but it only changes when the
            // selection does — re-rendering a sort or a page change must not pay the interop.
            var indeterminate = IsPartiallySelected;
            if (indeterminate != _lastSelectAllIndeterminate)
            {
                try
                {
                    await JS.InvokeVoidAsync("tmDataTable.setIndeterminate", _selectAllRef, indeterminate);
                    _lastSelectAllIndeterminate = indeterminate;
                }
                catch (Exception ex) when (ex is JSException or JSDisconnectedException
                                       or InvalidOperationException or TaskCanceledException or ObjectDisposedException)
                {
                }
            }
        }

        await SyncGridKeysAsync();
        await SyncColumnMenuKeysAsync();
        await FocusRowEditorAsync();
        await FocusActiveGridCellAsync();
    }

    /// <summary>Receives live width updates from the JavaScript resize drag (no persistence).</summary>
    [JSInvokable]
    public void OnColumnResized(string columnKey, double width)
    {
        // The state the column was in before the FIRST live update of the active gesture, so a
        // cancel can restore (or remove) it. An auto column had no width at all — the cancel must
        // return it to the auto layout, not to the gesture's start measurement baked in as a value.
        _resizeStart ??= (columnKey,
                          _columnWidths.TryGetValue(columnKey, out var before),
                          before);
        _columnWidths[columnKey] = Math.Max(MinColumnWidth, (int)Math.Round(width));
        RecomputeSticky();
        InvokeAsync(StateHasChanged);
    }

    // (columnKey, had a width before the gesture, that width) — null while no gesture is running.
    private (string Key, bool Existed, int Value)? _resizeStart;

    /// <summary>
    /// A cancelled resize drag (Escape, pointercancel, lost capture, mid-drag detach): the column
    /// returns to the state the FIRST live update found it in — the snapshot width back, or the
    /// width entry removed for a column that had none. The JavaScript side used to replay the
    /// press-time start width through <see cref="OnColumnResized"/>, which silently converted an
    /// auto column's measured width into a user-set one: the cancelled drag persisted something.
    /// Nothing is committed.
    /// </summary>
    [JSInvokable]
    public void OnColumnResizeCancelled()
    {
        if (_resizeStart is { } start)
        {
            if (start.Existed)
            {
                _columnWidths[start.Key] = start.Value;
            }
            else
            {
                _columnWidths.Remove(start.Key);
            }
            RecomputeSticky();
            InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Receives the resize-drag end signal from JavaScript and persists the layout.</summary>
    [JSInvokable]
    public Task OnColumnResizeCommitted()
    {
        // The gesture is finished: a stray late cancel must not roll a committed width back.
        _resizeStart = null;
        return PersistLayoutAsync();
    }

    /// <summary>
    /// The name the resize separator announces: <c>Title</c> wins, then <c>SortLabel</c>, then the
    /// column key. Unlike <see cref="SortButtonHeaderText"/> this never throws: a resizable column
    /// may legitimately carry only a HeaderTemplate (the resize handle predates any naming
    /// contract), and the key always names it.
    /// </summary>
    private string ResizeHandleAccessibleName(TmDataTableColumn<TItem> col)
    {
        if (!string.IsNullOrWhiteSpace(col.Title))
        {
            return col.Title;
        }

        if (!string.IsNullOrWhiteSpace(col.SortLabel))
        {
            return col.SortLabel!;
        }

        return col.Key;
    }

    /// <summary>
    /// The keyboard resize on a focused <c>role="separator"</c> handle (the WCAG 2.5.7 non-drag
    /// alternative). Arrows step by 8 px (Shift by 32 px, inverted in RTL like the drag delta),
    /// Home jumps to <see cref="MinColumnWidth"/> and End to <see cref="MaxColumnWidth"/>; every
    /// key commits through <see cref="SetColumnWidthAsync"/>, the single persisting path. The
    /// browser's default for these keys on the handle — scrolling the table — is cancelled in
    /// data-table.js, not here, so Tab and the other keys keep their behaviour.
    /// </summary>
    private async Task OnResizeHandleKeyAsync(TmDataTableColumn<TItem> col, KeyboardEventArgs e)
    {
        var current = GetPixelWidth(col) ?? await MeasureColumnWidthAsync(col);
        switch (e.Key)
        {
            case "ArrowLeft":
            case "ArrowRight":
                var step = e.ShiftKey ? 32 : 8;
                var growing = e.Key == "ArrowRight";
                if (await IsRtlAsync()) growing = !growing;
                await SetColumnWidthAsync(col.Key, current + (growing ? step : -step));
                break;
            case "Home":
                await SetColumnWidthAsync(col.Key, MinColumnWidth);
                break;
            case "End":
                await SetColumnWidthAsync(col.Key, MaxColumnWidth);
                break;
        }
    }

    /// <summary>
    /// The column's real rendered <c>&lt;th&gt;</c> width, measured live by
    /// <c>tmDataTable.measureColumn</c> (the keyboard step on a column without an explicit width
    /// must start where the column actually is, not at the 160 fallback). Returns
    /// <see cref="DefaultPinnedWidth"/> when JS is unavailable (tests, prerender) or the measure
    /// fails — the step still works, it just starts from the historical default.
    /// </summary>
    private async Task<int> MeasureColumnWidthAsync(TmDataTableColumn<TItem> col)
    {
        try
        {
            var width = await JS.InvokeAsync<double>("tmDataTable.measureColumn", _tableRef, col.Key);
            return width > 0 ? (int)Math.Round(width) : DefaultPinnedWidth;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or TaskCanceledException or ObjectDisposedException)
        {
            return DefaultPinnedWidth;
        }
    }

    /// <summary>
    /// The table's writing direction, read live from the DOM. In an RTL table the drag delta
    /// inverts in JavaScript; the keyboard arrows invert here for the same reason: the arrow that
    /// moves the column edge outward is the one that grows the width.
    /// </summary>
    private async Task<bool> IsRtlAsync()
    {
        try
        {
            var direction = await JS.InvokeAsync<string>("tmDataTable.directionOf", _tableRef);
            return string.Equals(direction, "rtl", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or TaskCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>No-op click handler: gives the resize handle's <c>@onclick:stopPropagation</c> a
    /// handler to be read by, so a tap on the handle cannot bubble a sort into the <c>&lt;th&gt;</c>
    /// (a stop without a handler is never visited by the dispatcher).</summary>
    private void SwallowHandleClick()
    {
    }

    private DataTableLayout CaptureLayout() => new()
    {
        ColumnWidths = new Dictionary<string, int>(_columnWidths, StringComparer.Ordinal),
        ColumnPins = new Dictionary<string, ColumnPin>(_columnPins, StringComparer.Ordinal)
    };

    private async Task PersistLayoutAsync()
    {
        var layout = CaptureLayout();
        if (LayoutStore is not null)
        {
            try { await LayoutStore.SaveLayoutAsync(ViewContext, layout, CurrentUserId); }
            catch { /* best-effort persistence */ }
        }

        await LayoutChanged.InvokeAsync(layout);
    }

    private async Task LoadLayoutAsync()
    {
        if (_layoutLoaded || LayoutStore is null) return;
        _layoutLoaded = true;

        try
        {
            var layout = await LayoutStore.LoadLayoutAsync(ViewContext, CurrentUserId);
            if (layout is null) return;

            _columnWidths.Clear();
            foreach (var pair in layout.ColumnWidths) _columnWidths[pair.Key] = pair.Value;
            _columnPins.Clear();
            foreach (var pair in layout.ColumnPins) _columnPins[pair.Key] = pair.Value;

            RebuildVisibleColumns();
        }
        catch { /* best-effort load */ }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Fire-and-forget: Dispose cannot await. The JS side cancels an in-flight drag first
        // (restoring the start width, committing nothing), so an unmounted table never keeps one.
        if (_resizeAttached)
        {
            _ = DetachColumnResizeAsync();
        }
        _dotNetRef?.Dispose();
        _dotNetRef = null;
        if (_gridKeysAttached)
            _ = DetachGridKeysAsync();
        if (_columnMenuKeysAttached)
            _ = DetachColumnMenuKeysAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_gridKeysAttached)
            await DetachGridKeysAsync();
        if (_columnMenuKeysAttached)
            await DetachColumnMenuKeysAsync();
        Dispose();
    }

    private async Task DetachColumnResizeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmDataTable.detachColumnResize", _tableRef);
            await JS.InvokeVoidAsync("tmDataTable.disposePinnedOffsets", _wrapperRef);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
                               or TaskCanceledException or ObjectDisposedException)
        {
        }
    }

    private IReadOnlyList<ColumnVisibilityItem> GetColumnVisibilityItems() =>
        _columns.Where(c => c.Hideable)
                .Select(c => new ColumnVisibilityItem(c.Key, c.Title, !_hiddenColumns.Contains(c.Key), c.Hideable))
                .ToList();

    // ── Client-side data (Items mode) ─────────────────────────────

    private void RefreshClientItems()
    {
        var items = (Items ?? []).AsEnumerable();

        // Search
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.Trim();
            items = items.Where(item =>
                _columns.Any(col =>
                    col.Field?.Invoke(item)?.ToString()
                       ?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }

        // Column filters
        foreach (var filter in _activeFilters.Values)
        {
            var col = _columns.FirstOrDefault(c => c.Key == filter.Column);
            if (col?.Field != null)
                items = ApplyClientFilter(items, col.Field, filter);
        }

        // Sort (multi-column)
        items = ApplySort(items);

        var list = items.ToList();
        _totalCount = list.Count;

        if (ScrollMode == DataTableScrollMode.Virtualized)
        {
            // Virtualized mode: no pagination, show all items
            SetDisplayedItems(list);
            _totalPages = 0;
        }
        else if (!ShowPagination)
        {
            // The pager is the only element that reaches pages 2..N, so slicing is derived from it:
            // with no pager the remaining rows would be in no element at all, and nothing in the UI
            // would say so. ShowPagination=false therefore means "do not slice", not "slice silently".
            SetDisplayedItems(list);
            _totalPages = 0;
        }
        else
        {
            _totalPages = _pageSize > 0 ? (int)Math.Ceiling((double)_totalCount / _pageSize) : 0;

            if (_currentPage > _totalPages && _totalPages > 0)
                _currentPage = _totalPages;
            else if (_currentPage < 1)
                _currentPage = 1;

            SetDisplayedItems(list.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList());
        }

        // Refresh grouping if active
        if (_groupByColumns.Count > 0)
            RefreshGroupedData();

        ClampGridCursor();
    }

    private static IEnumerable<TItem> ApplyClientFilter(
        IEnumerable<TItem> items,
        Func<TItem, object?> accessor,
        DataTableFilter filter)
    {
        var value = filter.Value?.ToString() ?? string.Empty;
        return filter.Operator.ToLowerInvariant() switch
        {
            "contains"                          => items.Where(x => accessor(x)?.ToString()?.Contains(value, StringComparison.OrdinalIgnoreCase) == true),
            "notcontains"                       => items.Where(x => accessor(x)?.ToString()?.Contains(value, StringComparison.OrdinalIgnoreCase) != true),
            "equals" or "eq"                    => items.Where(x => string.Equals(accessor(x)?.ToString(), value, StringComparison.OrdinalIgnoreCase)),
            "notequals"                         => items.Where(x => !string.Equals(accessor(x)?.ToString(), value, StringComparison.OrdinalIgnoreCase)),
            "startswith"                        => items.Where(x => accessor(x)?.ToString()?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true),
            "greaterthan"                       => items.Where(x => CompareValues(accessor(x), value) > 0),
            "lessthan"                          => items.Where(x => CompareValues(accessor(x), value) < 0),
            "greaterorequal" or "greaterthanorequal" => items.Where(x => CompareValues(accessor(x), value) >= 0),
            "lessorequal" or "lessthanorequal"  => items.Where(x => CompareValues(accessor(x), value) <= 0),
            "isempty"                           => items.Where(x => string.IsNullOrEmpty(accessor(x)?.ToString())),
            "isnotempty"                        => items.Where(x => !string.IsNullOrEmpty(accessor(x)?.ToString())),
            _                                   => items,
        };
    }

    private static int CompareValues(object? fieldValue, string filterValue)
    {
        if (fieldValue is null) return -1;

        // Try numeric comparison first
        if (double.TryParse(fieldValue.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fieldNum) &&
            double.TryParse(filterValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var filterNum))
            return fieldNum.CompareTo(filterNum);

        // Try date comparison
        if (fieldValue is DateTime dt && DateTime.TryParse(filterValue, out var filterDt))
            return dt.CompareTo(filterDt);
        if (fieldValue is DateTimeOffset dto && DateTimeOffset.TryParse(filterValue, out var filterDto))
            return dto.CompareTo(filterDto);

        // Fallback to string comparison
        return string.Compare(fieldValue.ToString(), filterValue, StringComparison.OrdinalIgnoreCase);
    }

    // ── Server-side data (DataProvider mode) ─────────────────────

    /// <summary>
    /// Id of the most recently issued provider query. Each load captures it and applies its result only while
    /// it is still the newest one, so a slow earlier response cannot overwrite a faster later one — the pager,
    /// the filters, sorting, search and the page size all start a load without waiting for the one in flight.
    /// </summary>
    private int _loadGeneration;

    private async Task LoadFromProviderAsync()
    {
        var generation = ++_loadGeneration;
        _isLoading = true;
        StateHasChanged();
        try
        {
            // When grouping is active, try server-side grouping first
            if (_groupByColumns.Count > 0)
            {
                var grouped = await DataProvider!.GetGroupedDataAsync(BuildQuery());
                if (generation != _loadGeneration) return; // superseded while in flight
                if (grouped is not null)
                {
                    // Server provided pre-grouped data — use directly
                    _groupedData = grouped.Groups.ToList();
                    _totalCount = grouped.TotalCount;
                    SetDisplayedItems([]);
                    _serverGroupPaging = grouped.GroupPaging;

                    // Set initial expand state
                    if (!GroupsCollapsedByDefault && _expandedGroups.Count == 0)
                        ExpandAllGroupsRecursive(_groupedData);

                    return;
                }
            }

            // Flat data fetch (non-grouped, or server doesn't support grouping)
            var result = await DataProvider!.GetDataAsync(BuildQuery());
            if (generation != _loadGeneration) return; // superseded while in flight
            SetDisplayedItems(result.Items.ToList());
            _totalCount = result.TotalCount;
            _currentPage = result.Page;
            _pageSize = result.PageSize;
            _totalPages = result.TotalPages;
            // After page size and page count: the clamp reads both, and a call before they are
            // assigned would clamp against the previous page.
            ClampGridCursor();
            _serverGroupPaging = null;

            // Fallback: group the server-provided items client-side
            if (_groupByColumns.Count > 0)
                RefreshGroupedData();

            // The provider is allowed to answer with a page size other than the one asked for (a server-side
            // cap, for instance). It wins — so a @bind-PageSize host has to hear about it, or its value
            // silently stops describing what the table is actually showing. Reported last, and only for a
            // result that was not superseded, so the host never hears a stale size.
            await NotifyPageSizeChangedAsync();
        }
        finally
        {
            // A superseded load must not clear the flag: the load that superseded it owns it now.
            if (generation == _loadGeneration)
            {
                _isLoading = false;
                _resultCountVersion++; // the data behind the preview count may have moved
                StateHasChanged();
            }
        }
    }

    /// <summary>Per-group pagination metadata from server-side grouping. Null when using client-side grouping.</summary>
    private IReadOnlyDictionary<string, GroupPagination>? _serverGroupPaging;

    private DataTableQuery BuildQuery() => new()
    {
        Page       = _currentPage,
        PageSize   = _pageSize,
        SortColumn = _sortColumn,
        SortDescending = _sortDescending,
        SortDescriptors = _sortDescriptors.ToList(),
        Filters    = _activeFilters.Values.ToList(),
        SearchText = _searchText,
        GroupByColumns = _groupByColumns.ToList(),
        GroupPageRequests = _groupPageRequests.Count > 0
            ? new Dictionary<string, int>(_groupPageRequests)
            : null,
    };

    // ── Sort ──────────────────────────────────────────────────────

    /// <summary>
    /// Cycles the sort state for a column. A plain click sorts by this column only (asc → desc → none).
    /// A multi-sort click (Shift) appends or cycles this column as an additional sort key, preserving the
    /// order in which columns were added.
    /// </summary>
    private async Task SortByAsync(TmDataTableColumn<TItem> col, bool multiSort)
    {
        if (!col.Sortable) return;

        var key = col.Key;
        var index = _sortDescriptors.FindIndex(s => s.Column == key);

        if (multiSort)
        {
            if (index < 0)
                _sortDescriptors.Add(new SortDescriptor(key, DataTableSortDirection.Ascending));
            else if (_sortDescriptors[index].Direction == DataTableSortDirection.Ascending)
                _sortDescriptors[index] = new SortDescriptor(key, DataTableSortDirection.Descending);
            else
                _sortDescriptors.RemoveAt(index);
        }
        else
        {
            var wasSoleAscending = _sortDescriptors.Count == 1 && index == 0
                && _sortDescriptors[0].Direction == DataTableSortDirection.Ascending;
            var wasSoleDescending = _sortDescriptors.Count == 1 && index == 0
                && _sortDescriptors[0].Direction == DataTableSortDirection.Descending;

            _sortDescriptors.Clear();
            if (wasSoleAscending)
                _sortDescriptors.Add(new SortDescriptor(key, DataTableSortDirection.Descending));
            else if (!wasSoleDescending)
                _sortDescriptors.Add(new SortDescriptor(key, DataTableSortDirection.Ascending));
            // wasSoleDescending → cleared (no sort)
        }

        _currentPage = 1;
        _groupPageRequests.Clear();
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    // ── Filter / Search ───────────────────────────────────────────

    private Task ApplyFilterAsync(string columnKey, DataTableFilter? filter)
        // The typed callback is the commit. It used to write the map itself while the string callback
        // removed from it, so the two disagreed about the value; both now end in SetColumnFilterAsync.
        => SetColumnFilterAsync(columnKey, filter?.Value?.ToString(), filter?.Operator);

    /// <summary>
    /// The single place a column filter is written. An empty value removes the filter; anything
    /// else replaces it. <c>TmColumnFilter.ValueChanged</c> is deliberately not bound: it fires on
    /// every keystroke, and writing there would reload the table twice per committed edit. The
    /// row reads the value back from the filter map, so the keystroke has nothing to store.
    /// </summary>
    private async Task SetColumnFilterAsync(string columnKey, string? value, string? op = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            _activeFilters.Remove(columnKey);
        }
        else
        {
            var column = _columns.FirstOrDefault(c => c.Key == columnKey);
            _activeFilters[columnKey] = new DataTableFilter(
                columnKey,
                op ?? column?.FilterOperator ?? FilterOperator.Contains.ToString(),
                value);
        }

        _currentPage = 1;
        _resultCountVersion++;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    private async Task OnSearchChangedAsync(string? value)
    {
        _searchText = value ?? string.Empty;
        _currentPage = 1;
        _groupPageRequests.Clear();
        _resultCountVersion++;

        // In controlled mode, advance the last-seen parameter so the upcoming
        // parent rerender from @bind-SearchText does not trigger a duplicate reload.
        if (SearchText is not null)
            _lastSearchTextParam = _searchText;

        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
        await SearchTextChanged.InvokeAsync(_searchText);
    }

    private async Task RemoveColumnFilterAsync(string columnKey)
    {
        _activeFilters.Remove(columnKey);
        _currentPage = 1;
        _resultCountVersion++;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    private async Task ClearAllFiltersAsync()
    {
        _activeFilters.Clear();
        _searchText = string.Empty;
        _currentPage = 1;
        _groupPageRequests.Clear();
        _resultCountVersion++;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    /// <summary>
    /// Counts the records matching the inline filter builder's filters. A data provider is asked for a
    /// single row so only <see cref="PagedResult{T}.TotalCount"/> moves; client-side data is counted with
    /// the table's own filter rules, including the search and column filters already applied.
    /// </summary>
    private sealed class DataTableResultCountProvider
    {
        private TmDataTable<TItem>? _table;

        public void Attach(TmDataTable<TItem> table) => _table = table;

        public async Task<int?> CountAsync(IReadOnlyList<ActiveFilter> filters, CancellationToken ct)
        {
            var table = _table;
            if (table is null) return null;

            if (table.DataProvider is { } provider)
            {
                var query = table.BuildQuery();
                query = new DataTableQuery
                {
                    Page = 1,
                    PageSize = 1,
                    SortColumn = query.SortColumn,
                    SortDescending = query.SortDescending,
                    SortDescriptors = query.SortDescriptors,
                    SearchText = query.SearchText,
                    GroupByColumns = query.GroupByColumns,
                    Filters = MergeExternalFilters(query.Filters, filters),
                };
                var result = await provider.GetDataAsync(query, ct);
                return result.TotalCount;
            }

            return table.CountClientItems(filters);
        }
    }

    /// <summary>
    /// Combines the table's own column filters with the filter builder's filters. The builder's
    /// filters win on their columns: an applied filter being edited is replaced by its draft, not
    /// counted next to it. Both the provider query and the client-side count share this rule.
    /// </summary>
    private static List<DataTableFilter> MergeExternalFilters(
        IReadOnlyList<DataTableFilter> current,
        IReadOnlyList<ActiveFilter> external)
    {
        var externalColumns = new HashSet<string>(external.Select(f => f.FieldName), StringComparer.Ordinal);
        var merged = current.Where(f => !externalColumns.Contains(f.Column)).ToList();
        merged.AddRange(external.Select(f => new DataTableFilter(f.FieldName, f.Operator.ToString(), f.Value)));
        return merged;
    }

    /// <summary>How many in-memory rows match the table's current search, column filters and these external filters.</summary>
    private int CountClientItems(IReadOnlyList<ActiveFilter> externalFilters)
    {
        var items = (Items ?? []).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.Trim();
            items = items.Where(item =>
                _columns.Any(col =>
                    col.Field?.Invoke(item)?.ToString()
                       ?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }

        foreach (var filter in MergeExternalFilters(_activeFilters.Values.ToList(), externalFilters))
        {
            var col = _columns.FirstOrDefault(c => c.Key == filter.Column);
            if (col?.Field != null)
                items = ApplyClientFilter(items, col.Field, filter);
        }

        return items.Count();
    }

    private async Task OnExternalFiltersChanged(IEnumerable<ActiveFilter> filters)
    {
        _externalFilters = filters.ToList();

        // Convert external filters to active filters
        _activeFilters.Clear();
        foreach (var filter in _externalFilters)
        {
            _activeFilters[filter.FieldName] = new DataTableFilter(filter.FieldName, filter.Operator.ToString(), filter.Value);
        }

        _currentPage = 1;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    // ── Pagination ────────────────────────────────────────────────

    private async Task GoToPageAsync(int page)
    {
        _currentPage = page;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    /// <summary>
    /// Returns the table to the first page and reloads. Call it from the host whenever the result set
    /// changes underneath the table — after a search, a filter change, or any other narrowing the page
    /// performs itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page that owns its own filter surface (<see cref="DataToolbarMode.ContentOnly"/>, feeding the
    /// table pre-filtered <see cref="Items"/> or a <see cref="DataProvider"/>) narrows the result set
    /// without the table knowing why. The table only clamps the current page *down* to the new last page,
    /// so searching while on page 3 leaves the user on page 3 — or on the new last page — instead of at
    /// the top of the results. The table's own search box resets the page, but that path is not taken
    /// when the host does the searching.
    /// </para>
    /// <para>
    /// Deliberately not automatic on an <see cref="Items"/> reference change: a table that re-polls the
    /// same list on a timer would then yank a reading user back to page 1 on every refresh. The host knows
    /// which change is a new query and which is the same query again, so it makes the call.
    /// </para>
    /// </remarks>
    public async Task ResetPageAsync()
    {
        _currentPage = 1;
        _groupPageRequests.Clear();

        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();

        // Called from the host's event handler, not the table's, so the table is not re-rendered for us.
        StateHasChanged();
    }

    /// <summary>
    /// Changes how many rows a page holds and returns to page one, without remounting the table.
    /// </summary>
    /// <param name="size">New page size. Must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is zero or negative.</exception>
    /// <remarks>
    /// <para>
    /// This is the imperative counterpart of the <see cref="PageSize"/> parameter, for a host that renders
    /// its own page-size control and keeps no state of its own. Before it existed, <see cref="DefaultPageSize"/>
    /// was read once during initialization and nothing public could change it afterwards, so the only way to
    /// resize a mounted table was to remount it through <c>@key</c> — which also throws away scroll position,
    /// focus, selection and expanded rows.
    /// </para>
    /// <para>
    /// The page is reset to one: page <c>N</c> denotes a different slice of the data at a different size, and
    /// at a larger size it may not exist at all. This matches what the built-in page-size dropdown has always
    /// done, so the public path and the built-in control behave identically.
    /// </para>
    /// <para>
    /// In server-side mode the new size reaches the <see cref="IDataTableDataProvider{TItem}"/> as
    /// <see cref="DataTableQuery.PageSize"/> of the immediately following query.
    /// <see cref="PageSizeChanged"/> fires either way, so a <c>@bind-PageSize</c> host stays in sync.
    /// </para>
    /// </remarks>
    public async Task ChangePageSizeAsync(int size)
    {
        ThrowIfNotPositive(size, nameof(size));

        _pageSize = size;
        await NotifyPageSizeChangedAsync(tableDriven: true);

        _currentPage = 1;
        _groupPageRequests.Clear();

        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();

        // May be called from the host's event handler, not the table's, in which case the table is not
        // re-rendered for us.
        StateHasChanged();
    }

    /// <summary>
    /// Reports the current <c>_pageSize</c> to a <c>@bind-PageSize</c> host. The remembered parameter value
    /// is updated first, so the value coming back in as a parameter is not mistaken for a new host-driven
    /// change and does not trigger a second load.
    /// </summary>
    /// <param name="tableDriven">
    /// True when the table itself decided the new size (dropdown, <see cref="ChangePageSizeAsync"/>, applied
    /// view) rather than the data provider answering with one. Only a table-driven change can leave a
    /// controlled host stale, so only it arms <c>_pageSizeOutOfSyncWithHost</c>.
    /// </param>
    private Task NotifyPageSizeChangedAsync(bool tableDriven = false)
    {
        if (_pageSize == _reportedPageSize)
            return Task.CompletedTask;

        _reportedPageSize = _pageSize;

        if (!PageSizeChanged.HasDelegate)
        {
            if (tableDriven && PageSize is not null)
                _pageSizeOutOfSyncWithHost = true;
            return Task.CompletedTask;
        }

        if (_lastPageSizeParam is not null)
            _lastPageSizeParam = _pageSize;

        return PageSizeChanged.InvokeAsync(_pageSize);
    }

    private static void ThrowIfNotPositive(int size, string paramName)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(paramName, size, "Page size must be greater than zero.");
    }

    // ── Selection ─────────────────────────────────────────────────

    private async Task ToggleRowSelectionAsync(TItem item, ChangeEventArgs e)
    {
        var isChecked = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        if (isChecked) SelectItem(item);
        else DeselectItem(item);
        ResetExportScopeIfSelectionEmpty();
        await OnSelectionChanged.InvokeAsync(_selectedItems.ToList());
    }

    private async Task ToggleSelectAllAsync(ChangeEventArgs e)
    {
        var isChecked = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        if (isChecked)
        {
            foreach (var item in _displayedItems) SelectItem(item);
        }
        else
        {
            ClearSelection();
        }

        ResetExportScopeIfSelectionEmpty();
        await OnSelectionChanged.InvokeAsync(_selectedItems.ToList());
    }

    private async Task DeselectAllAsync()
    {
        ClearSelection();
        ResetExportScopeIfSelectionEmpty();
        await OnSelectionChanged.InvokeAsync(_selectedItems.ToList());
    }

    /// <summary>Checks <paramref name="item"/>, appending it to the selection order only the first time.</summary>
    private void SelectItem(TItem item)
    {
        if (_selectedItems.Add(item))
        {
            _selectedOrder.Add(item);
        }
    }

    /// <summary>Unchecks <paramref name="item"/> and drops it from the selection order.</summary>
    private void DeselectItem(TItem item)
    {
        if (!_selectedItems.Remove(item))
        {
            return;
        }

        var index = _selectedOrder.FindIndex(selected => EqualityComparer<TItem>.Default.Equals(selected, item));
        if (index >= 0)
        {
            _selectedOrder.RemoveAt(index);
        }
    }

    private void ClearSelection()
    {
        _selectedItems.Clear();
        _selectedOrder.Clear();
    }

    // ── Column visibility ─────────────────────────────────────────

    private Task OnToggleColumnAsync(string key)
    {
        if (_hiddenColumns.Contains(key)) _hiddenColumns.Remove(key);
        else _hiddenColumns.Add(key);

        RebuildVisibleColumns();
        if (DataProvider is null) RefreshClientItems();
        return Task.CompletedTask;
    }

    private Task OnResetColumnsAsync()
    {
        _hiddenColumns.Clear();
        foreach (var col in _columns.Where(c => c.HiddenByDefault))
            _hiddenColumns.Add(col.Key);

        RebuildVisibleColumns();
        if (DataProvider is null) RefreshClientItems();
        return Task.CompletedTask;
    }

    // ── View manager ──────────────────────────────────────────────

    private async Task ApplyViewAsync(DataTableView view)
    {
        _activeViewId = view.Id;
        _sortDescriptors.Clear();
        if (!string.IsNullOrEmpty(view.SortField))
            _sortDescriptors.Add(new SortDescriptor(view.SortField, view.SortAscending ? DataTableSortDirection.Ascending : DataTableSortDirection.Descending));
        if (view.PageSize is > 0)
        {
            _pageSize = view.PageSize.Value;
            await NotifyPageSizeChangedAsync(tableDriven: true);
        }

        if (view.VisibleColumns.Count > 0)
        {
            _hiddenColumns.Clear();
            var visible = new HashSet<string>(view.VisibleColumns);
            foreach (var col in _columns.Where(c => !visible.Contains(c.Key)))
                _hiddenColumns.Add(col.Key);
            RebuildVisibleColumns();
        }

        // Update both internal filters and external filter builder
        _activeFilters.Clear();
        _externalFilters = view.Filters?.Where(f => !string.IsNullOrEmpty(f.Value)).Select(f =>
        {
            var fieldLabel = DisplayResolver?.Invoke(f.FieldName, null)
                ?? ExternalFilterDefinitions.FirstOrDefault(d => d.FieldName == f.FieldName)?.FieldLabel
                ?? f.FieldName;
            var displayValue = DisplayResolver?.Invoke(f.FieldName, f.Value) ?? f.Value;
            return new ActiveFilter(f.FieldName, fieldLabel, ParseFilterOperator(f.Operator), f.Value, displayValue);
        }).ToList() ?? [];

        foreach (var filter in _externalFilters)
            _activeFilters[filter.FieldName] = new DataTableFilter(filter.FieldName, filter.Operator.ToString(), filter.Value);

        // Apply grouping
        _groupByColumns.Clear();
        if (view.GroupByColumns?.Count > 0)
        {
            _groupByColumns.AddRange(view.GroupByColumns);
            _expandedGroups.Clear();
            RefreshGroupedData();
        }
        else
        {
            _groupedData = null;
        }

        _currentPage = 1;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
    }

    private DataTableView GetCurrentView() => new()
    {
        Name = Loc["TmDataTable_CurrentViewName"],
        SortField    = _sortColumn,
        SortAscending = !_sortDescending,
        PageSize     = _pageSize,
        VisibleColumns = _visibleColumns.Select(c => c.Key).ToList(),
        Filters      = _activeFilters.Select(kv => new FilterConfig
        {
            FieldName = kv.Key,
            Operator = kv.Value.Operator,
            Value = kv.Value.Value?.ToString() ?? ""
        }).ToList(),
        FiltersLegacy = _activeFilters.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.Value?.ToString()),
        GroupByColumns = _groupByColumns.ToList(),
    };

    private List<ViewColumnInfo> GetAvailableColumns() =>
        _columns.Select(c => new ViewColumnInfo
        {
            Key = c.Key,
            Title = c.Title,
            Visible = !_hiddenColumns.Contains(c.Key)
        }).ToList();

    private List<ViewColumnInfo> GetAvailableGroupableColumns() =>
        _columns.Where(c => c.Groupable).Select(c => new ViewColumnInfo
        {
            Key = c.Key,
            Title = c.Title,
            Visible = true
        }).ToList();

    private string ColumnFilterValue(string columnKey) =>
        _activeFilters.TryGetValue(columnKey, out var filter)
            ? filter.Value?.ToString() ?? string.Empty
            : string.Empty;

    // ── Grouping ────────────────────────────────────────────────────

    /// <summary>Add a column to the grouping configuration.</summary>
    public void AddGroupColumn(string columnKey)
    {
        if (_groupByColumns.Contains(columnKey)) return;
        var col = _columns.FirstOrDefault(c => c.Key == columnKey);
        if (col is null || !col.Groupable) return;

        _groupByColumns.Add(columnKey);
        _groupPageRequests.Clear();

        if (DataProvider is not null)
            _ = LoadFromProviderAsync();
        else
            RefreshGroupedData();

        _ = OnGroupingChanged.InvokeAsync(_groupByColumns.ToList());
        StateHasChanged();
    }

    /// <summary>Remove a column from the grouping configuration.</summary>
    public void RemoveGroupColumn(string columnKey)
    {
        if (!_groupByColumns.Remove(columnKey)) return;

        _expandedGroups.Clear();
        _groupPageRequests.Clear();
        if (_groupByColumns.Count > 0)
        {
            if (DataProvider is not null)
                _ = LoadFromProviderAsync();
            else
                RefreshGroupedData();
        }
        else
        {
            _groupedData = null;
            _serverGroupPaging = null;
            // Re-fetch flat data when grouping is fully removed (server-side mode empties _displayedItems)
            if (DataProvider is not null)
                _ = LoadFromProviderAsync();
        }

        _ = OnGroupingChanged.InvokeAsync(_groupByColumns.ToList());
        StateHasChanged();
    }

    /// <summary>Expand all groups.</summary>
    public void ExpandAllGroups()
    {
        if (_groupedData is null) return;
        ExpandAllGroupsRecursive(_groupedData);
        StateHasChanged();
    }

    /// <summary>Collapse all groups.</summary>
    public void CollapseAllGroups()
    {
        _expandedGroups.Clear();
        StateHasChanged();
    }

    private void ExpandAllGroupsRecursive(IReadOnlyList<DataGroup<TItem>> groups)
    {
        foreach (var g in groups)
        {
            _expandedGroups.Add(GetGroupId(g));
            if (g.SubGroups.Count > 0)
                ExpandAllGroupsRecursive(g.SubGroups);
        }
    }

    private void ToggleGroupExpansion(DataGroup<TItem> group)
    {
        var id = GetGroupId(group);
        var collapsed = _expandedGroups.Remove(id);
        if (!collapsed)
            _expandedGroups.Add(id);
        // Collapsing removes the rows the cursor may be sitting on. Land on the header that
        // remains and ask the next render to focus it, so the grid never loses its one tab stop.
        if (IsGrid)
        {
            if (collapsed)
            {
                var header = GroupHeaderIndex(group);
                if (header >= 0)
                    _activeRow = header;
            }
            ClampGridCursor();
            _gridFocusPending = true;
        }
        StateHasChanged();
    }

    /// <summary>Visible-row index of a group's header, or -1 when the group is not rendered.</summary>
    private int GroupHeaderIndex(DataGroup<TItem> group)
    {
        if (_groupedData is null) return -1;
        var cursor = 0;
        return FindGroupHeaderIndex(_groupedData, group, ref cursor);
    }

    private int FindGroupHeaderIndex(IReadOnlyList<DataGroup<TItem>> groups, DataGroup<TItem> target, ref int cursor)
    {
        foreach (var candidate in groups)
        {
            if (ReferenceEquals(candidate, target) || GetGroupId(candidate) == GetGroupId(target))
                return cursor;
            cursor++;
            if (!IsGroupExpanded(candidate))
                continue;
            if (candidate.SubGroups.Count > 0)
            {
                var nested = FindGroupHeaderIndex(candidate.SubGroups, target, ref cursor);
                if (nested >= 0)
                    return nested;
            }
            else
            {
                cursor += candidate.Items.Count;
            }
        }

        return -1;
    }

    private bool IsGroupExpanded(DataGroup<TItem> group)
    {
        return _expandedGroups.Contains(GetGroupId(group));
    }

    private static string GetGroupId(DataGroup<TItem> group)
    {
        return $"{group.FieldName}:{group.Key}";
    }

    /// <summary>Navigate to a specific page within a server-side group.</summary>
    private async Task NavigateGroupPageAsync(string groupKey, int page)
    {
        _groupPageRequests[groupKey] = page;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
    }

    private void RefreshGroupedData()
    {
        if (_groupByColumns.Count == 0)
        {
            _groupedData = null;
            return;
        }

        IEnumerable<TItem> items;

        if (DataProvider is not null)
        {
            // Server-side mode: items are already filtered/sorted by the provider
            items = _displayedItems;
        }
        else
        {
            // Client-side mode: apply search, filters, and sorting locally
            items = (Items ?? []).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.Trim();
                items = items.Where(item =>
                    _columns.Any(col =>
                        col.Field?.Invoke(item)?.ToString()
                           ?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
            }

            foreach (var filter in _activeFilters.Values)
            {
                var col = _columns.FirstOrDefault(c => c.Key == filter.Column);
                if (col?.Field != null)
                    items = ApplyClientFilter(items, col.Field, filter);
            }

            items = ApplySort(items);
        }

        var levels = _groupByColumns.Select(key =>
        {
            var col = _columns.FirstOrDefault(c => c.Key == key);
            var aggregateAccessors = _columns
                .Where(c => c.GroupAggregates is { Count: > 0 })
                .ToDictionary(c => c.Key, c => c.Field!);
            var aggregateTypes = _columns
                .Where(c => c.GroupAggregates is { Count: > 0 })
                .SelectMany(c => c.GroupAggregates!)
                .Distinct()
                .ToList();

            return new GroupingLevel<TItem>(
                key,
                col?.Field ?? (_ => null),
                DisplayFormatter: col?.GroupDisplayFormatter,
                AggregateAccessors: aggregateAccessors.Count > 0 ? new Dictionary<string, Func<TItem, object?>>(aggregateAccessors) : null,
                AggregateTypes: aggregateTypes.Count > 0 ? aggregateTypes : null
            );
        }).ToList();

        _groupedData = DataGroupingService.GroupItems(items, levels);

        // Set initial expand state
        if (!GroupsCollapsedByDefault && _expandedGroups.Count == 0)
        {
            ExpandAllGroupsRecursive(_groupedData);
        }
    }

    private int _groupRowIndex;

    private void AddGridCellAttributes(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder, ref int seq, int row, int col, TItem item)
    {
        if (!IsGrid) return;
        builder.AddAttribute(seq++, "role", "gridcell");
        builder.AddAttribute(seq++, "aria-colindex", col + 1);
        builder.AddAttribute(seq++, "data-grid-cell", GridCellKey(row, col));
        builder.AddAttribute(seq++, "tabindex", CellTabIndex(row, col));
        var capturedRow = row;
        var capturedCol = col;
        var capturedItem = item;
        builder.AddAttribute(seq++, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e => HandleGridKeyDownAsync(e, capturedRow, capturedCol, capturedItem)));
        builder.AddEventStopPropagationAttribute(seq++, "onkeydown", true);
        builder.AddEventStopPropagationAttribute(seq++, "onkeyup", true);
        // A mouse click focuses the clicked cell; the roving stop follows so Tab order
        // (Shift+Tab, Tab) resumes from the cell the user actually interacted with.
        builder.AddAttribute(seq++, "onfocusin", EventCallback.Factory.Create<FocusEventArgs>(this, () => SyncGridCursor(capturedRow, capturedCol)));
    }

    private Task HandleGroupHeaderKeyDownAsync(KeyboardEventArgs e, DataGroup<TItem> group)
    {
        if (!IsGrid || e.Repeat)
            return Task.CompletedTask;
        if (e.Key is "ArrowDown")
            return MoveGroupCursorAsync(1);
        if (e.Key is "ArrowUp")
            return MoveGroupCursorAsync(-1);
        if (e.Key is "Enter" or " ")
            ToggleGroupExpansion(group);
        // The header spans every column, so a horizontal arrow has nothing to move to — it drives
        // the group itself. Right expands, left collapses.
        else if (e.Key == "ArrowRight" && !IsGroupExpanded(group))
            ToggleGroupExpansion(group);
        else if (e.Key == "ArrowLeft" && IsGroupExpanded(group))
            ToggleGroupExpansion(group);
        return Task.CompletedTask;
    }

    private Task MoveGroupCursorAsync(int delta)
    {
        var count = CountGroupRows(_groupedData);
        if (count == 0)
            return Task.CompletedTask;
        _activeRow = Math.Clamp(_activeRow + delta, 0, count - 1);
        _activeCol = GridColumnForRow(_activeRow, _activeCol);
        _gridFocusPending = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private RenderFragment RenderGroupRows(IReadOnlyList<DataGroup<TItem>> groups, int level) => builder =>
    {
        if (level == 0)
            _groupRowIndex = 0;
        var seq = 0;
        foreach (var group in groups)
        {
            var g = group;
            var expanded = IsGroupExpanded(g);

            // Group header row. In Grid mode the header itself is the roving stop (role=gridcell, aria-colspan):
            // arrows are a no-op across the colspan, Enter/Space toggle the group.
            var groupIndex = _groupRowIndex++;
            builder.OpenElement(seq++, "tr");
            builder.AddAttribute(seq++, "class", $"tm-data-table-group-row tm-data-table-group-level-{level}");
            if (IsGrid)
            {
                builder.AddAttribute(seq++, "role", "row");
                builder.AddAttribute(seq++, "aria-rowindex", groupIndex + GridBodyRowOffset);
            }
            builder.OpenElement(seq++, "td");
            builder.AddAttribute(seq++, "colspan", ColSpan);
            if (IsGrid)
            {
                builder.AddAttribute(seq++, "role", "gridcell");
                builder.AddAttribute(seq++, "aria-colspan", ColSpan);
                builder.AddAttribute(seq++, "tabindex", groupIndex == _activeRow ? "0" : "-1");
                builder.AddAttribute(seq++, "data-grid-cell", GridCellKey(groupIndex, 0));
                builder.AddAttribute(seq++, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e => HandleGroupHeaderKeyDownAsync(e, g)));
                builder.AddEventStopPropagationAttribute(seq++, "onkeydown", true);
                builder.AddEventStopPropagationAttribute(seq++, "onkeyup", true);
                var capturedGroupRow = groupIndex;
                builder.AddAttribute(seq++, "onfocusin", EventCallback.Factory.Create<FocusEventArgs>(this, () => SyncGridCursor(capturedGroupRow, 0)));
            }

            // Toggle button. In Grid mode it is not its own tab stop: the header cell is.
            builder.OpenElement(seq++, "button");
            builder.AddAttribute(seq++, "type", "button");
            builder.AddAttribute(seq++, "class", "tm-data-table-group-toggle");
            if (IsGrid)
                builder.AddAttribute(seq++, "tabindex", "-1");
            builder.AddAttribute(seq++, "aria-expanded", expanded ? "true" : "false");
            builder.AddAttribute(seq++, "aria-label", string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Loc[expanded ? "TmDataTable_CollapseGroup" : "TmDataTable_ExpandGroup"],
                g.DisplayValue));
            builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => ToggleGroupExpansion(g)));
            builder.OpenComponent<TmIcon>(seq++);
            builder.AddAttribute(seq++, "Name", IconNames.ChevronRight);
            builder.AddAttribute(seq++, "Size", IconSize.Sm);
            builder.AddAttribute(seq++, "Class", expanded ? "tm-data-table-group-chevron tm-data-table-group-chevron--open" : "tm-data-table-group-chevron");
            builder.CloseComponent();
            builder.CloseElement(); // button

            if (GroupHeaderTemplate is not null)
            {
                builder.AddContent(seq++, GroupHeaderTemplate(g));
            }
            else
            {
                builder.OpenElement(seq++, "span");
                builder.AddAttribute(seq++, "class", "tm-data-table-group-label");
                builder.AddContent(seq++, g.DisplayValue);
                builder.CloseElement();
                builder.OpenElement(seq++, "span");
                builder.AddAttribute(seq++, "class", "tm-data-table-group-count");
                builder.AddContent(seq++, $"({g.Count})");
                builder.CloseElement();
            }

            builder.CloseElement(); // td
            builder.CloseElement(); // tr

            // Expanded content
            if (expanded)
            {
                if (g.SubGroups.Count > 0)
                {
                    builder.AddContent(seq++, RenderGroupRows(g.SubGroups, level + 1));
                }
                else
                {
                    foreach (var item in g.Items)
                    {
                        var rowItem = item;
                        builder.OpenElement(seq++, "tr");
                        builder.AddAttribute(seq++, "class", GetRowClass(rowItem));
                        // Same keyboard contract as the ungrouped rows: a non-native
                        // focusable <tr> offers Enter/Space -> OnRowClick itself.
                        // Grid mode moves that stop onto the cells.
                        builder.AddAttribute(seq++, "tabindex", RowTabIndex());
                        if (IsGrid)
                        {
                            builder.AddAttribute(seq++, "role", "row");
                            builder.AddAttribute(seq++, "aria-rowindex", _groupRowIndex + GridBodyRowOffset);
                        }
                        var groupItemRow = _groupRowIndex++;
                        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => HandleRowClickAsync(rowItem)));
                        builder.AddAttribute(seq++, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e => HandleRowKeyDownAsync(e, rowItem)));
                        builder.AddAttribute(seq++, "onkeyup", EventCallback.Factory.Create<KeyboardEventArgs>(this, e => HandleRowKeyUpAsync(e, rowItem)));
                        builder.AddMultipleAttributes(seq++, GetRowAttributes(rowItem));

                        if (HasDetail)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", "tm-col-expander");
                            AddGridCellAttributes(builder, ref seq, groupItemRow, 0, rowItem);
                            builder.CloseElement();
                        }

                        if (Selectable)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", "tm-col-check");
                            AddGridCellAttributes(builder, ref seq, groupItemRow, HasDetail ? 1 : 0, rowItem);
                            builder.OpenElement(seq++, "input");
                            builder.AddAttribute(seq++, "type", "checkbox");
                            builder.AddAttribute(seq++, "aria-label", Loc["TmDataTable_SelectRow"]);
                            if (IsGrid)
                                builder.AddAttribute(seq++, "tabindex", "-1");
                            builder.AddAttribute(seq++, "checked", IsSelected(rowItem));
                            builder.AddAttribute(seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => ToggleRowSelectionAsync(rowItem, e)));
                            // Same barriers as the ungrouped checkboxes: the row is a
                            // non-native focusable that emulates Enter/Space, so this
                            // native control's keydown/keyup/click must not leak into it.
                            // The keyup barrier is required too — Space activates on the
                            // release, so a keydown-only fence would still leak the Space
                            // keyup into the row's click handler.
                            builder.AddEventStopPropagationAttribute(seq++, "onkeydown", true);
                            builder.AddEventStopPropagationAttribute(seq++, "onkeyup", true);
                            builder.AddEventStopPropagationAttribute(seq++, "onclick", true);
                            builder.CloseElement(); // input
                            builder.CloseElement(); // td
                        }

                        var groupCol = GridDefaultColumn;
                        foreach (var col in _renderedCellColumns)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", GetCellClass(col));
                            builder.AddAttribute(seq++, "style", GetColumnStyle(col));
                            AddGridCellAttributes(builder, ref seq, groupItemRow, groupCol++, rowItem);
                            if (col.CellTemplate is not null)
                                builder.AddContent(seq++, col.CellTemplate(rowItem));
                            else
                                builder.AddContent(seq++, col.Field?.Invoke(rowItem)?.ToString());
                            builder.CloseElement(); // td
                        }

                        builder.CloseElement(); // tr
                    }

                    // Per-group mini-pager
                    var groupKeyStr = g.Key?.ToString() ?? "";
                    if (_serverGroupPaging is not null
                        && _serverGroupPaging.TryGetValue(groupKeyStr, out var paging)
                        && paging.TotalPages > 1)
                    {
                        var capturedKey = groupKeyStr;
                        builder.OpenElement(seq++, "tr");
                        builder.AddAttribute(seq++, "class", "tm-data-table-group-pagination");
                        builder.OpenElement(seq++, "td");
                        builder.AddAttribute(seq++, "colspan", ColSpan);

                        builder.OpenComponent<TmPagination>(seq++);
                        builder.AddComponentParameter(seq++, nameof(TmPagination.CurrentPage), paging.Page);
                        builder.AddComponentParameter(seq++, nameof(TmPagination.TotalPages), paging.TotalPages);
                        builder.AddComponentParameter(seq++, nameof(TmPagination.TotalCount), paging.TotalCount);
                        builder.AddComponentParameter(seq++, nameof(TmPagination.PageSize), paging.PageSize);
                        builder.AddComponentParameter(seq++, nameof(TmPagination.PageSizeOptions), (IReadOnlyList<int>?)null);
                        builder.AddComponentParameter(seq++, nameof(TmPagination.Class), "tm-pagination-compact");
                        // Namespace the per-group pager so several group pagers on one table stay individually targetable.
                        builder.AddComponentParameter(seq++, nameof(TmPagination.TestIdPrefix), TestId($"group-{capturedKey}"));
                        builder.AddComponentParameter(seq++, nameof(TmPagination.OnPageChange),
                            EventCallback.Factory.Create<int>(this, page => NavigateGroupPageAsync(capturedKey, page)));
                        builder.CloseComponent();

                        builder.CloseElement(); // td
                        builder.CloseElement(); // tr
                    }
                }
            }
        }
    };

    private void HandleRemoveGroupChip(string columnKey)
    {
        RemoveGroupColumn(columnKey);
    }

    /// <summary>Sets the dragged column key when a column header drag starts.</summary>
    public void OnColumnDragStart(string columnKey) => _draggedColumnKey = columnKey;

    private void OnColumnDragEnd()
    {
        _draggedColumnKey = null;
        _isDragOver = false;
    }

    private void HandleGroupZoneDragOver()
    {
        // Just allow drag over - preventDefault is on the element
    }

    private void HandleGroupZoneDrop()
    {
        _isDragOver = false;
        if (_draggedColumnKey != null && !_groupByColumns.Contains(_draggedColumnKey))
        {
            AddGroupColumn(_draggedColumnKey);
            _draggedColumnKey = null;
        }
    }

    private void OnChipDragStart(int index)
    {
        _draggedChipIndex = index;
    }

    private void OnChipDrop(int targetIndex)
    {
        if (_draggedChipIndex.HasValue && _draggedChipIndex.Value != targetIndex)
        {
            var item = _groupByColumns[_draggedChipIndex.Value];
            _groupByColumns.RemoveAt(_draggedChipIndex.Value);
            _groupByColumns.Insert(targetIndex, item);
            RefreshGroupedData();
            _ = OnGroupingChanged.InvokeAsync(_groupByColumns.ToList());
            StateHasChanged();
        }
        _draggedChipIndex = null;
    }

    // ── Row click ─────────────────────────────────────────────────

    private Task HandleRowClickAsync(TItem item) => OnRowClick.InvokeAsync(item);

    // The <tr> is a non-native focusable — the emulation is required, and it follows the
    // native <button> split exactly (docs/keyboard-activation-convention.md): Enter fires
    // on keydown (the !Repeat guard bounds it to the real press — a held Enter's auto-repeat
    // stream must not re-fire the click), Space fires on keyup only. Native children inside
    // the row (selection checkbox, expander, edit-start) fence BOTH their keydowns and
    // keyups so their own activation never reaches these handlers.
    private Task HandleRowKeyDownAsync(KeyboardEventArgs e, TItem item)
    {
        // Grid cells own activation. A bubbling Enter must not fire the row as well.
        if (IsGrid)
            return Task.CompletedTask;
        if (e.Key == "Enter" && !e.Repeat)
            return HandleRowClickAsync(item);
        return Task.CompletedTask;
    }

    private Task HandleRowKeyUpAsync(KeyboardEventArgs e, TItem item)
    {
        if (IsGrid)
            return Task.CompletedTask;
        if (e.Key == " ")
            return HandleRowClickAsync(item);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The Shift state of the keydown that initiated the in-flight keyboard activation. Captured
    /// on the way through the header because the event the sort decision reads — the click —
    /// cannot be trusted to carry it.
    /// </summary>
    /// <remarks>
    /// Measured: Firefox dispatches a button's keyboard-synthesized <c>click</c> with
    /// <c>shiftKey=false</c> even when Shift was held for the Enter — Chromium copies the key's
    /// modifiers into the click, Firefox does not. The <c>keydown</c> reports Shift correctly in
    /// both engines, so it is recorded here and consulted for clicks with <c>detail == 0</c>
    /// (keyboard-originated). The flag is consumed by that click; a fresh keydown always
    /// overwrites it, and a real mouse click (<c>detail &gt;= 1</c>) ignores it entirely — the
    /// event's own <c>shiftKey</c> is authoritative there.
    /// </remarks>
    private bool _activationKeyShift;

    /// <summary>
    /// When <see cref="_activationKeyShift"/> was armed, on the injectable clock. The flag's click
    /// can be lost — Space pressed, focus moved away before keyup dispatches the click — and a
    /// much later keyboard-originated click (any <c>detail == 0</c> click, including a
    /// programmatic <c>.click()</c>) would inherit the stale modifier as a phantom multi-sort.
    /// The flag is therefore time-bound like the overlay's Escape-keyup suppressor (N163): beyond
    /// <see cref="ActivationKeyShiftWindow"/> a click answers only its own <c>shiftKey</c>.
    /// </summary>
    private long _activationKeyShiftArmedAt;

    /// <summary>
    /// How long an armed <see cref="_activationKeyShift"/> stays consumable. Enter's synthesized
    /// click lands inside the keydown dispatch itself; Space's lands on keyup, so the window must
    /// cover an ordinary key-hold — a second does that without surviving into the next gesture
    /// (same bound as the N163 suppressor). A Space held longer falls back to the click's own
    /// <c>shiftKey</c>, which is the Firefox shape the flag exists for — Shift+Space held over a
    /// second then released is the documented edge the bound trades away.
    /// </summary>
    private static readonly TimeSpan ActivationKeyShiftWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Clock behind the armed-flag deadline. Resolved through <see cref="IServiceProvider"/> so a
    /// consumer that never registers a <see cref="TimeProvider"/> still gets
    /// <see cref="TimeProvider.System"/> — tests register a FakeTimeProvider (same pattern as
    /// <c>TmSearchInput</c>'s debounce clock).
    /// </summary>
    private TimeProvider Clock
        => _clock ??= ServiceProvider.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
    private TimeProvider? _clock;

    /// <summary>
    /// The <c>&lt;th&gt;</c>'s own key handler exists for ONE shortcut now: P for the pin. Activation is
    /// deliberately NOT handled here — a sortable header's activatable element is the
    /// <c>.tm-th-sort</c> button inside it, where Enter and Space produce a native <c>click</c> that
    /// bubbles up to the <c>&lt;th&gt;</c>'s <c>@onclick</c> (WCAG 2.1.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a button inside, and why no Enter branch here.</b> Up to 2.8.25 the header itself was the
    /// control — <c>&lt;th tabindex="0"&gt;</c> answering Enter in this handler — because the header
    /// could not be wrapped in a <c>&lt;button&gt;</c> without nesting the pin toggle, the resize handle
    /// and a consumer's <c>HeaderTemplate</c> inside interactive content. 2.8.26 wraps only the label and
    /// the sort icon instead: those siblings stay outside, the markup stays valid, and the key handling
    /// the <c>&lt;th&gt;</c> used to emulate becomes native. Space now activates too — a button's default
    /// for Space is "click", not "scroll one screen", so the old sort-and-jump conflict is gone rather
    /// than suppressed.
    /// </para>
    /// <para>
    /// Keeping an Enter branch HERE would sort twice in a real browser: once from the keydown, and once
    /// from the <c>click</c> the keydown itself synthesizes on a button. So this method answers P only;
    /// activation arrives exclusively through the bubbled click, for keyboard and mouse alike.
    /// </para>
    /// <para>
    /// P reaches this handler by bubbling — pressed on the sort button it travels to the
    /// <c>&lt;th&gt;</c>; pressed inside a consumer's <c>HeaderTemplate</c> the keydown barrier stops it
    /// before it gets here (WCAG 2.1.4 — typing into a filter box in the header pinned the column).
    /// </para>
    /// <para>
    /// <b>P cycles the column pin</b>, added in 2.8.22 together with <c>tabindex="-1"</c> on the pin
    /// button. That button was a real <c>&lt;button&gt;</c> inside every visible header, so six columns
    /// cost eleven Tab presses and five of those stops painted nothing until hover; taking it out of the
    /// sequential order WITHOUT giving the header a key would have made pinning unreachable rather than
    /// cheaper. Pinning does not need a sortable column, so P is answered on any header while
    /// <c>ShowColumnMenu</c> is on, and <c>aria-keyshortcuts</c> announces it on whichever element is the
    /// stop — the sort button, or the <c>&lt;th&gt;</c> itself when the pin is all it offers.
    /// </para>
    /// </remarks>
    private Task HandleHeaderKeyDownAsync(KeyboardEventArgs e, TmDataTableColumn<TItem> col)
    {
        // Record the modifier state for the activation keys that synthesize a click on the sort
        // button (Enter fires click on keydown, Space on keyup — both bubble through here first).
        // Only a SORTABLE header's button produces the bubbled click that consumes this flag
        // (HeaderClickRequestsMultiSort) — arming it for a pin-only header (no .tm-th-sort button,
        // no click follows) leaves it stuck true until an unrelated later click on a DIFFERENT
        // sortable column inherits it (N203, review 2026-09-22).
        if (col.Sortable && e.Key is "Enter" or " ")
        {
            _activationKeyShift = e.ShiftKey;
            _activationKeyShiftArmedAt = Clock.GetTimestamp();
        }

        // P keeps pinning in both modes: Menu mode removes the pin BUTTON, not the shortcut.
        if (ShowColumnMenu && (e.Key is "p" or "P"))
        {
            return CyclePinAsync(col);
        }

        // M opens this header's column menu. It is answered only while the header itself is the
        // event target: a consumer's HeaderTemplate stops keydown, so typing into a filter box
        // inside the header never reaches here (WCAG 2.1.4 — same fence the P shortcut uses).
        if (ShowColumnMenu && ColumnMenuMode == ColumnMenuMode.Menu && (e.Key is "m" or "M"))
        {
            return ToggleColumnMenuAsync(col);
        }

        // Alt+ArrowDown is the announced shortcut (aria-keyshortcuts) for the same menu. M stays as
        // the shorter alternative; both are answered only while the header itself is the target.
        if (ShowColumnMenu && ColumnMenuMode == ColumnMenuMode.Menu && e.AltKey && e.Key == "ArrowDown")
        {
            return ToggleColumnMenuAsync(col);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether this header click is a multi-sort gesture. A pointer click answers its own
    /// <c>shiftKey</c>; a keyboard-originated click (<c>detail == 0</c>) consults the Shift state
    /// recorded by the keydown that produced it, because Firefox ships such clicks with every
    /// modifier cleared (see <see cref="_activationKeyShift"/>).
    /// </summary>
    private bool HeaderClickRequestsMultiSort(MouseEventArgs e)
    {
        if (e.Detail != 0)
        {
            return e.ShiftKey;
        }

        // Honour the recorded modifier only inside its window — a click this late did not come
        // from the keydown that armed it (the Space→focus-move→keyup path loses the click, and a
        // later detail==0 click would otherwise inherit the stale Shift).
        var shift = _activationKeyShift
            && Clock.GetElapsedTime(_activationKeyShiftArmedAt) < ActivationKeyShiftWindow;
        _activationKeyShift = false;
        return shift || e.ShiftKey;
    }

    /// <summary>
    /// Whether the <c>&lt;th&gt;</c> itself — not a control inside it — is the focus stop. That is true
    /// only for a header whose sole keyboard affordance is the pin: a sortable header's stop is its
    /// <c>.tm-th-sort</c> button, and a header with no affordance at all is no stop. See
    /// <c>TmDataTableHeaderAccessibilityTests.AHeaderThatOnlyOffersThePin_IsStillAFocusStop</c>.
    /// </summary>
    private bool IsPinOnlyHeader(TmDataTableColumn<TItem> col) => !col.Sortable && ShowColumnMenu;

    /// <summary>
    /// The shortcuts a header announces. <c>P</c> pins, but only while the standalone pin button
    /// exists; <c>Alt+ArrowDown</c> opens the column menu. A sortable header announces them on its
    /// sort button (the real stop); a pin-only header announces them on the <c>&lt;th&gt;</c>.
    /// </summary>
    private string? HeaderKeyShortcuts(TmDataTableColumn<TItem> col)
    {
        if (!ShowColumnMenu)
            return null;

        var shortcuts = new List<string>();
        shortcuts.Add("P");
        if (ColumnMenuMode == ColumnMenuMode.Menu)
            shortcuts.Add("Alt+ArrowDown");

        return shortcuts.Count == 0 ? null : string.Join(" ", shortcuts);
    }

    /// <summary>
    /// Does nothing, on purpose. It exists so the barrier around a consumer's <c>HeaderTemplate</c> is a
    /// stop on the event path: a dispatcher visits an ancestor because a handler is registered there and
    /// reads <c>:stopPropagation</c> while visiting, so the flag on its own — measured — does not stop
    /// the bubble. Being empty is what keeps the barrier stateless, which is the property the flag-based
    /// attempt in 2.8.23 lacked.
    /// </summary>
    private static void SwallowTemplateKey(KeyboardEventArgs e) => _ = e;


    // ── Helper methods ────────────────────────────────────────────

    private static FilterOperator ParseFilterOperator(string? op) => Helpers.FilterOperatorParser.Parse(op);

    // ── CSS helpers ───────────────────────────────────────────────

    private string GetHeaderClass(TmDataTableColumn<TItem> col)
    {
        var parts = new List<string>();
        if (IsMobilePinned(col)) parts.Add("data-mobile-pinned");
        // The kebab overlays the inline end of the header; the th must reserve its width so it
        // never paints over the label, the sort arrow or the multi-sort index.
        if (ShowColumnMenu && ColumnMenuMode == ColumnMenuMode.Menu) parts.Add("tm-col-has-menu");
        if (col.Sortable) parts.Add("tm-col-sortable");
        if (col.Groupable) parts.Add("tm-col-groupable");
        var headerSort = GetColumnSort(col.Key);
        if (headerSort is not null)
            parts.Add(headerSort.Direction == DataTableSortDirection.Descending ? "tm-col-sorted-desc" : "tm-col-sorted-asc");
        if (col.Align == ColumnAlign.Center) parts.Add("tm-text-center");
        if (col.Align == ColumnAlign.Right) parts.Add("tm-text-right");
        var headerPin = GetPinClass(col);
        if (!string.IsNullOrEmpty(headerPin)) parts.Add(headerPin);
        return string.Join(" ", parts);
    }

    private string GetCellClass(TmDataTableColumn<TItem> col)
        => _cellClassByKey.TryGetValue(col.Key, out var cached) ? cached : BuildCellClass(col);

    private string BuildCellClass(TmDataTableColumn<TItem> col)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(col.CssClass)) parts.Add(col.CssClass);
        if (IsMobilePinned(col)) parts.Add("data-mobile-pinned");
        if (IsMobileCards) parts.Add("tm-data-table-cell--card");
        if (col.Align == ColumnAlign.Center) parts.Add("tm-text-center");
        if (col.Align == ColumnAlign.Right) parts.Add("tm-text-right");
        var cellPin = GetPinClass(col);
        if (!string.IsNullOrEmpty(cellPin)) parts.Add(cellPin);
        return string.Join(" ", parts);
    }

    private string GetSortIconClass(TmDataTableColumn<TItem> col)
    {
        var sort = GetColumnSort(col.Key);
        if (sort is null) return "tm-sort-none";
        return sort.Direction == DataTableSortDirection.Descending ? "tm-sort-desc" : "tm-sort-asc";
    }

    /// <summary>
    /// The <c>aria-sort</c> of a header, or <c>null</c> where the attribute would be a lie.
    /// </summary>
    /// <remarks>
    /// ARIA defines <c>aria-sort</c> for a <c>columnheader</c> whose column participates in sorting, and
    /// <c>none</c> there means "sortable, not currently sorted". On a column that cannot be sorted — an
    /// ACTIONS column — it announced an affordance that does not exist. Blazor omits an attribute whose
    /// value is null, so returning null is what removes it.
    /// <para>
    /// ARIA allows at most ONE directional <c>aria-sort</c> claim per table — under multi-sort every
    /// participating header used to announce its own direction, which is out of contract (carry-forward
    /// from the 20C UX review). Only the PRIMARY sort descriptor (<c>_sortDescriptors[0]</c>) announces
    /// ascending/descending; a secondary sort key reads <c>none</c> — sortable, not the column
    /// controlling the sort — while its precedence badge (aria-hidden) and the sort button's
    /// next-action name still carry the detail.
    /// </para>
    /// </remarks>
    private string? GetAriaSortValue(TmDataTableColumn<TItem> col)
    {
        if (!col.Sortable) return null;
        if (_sortDescriptors.Count == 0 || _sortDescriptors[0].Column != col.Key) return "none";
        return _sortDescriptors[0].Direction == DataTableSortDirection.Descending ? "descending" : "ascending";
    }

    /// <summary>
    /// The name the sort button announces: "Sort by {headerText} — {next action}". The header text
    /// is the column <c>Title</c> — the caption the user sees — with
    /// <see cref="TmDataTableColumn{TItem}.SortLabel"/> as the consumer's replacement for a column
    /// that has none. When neither exists the header must be a <c>HeaderTemplate</c>: that
    /// combination is a developer error — the template replaced the caption, so nothing names the
    /// button — and in DEBUG it throws rather than ship an unnamed control. In RELEASE the button
    /// falls back to a localized positional name ("Column {n}") because a published app serves the
    /// reader it has, not the one it should have had.
    /// </summary>
    /// <remarks>
    /// The second half of the name is the state the NEXT activation reaches — the same tri-state
    /// cycle <c>SortByAsync</c> walks (none → ascending → descending → none), so the name never
    /// announces "sort ascending" while the next press sorts descending.
    /// </remarks>
    private string SortButtonAccessibleName(TmDataTableColumn<TItem> col)
        => Loc["TmDataTable_SortBy", SortButtonHeaderText(col), Loc[NextSortActionKey(col)]];

    /// <summary>
    /// The subject inside "Sort by {0}": <c>Title</c> wins, then <c>SortLabel</c>. The
    /// <c>#if DEBUG</c> throw is a dev-loop safety net for source-embedded/debug-build consumers
    /// and this library's own tests — a Release NuGet package never compiles it in, so a templated
    /// header with neither <c>Title</c> nor <c>SortLabel</c> always falls back to the positional
    /// name in a published app; it never throws there (N143).
    /// </summary>
    private string SortButtonHeaderText(TmDataTableColumn<TItem> col)
    {
        if (!string.IsNullOrWhiteSpace(col.Title))
        {
            return col.Title;
        }

        if (!string.IsNullOrWhiteSpace(col.SortLabel))
        {
            return col.SortLabel!;
        }

#if DEBUG
        if (col.HeaderTemplate is not null)
        {
            throw new InvalidOperationException(
                $"TmDataTable column '{col.Key}' renders a HeaderTemplate but supplies neither Title " +
                "nor SortLabel — the sort button would announce a bare column number. Set Title or " +
                "SortLabel so the control has a real name.");
        }
#endif

        // No caption to name: the localized positional fallback. (A HeaderTemplate in this state
        // is the DEBUG throw above; a plain header with no Title renders an empty caption either
        // way, and the button still owes the reader a name.)
        return Loc["TmDataTable_SortColumn", _visibleColumns.IndexOf(col) + 1];
    }

    /// <summary>The resource key of the state the next activation on this column reaches.</summary>
    private string NextSortActionKey(TmDataTableColumn<TItem> col)
    {
        // The tri-state cycle (ascending → descending → cleared) only applies when this column is
        // the SOLE active sort — SortByAsync's plain-click branch resets every descriptor and sets
        // just this column ascending whenever more than one is active, regardless of this column's
        // own current direction (N202, review 2026-09-22).
        if (_sortDescriptors.Count == 1)
        {
            var sort = GetColumnSort(col.Key);
            if (sort is not null)
            {
                return sort.Direction == DataTableSortDirection.Ascending
                    ? "TmDataTable_SortDescending"
                    : "TmDataTable_ClearSort";
            }
        }

        return "TmDataTable_SortAscending";
    }

    private string GetRowClass(TItem item) => IsSelected(item) ? "tm-row-selected" : string.Empty;

    // ── Scroll container helpers ─────────────────────────────────

    private string GetScrollContainerClass() =>
        ScrollMode == DataTableScrollMode.Virtualized
            ? "tm-data-table-scroll tm-data-table-virtual-scroll"
            : "tm-data-table-scroll";

    private string GetScrollContainerStyle() =>
        ScrollMode == DataTableScrollMode.Virtualized
            ? $"height: {VirtualScrollHeight ?? "600px"}; overflow-y: auto;{(HasExplicitRowHeight ? $" --tm-data-table-row-height: {VirtualItemSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;" : string.Empty)}"
            : string.Empty;
}
