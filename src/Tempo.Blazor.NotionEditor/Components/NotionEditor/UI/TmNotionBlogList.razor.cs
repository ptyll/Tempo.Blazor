using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>Chronological blog list and post reader for a Notion space.</summary>
public partial class TmNotionBlogList : TmComponentBase
{
    /// <summary>Blog provider used to query and mutate posts.</summary>
    [Parameter, EditorRequired] public INotionBlogProvider BlogProvider { get; set; } = default!;

    /// <summary>Space whose blog posts are shown.</summary>
    [Parameter, EditorRequired] public string SpaceId { get; set; } = string.Empty;

    /// <summary>Current user id used as author for newly created posts.</summary>
    [Parameter] public string? CurrentUserId { get; set; }

    /// <summary>Number of posts per page.</summary>
    [Parameter] public int PageSize { get; set; } = 5;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when the panel close button is clicked.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    private readonly List<NotionBlogPostDto> _posts = [];
    private NotionBlogPostDto? _selectedPost;
    private bool _isLoading;
    private bool _isCreating;
    private string? _loadError;
    private string? _publishingPostId;
    private string? _loadedSpaceId;
    private int _skip;
    private int _totalCount;
    private bool _hasNextPage;

    private int EffectivePageSize => Math.Clamp(PageSize, 1, 50);
    private int CurrentPage => _skip / EffectivePageSize + 1;

    protected override async Task OnParametersSetAsync()
    {
        if (!string.Equals(_loadedSpaceId, SpaceId, StringComparison.OrdinalIgnoreCase))
        {
            _loadedSpaceId = SpaceId;
            _skip = 0;
            await LoadPostsAsync();
        }
    }

    private async Task LoadPostsAsync()
    {
        if (string.IsNullOrWhiteSpace(SpaceId))
            return;

        _isLoading = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            var result = await BlogProvider.GetPostsAsync(
                SpaceId,
                new NotionBlogQuery { IncludeDrafts = true, Skip = _skip, Take = EffectivePageSize });

            _posts.Clear();
            _posts.AddRange(result.Items);
            _totalCount = result.TotalCount;
            _hasNextPage = result.HasNextPage;
            _selectedPost = _posts.Count > 0
                ? await BlogProvider.GetPostAsync(_posts[0].Id)
                : null;
        }
        catch
        {
            _loadError = Loc["Notion_Blog_LoadError"];
            _posts.Clear();
            _selectedPost = null;
            _totalCount = 0;
            _hasNextPage = false;
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task SelectPostAsync(string postId)
    {
        var post = await BlogProvider.GetPostAsync(postId);
        if (post is not null)
        {
            _selectedPost = post;
            StateHasChanged();
        }
    }

    private async Task CreatePostAsync()
    {
        if (string.IsNullOrWhiteSpace(SpaceId))
            return;

        _isCreating = true;
        StateHasChanged();

        try
        {
            var bodyBlockId = Guid.NewGuid();
            var created = await BlogProvider.CreatePostAsync(new CreateNotionBlogPostRequest
            {
                SpaceId = SpaceId,
                Title = Loc["Notion_Blog_NewPostTitle"],
                AuthorId = string.IsNullOrWhiteSpace(CurrentUserId) ? string.Empty : CurrentUserId,
                Blocks =
                [
                    new PageBlock
                    {
                        Id = bodyBlockId,
                        PageId = Guid.NewGuid(),
                        Type = BlockType.Paragraph,
                        Order = 0,
                        Content = new TextBlockContent { Html = Loc["Notion_Blog_NewPostBody"] },
                        CreatedAt = DateTime.UtcNow,
                        LastEditedAt = DateTime.UtcNow
                    }
                ]
            });

            _skip = 0;
            await LoadPostsAsync();
            await SelectPostAsync(created.Id);
        }
        finally
        {
            _isCreating = false;
            StateHasChanged();
        }
    }

    private async Task PublishPostAsync(string postId)
    {
        _publishingPostId = postId;
        StateHasChanged();

        try
        {
            var published = await BlogProvider.PublishAsync(postId, new PublishNotionBlogPostRequest());
            _selectedPost = published;
            await LoadPostsAsync();
            await SelectPostAsync(published.Id);
        }
        finally
        {
            _publishingPostId = null;
            StateHasChanged();
        }
    }

    private async Task PreviousPageAsync()
    {
        if (_skip == 0) return;
        _skip = Math.Max(0, _skip - EffectivePageSize);
        await LoadPostsAsync();
    }

    private async Task NextPageAsync()
    {
        if (!_hasNextPage) return;
        _skip += EffectivePageSize;
        await LoadPostsAsync();
    }

    private string PostButtonClass(NotionBlogPostDto post)
        => string.Equals(_selectedPost?.Id, post.Id, StringComparison.OrdinalIgnoreCase)
            ? "tm-notion-blog__post tm-notion-blog__post--active"
            : "tm-notion-blog__post";

    private static string PostStatusClass(NotionBlogPostDto post)
        => post.PublishedAt is null
            ? "tm-notion-blog__status tm-notion-blog__status--draft"
            : "tm-notion-blog__status tm-notion-blog__status--published";
}
