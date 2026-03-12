using Tempo.Blazor.Models;

namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Server-side (or async) data provider for TmDataTable.
/// Implement this interface to power sorting, filtering, and pagination from an API or database.
/// For in-memory scenarios, use InMemoryDataProvider&lt;TItem&gt; from Tempo.Blazor.
/// </summary>
public interface IDataTableDataProvider<TItem>
{
    /// <summary>Fetches a page of data applying the given query (sort, filter, search, page).</summary>
    Task<PagedResult<TItem>> GetDataAsync(DataTableQuery query, CancellationToken ct = default);

    /// <summary>
    /// Fetches grouped data from the server with per-group pagination.
    /// Called when <see cref="DataTableQuery.GroupByColumns"/> is non-empty.
    /// Default implementation returns <c>null</c>, which causes the component to fall back
    /// to client-side grouping via <see cref="GetDataAsync"/>.
    /// </summary>
    Task<GroupedPagedResult<TItem>?> GetGroupedDataAsync(DataTableQuery query, CancellationToken ct = default)
        => Task.FromResult<GroupedPagedResult<TItem>?>(null);
}
