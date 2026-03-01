using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Tests;

public class AttachmentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AttachmentEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetAttachments_ReturnsList()
    {
        var attachments = await _client.GetFromJsonAsync<List<AttachmentDto>>(
            "/api/attachments/task-1");

        Assert.NotNull(attachments);
        Assert.True(attachments.Count > 0);
    }

    [Fact]
    public async Task PostAttachmentChunk_AcceptsChunk()
    {
        var chunk = new
        {
            FileName = "test.txt",
            ContentType = "text/plain",
            TotalSize = 100L,
            ChunkIndex = 0,
            TotalChunks = 1,
            Data = Convert.ToBase64String("hello world"u8.ToArray()),
            EntityId = "task-1"
        };

        var response = await _client.PostAsJsonAsync("/api/attachments/chunk", chunk);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ChunkResult>();
        Assert.NotNull(result);
        Assert.True(result.Completed);
        Assert.NotNull(result.AttachmentId);
    }

    [Fact]
    public async Task GetDownloadUrl_ReturnsUrl()
    {
        var response = await _client.GetFromJsonAsync<DownloadUrlResult>(
            "/api/attachments/download/some-id");

        Assert.NotNull(response);
        Assert.Contains("some-id", response.Url);
    }

    private record ChunkResult(string? AttachmentId, bool Completed);
    private record DownloadUrlResult(string Url);
}
