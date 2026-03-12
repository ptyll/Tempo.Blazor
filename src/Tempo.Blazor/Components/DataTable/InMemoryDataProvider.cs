using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.DataTable;

/// <summary>
/// Client-side IDataTableDataProvider implementation that sorts, filters, searches,
/// and paginates an in-memory list.
/// </summary>
public sealed class InMemoryDataProvider<TItem> : IDataTableDataProvider<TItem>
{
    private readonly IReadOnlyList<TItem> _source;
    private readonly IReadOnlyDictionary<string, Func<TItem, object?>>? _accessors;

    /// <param name="source">The full in-memory data set.</param>
    /// <param name="accessors">Optional named field accessors used for sort/filter/search.
    /// Key = column key (PropertyName or Title). If null, only pagination is supported.</param>
    public InMemoryDataProvider(
        IReadOnlyList<TItem> source,
        IReadOnlyDictionary<string, Func<TItem, object?>>? accessors = null)
    {
        _source = source;
        _accessors = accessors;
    }

    public Task<PagedResult<TItem>> GetDataAsync(DataTableQuery query, CancellationToken ct = default)
    {
        IEnumerable<TItem> items = _source;

        // 1. Apply column filters
        foreach (var filter in query.Filters)
        {
            if (_accessors != null && _accessors.TryGetValue(filter.Column, out var accessor))
            {
                items = ApplyFilter(items, accessor, filter);
            }
        }

        // 2. Apply global search text across all registered accessors
        if (!string.IsNullOrWhiteSpace(query.SearchText) && _accessors != null)
        {
            var search = query.SearchText.Trim();
            items = items.Where(item =>
                _accessors.Values.Any(acc =>
                    acc(item)?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }

        // 3. Apply sort
        if (!string.IsNullOrEmpty(query.SortColumn)
            && _accessors != null
            && _accessors.TryGetValue(query.SortColumn, out var sortAccessor))
        {
            items = query.SortDescending
                ? items.OrderByDescending(x => sortAccessor(x))
                : items.OrderBy(x => sortAccessor(x));
        }

        // 4. Materialise for count
        var list = items.ToList();
        var totalCount = list.Count;

        // 5. Paginate
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Max(1, query.PageSize);
        var pageItems = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var result = new PagedResult<TItem>
        {
            Items = pageItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };

        return Task.FromResult(result);
    }

    private static IEnumerable<TItem> ApplyFilter(
        IEnumerable<TItem> items,
        Func<TItem, object?> accessor,
        DataTableFilter filter)
    {
        var op = filter.Operator.ToLowerInvariant();
        var filterValue = filter.Value?.ToString() ?? string.Empty;

        return op switch
        {
            "contains" => items.Where(x =>
                accessor(x)?.ToString()?.Contains(filterValue, StringComparison.OrdinalIgnoreCase) == true),
            "notcontains" => items.Where(x =>
                accessor(x)?.ToString()?.Contains(filterValue, StringComparison.OrdinalIgnoreCase) != true),
            "equals" or "eq" => items.Where(x =>
                string.Equals(accessor(x)?.ToString(), filterValue, StringComparison.OrdinalIgnoreCase)),
            "notequals" => items.Where(x =>
                !string.Equals(accessor(x)?.ToString(), filterValue, StringComparison.OrdinalIgnoreCase)),
            "startswith" => items.Where(x =>
                accessor(x)?.ToString()?.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase) == true),
            "endswith" => items.Where(x =>
                accessor(x)?.ToString()?.EndsWith(filterValue, StringComparison.OrdinalIgnoreCase) == true),
            "gt" or "greaterthan" => items.Where(x => CompareValues(accessor(x), filter.Value) > 0),
            "lt" or "lessthan" => items.Where(x => CompareValues(accessor(x), filter.Value) < 0),
            "gte" or "greaterorequal" or "greaterthanorequal" => items.Where(x => CompareValues(accessor(x), filter.Value) >= 0),
            "lte" or "lessorequal" or "lessthanorequal" => items.Where(x => CompareValues(accessor(x), filter.Value) <= 0),
            "isempty" => items.Where(x => string.IsNullOrEmpty(accessor(x)?.ToString())),
            "isnotempty" => items.Where(x => !string.IsNullOrEmpty(accessor(x)?.ToString())),
            _ => items
        };
    }

    private static int CompareValues(object? a, object? b)
    {
        if (a is IComparable ca && b != null)
        {
            try { return ca.CompareTo(Convert.ChangeType(b, a.GetType())); }
            catch { /* ignore type mismatch */ }
        }
        return 0;
    }
}
