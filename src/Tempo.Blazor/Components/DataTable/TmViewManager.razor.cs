using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.DataTable;

public partial class TmViewManager : ComponentBase
{
    /// <summary>Data provider for views.</summary>
    [Parameter, EditorRequired] public IDataTableViewProvider Provider { get; set; } = default!;

    /// <summary>
    /// Unique identifier for this table instance. 
    /// Used to scope saved views to specific tables (e.g., "employees", "projects").
    /// </summary>
    [Parameter, EditorRequired] public string ViewContext { get; set; } = default!;

    /// <summary>Callback when a view is applied.</summary>
    [Parameter] public EventCallback<DataTableView> OnViewApplied { get; set; }

    /// <summary>Callback to get current view state.</summary>
    [Parameter] public Func<DataTableView>? GetCurrentView { get; set; }

    /// <summary>Callback to get current active filters.</summary>
    [Parameter] public Func<List<ActiveFilter>>? GetCurrentFilters { get; set; }

    /// <summary>Callback when creating a new view.</summary>
    [Parameter] public EventCallback<DataTableView> OnCreateView { get; set; }

    /// <summary>Available columns for selection.</summary>
    [Parameter] public List<ViewColumnInfo> AvailableColumns { get; set; } = [];

    /// <summary>Filter definitions for the filter builder.</summary>
    [Parameter] public List<FilterDefinition> FilterDefinitions { get; set; } = [];

    /// <summary>Available columns for grouping selection in the view modal.</summary>
    [Parameter] public List<ViewColumnInfo> AvailableGroupableColumns { get; set; } = [];

    /// <summary>
    /// Universal display resolver for field labels and filter values.
    /// Passed from parent TmDataTable/TmMultiViewList — flows to FilterBuilder.
    /// </summary>
    [Parameter] public Func<string, string?, string?>? DisplayResolver { get; set; }

    /// <summary>Whether the user can create tenant-wide views.</summary>
    [Parameter] public bool CanCreateTenantViews { get; set; } = true;

    /// <summary>Current user's identifier for personal views.</summary>
    [Parameter] public string? CurrentUserId { get; set; }

    /// <summary>Current tenant identifier.</summary>
    [Parameter] public string? CurrentTenantId { get; set; }

    private bool _isOpen;
    private bool _showModal;
    private List<DataTableView> _views = [];
    private DataTableView? _editingView;
    private DataTableView? _defaultView;
    private DataTableView? _activeView;

    // Modal form fields
    private string _viewName = "";
    private ViewScope _viewScope = ViewScope.Personal;
    private List<string> _selectedColumns = [];
    private List<string> _selectedGroupColumns = [];
    private List<ActiveFilter> _viewFilters = [];
    private string? _errorMessage;

    private bool _dataLoaded;

    protected override async Task OnInitializedAsync()
    {
        if (_dataLoaded) return;
        await LoadViewsAsync();
        _dataLoaded = true;
    }

    private async Task LoadViewsAsync()
    {
        try
        {
            _views = (await Provider.GetViewsAsync(ViewContext, CurrentTenantId, CurrentUserId)).ToList();
            _defaultView = await Provider.GetDefaultViewAsync(ViewContext, CurrentTenantId, CurrentUserId);
        }
        catch
        {
            _views = [];
        }
    }

    private async Task ToggleDropdownAsync()
    {
        _isOpen = !_isOpen;
        if (_isOpen)
        {
            // Refresh views when opening dropdown
            await LoadViewsAsync();
            StateHasChanged();
        }
    }

    private void CloseDropdown()
    {
        _isOpen = false;
    }

    private async Task ApplyViewAsync(DataTableView view)
    {
        _activeView = view;
        CloseDropdown();
        try
        {
            await OnViewApplied.InvokeAsync(view);
        }
        catch
        {
            // Silently ignore apply failures
        }
    }

    private async Task DeleteViewAsync(DataTableView view)
    {
        var canDelete = view.Scope == ViewScope.Personal
            ? view.CreatedBy == CurrentUserId
            : CanCreateTenantViews;

        if (!canDelete) return;

        try
        {
            _errorMessage = null;
            if (view.Id != null)
            {
                await Provider.DeleteViewAsync(ViewContext, view.Id);
            }
            await LoadViewsAsync();
        }
        catch
        {
            _errorMessage = Loc["TmViewManager_DeleteError"];
        }
    }

    private void OpenCreateModal()
    {
        _editingView = null;
        _viewName = "";
        _viewScope = ViewScope.Personal;
        _selectedColumns = AvailableColumns.Where(c => c.Visible).Select(c => c.Key).ToList();
        _selectedGroupColumns = [];
        // Use current filters if available
        _viewFilters = GetCurrentFilters?.Invoke() ?? [];
        _showModal = true;
        _isOpen = false;
    }

    private void OpenEditModal(DataTableView view)
    {
        _editingView = view;
        _viewName = view.Name;
        _viewScope = view.Scope;
        _selectedColumns = view.VisibleColumns?.ToList() ?? [];
        _selectedGroupColumns = view.GroupByColumns?.ToList() ?? [];
        _viewFilters = view.Filters?.Select(ResolveActiveFilter).ToList() ?? [];
        _showModal = true;
        _isOpen = false;
    }

    private void CloseModal()
    {
        _showModal = false;
        _editingView = null;
    }

    private void ToggleColumn(string columnKey, bool isChecked)
    {
        if (isChecked)
        {
            if (!_selectedColumns.Contains(columnKey))
                _selectedColumns.Add(columnKey);
        }
        else
        {
            _selectedColumns.Remove(columnKey);
        }
    }

    private void ToggleGroupColumn(string columnKey, bool isChecked)
    {
        if (isChecked)
        {
            if (!_selectedGroupColumns.Contains(columnKey))
                _selectedGroupColumns.Add(columnKey);
        }
        else
        {
            _selectedGroupColumns.Remove(columnKey);
        }
    }

    private async Task SaveViewAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewName)) return;
        if (!_selectedColumns.Any()) return;

        try
        {
            _errorMessage = null;
            await SaveViewCoreAsync();
        }
        catch
        {
            _errorMessage = Loc["TmViewManager_SaveError"];
        }
    }

    private async Task SaveViewCoreAsync()
    {
        var view = new DataTableView
        {
            Id = _editingView?.Id ?? Guid.NewGuid().ToString(),
            Name = _viewName.Trim(),
            Scope = _viewScope,
            TenantId = _viewScope == ViewScope.Tenant ? CurrentTenantId : null,
            VisibleColumns = _selectedColumns.ToList(),
            Filters = _viewFilters.Select(f => new FilterConfig
            {
                FieldName = f.FieldName,
                Operator = f.Operator.ToString(),
                Value = f.Value?.ToString() ?? ""
            }).ToList(),
            GroupByColumns = _selectedGroupColumns.ToList(),
            SortColumn = _editingView?.SortColumn,
            SortDirection = _editingView?.SortDirection ?? "asc",
            PageSize = _editingView?.PageSize ?? 10,
            IsDefault = _editingView?.IsDefault ?? false,
            CreatedBy = CurrentUserId ?? "system",
            CreatedAt = _editingView?.CreatedAt ?? DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        await Provider.SaveViewAsync(ViewContext, view, CurrentTenantId, CurrentUserId);
        
        // If editing current active view, re-apply it to refresh data with new filters
        if (_editingView != null && _activeView?.Id == _editingView.Id)
        {
            _activeView = view;
            await OnViewApplied.InvokeAsync(view);
        }
        else if (_editingView == null)
        {
            // New view created - set as active and notify parent
            _activeView = view;
            await OnCreateView.InvokeAsync(view);
            await OnViewApplied.InvokeAsync(view);
        }

        await LoadViewsAsync();
        StateHasChanged();
        CloseModal();
    }

    private async Task CreateViewFromCurrentAsync()
    {
        if (GetCurrentView == null) return;

        var current = GetCurrentView();
        _editingView = null;
        _viewName = $"{current.Name} (Copy)";
        _viewScope = ViewScope.Personal;
        _selectedColumns = current.VisibleColumns?.ToList() ?? AvailableColumns.Where(c => c.Visible).Select(c => c.Key).ToList();
        _selectedGroupColumns = current.GroupByColumns?.ToList() ?? [];
        _viewFilters = current.Filters?.Select(ResolveActiveFilter).ToList() ?? [];
        _showModal = true;
        _isOpen = false;
    }

    private string GetScopeLabel(ViewScope scope) => scope switch
    {
        ViewScope.Tenant => "Tenant",
        _ => "Personal"
    };

    private bool CanEditView(DataTableView view) => view.Scope switch
    {
        ViewScope.Personal => view.CreatedBy == CurrentUserId,
        ViewScope.Tenant => CanCreateTenantViews,
        _ => false
    };

    private bool CanDeleteView(DataTableView view) => CanEditView(view);

    private static FilterOperator ParseOperator(string? op) => Helpers.FilterOperatorParser.Parse(op);

    private ActiveFilter ResolveActiveFilter(FilterConfig f)
    {
        var fieldLabel = DisplayResolver?.Invoke(f.FieldName, null)
            ?? FilterDefinitions.FirstOrDefault(d => d.FieldName == f.FieldName)?.FieldLabel
            ?? f.FieldName;
        var displayValue = DisplayResolver?.Invoke(f.FieldName, f.Value) ?? f.Value;
        return new ActiveFilter(f.FieldName, fieldLabel, ParseOperator(f.Operator), f.Value, displayValue);
    }
}

/// <summary>Information about an available column.</summary>
public class ViewColumnInfo
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Visible { get; set; } = true;
}
