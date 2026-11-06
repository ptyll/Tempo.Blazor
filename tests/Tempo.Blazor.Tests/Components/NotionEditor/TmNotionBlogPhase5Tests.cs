using Bunit;
using Bunit.Rendering;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 5 (P5): blog list / post per the reference — active row with <c>aria-current</c>,
/// the perex (first paragraph) on each row, author resolved through the mention provider
/// (raw id fallback), the first image block as the post cover, and the compact
/// master → detail flow with a "Back to blog" action.
/// </summary>
public sealed class TmNotionBlogPhase5Tests : LocalizationTestBase
{
    public TmNotionBlogPhase5Tests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Loading"] = "Loading",
            ["Tm_Retry"] = "Retry",
            ["Notion_Blog_Title"] = "Blog",
            ["Notion_Blog_Count"] = "{0} posts",
            ["Notion_Blog_New"] = "New",
            ["Notion_Blog_Publish"] = "Publish",
            ["Notion_Blog_Draft"] = "Draft",
            ["Notion_Blog_Published"] = "Published",
            ["Notion_Blog_PublishedOn"] = "Published {0}",
            ["Notion_Blog_BackToList"] = "Blog",
            ["Notion_Blog_Empty"] = "No blog posts in this space.",
            ["Notion_Blog_LoadError"] = "Blog posts could not be loaded.",
            ["Notion_Blog_Paging"] = "Blog paging",
            ["Notion_Blog_Previous"] = "Previous",
            ["Notion_Blog_Next"] = "Next",
            ["Notion_Blog_Page"] = "Page {0}",
            ["Notion_Blog_NoPostSelected"] = "Select a post to read it.",
            ["Notion_Blog_NewPostTitle"] = "Untitled blog post",
            ["Notion_Blog_NewPostBody"] = "Draft the post body here.",
            ["TmNotionBlock_ParagraphPlaceholder"] = "Paragraph",
            ["TmNotionBlock_Comments"] = "Comments",
            ["TmNotionBlock_ThreadTooltipLabel"] = "Thread"
        });
    }

    [Fact]
    public void ActiveRow_CarriesAriaCurrent_AndInertRowsDoNot()
    {
        var cut = RenderBlog(new FakeBlogProvider(SamplePosts()));

        cut.WaitForAssertion(() =>
        {
            var active = cut.Find("[data-testid='notion-blog-post-item'][aria-current='true']");
            active.TextContent.Should().Contain("Newest post");
            var others = cut.FindAll("[data-testid='notion-blog-post-item']")
                .Where(item => item.GetAttribute("aria-current") is null)
                .ToList();
            others.Should().HaveCount(2, "only the selected post is current");
        });
    }

    [Fact]
    public void ListRows_EmitNoLiteralDirectiveAttributes()
    {
        // `@ref:set` is not a Razor directive: it compiled into a literal "@ref:set" DOM
        // attribute, the browser renderer threw on it and the panel froze on "0 posts".
        var cut = RenderBlog(new FakeBlogProvider(SamplePosts()));

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='notion-blog-post-item']");
            rows.Should().HaveCount(3);
            cut.FindAll("*")
                .SelectMany(element => element.Attributes)
                .Select(attribute => attribute.Name)
                .Should().NotContain(name => name.StartsWith("@"));
        });
    }

    [Fact]
    public void ListRowCss_RowsAndPerexDoNotShrinkInTheScrollingList()
    {
        // At 390 the sheet's list is a height-limited flex column: without flex-shrink: 0 the
        // rows shrank to their min-height and the overflow-clipped perex collapsed to 0px.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        var css = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor", "UI", "TmNotionBlogList.razor.css"));

        foreach (var selector in new[] { @"\.tm-notion-blog__post", @"\.tm-notion-blog__post-excerpt" })
        {
            var rule = System.Text.RegularExpressions.Regex.Match(css, selector + @"\s*\{(?<body>[^}]*)\}");
            rule.Success.Should().BeTrue();
            rule.Groups["body"].Value.Should().Contain("flex-shrink: 0");
        }
    }

    [Fact]
    public void ListRow_ShowsPerexFromFirstParagraph()
    {
        var cut = RenderBlog(new FakeBlogProvider(SamplePosts()));

        cut.WaitForAssertion(() =>
        {
            var active = cut.Find("[data-testid='notion-blog-post-item'][aria-current='true']");
            active.QuerySelector(".tm-notion-blog__post-excerpt")!.TextContent
                .Should().Contain("Newest body");
        });
    }

    [Fact]
    public void PostMeta_ResolvesAuthorNameThroughMentionProvider()
    {
        var cut = RenderBlog(
            new FakeBlogProvider(SamplePosts()),
            mentionProvider: new FakePeopleProvider(("alice", "Alice Cooper")));

        cut.WaitForAssertion(() =>
        {
            var meta = cut.Find(".tm-notion-blog-post__meta");
            meta.TextContent.Should().Contain("Alice Cooper");
            meta.TextContent.Should().NotContain("alice");
        });
    }

    [Fact]
    public void PostMeta_FallsBackToAuthorId_WithoutResolver()
    {
        var cut = RenderBlog(new FakeBlogProvider(SamplePosts()));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-notion-blog-post__meta").TextContent.Should().Contain("alice"));
    }

    [Fact]
    public async Task CompactFlow_FocusesDetailHeadingOnOpen_AndListItemOnBack()
    {
        var cut = RenderBlog(new FakeBlogProvider(SamplePosts()));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='notion-blog-post-item']").Should().NotBeEmpty());

        var other = cut.FindAll("[data-testid='notion-blog-post-item']")
            .First(item => item.GetAttribute("aria-current") is null);
        await other.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find(".tm-notion-blog-post__title").GetAttribute("tabindex").Should().Be("-1");
        cut.Find(".tm-notion-blog-post__title").GetAttribute("data-focus-pending").Should().Be("true");

        await cut.Find("[data-testid='notion-blog-back']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='notion-blog-post-item'][data-post-id='" + other.GetAttribute("data-post-id") + "']")
            .GetAttribute("data-focus-pending").Should().Be("true");
    }

    [Fact]
    public void PostDetail_RendersCoverFromFirstImageBlock()
    {
        var withImage = new NotionBlogPostDto
        {
            Id = "img",
            SpaceId = "team",
            Title = "With cover",
            AuthorId = "alice",
            Blocks =
            [
                new PageBlock
                {
                    Id = Guid.NewGuid(), PageId = Guid.NewGuid(), Type = BlockType.Image, Order = 0,
                    Content = new ImageBlockContent { Url = "https://example.test/cover.png" },
                    CreatedAt = DateTime.UtcNow, LastEditedAt = DateTime.UtcNow
                },
                new PageBlock
                {
                    Id = Guid.NewGuid(), PageId = Guid.NewGuid(), Type = BlockType.Paragraph, Order = 1,
                    Content = new TextBlockContent { Html = "body" },
                    CreatedAt = DateTime.UtcNow, LastEditedAt = DateTime.UtcNow
                }
            ]
        };
        var cut = RenderBlog(new FakeBlogProvider([withImage]));

        cut.WaitForAssertion(() =>
        {
            var cover = cut.Find(".tm-notion-blog-post__cover");
            cover.GetAttribute("src").Should().Be("https://example.test/cover.png");
            cover.GetAttribute("alt").Should().Be("With cover");
        });
    }

    [Fact]
    public async Task CompactSelection_BackReturnsToList()
    {
        var cut = RenderBlog(new FakeBlogProvider(SamplePosts()));

        // The load-time auto-selection only previews the newest post beside the list: on a
        // compact panel the list must stay visible until the user opens a post.
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='notion-blog-post-item']").Should().HaveCount(3));
        cut.Find("[data-testid='notion-blog-panel']").ClassList
            .Should().NotContain("tm-notion-blog--detail-open");

        await cut.Find("[data-testid='notion-blog-post-item'][aria-current='true']")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-blog-panel']").ClassList
                .Should().Contain("tm-notion-blog--detail-open"));

        var back = cut.Find(".tm-notion-blog__back");
        back.TextContent.Should().Contain("Blog");

        await back.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='notion-blog-panel']").ClassList
            .Should().NotContain("tm-notion-blog--detail-open");
        cut.FindAll("[data-testid='notion-blog-post-item'][aria-current='true']").Should().BeEmpty();
    }

    private IRenderedComponent<ContainerFragment> RenderBlog(
        INotionBlogProvider provider,
        FakePeopleProvider? mentionProvider = null)
    {
        var context = new NotionEditorContext
        {
            DataProvider = new FakeDataProvider(),
            BlockService = new FakeBlockService(),
            MentionProvider = mentionProvider
        };

        return Render(builder =>
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", context);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<TmNotionBlogList>(3);
                child.AddAttribute(4, nameof(TmNotionBlogList.BlogProvider), provider);
                child.AddAttribute(5, nameof(TmNotionBlogList.SpaceId), "team");
                child.AddAttribute(6, nameof(TmNotionBlogList.CurrentUserId), "alice");
                child.CloseComponent();
            }));
            builder.CloseComponent();
        });
    }

    private static IReadOnlyList<NotionBlogPostDto> SamplePosts()
        =>
        [
            Post("old", "Older post", new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc), "Older body"),
            Post("new", "Newest post", new DateTime(2026, 1, 12, 8, 0, 0, DateTimeKind.Utc), "Newest body"),
            Post("draft", "Draft post", null, "Draft body")
        ];

    private static NotionBlogPostDto Post(string id, string title, DateTime? publishedAt, string body)
        => new()
        {
            Id = id,
            SpaceId = "team",
            Title = title,
            PublishedAt = publishedAt,
            AuthorId = "alice",
            Blocks =
            [
                new PageBlock
                {
                    Id = Guid.NewGuid(),
                    PageId = Guid.NewGuid(),
                    Type = BlockType.Paragraph,
                    Order = 0,
                    Content = new TextBlockContent { Html = body },
                    CreatedAt = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                }
            ]
        };

    private sealed class FakePeopleProvider : ITmPeopleProvider
    {
        private readonly Dictionary<string, string> _names;

        public FakePeopleProvider(params (string Id, string Name)[] names)
            => _names = names.ToDictionary(pair => pair.Id, pair => pair.Name, StringComparer.OrdinalIgnoreCase);

        public TmPeopleProviderCapabilities Capabilities => TmPeopleProviderCapabilities.Read;

        public Task<TmUser?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_names.TryGetValue(id, out var name)
                ? new TmUser { Id = id, DisplayName = name }
                : null);

        public Task<IReadOnlyList<TmUser>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmUser>>(ids
                .Where(id => _names.TryGetValue(id, out var name))
                .Select(id => new TmUser { Id = id, DisplayName = _names[id] })
                .ToArray());

        public Task<IReadOnlyList<TmUser>> SearchAsync(TmPeopleQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmUser>>([]);
    }

    private sealed class FakeBlogProvider : INotionBlogProvider
    {
        private readonly List<NotionBlogPostDto> _posts;

        public FakeBlogProvider(IEnumerable<NotionBlogPostDto> posts)
            => _posts = posts.Select(Clone).ToList();

        public Task<PagedResult<NotionBlogPostDto>> GetPostsAsync(string spaceId, NotionBlogQuery paging, CancellationToken cancellationToken = default)
        {
            var items = _posts
                .Where(post => post.SpaceId == spaceId)
                .Where(post => paging.IncludeDrafts || post.PublishedAt is not null)
                .OrderByDescending(post => post.PublishedAt ?? DateTime.MinValue)
                .ThenBy(post => post.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Task.FromResult(new PagedResult<NotionBlogPostDto>
            {
                Items = items.Skip(paging.Skip).Take(paging.Take).Select(Clone).ToArray(),
                TotalCount = items.Length,
                Page = paging.Skip / paging.Take + 1,
                PageSize = paging.Take
            });
        }

        public Task<NotionBlogPostDto?> GetPostAsync(string postId, CancellationToken cancellationToken = default)
            => Task.FromResult(_posts.FirstOrDefault(post => post.Id == postId) is { } post ? Clone(post) : null);

        public Task<NotionBlogPostDto> CreatePostAsync(CreateNotionBlogPostRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<NotionBlogPostDto> PublishAsync(string postId, PublishNotionBlogPostRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static NotionBlogPostDto Clone(NotionBlogPostDto post) => new()
        {
            Id = post.Id,
            SpaceId = post.SpaceId,
            Title = post.Title,
            PublishedAt = post.PublishedAt,
            AuthorId = post.AuthorId,
            Blocks = post.Blocks
        };
    }

    private sealed class FakeBlockService : INotionEditorBlockService
    {
        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId) => Task.FromResult(block);
        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId) => Task.FromResult(blocks);
        public Task UpdateBlockAsync(IPageBlock block) => Task.CompletedTask;
        public Task DeleteBlockAsync(string blockId) => Task.CompletedTask;
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => Task.CompletedTask;
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => Task.CompletedTask;
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => Task.CompletedTask;
        public Task<IPageBlock> DuplicateBlockAsync(string blockId) => throw new KeyNotFoundException(blockId);
        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType) => throw new KeyNotFoundException(blockId);
        public Task<string> GetBlockLinkAsync(string blockId) => Task.FromResult($"#{blockId}");
    }

    private sealed class FakeDataProvider : INotionDataProvider
    {
        public Task<INotionPage> GetPageAsync(string pageId) => Task.FromResult<INotionPage>(new NotionPage { Id = Guid.Parse(pageId), Title = pageId });
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => Task.FromResult<INotionPage>(new NotionPage { Id = Guid.NewGuid(), ParentId = parentId is null ? null : Guid.Parse(parentId), Title = title });
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => Task.FromResult<INotionPage>(new NotionPage { Id = Guid.NewGuid(), Title = pageId });
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
