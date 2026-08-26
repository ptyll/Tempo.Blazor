using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Components.DataTable;

/// <summary>
/// A fully-featured data table component with support for sorting, filtering, pagination,
/// selection, grouping, and view management. Supports both client-side and server-side data.
/// </summary>
public partial class TmDataTable<TItem> : IDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

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

    /// <summary>Whether column header controls (pin toggle, resize handle) are shown. Default true.</summary>
    [Parameter] public bool ShowColumnMenu { get; set; } = true;

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
        if (!Editable || !AnyEditableColumn) return;
        if (_editContext is not null && _editingRowIndex != rowIndex)
        {
            await CancelEditAsync();
        }

        SetEditingRow(rowIndex, item);
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

    private Task HandleEditKeyDownAsync(KeyboardEventArgs e)
        => e.Key switch
        {
            "Escape" => CancelEditAsync(),
            "Enter" => CommitEditAsync(),
            _ => Task.CompletedTask
        };

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
    /// Materializes the full result set for the current filter/sort (all pages, paging bypassed)
    /// as an export snapshot of the visible columns.
    /// </summary>
    public async Task<DataTableExportData> BuildExportDataAsync()
    {
        var rows = await GatherAllRowsAsync();
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
    public async Task ExportAsync(IDataTableExporter exporter, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        var data = await BuildExportDataAsync();
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
    public async Task ExportAsync(DataTableExportFormat format)
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
            var data = await BuildExportDataAsync();
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
            await OnExportCompleted.InvokeAsync(new DataTableExportResult(format, fileName, data.Rows.Count));
        }
        finally
        {
            _isExporting = false;
            StateHasChanged();
        }
    }

    private Task HandleExportSelectionAsync(string format) => format switch
    {
        "csv" => ExportAsync(DataTableExportFormat.Csv),
        "xlsx" => ExportAsync(DataTableExportFormat.Xlsx),
        _ => Task.CompletedTask
    };
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
    private readonly HashSet<TItem> _selectedItems = new();

    // ── Filtering / Search ───────────────────────────────────────
    private readonly Dictionary<string, DataTableFilter> _activeFilters = new();
    private string _searchText = string.Empty;
    private string? _lastSearchTextParam;
    private List<ActiveFilter> _externalFilters = [];
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
        CardMode ? "tm-data-table-wrapper tm-data-table-wrapper--card" : "tm-data-table-wrapper";

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

    /// <summary>Current paging state as handed to <see cref="PaginationInfoTemplate"/>.</summary>
    private DataTablePaginationInfo CurrentPaginationInfo => new(
        CurrentPage: _currentPage,
        TotalPages: _totalPages,
        PageSize: _pageSize,
        TotalCount: _totalCount,
        StartItem: _totalCount == 0 ? 0 : ((_currentPage - 1) * _pageSize) + 1,
        EndItem: Math.Min(_currentPage * _pageSize, _totalCount));

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

    /// <summary>Height of a single row in pixels (required for Virtualize). Default: 48.</summary>
    [Parameter] public float VirtualItemSize { get; set; } = 48f;

    /// <summary>Number of extra items to render above/below viewport. Default: 3.</summary>
    [Parameter] public int VirtualOverscanCount { get; set; } = 3;

    /// <summary>Fixed height for the virtualized scroll container (e.g. "600px", "80vh").</summary>
    [Parameter] public string? VirtualScrollHeight { get; set; }

    // ── Parameters: grouping ────────────────────────────────────────

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
    private bool IsSelected(TItem item) => _selectedItems.Contains(item);
    private int ColSpan => Math.Max(1, (HasDetail ? 1 : 0) + (Selectable ? 1 : 0) + _visibleColumns.Count + (HasEditActions ? 1 : 0));
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
        ShouldRenderExport();

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

    /// <summary>True when the external filter builder should be rendered.</summary>
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
            RefreshClientItems();
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

    private void RecomputeSticky()
    {
        _stickyLeft.Clear();
        _stickyRight.Clear();

        // Left-pinned columns stack from the container edge. Leading utility columns (expander,
        // checkbox) are not sticky, so their width is not reserved — a pinned column simply sticks
        // over them once they scroll away.
        var leftOffset = 0;
        foreach (var col in _visibleColumns.Where(c => GetPin(c) == ColumnPin.Left))
        {
            _stickyLeft[col.Key] = leftOffset;
            leftOffset += GetPixelWidth(col) ?? DefaultPinnedWidth;
        }

        var rightOffset = 0;
        foreach (var col in _visibleColumns.Where(c => GetPin(c) == ColumnPin.Right).Reverse())
        {
            _stickyRight[col.Key] = rightOffset;
            rightOffset += GetPixelWidth(col) ?? DefaultPinnedWidth;
        }
    }

    private string GetColumnStyle(TmDataTableColumn<TItem> col)
    {
        var parts = new List<string>();
        var px = GetPixelWidth(col);
        if (px.HasValue) parts.Add($"width:{px}px");
        else if (!string.IsNullOrEmpty(col.Width)) parts.Add($"width:{col.Width}");
        if (!string.IsNullOrEmpty(col.MinWidth)) parts.Add($"min-width:{col.MinWidth}");

        switch (GetPin(col))
        {
            case ColumnPin.Left when _stickyLeft.TryGetValue(col.Key, out var l):
                parts.Add("position:sticky");
                parts.Add($"left:{l}px");
                break;
            case ColumnPin.Right when _stickyRight.TryGetValue(col.Key, out var r):
                parts.Add("position:sticky");
                parts.Add($"right:{r}px");
                break;
        }

        return string.Join(";", parts);
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

    private async Task StartColumnResizeAsync(TmDataTableColumn<TItem> col, PointerEventArgs e)
    {
        var startWidth = GetPixelWidth(col) ?? DefaultPinnedWidth;
        _dotNetRef ??= DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("tmDataTable.startColumnResize", _dotNetRef, col.Key, e.ClientX, startWidth, MinColumnWidth);
        }
        catch { /* JS unavailable (e.g. tests) — resize is a no-op */ }
    }

    /// <summary>Receives live width updates from the JavaScript resize drag (no persistence).</summary>
    [JSInvokable]
    public void OnColumnResized(string columnKey, double width)
    {
        _columnWidths[columnKey] = Math.Max(MinColumnWidth, (int)Math.Round(width));
        RecomputeSticky();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Receives the resize-drag end signal from JavaScript and persists the layout.</summary>
    [JSInvokable]
    public Task OnColumnResizeCommitted() => PersistLayoutAsync();

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
        _dotNetRef?.Dispose();
        _dotNetRef = null;
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
            _displayedItems = list;
            _totalPages = 0;
        }
        else if (!ShowPagination)
        {
            // The pager is the only element that reaches pages 2..N, so slicing is derived from it:
            // with no pager the remaining rows would be in no element at all, and nothing in the UI
            // would say so. ShowPagination=false therefore means "do not slice", not "slice silently".
            _displayedItems = list;
            _totalPages = 0;
        }
        else
        {
            _totalPages = _pageSize > 0 ? (int)Math.Ceiling((double)_totalCount / _pageSize) : 0;

            if (_currentPage > _totalPages && _totalPages > 0)
                _currentPage = _totalPages;
            else if (_currentPage < 1)
                _currentPage = 1;

            _displayedItems = list.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
        }

        // Refresh grouping if active
        if (_groupByColumns.Count > 0)
            RefreshGroupedData();
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
                    _displayedItems = [];
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
            _displayedItems = result.Items.ToList();
            _totalCount = result.TotalCount;
            _currentPage = result.Page;
            _pageSize = result.PageSize;
            _totalPages = result.TotalPages;
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

    private async Task ApplyFilterAsync(string columnKey, DataTableFilter? filter)
    {
        if (filter is null)
            _activeFilters.Remove(columnKey);
        else
            _activeFilters[columnKey] = filter;

        _currentPage = 1;
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
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
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
        if (isChecked) _selectedItems.Add(item);
        else _selectedItems.Remove(item);
        await OnSelectionChanged.InvokeAsync(_selectedItems.ToList());
    }

    private async Task ToggleSelectAllAsync(ChangeEventArgs e)
    {
        var isChecked = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        if (isChecked)
            foreach (var item in _displayedItems) _selectedItems.Add(item);
        else
            _selectedItems.Clear();
        await OnSelectionChanged.InvokeAsync(_selectedItems.ToList());
    }

    private async Task DeselectAllAsync()
    {
        _selectedItems.Clear();
        await OnSelectionChanged.InvokeAsync(_selectedItems.ToList());
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
        if (!_expandedGroups.Remove(id))
            _expandedGroups.Add(id);
        StateHasChanged();
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

    private RenderFragment RenderGroupRows(IReadOnlyList<DataGroup<TItem>> groups, int level) => builder =>
    {
        var seq = 0;
        foreach (var group in groups)
        {
            var g = group;
            var expanded = IsGroupExpanded(g);

            // Group header row
            builder.OpenElement(seq++, "tr");
            builder.AddAttribute(seq++, "class", $"tm-data-table-group-row tm-data-table-group-level-{level}");
            builder.OpenElement(seq++, "td");
            builder.AddAttribute(seq++, "colspan", ColSpan);

            // Toggle button
            builder.OpenElement(seq++, "button");
            builder.AddAttribute(seq++, "type", "button");
            builder.AddAttribute(seq++, "class", "tm-data-table-group-toggle");
            builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => ToggleGroupExpansion(g)));
            builder.AddContent(seq++, expanded ? "▼" : "▶");
            builder.CloseElement(); // button

            // Group label
            builder.OpenElement(seq++, "span");
            builder.AddAttribute(seq++, "class", "tm-data-table-group-label");
            builder.AddContent(seq++, g.DisplayValue);
            builder.CloseElement(); // span

            // Count
            builder.OpenElement(seq++, "span");
            builder.AddAttribute(seq++, "class", "tm-data-table-group-count");
            builder.AddContent(seq++, $"({g.Count})");
            builder.CloseElement(); // span

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
                        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => HandleRowClickAsync(rowItem)));
                        builder.AddMultipleAttributes(seq++, GetRowAttributes(rowItem));

                        if (HasDetail)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", "tm-col-expander");
                            builder.CloseElement();
                        }

                        if (Selectable)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", "tm-col-check");
                            builder.OpenElement(seq++, "input");
                            builder.AddAttribute(seq++, "type", "checkbox");
                            builder.AddAttribute(seq++, "aria-label", Loc["TmDataTable_SelectRow"]);
                            builder.AddAttribute(seq++, "checked", IsSelected(rowItem));
                            builder.AddAttribute(seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => ToggleRowSelectionAsync(rowItem, e)));
                            builder.CloseElement(); // input
                            builder.CloseElement(); // td
                        }

                        foreach (var col in _visibleColumns)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", GetCellClass(col));
                            builder.AddAttribute(seq++, "style", GetColumnStyle(col));
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

    private Task HandleRowKeyDownAsync(KeyboardEventArgs e, TItem item)
    {
        if (e.Key is "Enter" or " ")
            return HandleRowClickAsync(item);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Keyboard contract of a column header: Enter sorts (Shift mirrors the multi-sort modifier of the
    /// click) and P cycles the pin, so neither is mouse-only (WCAG 2.1.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Enter only — Space deliberately does not sort.</b> The header is a plain
    /// <c>&lt;th tabindex="0"&gt;</c>, not a <c>&lt;button&gt;</c>, so Space keeps its browser meaning
    /// there: scroll down one screen. Accepting Space as well meant every press did BOTH — the table
    /// re-sorted and the page jumped a screen down, away from the result the user had just asked for.
    /// </para>
    /// <para>
    /// The scroll cannot be suppressed from here. Blazor's <c>@onkeydown:preventDefault</c> is evaluated
    /// when the handler is REGISTERED, not per event, so it cannot be made conditional on the key: a
    /// static <c>true</c> would cancel the default action of every keydown on the header, including
    /// <b>Tab</b>, turning a cosmetic annoyance into a keyboard trap (WCAG 2.1.2). Suppressing it from
    /// JavaScript would make sorting depend on an interop module the consumer has to reference, which a
    /// keyboard path must not.
    /// </para>
    /// <para>
    /// Enter alone is also the conventional answer: Space is the activation key of a <c>button</c>, while
    /// this element is a <c>columnheader</c> inside a <c>grid</c>, where Space carries no activation
    /// meaning. WCAG 2.1.1 is satisfied by Enter, and a keyboard user keeps paging through the table with
    /// the key that has always done it.
    /// </para>
    /// <para>
    /// A key pressed inside a consumer's <c>HeaderTemplate</c> never reaches this method: the template is
    /// wrapped in a barrier that stops keydown from bubbling (see <c>TmDataTable.razor</c>). Reaching the
    /// shortcut from there was WCAG 2.1.4 — typing into a filter box in the header pinned the column.
    /// </para>
    /// <para>
    /// <b>P cycles the column pin</b>, added in 2.8.22 together with <c>tabindex="-1"</c> on the pin
    /// button. That button was a real <c>&lt;button&gt;</c> inside every visible header, so six columns
    /// cost eleven Tab presses and five of those stops painted nothing until hover; taking it out of the
    /// sequential order WITHOUT giving the header a key would have made pinning unreachable rather than
    /// cheaper. Pinning does not need a sortable column, so P is answered on any header while
    /// <c>ShowColumnMenu</c> is on, and <c>aria-keyshortcuts</c> announces it.
    /// </para>
    /// </remarks>
    private Task HandleHeaderKeyDownAsync(KeyboardEventArgs e, TmDataTableColumn<TItem> col)
    {
        if (ShowColumnMenu && (e.Key is "p" or "P"))
        {
            return CyclePinAsync(col);
        }

        if (!col.Sortable) return Task.CompletedTask;
        if (e.Key is not "Enter") return Task.CompletedTask;
        return SortByAsync(col, e.ShiftKey);
    }

    /// <summary>
    /// Whether the header offers anything a keyboard user can do — and therefore whether it belongs in
    /// the focus order at all. Sorting is one such thing; since 2.8.22 the pin is the other, because the
    /// pin button itself is no longer a stop.
    /// </summary>
    private bool IsHeaderOperable(TmDataTableColumn<TItem> col) => col.Sortable || ShowColumnMenu;

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
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(col.CssClass)) parts.Add(col.CssClass);
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
    /// </remarks>
    private string? GetAriaSortValue(TmDataTableColumn<TItem> col)
    {
        if (!col.Sortable) return null;
        var sort = GetColumnSort(col.Key);
        if (sort is null) return "none";
        return sort.Direction == DataTableSortDirection.Descending ? "descending" : "ascending";
    }

    private string GetRowClass(TItem item) => IsSelected(item) ? "tm-row-selected" : string.Empty;

    // ── Scroll container helpers ─────────────────────────────────

    private string GetScrollContainerClass() =>
        ScrollMode == DataTableScrollMode.Virtualized
            ? "tm-data-table-scroll tm-data-table-virtual-scroll"
            : "tm-data-table-scroll";

    private string GetScrollContainerStyle() =>
        ScrollMode == DataTableScrollMode.Virtualized
            ? $"height: {VirtualScrollHeight ?? "600px"}; overflow-y: auto;"
            : string.Empty;
}
