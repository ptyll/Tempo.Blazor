using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Services;

public sealed class DemoNotionBlogProvider : INotionBlogProvider
{
    // Web defaults: the demo API writes camelCase. Case-sensitive options drop "items".
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public DemoNotionBlogProvider(IHttpClientFactory factory)
        => _http = factory.CreateClient("DemoApi");

    public async Task<PagedResult<NotionBlogPostDto>> GetPostsAsync(string spaceId, NotionBlogQuery paging, CancellationToken cancellationToken = default)
    {
        paging ??= new NotionBlogQuery();
        var url = $"/api/notion/blog/spaces/{Uri.EscapeDataString(spaceId)}/posts" +
                  $"?skip={Math.Max(0, paging.Skip)}" +
                  $"&take={Math.Max(1, paging.Take)}" +
                  $"&includeDrafts={paging.IncludeDrafts.ToString().ToLowerInvariant()}";

        // The API writes camelCase (web defaults). The parameterless GetFromJsonAsync uses
        // case-sensitive options, so "items" never binds and the panel renders "0 posts".
        return await _http.GetFromJsonAsync<PagedResult<NotionBlogPostDto>>(url, Json, cancellationToken)
               ?? new PagedResult<NotionBlogPostDto>();
    }

    public async Task<NotionBlogPostDto?> GetPostAsync(string postId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"/api/notion/blog/posts/{Uri.EscapeDataString(postId)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotionBlogPostDto>(Json, cancellationToken);
    }

    public async Task<NotionBlogPostDto> CreatePostAsync(CreateNotionBlogPostRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("/api/notion/blog/posts", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotionBlogPostDto>(Json, cancellationToken)
               ?? throw new InvalidOperationException("The demo API did not return the created blog post.");
    }

    public async Task<NotionBlogPostDto> PublishAsync(string postId, PublishNotionBlogPostRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/notion/blog/posts/{Uri.EscapeDataString(postId)}/publish",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotionBlogPostDto>(Json, cancellationToken)
               ?? throw new InvalidOperationException("The demo API did not return the published blog post.");
    }
}
