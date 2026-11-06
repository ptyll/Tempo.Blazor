using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>Read-only blog post detail that reuses the Notion block renderer.</summary>
public partial class TmNotionBlogPost : TmComponentBase
{
    [CascadingParameter] private NotionEditorContext? Context { get; set; }

    /// <summary>Blog post to render.</summary>
    [Parameter, EditorRequired] public NotionBlogPostDto Post { get; set; } = default!;

    /// <summary>True while the post is being published.</summary>
    [Parameter] public bool IsPublishing { get; set; }

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when the publish button is clicked. Arg is the post id.</summary>
    [Parameter] public EventCallback<string> OnPublish { get; set; }

    /// <summary>When true, the heading takes focus after render (compact master → detail).</summary>
    [Parameter] public bool FocusHeading { get; set; }

    private ElementReference _titleRef;
    private bool _pendingHeadingFocus;

    private string? _lastPostId;
    private string _authorName = string.Empty;

    /// <summary>Cover: the URL of the first image block (Q-P4, no DTO change).</summary>
    private string? CoverUrl => Post.Blocks
        .Where(block => block.Type == BlockType.Image)
        .OrderBy(block => block.Order)
        .Select(block => (block.Content as IImageBlockContent)?.Url)
        .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

    private IReadOnlyList<IPageBlock> PostBlocks => Post.Blocks.Cast<IPageBlock>().ToArray();

    private string StatusText => Post.PublishedAt is null
        ? Loc["Notion_Blog_Draft"]
        : Loc["Notion_Blog_Published"];

    private string StatusClass => Post.PublishedAt is null
        ? "tm-notion-blog-post__status tm-notion-blog-post__status--draft"
        : "tm-notion-blog-post__status tm-notion-blog-post__status--published";

    protected override async Task OnParametersSetAsync()
    {
        if (FocusHeading)
            _pendingHeadingFocus = true;
        if (string.Equals(_lastPostId, Post.Id, StringComparison.Ordinal))
            return;
        _lastPostId = Post.Id;
        _authorName = Post.AuthorId ?? string.Empty;
        await ResolveAuthorAsync();
    }

    /// <summary>Resolves the author display name through the mention provider; the raw id is the fallback.</summary>
    private async Task ResolveAuthorAsync()
    {
        if (string.IsNullOrWhiteSpace(Post.AuthorId) || Context?.MentionProvider is null)
            return;
        try
        {
            var match = await Context.MentionProvider.GetByIdAsync(Post.AuthorId.Trim());
            if (match is not null && !string.IsNullOrWhiteSpace(match.DisplayName))
                _authorName = match.DisplayName;
        }
        catch { /* provider unavailable — keep the id fallback */ }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_pendingHeadingFocus)
            return;
        _pendingHeadingFocus = false;
        try { await _titleRef.FocusAsync(); }
        catch { /* SSR / test */ }
    }
}
