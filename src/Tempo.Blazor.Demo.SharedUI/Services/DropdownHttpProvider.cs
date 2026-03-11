using System.Net.Http.Json;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Services;

public class DropdownHttpProvider : IDropdownDataProvider<DropdownItem<string>>
{
    private readonly HttpClient _http;
    private readonly string _endpoint;

    public DropdownHttpProvider(IHttpClientFactory factory, string endpoint)
    {
        _http = factory.CreateClient("DemoApi");
        _endpoint = endpoint;
    }

    public async Task<DropdownDataResult<DropdownItem<string>>> GetItemsAsync(
        DropdownSearchRequest request, CancellationToken ct = default)
    {
        var url = $"/api/dropdowns/{_endpoint}?q={Uri.EscapeDataString(request.SearchText)}" +
                  $"&page={request.Page}&pageSize={request.PageSize}";

        return await _http.GetFromJsonAsync<DropdownDataResult<DropdownItem<string>>>(url, ct)
               ?? DropdownDataResult<DropdownItem<string>>.Empty();
    }
}
