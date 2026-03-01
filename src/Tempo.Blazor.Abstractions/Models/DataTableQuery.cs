namespace Tempo.Blazor.Models;

/// <summary>
/// Encapsulates all parameters for a DataTable data fetch: paging, sorting, filtering, and search.
/// </summary>
public class DataTableQuery
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Number of items per page.</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>Column key (PropertyName or Title) to sort by. Null = no sort.</summary>
    public string? SortColumn { get; init; }

    /// <summary>True = sort descending; false = sort ascending.</summary>
    public bool SortDescending { get; init; }

    /// <summary>Column filters to apply.</summary>
    public IReadOnlyList<DataTableFilter> Filters { get; init; } = [];

    /// <summary>Global search text applied across all searchable columns.</summary>
    public string? SearchText { get; init; }
}

/// <summary>Represents a single column filter predicate.</summary>
public record DataTableFilter(string Column, string Operator, object? Value);
