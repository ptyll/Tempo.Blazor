using System.Net.Http.Json;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Services;

public class ActivityHttpService
{
    private readonly HttpClient _http;

    public ActivityHttpService(IHttpClientFactory factory)
        => _http = factory.CreateClient("DemoApi");

    public async Task<IReadOnlyList<TimelineEntryDto>> GetTimelineAsync(string entityId, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<TimelineEntryDto>>($"/api/activity/{entityId}/timeline", ct)
           ?? [];

    public async Task<IReadOnlyList<CommentDto>> GetCommentsAsync(string entityId, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<CommentDto>>($"/api/activity/{entityId}/comments", ct)
           ?? [];

    public async Task<CommentDto?> AddCommentAsync(string entityId, string htmlContent, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/activity/{entityId}/comments",
            new { htmlContent },
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CommentDto>(ct);
    }

    public async Task UpdateCommentAsync(string entityId, string commentId, string htmlContent, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"/api/activity/{entityId}/comments/{commentId}",
            new { htmlContent },
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteCommentAsync(string entityId, string commentId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/activity/{entityId}/comments/{commentId}", ct);
        response.EnsureSuccessStatusCode();
    }
}
