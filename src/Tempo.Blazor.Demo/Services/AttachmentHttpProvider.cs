using System.Net.Http.Json;
using Tempo.Blazor.Demo.Shared;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Services;

public class AttachmentHttpProvider : IFileAttachmentProvider
{
    private readonly HttpClient _http;

    public AttachmentHttpProvider(IHttpClientFactory factory)
        => _http = factory.CreateClient("DemoApi");

    public async Task<IReadOnlyList<IFileAttachment>> GetAttachmentsAsync(string entityId, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<AttachmentDto>>($"/api/attachments/{entityId}", ct);
        return result?.Cast<IFileAttachment>().ToList() ?? [];
    }

    public async Task<string> GetDownloadUrlAsync(string attachmentId, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<DownloadUrlResponse>($"/api/attachments/download/{attachmentId}", ct);
        return result?.Url ?? string.Empty;
    }

    public async Task DeleteAttachmentAsync(string attachmentId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/attachments/item/{attachmentId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> UploadChunkAsync(FileChunkData chunk, CancellationToken ct = default)
    {
        var body = new
        {
            chunk.FileName,
            chunk.ContentType,
            chunk.TotalSize,
            chunk.ChunkIndex,
            chunk.TotalChunks,
            Data = Convert.ToBase64String(chunk.Data),
            chunk.EntityId
        };

        var response = await _http.PostAsJsonAsync("/api/attachments/chunk", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChunkUploadResponse>(ct);
        return result?.Completed == true ? result.AttachmentId : null;
    }

    private sealed record DownloadUrlResponse(string Url);
    private sealed record ChunkUploadResponse(string? AttachmentId, bool Completed);
}
