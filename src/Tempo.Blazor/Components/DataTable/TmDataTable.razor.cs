using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Components.DataTable;

public partial class TmDataTable<TItem>
{
    // ── Column registry ──────────────────────────────────────────
    private readonly List<TmDataTableColumn<TItem>> _columns = [];
    private readonly List<TmDataTableColumn<TItem>> _visibleColumns = [];
    private readonly HashSet<string> _hiddenColumns = new();

    // ── Sort state ───────────────────────────────────────────────
    private string? _sortColumn;
    private bool _sortDescending;

    // ── Pagination state ─────────────────────────────────────────
    private int _currentPage = 1;
    private int _pageSize;
    private int _totalCount;
    private int _totalPages;

    // ── Data ─────────────────────────────────────────────────────
    private List<TItem> _displayedItems = [];
    private bool _isLoading;

    // ── Selection ────────────────────────────────────────────────
    private readonly HashSet<TItem> _selectedItems = new();

    // ── Filtering / Search ───────────────────────────────────────
    private readonly Dictionary<string, DataTableFilter> _activeFilters = new();
    private string _searchText = string.Empty;
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

    /// <summary>When true, shows the column visibility picker. Default: true.</summary>
    [Parameter] public bool ShowColumnPicker { get; set; } = true;

    /// <summary>When true, shows the TmPagination bar when more than one page exists. Default: true.</summary>
    [Parameter] public bool ShowPagination { get; set; } = true;

    /// <summary>Initial page size. Default: 25.</summary>
    [Parameter] public int DefaultPageSize { get; set; } = 25;

    /// <summary>Page size options shown in the pagination dropdown.</summary>
    [Parameter] public IReadOnlyList<int> PageSizeOptions { get; set; } = [5, 10, 25, 50, 100];

    /// <summary>Optional view persistence provider. When set, shows TmViewManager.</summary>
    [Parameter] public IDataTableViewProvider? ViewProvider { get; set; }

    /// <summary>Filter definitions for the view manager filter builder.</summary>
    [Parameter] public List<FilterDefinition> ViewFilterDefinitions { get; set; } = [];

    /// <summary>When true, shows FilterBuilder for external filtering (visible when ViewProvider is set).</summary>
    [Parameter] public bool ShowExternalFilterBuilder { get; set; } = true;

    /// <summary>Filter definitions for the external filter builder (shown above table when ViewProvider is set).</summary>
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
    private int ColSpan => Math.Max(1, (Selectable ? 1 : 0) + _visibleColumns.Count);

    // ── Lifecycle ─────────────────────────────────────────────────

    private bool _dataLoaded;

    protected override async Task OnInitializedAsync()
    {
        if (_dataLoaded) return;

        _pageSize = DefaultPageSize;

        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();

        _dataLoaded = true;
    }

    protected override Task OnParametersSetAsync()
    {
        // Re-apply when Items collection reference changes in client-side mode
        if (DataProvider is null)
            RefreshClientItems();
        return Task.CompletedTask;
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
                    .OrderBy(c => c.Order));
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

        // Sort
        if (_sortColumn != null)
        {
            var sortCol = _columns.FirstOrDefault(c => c.Key == _sortColumn);
            if (sortCol?.Field != null)
            {
                items = _sortDescending
                    ? items.OrderByDescending(x => sortCol.Field(x))
                    : items.OrderBy(x => sortCol.Field(x));
            }
        }

        var list = items.ToList();
        _totalCount = list.Count;

        if (ScrollMode == DataTableScrollMode.Virtualized)
        {
            // Virtualized mode: no pagination, show all items
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

    private async Task LoadFromProviderAsync()
    {
        _isLoading = true;
        StateHasChanged();
        try
        {
            // When grouping is active, try server-side grouping first
            if (_groupByColumns.Count > 0)
            {
                var grouped = await DataProvider!.GetGroupedDataAsync(BuildQuery());
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
            _displayedItems = result.Items.ToList();
            _totalCount = result.TotalCount;
            _currentPage = result.Page;
            _pageSize = result.PageSize;
            _totalPages = result.TotalPages;
            _serverGroupPaging = null;

            // Fallback: group the server-provided items client-side
            if (_groupByColumns.Count > 0)
                RefreshGroupedData();
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
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
        Filters    = _activeFilters.Values.ToList(),
        SearchText = _searchText,
        GroupByColumns = _groupByColumns.ToList(),
        GroupPageRequests = _groupPageRequests.Count > 0
            ? new Dictionary<string, int>(_groupPageRequests)
            : null,
    };

    // ── Sort ──────────────────────────────────────────────────────

    private async Task SortByAsync(TmDataTableColumn<TItem> col)
    {
        if (!col.Sortable) return;

        var key = col.Key;
        if (_sortColumn != key)
        {
            _sortColumn = key;
            _sortDescending = false;
        }
        else if (!_sortDescending)
        {
            _sortDescending = true;
        }
        else
        {
            _sortColumn = null;
            _sortDescending = false;
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
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
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

    private async Task ChangePageSizeAsync(int size)
    {
        _pageSize = size;
        _currentPage = 1;
        if (DataProvider is not null)
            await LoadFromProviderAsync();
        else
            RefreshClientItems();
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
        _sortColumn    = view.SortField;
        _sortDescending = !view.SortAscending;
        if (view.PageSize.HasValue) _pageSize = view.PageSize.Value;

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

            if (_sortColumn != null)
            {
                var sortCol = _columns.FirstOrDefault(c => c.Key == _sortColumn);
                if (sortCol?.Field != null)
                {
                    items = _sortDescending
                        ? items.OrderByDescending(x => sortCol.Field(x))
                        : items.OrderBy(x => sortCol.Field(x));
                }
            }
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

                        if (Selectable)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", "tm-col-check");
                            builder.OpenElement(seq++, "input");
                            builder.AddAttribute(seq++, "type", "checkbox");
                            builder.AddAttribute(seq++, "checked", IsSelected(rowItem));
                            builder.AddAttribute(seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => ToggleRowSelectionAsync(rowItem, e)));
                            builder.CloseElement(); // input
                            builder.CloseElement(); // td
                        }

                        foreach (var col in _visibleColumns)
                        {
                            builder.OpenElement(seq++, "td");
                            builder.AddAttribute(seq++, "class", GetCellClass(col));
                            builder.AddAttribute(seq++, "style", GetCellStyle(col));
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

    // ── Helper methods ────────────────────────────────────────────

    private static FilterOperator ParseFilterOperator(string? op) => Helpers.FilterOperatorParser.Parse(op);

    // ── CSS helpers ───────────────────────────────────────────────

    private string GetHeaderClass(TmDataTableColumn<TItem> col)
    {
        var parts = new List<string>();
        if (col.Sortable) parts.Add("tm-col-sortable");
        if (col.Groupable) parts.Add("tm-col-groupable");
        if (col.Key == _sortColumn)
            parts.Add(_sortDescending ? "tm-col-sorted-desc" : "tm-col-sorted-asc");
        if (col.Align == ColumnAlign.Center) parts.Add("tm-text-center");
        if (col.Align == ColumnAlign.Right) parts.Add("tm-text-right");
        return string.Join(" ", parts);
    }

    private static string GetHeaderStyle(TmDataTableColumn<TItem> col)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(col.Width)) parts.Add($"width:{col.Width}");
        if (!string.IsNullOrEmpty(col.MinWidth)) parts.Add($"min-width:{col.MinWidth}");
        return string.Join(";", parts);
    }

    private static string GetCellClass(TmDataTableColumn<TItem> col)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(col.CssClass)) parts.Add(col.CssClass);
        if (col.Align == ColumnAlign.Center) parts.Add("tm-text-center");
        if (col.Align == ColumnAlign.Right) parts.Add("tm-text-right");
        return string.Join(" ", parts);
    }

    private static string GetCellStyle(TmDataTableColumn<TItem> col) =>
        !string.IsNullOrEmpty(col.Width) ? $"width:{col.Width}" : string.Empty;

    private string GetSortIconClass(TmDataTableColumn<TItem> col)
    {
        if (col.Key != _sortColumn) return "tm-sort-none";
        return _sortDescending ? "tm-sort-desc" : "tm-sort-asc";
    }

    private string GetAriaSortValue(TmDataTableColumn<TItem> col)
    {
        if (!col.Sortable) return "none";
        if (col.Key != _sortColumn) return "none";
        return _sortDescending ? "descending" : "ascending";
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
