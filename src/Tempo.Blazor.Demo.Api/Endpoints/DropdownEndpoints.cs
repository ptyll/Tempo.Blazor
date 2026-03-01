using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class DropdownEndpoints
{
    public static void MapDropdownEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dropdowns").WithTags("Dropdowns");

        group.MapGet("/countries", (string? q, int? page, int? pageSize, MockDropdownStore store) =>
        {
            var pg = page ?? 1;
            var ps = pageSize ?? 20;

            IEnumerable<DropdownItem<string>> items = store.Countries;

            if (!string.IsNullOrWhiteSpace(q))
                items = items.Where(c => c.Label.Contains(q, StringComparison.OrdinalIgnoreCase));

            var all = items.ToList();
            var paged = all.Skip((pg - 1) * ps).Take(ps).ToList();

            return Results.Ok(DropdownDataResult<DropdownItem<string>>.WithItems(paged, all.Count));
        });

        group.MapGet("/users", (string? q, MockDropdownStore dropdownStore, MockPersonStore personStore) =>
        {
            var users = dropdownStore.GetUsers(personStore);

            if (!string.IsNullOrWhiteSpace(q))
                users = users.Where(u => u.Label.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            return Results.Ok(DropdownDataResult<DropdownItem<string>>.WithItems(
                users.Take(20).ToList(), users.Count));
        });
    }
}
