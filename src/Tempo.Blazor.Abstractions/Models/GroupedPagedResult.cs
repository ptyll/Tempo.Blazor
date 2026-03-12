namespace Tempo.Blazor.Models;

/// <summary>
/// Represents server-side grouped data with per-group pagination.
/// Returned by <see cref="Interfaces.IDataTableDataProvider{TItem}.GetGroupedDataAsync"/>.
/// </summary>
public class GroupedPagedResult<T>
{
    /// <summary>Top-level groups (may contain nested <see cref="DataGroup{TItem}.SubGroups"/> for multi-level grouping).</summary>
    public IReadOnlyList<DataGroup<T>> Groups { get; init; } = [];

    /// <summary>Total number of items across all groups (after filtering, before grouping).</summary>
    public int TotalCount { get; init; }

    /// <summary>Column keys used for grouping, in order.</summary>
    public IReadOnlyList<string> GroupByColumns { get; init; } = [];

    /// <summary>
    /// Per-group pagination metadata. Key = group key (e.g. "Engineering"), value = paging info.
    /// Null when the server returns all items in each group.
    /// </summary>
    public IReadOnlyDictionary<string, GroupPagination>? GroupPaging { get; init; }
}

/// <summary>
/// Pagination metadata for items within a single group.
/// </summary>
public class GroupPagination
{
    /// <summary>1-based page number within this group.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size within this group.</summary>
    public int PageSize { get; init; }

    /// <summary>Total items in this group (across all pages).</summary>
    public int TotalCount { get; init; }

    /// <summary>Total pages within this group.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>True when a previous page exists within this group.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>True when a next page exists within this group.</summary>
    public bool HasNextPage => Page < TotalPages;
}
