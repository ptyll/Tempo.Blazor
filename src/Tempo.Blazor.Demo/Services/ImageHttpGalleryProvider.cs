using System.Net.Http.Json;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Demo.Services;

public class ImageHttpGalleryProvider : IImageGalleryDataProvider
{
    private readonly HttpClient _http;

    public ImageHttpGalleryProvider(IHttpClientFactory factory)
        => _http = factory.CreateClient("DemoApi");

    public async Task<IReadOnlyList<IGalleryImage>> GetImagesAsync(string? entityId = null, CancellationToken ct = default)
    {
        var id = entityId ?? "gallery";
        var result = await _http.GetFromJsonAsync<List<GalleryImageDto>>($"/api/images/{id}", ct);
        return result?.Cast<IGalleryImage>().ToList() ?? [];
    }

    public async Task<string> GetImageTicketUrlAsync(string imageId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/images/{imageId}/ticket", null, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TicketResponse>(ct);
        var baseUrl = _http.BaseAddress?.ToString().TrimEnd('/') ?? "";
        return $"{baseUrl}{result?.TicketUrl ?? ""}";
    }

    public async Task DeleteImageAsync(string imageId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/images/{imageId}", ct);
        response.EnsureSuccessStatusCode();
    }

    private sealed record TicketResponse(string TicketUrl);
}
