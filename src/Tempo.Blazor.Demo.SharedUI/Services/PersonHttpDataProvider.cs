using System.Net.Http.Json;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Services;

public class PersonHttpDataProvider : IDataTableDataProvider<PersonDto>
{
    private readonly HttpClient _http;

    public PersonHttpDataProvider(IHttpClientFactory factory)
        => _http = factory.CreateClient("DemoApi");

    public async Task<PagedResult<PersonDto>> GetDataAsync(DataTableQuery query, CancellationToken ct = default)
    {
        var url = $"/api/persons?page={query.Page}&pageSize={query.PageSize}" +
                  $"&sortColumn={Uri.EscapeDataString(query.SortColumn ?? "")}" +
                  $"&sortDescending={query.SortDescending}" +
                  $"&searchText={Uri.EscapeDataString(query.SearchText ?? "")}";

        // Add filters to URL
        if (query.Filters?.Count > 0)
        {
            foreach (var filter in query.Filters)
            {
                url += $"&filterColumn={Uri.EscapeDataString(filter.Column)}" +
                       $"&filterOperator={Uri.EscapeDataString(filter.Operator)}" +
                       $"&filterValue={Uri.EscapeDataString(filter.Value?.ToString() ?? "")}";
            }
        }

        // Add groupBy columns to URL
        if (query.GroupByColumns?.Count > 0)
        {
            foreach (var col in query.GroupByColumns)
            {
                url += $"&groupBy={Uri.EscapeDataString(col)}";
            }
        }

        return await _http.GetFromJsonAsync<PagedResult<PersonDto>>(url, ct) ?? new();
    }
}
