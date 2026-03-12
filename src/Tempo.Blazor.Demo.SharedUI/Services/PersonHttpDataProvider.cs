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
        var url = BuildBaseUrl(query,
            $"/api/persons?page={query.Page}&pageSize={query.PageSize}");

        return await _http.GetFromJsonAsync<PagedResult<PersonDto>>(url, ct) ?? new();
    }

    public async Task<GroupedPagedResult<PersonDto>?> GetGroupedDataAsync(DataTableQuery query, CancellationToken ct = default)
    {
        if (query.GroupByColumns.Count == 0)
            return null;

        var url = BuildBaseUrl(query,
            $"/api/persons/grouped?groupPageSize={query.PageSize}");

        // Add groupBy columns
        foreach (var col in query.GroupByColumns)
            url += $"&groupBy={Uri.EscapeDataString(col)}";

        // Add per-group page requests
        if (query.GroupPageRequests is { Count: > 0 })
        {
            foreach (var (key, page) in query.GroupPageRequests)
                url += $"&groupPage[{Uri.EscapeDataString(key)}]={page}";
        }

        return await _http.GetFromJsonAsync<GroupedPagedResult<PersonDto>>(url, ct);
    }

    private static string BuildBaseUrl(DataTableQuery query, string baseUrl)
    {
        var url = baseUrl +
                  $"&sortColumn={Uri.EscapeDataString(query.SortColumn ?? "")}" +
                  $"&sortDescending={query.SortDescending}" +
                  $"&searchText={Uri.EscapeDataString(query.SearchText ?? "")}";

        // Add filters
        if (query.Filters?.Count > 0)
        {
            foreach (var filter in query.Filters)
            {
                url += $"&filterColumn={Uri.EscapeDataString(filter.Column)}" +
                       $"&filterOperator={Uri.EscapeDataString(filter.Operator)}" +
                       $"&filterValue={Uri.EscapeDataString(filter.Value?.ToString() ?? "")}";
            }
        }

        return url;
    }
}
