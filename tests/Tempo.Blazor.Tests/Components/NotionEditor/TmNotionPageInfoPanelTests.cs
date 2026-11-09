using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Avatars;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionPageInfoPanelTests : LocalizationTestBase
{
    public TmNotionPageInfoPanelTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Notion_PageInfo_Title"] = "Page info",
            ["Notion_PageInfo_Open"] = "Open page info",
            ["Notion_PageInfo_Created"] = "Created",
            ["Notion_PageInfo_LastEdited"] = "Last edited",
            ["Notion_PageInfo_Words"] = "Words",
            ["Notion_PageInfo_ReadingTime"] = "Reading time",
            ["Notion_PageInfo_Views"] = "Views",
            ["Notion_PageInfo_UnknownUser"] = "Unknown author",
            ["Notion_PageInfo_Minutes"] = "{0} min"
        });
    }

    [Fact]
    public void Panel_ShowsMetadataStatsAndViewsWhenProviderExists()
    {
        var page = MakePage("ada", "grace");
        var context = BuildContext(new FakeAnalyticsProvider(128), new FakeMentionProvider());
        var cut = RenderPanel(context, page, MakeBlocks("Alpha beta gamma delta.", "One two three four five six."));

        cut.Markup.Should().Contain("Created");
        cut.Markup.Should().Contain("Ada Lovelace");
        cut.Markup.Should().Contain("Last edited");
        cut.Markup.Should().Contain("Grace Hopper");
        cut.Find(".tm-page-info__words .tm-page-info__metric-value").TextContent.Trim().Should().Be("10");
        cut.Find(".tm-page-info__reading-time .tm-page-info__metric-value").TextContent.Trim().Should().Be("1 min");
        cut.Find(".tm-page-info__views .tm-page-info__metric-value").TextContent.Trim().Should().Be("128");
    }

    [Fact]
    public void Panel_HidesViewsWhenAnalyticsProviderIsMissing()
    {
        var page = MakePage("ada", "grace");
        var cut = RenderPanel(BuildContext(mentionProvider: new FakeMentionProvider()), page, MakeBlocks("Alpha beta gamma."));

        cut.FindAll(".tm-page-info__views").Should().BeEmpty();
    }

    [Fact]
    public void Panel_RendersEmptyStatsAndUnknownAuthor()
    {
        var page = MakePage(null, null);
        var cut = RenderPanel(BuildContext(), page, []);

        cut.Markup.Should().Contain("Unknown author");
        cut.Find(".tm-page-info__words .tm-page-info__metric-value").TextContent.Trim().Should().Be("0");
        cut.Find(".tm-page-info__reading-time .tm-page-info__metric-value").TextContent.Trim().Should().Be("0 min");
    }

    [Fact]
    public void Panel_HidesRawUserIdWhenResolverCannotFindDisplayName()
    {
        var page = MakePage("6f1e3b48-5df4-4ef5-8308-7bc282f71f26", "unknown-user-id");
        var cut = RenderPanel(BuildContext(mentionProvider: new FakeMentionProvider()), page, []);

        cut.Markup.Should().Contain("Unknown author");
        cut.Markup.Should().NotContain("6f1e3b48-5df4-4ef5-8308-7bc282f71f26");
        cut.Markup.Should().NotContain("unknown-user-id");
    }

    [Fact]
    public void Panel_Escape_RaisesVisibleChangedFalse()
    {
        var visibleChanges = new List<bool>();
        var page = MakePage("ada", "grace");
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, BuildContext(mentionProvider: new FakeMentionProvider()))
            .AddChildContent<TmNotionPageInfoPanel>(child => child
                .Add(p => p.Page, page)
                .Add(p => p.Blocks, [])
                .Add(p => p.Visible, true)
                .Add(p => p.VisibleChanged, EventCallback.Factory.Create<bool>(this, v => visibleChanges.Add(v)))));

        cut.WaitForAssertion(() => cut.FindComponent<TmModal>().Should().NotBeNull());
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        visibleChanges.Should().ContainSingle().Which.Should().BeFalse();
    }

    [Fact]
    public void Panel_RendersUsersThroughTmAvatar()
    {
        var page = MakePage("ada", "grace");
        var cut = RenderPanel(BuildContext(mentionProvider: new FakeMentionProvider()), page, []);

        cut.FindComponents<TmAvatar>().Should().HaveCountGreaterThanOrEqualTo(2,
            "created-by and last-edited-by render as avatars (image or initials fallback)");
        cut.Markup.Should().Contain("Ada Lovelace");
    }

    [Fact]
    public void Panel_HasCloseButtonFooter()
    {
        var page = MakePage("ada", "grace");
        var cut = RenderPanel(BuildContext(mentionProvider: new FakeMentionProvider()), page, []);

        var footer = cut.Find(".tm-modal-footer");
        footer.Should().NotBeNull();
        footer.TextContent.Should().Contain("Close");
    }

    [Fact]
    public void Panel_UsesCoreModal_WithoutOwnBackdropOrZIndex()
    {
        var css = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
                "Components", "NotionEditor", "Page", "TmNotionPageInfoPanel.razor.css")),
            @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        css.Should().NotContain("tm-page-info-backdrop");
        css.Should().NotContain("z-index");
    }

    [Fact]
    public void Panel_MetricCards_AnchorValuesAndClampLabels()
    {
        var css = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
                "Components", "NotionEditor", "Page", "TmNotionPageInfoPanel.razor.css")),
            @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Rows auto 1fr with the value at the end keeps one baseline per row; the label truncates
        // instead of wrapping (UX review of the phase-4 page-info capture).
        var metric = System.Text.RegularExpressions.Regex.Match(css,
            @"\.tm-page-info__metric\s*\{(?<body>[^}]*)\}");
        metric.Success.Should().BeTrue();
        metric.Groups["body"].Value.Should().Contain("grid-template-rows: auto 1fr");

        var label = System.Text.RegularExpressions.Regex.Matches(css,
            @"\.tm-page-info__metric-label\s*\{(?<body>[^}]*)\}");
        label.Count.Should().BeGreaterThan(1, "the shared dt/label rule plus the metric-only override");
        var labelBody = label[^1].Groups["body"].Value;
        labelBody.Should().Contain("white-space: nowrap");
        labelBody.Should().Contain("text-overflow: ellipsis");

        var value = System.Text.RegularExpressions.Regex.Match(css,
            @"\.tm-page-info__metric-value\s*\{(?<body>[^}]*)\}");
        value.Success.Should().BeTrue();
        value.Groups["body"].Value.Should().Contain("align-self: end");
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderPanel(
        NotionEditorContext context,
        INotionPage page,
        IReadOnlyList<IPageBlock> blocks)
        => Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, context)
            .AddChildContent<TmNotionPageInfoPanel>(child => child
                .Add(p => p.Page, page)
                .Add(p => p.Blocks, blocks)
                .Add(p => p.Visible, true)));

    private static NotionEditorContext BuildContext(
        INotionAnalyticsProvider? analyticsProvider = null,
        ITmPeopleProvider? mentionProvider = null)
        => new()
        {
            DataProvider = new FakeDataProvider(),
            BlockService = new FakeBlockService(),
            AnalyticsProvider = analyticsProvider,
            MentionProvider = mentionProvider
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find TempoBlazor.slnx.");
    }

    private static NotionPage MakePage(string? createdBy, string? editedBy) => new()
    {
        Id = Guid.Parse("cf160000-0000-0000-0000-000000000001"),
        Title = "CF16 Page Info",
        CreatedAt = new DateTime(2026, 2, 3, 10, 0, 0, DateTimeKind.Utc),
        CreatedByUserId = createdBy,
        LastEditedAt = new DateTime(2026, 2, 4, 11, 30, 0, DateTimeKind.Utc),
        LastEditedByUserId = editedBy
    };

    private static IReadOnlyList<IPageBlock> MakeBlocks(params string[] html)
        => html.Select((value, index) => new PageBlock
        {
            Id = Guid.NewGuid(),
            PageId = Guid.Parse("cf160000-0000-0000-0000-000000000001"),
            Type = BlockType.Paragraph,
            Order = index,
            Content = new TextBlockContent { Html = value }
        }).Cast<IPageBlock>().ToArray();

    private sealed class FakeAnalyticsProvider(int views) : INotionAnalyticsProvider
    {
        public Task RecordViewAsync(Guid pageId, string? userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PageAnalyticsDto?> GetPageAnalyticsAsync(Guid pageId, CancellationToken cancellationToken = default)
            => Task.FromResult<PageAnalyticsDto?>(new PageAnalyticsDto { PageId = pageId, Views = views });

        public Task<IReadOnlyList<PageAnalyticsDto>> GetTopPagesAsync(string spaceId, NotionAnalyticsRange range, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PageAnalyticsDto>>([]);
    }

    private sealed class FakeMentionProvider : TmPeopleProviderBase
    {
        private static readonly IReadOnlyList<TmUser> Users =
        [
            new() { Id = "ada", UserName = "ada", DisplayName = "Ada Lovelace" },
            new() { Id = "grace", UserName = "grace", DisplayName = "Grace Hopper" }
        ];

        public override Task<IReadOnlyList<TmUser>> SearchAsync(TmPeopleQuery query, CancellationToken cancellationToken = default)
        {
            IEnumerable<TmUser> users = Users;
            if (query.Ids.Count > 0)
            {
                var ids = query.Ids.ToHashSet(StringComparer.Ordinal);
                users = users.Where(user => ids.Contains(user.Id));
            }
            else if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                users = users.Where(user => string.Equals(user.Id, query.SearchText, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<TmUser>>(users.ToArray());
        }
    }
    private sealed class FakeDataProvider : INotionDataProvider
    {
        public Task<INotionPage> GetPageAsync(string pageId) => throw new NotSupportedException();
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => throw new NotSupportedException();
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => throw new NotSupportedException();
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => throw new NotSupportedException();
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => throw new NotSupportedException();
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => throw new NotSupportedException();
        public Task UpdatePageAsync(INotionPage page) => throw new NotSupportedException();
        public Task DeletePageAsync(string pageId) => throw new NotSupportedException();
        public Task RestorePageAsync(string pageId) => throw new NotSupportedException();
        public Task PermanentlyDeletePageAsync(string pageId) => throw new NotSupportedException();
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => throw new NotSupportedException();
        public Task MovePageAsync(string pageId, string? newParentId) => throw new NotSupportedException();
        public Task<INotionPage> DuplicatePageAsync(string pageId) => throw new NotSupportedException();
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeBlockService : INotionEditorBlockService
    {
        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId) => throw new NotSupportedException();
        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId) => throw new NotSupportedException();
        public Task UpdateBlockAsync(IPageBlock block) => throw new NotSupportedException();
        public Task DeleteBlockAsync(string blockId) => throw new NotSupportedException();
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => throw new NotSupportedException();
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => throw new NotSupportedException();
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => throw new NotSupportedException();
        public Task<IPageBlock> DuplicateBlockAsync(string blockId) => throw new NotSupportedException();
        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType) => throw new NotSupportedException();
        public Task<string> GetBlockLinkAsync(string blockId) => throw new NotSupportedException();
    }
}
