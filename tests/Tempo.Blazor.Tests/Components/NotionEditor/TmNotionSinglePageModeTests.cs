using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionSinglePageModeTests : LocalizationTestBase
{
    public TmNotionSinglePageModeTests()
    {
        var notifications = new InMemoryNotificationStore();
        Services.AddSingleton<ITmNotificationService>(notifications);
        Services.AddSingleton<CommentNotificationOrchestrator>();
        Services.AddSingleton<NavigationManager>(new SinglePageNavigationManager());
    }

    // ── NotionEditorContext.IsBlockTypeAllowed — the single enforcement point ──

    [Fact]
    public void Context_SinglePageDenied_BlocksMultiPageTypes()
    {
        var ctx = new NotionEditorContext { DeniedBlockTypes = TmNotionEditor.SinglePageDeniedBlockTypes };

        ctx.IsBlockTypeAllowed(BlockType.ChildPage).Should().BeFalse();
        ctx.IsBlockTypeAllowed(BlockType.LinkedPage).Should().BeFalse();
        ctx.IsBlockTypeAllowed(BlockType.LinkedDatabase).Should().BeFalse();
        ctx.IsBlockTypeAllowed(BlockType.IncludePage).Should().BeFalse();
        ctx.IsBlockTypeAllowed(BlockType.ChildrenDisplay).Should().BeFalse();
        ctx.IsBlockTypeAllowed(BlockType.ContentByLabel).Should().BeFalse();
        ctx.IsBlockTypeAllowed(BlockType.Breadcrumb).Should().BeFalse();

        ctx.IsBlockTypeAllowed(BlockType.Paragraph).Should().BeTrue();
        ctx.IsBlockTypeAllowed(BlockType.Heading1).Should().BeTrue();
        ctx.IsBlockTypeAllowed(BlockType.Table).Should().BeTrue();
    }

    [Fact]
    public void Context_DeniedIntersectsWithAllowed()
    {
        var ctx = new NotionEditorContext
        {
            AllowedBlockTypes = new HashSet<BlockType> { BlockType.Paragraph, BlockType.ChildPage },
            DeniedBlockTypes = TmNotionEditor.SinglePageDeniedBlockTypes
        };

        ctx.IsBlockTypeAllowed(BlockType.Paragraph).Should().BeTrue();   // allowed and not denied
        ctx.IsBlockTypeAllowed(BlockType.ChildPage).Should().BeFalse();  // allowed but denied wins
        ctx.IsBlockTypeAllowed(BlockType.Heading1).Should().BeFalse();   // not in allow-list
    }

    // ── Hidden affordances ────────────────────────────────────────────────────

    [Fact]
    public void SinglePageMode_HidesSidebar_EvenWhenShowSidebarTrue()
    {
        var cut = RenderEditor(singlePage: true, showSidebar: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.FindAll(".tm-notion-sidebar").Should().BeEmpty();
        cut.FindAll(".tm-notion-sidebar-toggle").Should().BeEmpty();
        cut.FindAll("[data-tm-group='navigation']").Should().BeEmpty();
    }

    [Fact]
    public void SinglePageMode_AddsRootModifierClass()
    {
        var cut = RenderEditor(singlePage: true, showSidebar: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-editor").ClassList.Should().Contain("tm-notion-editor--single-page");
    }

    [Fact]
    public void SinglePageMode_False_KeepsSidebar()
    {
        var cut = RenderEditor(singlePage: false, showSidebar: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-sidebar").Should().NotBeNull(); // unchanged legacy behaviour
        cut.Find(".tm-notion-editor").ClassList.Should().NotContain("tm-notion-editor--single-page");
    }

    // ── Navigation is delegated to the host ───────────────────────────────────

    [Fact]
    public async Task SinglePageMode_NavigateToOtherPage_RaisesEvent_WithoutNavigating()
    {
        string? requested = null;
        var cut = RenderEditor(singlePage: true, showSidebar: true, onNav: id => requested = id);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());

        await cut.InvokeAsync(() => cut.Instance.NavigateToPageAsync(SinglePageProvider.OtherPageId.ToString("D")));

        requested.Should().Be(SinglePageProvider.OtherPageId.ToString("D"));
        // Still showing the original page (title unchanged)
        cut.Markup.Should().Contain(SinglePageProvider.MainTitle);
        cut.Markup.Should().NotContain(SinglePageProvider.OtherTitle);
    }

    [Fact]
    public async Task NonSinglePageMode_NavigateToOtherPage_ActuallyNavigates()
    {
        string? requested = null;
        var cut = RenderEditor(singlePage: false, showSidebar: true, onNav: id => requested = id);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());

        await cut.InvokeAsync(() => cut.Instance.NavigateToPageAsync(SinglePageProvider.OtherPageId.ToString("D")));

        requested.Should().BeNull(); // legacy: no delegation
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(SinglePageProvider.OtherTitle));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public void SinglePageMode_WithoutInitialPageId_ShowsLocalizedConfigError()
    {
        var provider = new SinglePageProvider();
        var cut = Render<TmNotionEditor>(p => p
            .Add(c => c.DataProvider, provider)
            .Add(c => c.AggregateProvider, new NotionAggregateTestAdapter(provider, provider))
            .Add(c => c.SinglePageMode, true)); // no InitialPageId

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-single-page-config-error']").Should().NotBeNull());
        cut.FindAll(".tm-notion-page").Should().BeEmpty();
    }

    [Fact]
    public void SinglePageMode_WithReadOnly_LoadsLockedPage()
    {
        var cut = RenderEditor(singlePage: true, showSidebar: true, readOnly: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-page").ClassList.Should().Contain("tm-notion-page--readonly");
        cut.FindAll(".tm-notion-sidebar").Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IRenderedComponent<TmNotionEditor> RenderEditor(
        bool singlePage,
        bool showSidebar,
        bool readOnly = false,
        Action<string>? onNav = null)
    {
        var provider = new SinglePageProvider();
        return Render<TmNotionEditor>(p =>
        {
            p.Add(c => c.DataProvider, provider)
             .Add(c => c.AggregateProvider, new NotionAggregateTestAdapter(provider, provider))
             .Add(c => c.InitialPageId, SinglePageProvider.MainPageId.ToString("D"))
             .Add(c => c.ShowSidebar, showSidebar)
             .Add(c => c.SinglePageMode, singlePage)
             .Add(c => c.ReadOnly, readOnly);
            if (onNav is not null)
                p.Add(c => c.OnPageNavigationRequested, onNav);
        });
    }

    private sealed class SinglePageProvider : INotionDataProvider, INotionEditorBlockService
    {
        public static readonly Guid MainPageId = Guid.Parse("dd190000-0000-0000-0000-000000000001");
        public static readonly Guid OtherPageId = Guid.Parse("dd190000-0000-0000-0000-000000000002");
        public const string MainTitle = "Work item description";
        public const string OtherTitle = "Some other page";

        private readonly NotionPage _main = new()
        {
            Id = MainPageId,
            Title = MainTitle,
            CreatedAt = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow
        };

        private readonly NotionPage _other = new()
        {
            Id = OtherPageId,
            Title = OtherTitle,
            CreatedAt = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow
        };

        private readonly List<IPageBlock> _blocks =
        [
            new PageBlock
            {
                Id = Guid.Parse("dd190000-0000-0000-0000-000000000010"),
                PageId = MainPageId,
                Type = BlockType.Paragraph,
                Order = 0,
                Content = new TextBlockContent { Html = "Describe the work item here." }
            }
        ];

        public Task<INotionPage> GetPageAsync(string pageId)
            => Task.FromResult<INotionPage>(pageId == OtherPageId.ToString("D") ? _other : _main);

        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId)
            => Task.FromResult<IEnumerable<INotionPage>>([_main]);

        public Task<IEnumerable<INotionPage>> GetFavoritesAsync()
            => Task.FromResult<IEnumerable<INotionPage>>([]);

        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count)
            => Task.FromResult<IEnumerable<INotionPage>>([_main]);

        public Task<IEnumerable<INotionPage>> GetTrashAsync()
            => Task.FromResult<IEnumerable<INotionPage>>([]);

        public Task<INotionPage> CreatePageAsync(string? parentId, string title)
            => Task.FromResult<INotionPage>(_main);

        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => Task.FromResult<INotionPage>(_main);

        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<INotionPage>>([]);

        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId)
            => Task.FromResult<IEnumerable<IPageBlock>>(pageId == OtherPageId.ToString("D") ? [] : _blocks);

        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId)
            => Task.FromResult<IEnumerable<IPageBlock>>([]);

        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId)
            => Task.FromResult(block);

        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId)
            => Task.FromResult(blocks);

        public Task UpdateBlockAsync(IPageBlock block) => Task.CompletedTask;
        public Task DeleteBlockAsync(string blockId) => Task.CompletedTask;
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => Task.CompletedTask;
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => Task.CompletedTask;
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => Task.CompletedTask;

        public Task<IPageBlock> DuplicateBlockAsync(string blockId)
            => Task.FromResult(_blocks[0]);

        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType)
            => Task.FromResult(_blocks[0]);

        public Task<string> GetBlockLinkAsync(string blockId)
            => Task.FromResult($"https://localhost/notion/{MainPageId:D}#{blockId}");
    }

    private sealed class SinglePageNavigationManager : NavigationManager
    {
        public SinglePageNavigationManager()
            => Initialize("https://localhost/", "https://localhost/notion-editor");

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
