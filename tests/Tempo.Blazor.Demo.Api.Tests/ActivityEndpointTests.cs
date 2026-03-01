using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Tests;

public class ActivityEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ActivityEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetTimeline_ReturnsEntries()
    {
        var entries = await _client.GetFromJsonAsync<List<TimelineEntryDto>>(
            "/api/activity/task-1/timeline");

        Assert.NotNull(entries);
        Assert.True(entries.Count > 0);
    }

    [Fact]
    public async Task GetTimeline_UnknownEntity_ReturnsEmptyList()
    {
        var entries = await _client.GetFromJsonAsync<List<TimelineEntryDto>>(
            "/api/activity/unknown-entity/timeline");

        Assert.NotNull(entries);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetComments_ReturnsComments()
    {
        var comments = await _client.GetFromJsonAsync<List<CommentDto>>(
            "/api/activity/task-1/comments");

        Assert.NotNull(comments);
        Assert.True(comments.Count > 0);
    }

    [Fact]
    public async Task PostComment_CreatesComment()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/activity/task-1/comments",
            new { htmlContent = "<p>Test comment</p>" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var comment = await response.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(comment);
        Assert.Equal("<p>Test comment</p>", comment.HtmlContent);
        Assert.Equal("Demo User", comment.AuthorName);
    }

    [Fact]
    public async Task DeleteComment_RemovesComment()
    {
        var postResponse = await _client.PostAsJsonAsync(
            "/api/activity/task-2/comments",
            new { htmlContent = "<p>To delete</p>" });
        var created = await postResponse.Content.ReadFromJsonAsync<CommentDto>();

        var deleteResponse = await _client.DeleteAsync(
            $"/api/activity/task-2/comments/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
