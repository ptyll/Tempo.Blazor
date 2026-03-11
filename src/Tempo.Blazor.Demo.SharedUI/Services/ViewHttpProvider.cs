using System.Net.Http.Json;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Services;

public class ViewHttpProvider : IDataTableViewProvider
{
    private readonly HttpClient _http;

    public ViewHttpProvider(IHttpClientFactory factory)
        => _http = factory.CreateClient("DemoApi");

    public async Task<IEnumerable<DataTableView>> GetViewsAsync(
        string viewContext, 
        string? tenantId = null, 
        string? userId = null, 
        CancellationToken ct = default)
    {
        var url = $"/api/views/{Uri.EscapeDataString(viewContext)}?tenantId={tenantId}&userId={userId}";
        return await _http.GetFromJsonAsync<List<DataTableView>>(url, ct) ?? [];
    }

    public async Task<DataTableView> SaveViewAsync(
        string viewContext,
        DataTableView view, 
        string? tenantId = null, 
        string? userId = null, 
        CancellationToken ct = default)
    {
        HttpResponseMessage response;
        var baseUrl = $"/api/views/{Uri.EscapeDataString(viewContext)}";

        if (string.IsNullOrEmpty(view.Id))
            response = await _http.PostAsJsonAsync($"{baseUrl}?tenantId={tenantId}&userId={userId}", view, ct);
        else
            response = await _http.PutAsJsonAsync($"{baseUrl}/{view.Id}?tenantId={tenantId}&userId={userId}", view, ct);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DataTableView>(ct) ?? view;
    }

    public async Task DeleteViewAsync(
        string viewContext, 
        string viewId, 
        CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/views/{Uri.EscapeDataString(viewContext)}/{viewId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<DataTableView?> GetDefaultViewAsync(
        string viewContext, 
        string? tenantId = null, 
        string? userId = null, 
        CancellationToken ct = default)
    {
        var url = $"/api/views/{Uri.EscapeDataString(viewContext)}/default?tenantId={tenantId}&userId={userId}";
        return await _http.GetFromJsonAsync<DataTableView>(url, ct);
    }

    public async Task SetDefaultViewAsync(
        string viewContext, 
        string viewId, 
        string? tenantId = null, 
        string? userId = null, 
        CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"/api/views/{Uri.EscapeDataString(viewContext)}/{viewId}/default?tenantId={tenantId}&userId={userId}", 
            null, ct);
        response.EnsureSuccessStatusCode();
    }
}
