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
            IEnumerable<PersonDto> query = store.Persons;

            // Parse filters from query string manually
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

            var items = query.ToList();
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

        group.MapGet("/{id}", (string id, MockPersonStore store) =>
            store.Persons.FirstOrDefault(p => p.Id == id) is { } person
                ? Results.Ok(person)
                : Results.NotFound());

        group.MapGet("/departments", (MockPersonStore store) =>
            Results.Ok(store.Persons.Select(p => p.Department).Distinct().Order().ToList()));
    }
}
