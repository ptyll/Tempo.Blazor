namespace Tempo.Blazor.Models;

/// <summary>
/// Represents a paginated slice of results returned by IDataTableDataProvider&lt;TItem&gt;.
/// </summary>
public class PagedResult<T>
{
    /// <summary>Items on the current page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Total number of items across all pages (after filtering).</summary>
    public int TotalCount { get; init; }

    /// <summary>The 1-based page number returned.</summary>
    public int Page { get; init; }

    /// <summary>Page size used.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>True when a previous page exists.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>True when a next page exists.</summary>
    public bool HasNextPage => Page < TotalPages;
}
