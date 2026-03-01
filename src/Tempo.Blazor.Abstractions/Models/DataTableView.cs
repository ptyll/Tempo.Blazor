namespace Tempo.Blazor.Models;

/// <summary>
/// Scope of the view - personal (user-only) or tenant (shared).
/// </summary>
public enum ViewScope
{
    /// <summary>Personal view visible only to the creator.</summary>
    Personal,
    
    /// <summary>Tenant-wide view visible to all users in the tenant.</summary>
    Tenant
}

/// <summary>
/// Represents a saved DataTable view configuration (column visibility, sort, filters).
/// Persisted via IDataTableViewProvider.
/// </summary>
public sealed class DataTableView
{
    /// <summary>Unique identifier. Null for unsaved/new views.</summary>
    public string? Id { get; set; }

    /// <summary>Display name for this view.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this is the default view loaded on first render.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Visible column field names in display order.</summary>
    public IList<string> VisibleColumns { get; set; } = new List<string>();

    /// <summary>Field name to sort by. Null = no sort.</summary>
    public string? SortField { get; set; }

    /// <summary>Sort direction. True = ascending, false = descending.</summary>
    public bool SortAscending { get; set; } = true;
    
    /// <summary>Sort column name (alternative to SortField).</summary>
    public string? SortColumn 
    { 
        get => SortField; 
        set => SortField = value; 
    }
    
    /// <summary>Sort direction as string: "asc" or "desc".</summary>
    public string SortDirection 
    { 
        get => SortAscending ? "asc" : "desc"; 
        set => SortAscending = value?.ToLowerInvariant() != "desc"; 
    }

    /// <summary>Active filter state serialized as key-value pairs (legacy).</summary>
    public IDictionary<string, string?> FiltersLegacy { get; set; } = new Dictionary<string, string?>();
    
    /// <summary>Active filters with full configuration.</summary>
    public IList<FilterConfig> Filters { get; set; } = new List<FilterConfig>();

    /// <summary>Page size preference for this view.</summary>
    public int? PageSize { get; set; }

    /// <summary>When this view was created.</summary>
    public DateTime? CreatedAt { get; set; }
    
    /// <summary>When this view was last modified.</summary>
    public DateTime? ModifiedAt { get; set; }
    
    /// <summary>When this view was created or last modified (legacy).</summary>
    public DateTime? UpdatedAt 
    { 
        get => ModifiedAt ?? CreatedAt; 
        set => ModifiedAt = value; 
    }
    
    /// <summary>Scope of the view - personal or tenant-wide.</summary>
    public ViewScope Scope { get; set; } = ViewScope.Personal;
    
    /// <summary>User ID who created this view.</summary>
    public string CreatedBy { get; set; } = string.Empty;
    
    /// <summary>Tenant ID this view belongs to.</summary>
    public string? TenantId { get; set; }
}

/// <summary>
/// Configuration for a single filter in a view.
/// </summary>
public sealed class FilterConfig
{
    /// <summary>Field name being filtered.</summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>Filter operator (equals, contains, greaterThan, etc.).</summary>
    public string Operator { get; set; } = "equals";
    
    /// <summary>Filter value.</summary>
    public string Value { get; set; } = string.Empty;
}
