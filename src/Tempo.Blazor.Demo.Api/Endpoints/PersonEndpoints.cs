using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class PersonEndpoints
{
    public static void MapPersonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/persons").WithTags("Persons");

        group.MapGet("/", (
            HttpContext context,
            int page,
            int pageSize,
            string? sortColumn,
            bool sortDescending,
            string? searchText,
            MockPersonStore store) =>
        {
            var items = ApplyFiltersSearchSort(context, store.Persons, sortColumn, sortDescending, searchText);
            var totalCount = items.Count;

            var pagedItems = items
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Results.Ok(new PagedResult<PersonDto>
            {
                Items = pagedItems,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        });

        // Server-side grouped endpoint with per-group pagination
        group.MapGet("/grouped", (
            HttpContext context,
            int groupPageSize,
            string? sortColumn,
            bool sortDescending,
            string? searchText,
            MockPersonStore store) =>
        {
            var items = ApplyFiltersSearchSort(context, store.Persons, sortColumn, sortDescending, searchText);
            var totalCount = items.Count;

            var groupByColumns = context.Request.Query["groupBy"].ToList();
            var groupPages = ParseGroupPages(context);

            if (groupByColumns.Count == 0)
                return Results.BadRequest("At least one groupBy column is required.");

            var gpSize = groupPageSize > 0 ? groupPageSize : 10;

            // Build grouped result (supports multi-level)
            var groups = BuildGroups(items, groupByColumns, 0, sortColumn, sortDescending, gpSize, groupPages);

            // Build per-group pagination metadata
            var pagingDict = BuildGroupPaging(items, groupByColumns[0], gpSize, groupPages);

            return Results.Ok(new GroupedPagedResult<PersonDto>
            {
                Groups = groups,
                TotalCount = totalCount,
                GroupByColumns = groupByColumns!,
                GroupPaging = pagingDict
            });
        });

        group.MapGet("/{id}", (string id, MockPersonStore store) =>
            store.Persons.FirstOrDefault(p => p.Id == id) is { } person
                ? Results.Ok(person)
                : Results.NotFound());

        group.MapGet("/departments", (MockPersonStore store) =>
            Results.Ok(store.Persons.Select(p => p.Department).Distinct().Order().ToList()));
    }

    // ── Shared helpers ──────────────────────────────────────────────

    private static List<PersonDto> ApplyFiltersSearchSort(
        HttpContext context,
        IReadOnlyList<PersonDto> source,
        string? sortColumn,
        bool sortDescending,
        string? searchText)
    {
        IEnumerable<PersonDto> query = source;

        // Parse filters from query string
        var filterColumns = context.Request.Query["filterColumn"].ToList();
        var filterOperators = context.Request.Query["filterOperator"].ToList();
        var filterValues = context.Request.Query["filterValue"].ToList();

        for (int i = 0; i < filterColumns.Count && i < filterValues.Count; i++)
        {
            var column = filterColumns[i];
            var op = i < filterOperators.Count ? filterOperators[i] : "equals";
            var value = filterValues[i];

            if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(value))
                continue;

            query = column.ToLowerInvariant() switch
            {
                "department" => query.Where(p =>
                    op?.ToLowerInvariant() == "equals"
                        ? p.Department.Equals(value, StringComparison.OrdinalIgnoreCase)
                        : p.Department.Contains(value, StringComparison.OrdinalIgnoreCase)),
                "role" => query.Where(p =>
                    op?.ToLowerInvariant() == "equals"
                        ? p.Role.Equals(value, StringComparison.OrdinalIgnoreCase)
                        : p.Role.Contains(value, StringComparison.OrdinalIgnoreCase)),
                "isactive" => query.Where(p =>
                    bool.TryParse(value, out var isActive) && p.IsActive == isActive),
                "firstname" => query.Where(p =>
                    p.FirstName.Contains(value, StringComparison.OrdinalIgnoreCase)),
                "lastname" => query.Where(p =>
                    p.FullName.Contains(value, StringComparison.OrdinalIgnoreCase)),
                "email" => query.Where(p =>
                    p.Email.Contains(value, StringComparison.OrdinalIgnoreCase)),
                _ => query
            };
        }

        // Apply search text
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim();
            query = query.Where(p =>
                p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Department.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Apply sorting
        if (!string.IsNullOrEmpty(sortColumn))
        {
            query = (sortColumn, sortDescending) switch
            {
                ("FirstName", false) => query.OrderBy(p => p.FirstName),
                ("FirstName", true) => query.OrderByDescending(p => p.FirstName),
                ("LastName", false) => query.OrderBy(p => p.LastName),
                ("LastName", true) => query.OrderByDescending(p => p.LastName),
                ("Email", false) => query.OrderBy(p => p.Email),
                ("Email", true) => query.OrderByDescending(p => p.Email),
                ("Department", false) => query.OrderBy(p => p.Department),
                ("Department", true) => query.OrderByDescending(p => p.Department),
                ("Role", false) => query.OrderBy(p => p.Role),
                ("Role", true) => query.OrderByDescending(p => p.Role),
                ("HiredAt", false) => query.OrderBy(p => p.HiredAt),
                ("HiredAt", true) => query.OrderByDescending(p => p.HiredAt),
                _ => query
            };
        }

        return query.ToList();
    }

    private static Func<PersonDto, object?> GetFieldAccessor(string columnKey) => columnKey switch
    {
        "FirstName" => p => p.FirstName,
        "LastName" => p => p.FullName,
        "Email" => p => p.Email,
        "Department" => p => p.Department,
        "Role" => p => p.Role,
        "IsActive" => p => p.IsActive,
        "HiredAt" => p => p.HiredAt,
        _ => _ => null
    };

    /// <summary>Parse per-group page numbers from query string: groupPage[Engineering]=2</summary>
    private static Dictionary<string, int> ParseGroupPages(HttpContext context)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in context.Request.Query.Keys)
        {
            if (key.StartsWith("groupPage[", StringComparison.OrdinalIgnoreCase)
                && key.EndsWith(']'))
            {
                var groupKey = key["groupPage[".Length..^1];
                if (int.TryParse(context.Request.Query[key], out var pg))
                    result[groupKey] = pg;
            }
        }
        return result;
    }

    private static List<DataGroup<PersonDto>> BuildGroups(
        List<PersonDto> items,
        IList<string?> groupByColumns,
        int level,
        string? sortColumn,
        bool sortDescending,
        int groupPageSize,
        Dictionary<string, int> groupPages)
    {
        if (level >= groupByColumns.Count)
            return [];

        var columnKey = groupByColumns[level] ?? "";
        var accessor = GetFieldAccessor(columnKey);

        var grouped = items.GroupBy(p => accessor(p)?.ToString() ?? "").OrderBy(g => g.Key);
        var result = new List<DataGroup<PersonDto>>();

        foreach (var g in grouped)
        {
            var groupItems = g.ToList();
            var groupPage = groupPages.TryGetValue(g.Key, out var gp) ? gp : 1;

            List<PersonDto> pagedItems;
            IReadOnlyList<DataGroup<PersonDto>> subGroups = [];

            if (level + 1 < groupByColumns.Count)
            {
                // Multi-level: recurse into sub-groups, no item-level pagination at this level
                subGroups = BuildGroups(groupItems, groupByColumns, level + 1,
                    sortColumn, sortDescending, groupPageSize, groupPages);
                pagedItems = [];
            }
            else
            {
                // Leaf level: apply per-group pagination
                pagedItems = groupItems
                    .Skip((groupPage - 1) * groupPageSize)
                    .Take(groupPageSize)
                    .ToList();
            }

            result.Add(new DataGroup<PersonDto>
            {
                FieldName = columnKey,
                Key = g.Key,
                DisplayValue = g.Key,
                Count = groupItems.Count,
                Items = pagedItems,
                SubGroups = subGroups
            });
        }

        return result;
    }

    private static Dictionary<string, GroupPagination> BuildGroupPaging(
        List<PersonDto> items,
        string? firstGroupColumn,
        int groupPageSize,
        Dictionary<string, int> groupPages)
    {
        var accessor = GetFieldAccessor(firstGroupColumn ?? "");
        var grouped = items.GroupBy(p => accessor(p)?.ToString() ?? "");
        var dict = new Dictionary<string, GroupPagination>();

        foreach (var g in grouped)
        {
            var page = groupPages.TryGetValue(g.Key, out var gp) ? gp : 1;
            dict[g.Key] = new GroupPagination
            {
                Page = page,
                PageSize = groupPageSize,
                TotalCount = g.Count()
            };
        }

        return dict;
    }
}
