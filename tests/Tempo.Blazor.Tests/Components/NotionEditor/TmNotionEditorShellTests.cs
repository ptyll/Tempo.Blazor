using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 3 (N0): the editor shell resolves its layout from <see cref="TmLayoutMode"/> and renders
/// the mobile action bar, the drawer sidebar and the inspector slot through the core primitives.
/// </summary>
public sealed class TmNotionEditorShellTests : LocalizationTestBase
{
    internal static Guid PageId => Provider.MainPageId;
    public TmNotionEditorShellTests()
    {
        Services.AddSingleton<ITmNotificationService>(new InMemoryNotificationStore());
        Services.AddSingleton<CommentNotificationOrchestrator>();
        Services.AddSingleton<NavigationManager>(new ShellNavigationManager());
    }

    [Fact]
    public void Mobile_RendersBottomBar_MaxThreeActionsPlusMore()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, workItems: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());

        var bar = cut.Find(".tm-mobile-action-bar");
        bar.Should().NotBeNull();
        // The bar itself pins at most three actions; the rest live behind More.
        bar.QuerySelectorAll(".tm-mobile-action-bar__action").Length.Should().BeLessThanOrEqualTo(3);
        cut.Find(".tm-mobile-action-bar__more").Should().NotBeNull();
    }

    [Fact]
    public void Desktop_NoBottomBar()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, workItems: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.FindAll(".tm-mobile-action-bar").Should().BeEmpty();
    }

    [Fact]
    public void MinimalChrome_RendersNoShellHeaderBand()
    {
        var minimal = RenderEditor(TmLayoutMode.Desktop, chrome: NotionEditorChrome.Minimal);
        minimal.WaitForAssertion(() => minimal.Find(".tm-notion-page").Should().NotBeNull());
        minimal.FindAll(".tm-editor-shell__header").Should().BeEmpty(
            "Chrome=Minimal/None pass a null header slot — an empty fragment would still draw the band");

        var full = RenderEditor(TmLayoutMode.Desktop);
        full.WaitForAssertion(() => full.Find(".tm-notion-page").Should().NotBeNull());
        full.Find(".tm-editor-shell__header").Should().NotBeNull();
    }

    [Fact]
    public void NoWorkItemSource_MyTasksHiddenInBarAndMenu()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, workItems: false);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.FindAll("[data-action-id='tasks']").Should().BeEmpty();
        cut.FindAll(".tm-notion-topbar__tasks").Should().BeEmpty();
        cut.Markup.Should().NotContain("data-action-id=\"tasks\"");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop, "never")]
    [InlineData(TmLayoutMode.Tablet, "never")]
    [InlineData(TmLayoutMode.Mobile, "auto")]
    public void MyTasks_PinnedExceptOnMobile(TmLayoutMode mode, string pin)
    {
        // On the mobile band a pinned Tasks could not fold into More once the pinned toggle /
        // watch / bell filled a 320px bar, and the More trigger painted over it.
        var cut = RenderEditor(mode, workItems: true);

        cut.WaitForAssertion(() => cut.Find("[data-testid='notion-tasks']").Should().NotBeNull());
        cut.Find("[data-testid='notion-tasks']").GetAttribute("data-pin").Should().Be(pin);
    }

    [Fact]
    public void Tablet_LowPriorityItemsInMoreMenu_WithTestIds()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet, workItems: true, audit: true, blog: true, analytics: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());

        // The More menu is an overlay: its entries exist once it is open.
        cut.Find(".tm-toolbar-more").Click();

        // The provider-gated entries keep their test ids even after they leave the bar.
        cut.Find("[data-testid='notion-audit-open'], [data-menu-testid='notion-audit-open']").Should().NotBeNull();
        cut.Find("[data-testid='notion-blog-open'], [data-menu-testid='notion-blog-open']").Should().NotBeNull();
        cut.Find("[data-testid='notion-analytics-open'], [data-menu-testid='notion-analytics-open']").Should().NotBeNull();

        // Tablet overflow: the low-priority tools are marked OverflowOnly, so they are not bar buttons.
        cut.FindAll(".tm-toolbar-btn[data-testid='notion-audit-open']").Should().BeEmpty();
        cut.FindAll(".tm-toolbar-more").Should().NotBeEmpty();
    }

    [Fact]
    public void SidebarToggle_AriaExpandedReflectsState()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        var toggle = cut.Find(".tm-notion-sidebar-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("true");

        toggle.Click();

        cut.Find(".tm-notion-sidebar-toggle").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Mobile_SidebarClosedByDefault()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-sidebar-toggle").GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll(".tm-notion-sidebar--visible").Should().BeEmpty();
    }

    [Fact]
    public async Task Escape_ClosesDrawer_FocusReturnsToToggle()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-sidebar-toggle").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-notion-sidebar--visible").Should().NotBeNull());

        // Escape is owned by the shell's focus trap (a document-level listener in the browser);
        // the editor no longer duplicates an own Escape handler on the sidebar.
        var scope = cut.FindComponents<TmFocusScope>()
            .First(c => c.Markup.Contains("tm-notion-sidebar", StringComparison.Ordinal));
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-notion-sidebar--visible").Should().BeEmpty();
            cut.Find(".tm-notion-sidebar-toggle").GetAttribute("aria-expanded").Should().Be("false");
        });
    }

    [Fact]
    public async Task NavigateToPage_ClosesDrawer()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-sidebar-toggle").Click();
        cut.Find(".tm-notion-sidebar--visible").Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.NavigateToPageAsync(Provider.OtherPageId.ToString("D")));

        cut.WaitForAssertion(() => cut.FindAll(".tm-notion-sidebar--visible").Should().BeEmpty());
    }

    [Fact]
    public void OpeningBlog_ClosesAnalytics()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, blog: true, analytics: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find("[data-testid='notion-analytics-open']").Click();
        cut.Find(".tm-notion-analytics-panel, .tm-notion-shell-panel .tm-notion-analytics").Should().NotBeNull();

        cut.Find("[data-testid='notion-blog-open']").Click();

        cut.FindAll(".tm-notion-analytics").Should().BeEmpty();
        cut.Find(".tm-notion-blog").Should().NotBeNull();
    }

    [Fact]
    public void ReadingMode_BottomBarShowsOnlyExit()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, workItems: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-reading-toggle").Click();

        var actions = cut.FindAll(".tm-mobile-action-bar__action");
        actions.Should().ContainSingle();
        actions[0].GetAttribute("data-action-id").Should().Be("exit-reading");
        cut.FindAll("[data-action-id='more']").Should().BeEmpty();
    }

    [Fact]
    public void OnLayoutModeChanged_Mobile_AddsModeClass()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notion-editor").GetAttribute("data-editor-layout").Should().Be("mobile");
        cut.Find(".tm-notion-editor").ClassList.Should().Contain("tm-notion-editor--mobile");
    }

    [Fact]
    public void InspectorContent_Null_RendersNoRightRegion()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.FindAll("[data-region='right'], .tm-editor-shell__panel--right, .tm-notion-inspector").Should().BeEmpty();
    }

    [Fact]
    public void ShellPanelPresentation_Main_HidesPageWrapper()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, blog: true,
            shellPanelPresentation: NotionShellPanelPresentation.Main);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find("[data-testid='notion-blog-open']").Click();

        // The shell panel IS the surface: no second wrapper around the page while it is open.
        cut.FindAll(".tm-notion-shell-panel--main .tm-notion-page-wrap, .tm-notion-page-wrap").Should().BeEmpty();
        cut.Find(".tm-notion-shell-panel--main").Should().NotBeNull();
    }

    [Fact]
    public void DockedTasks_Escape_ClosesAndRestoresTheTrigger()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, workItems: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find("[data-testid='notion-tasks']").Click();

        var scope = cut.FindComponents<TmFocusScope>()
            .Single(item => item.Instance.RestoreFocusTargetId == "notion-tasks");
        scope.Instance.CloseOnEscape.Should().BeTrue();

        cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut.FindAll(".tm-my-tasks").Should().BeEmpty());
    }

    [Fact]
    public void ShellSheet_ForwardsTheTriggerAsRestoreTarget()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet, workItems: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find("[data-testid='notion-tasks']").Click();

        cut.FindComponents<TmFocusScope>()
            .Should().Contain(scope => scope.Instance.RestoreFocusTargetId == "notion-tasks");
    }

    [Fact]
    public void ShellPanelPresentation_Overlay_KeepsPageMounted()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, blog: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find("[data-testid='notion-blog-open']").Click();

        // Overlay (default): the panel rides the core side panel; the page stays mounted under it.
        cut.Find(".tm-notion-page").Should().NotBeNull();
        cut.Find(".tm-notion-shell-panel--overlay").Should().NotBeNull();
        cut.Find(".tm-notion-shell-panel .tm-notion-blog").Should().NotBeNull();
    }

    [Fact]
    public void LayoutModeFlip_DesktopToMobile_PageSurvivesAtEditorLevel()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());

        // Crossing into the mobile band remounts the canvas subtree (the core shell renders a
        // different branch there — see decision NOTION-EDITOR-SHELL-CANVAS-REMOUNT). The editor
        // state (current page) lives above the canvas, so the page identity survives the flip.
        cut.Render(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-notion-editor").GetAttribute("data-editor-layout").Should().Be("mobile");
            cut.Find(".tm-notion-page").GetAttribute("data-page-id").Should().Be(Provider.MainPageId.ToString("D"));
        });
    }

    [Fact]
    public void MobileBar_AddBlock_OpensSlashMenu()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-mobile-action-bar [data-action-id='add']").Click();

        // The page owns the action: the slash menu opens anchored at the (last) block. The consumer
        // class rides the open-only body wrapper (N5); the dialog role stays on the panel root.
        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-notion-slash").Should().NotBeNull();
            cut.Find(".tm-notion-floating-menu [role='dialog']").Should().NotBeNull();
        });
    }

    [Fact]
    public void MobileBar_Comments_ExpandsPageCommentsPanel()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, comments: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-mobile-action-bar [data-action-id='comments']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-npcp--expanded").Should().NotBeNull());
    }

    [Fact]
    public void MobileBar_MoreSheet_HoldsToolEntriesWithTestIds()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, workItems: true,
            audit: true, blog: true, analytics: true, share: true, watch: true);

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-mobile-action-bar__more").Click();

        cut.Find("[data-testid='notion-audit-open']").Should().NotBeNull();
        cut.Find("[data-testid='notion-blog-open']").Should().NotBeNull();
        cut.Find("[data-testid='notion-analytics-open']").Should().NotBeNull();
        cut.Find("[data-testid='notion-share-open']").Should().NotBeNull();
        cut.Find("[data-testid='notion-reading-toggle']").Should().NotBeNull();
        cut.Find("[data-testid='notion-presentation-toggle']").Should().NotBeNull();
        cut.Find("[data-testid='notion-watch-toggle']").Should().NotBeNull();
        cut.Find("[data-testid='notion-page-settings']").Should().NotBeNull();
    }

    [Fact]
    public void ShareButton_RenderedOnlyWithPublicShareProvider()
    {
        var without = RenderEditor(TmLayoutMode.Desktop);
        without.WaitForAssertion(() => without.Find(".tm-notion-page").Should().NotBeNull());
        without.FindAll("[data-testid='notion-share-open']").Should().BeEmpty();

        var with = RenderEditor(TmLayoutMode.Desktop, share: true);
        with.WaitForAssertion(() => with.Find(".tm-notion-page").Should().NotBeNull());
        with.Find("[data-testid='notion-share-open']").Should().NotBeNull();
    }

    private IRenderedComponent<TmNotionEditor> RenderEditor(
        TmLayoutMode mode,
        bool workItems = false,
        bool audit = false,
        bool blog = false,
        bool analytics = false,
        bool share = false,
        bool watch = false,
        bool comments = false,
        NotionShellPanelPresentation? shellPanelPresentation = null,
        NotionEditorChrome chrome = NotionEditorChrome.Full)
    {
        var provider = new Provider();
        return Render<TmNotionEditor>(p =>
        {
            p.Add(c => c.DataProvider, provider)
             .Add(c => c.AggregateProvider, new NotionAggregateTestAdapter(provider, provider))
             .Add(c => c.InitialPageId, Provider.MainPageId.ToString("D"))
             .Add(c => c.LayoutMode, mode);
            if (workItems) p.Add(c => c.WorkItemSource, new ShellWorkItems());
            if (audit) p.Add(c => c.AuditProvider, new ShellAudit());
            if (blog) p.Add(c => c.BlogProvider, new ShellBlog());
            if (analytics) p.Add(c => c.AnalyticsProvider, new ShellAnalytics());
            if (share) p.Add(c => c.PublicShareProvider, new ShellShare());
            if (watch) p.Add(c => c.WatchProvider, new ShellWatch());
            if (comments) p.Add(c => c.CommentProvider, new ShellComments());
            if (shellPanelPresentation is { } presentation) p.Add(c => c.ShellPanelPresentation, presentation);
            if (chrome != NotionEditorChrome.Full) p.Add(c => c.Chrome, chrome);
        });
    }

    private sealed class ShellNavigationManager : NavigationManager
    {
        public ShellNavigationManager() => Initialize("https://localhost/", "https://localhost/notion-editor");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    internal sealed class Provider : INotionDataProvider, INotionEditorBlockService
    {
        public static readonly Guid MainPageId = Guid.Parse("aa300000-0000-0000-0000-000000000001");
        public static readonly Guid OtherPageId = Guid.Parse("aa300000-0000-0000-0000-000000000002");

        private readonly NotionPage _main = new()
        {
            Id = MainPageId, Title = "Shell page", CreatedAt = DateTime.UtcNow, LastEditedAt = DateTime.UtcNow
        };
        private readonly NotionPage _other = new()
        {
            Id = OtherPageId, Title = "Other page", CreatedAt = DateTime.UtcNow, LastEditedAt = DateTime.UtcNow
        };
        private readonly List<IPageBlock> _blocks =
        [
            new PageBlock
            {
                Id = Guid.Parse("aa300000-0000-0000-0000-000000000010"),
                PageId = MainPageId, Type = BlockType.Paragraph, Order = 0,
                Content = new TextBlockContent { Html = "Body" }
            }
        ];

        public Task<INotionPage> GetPageAsync(string pageId)
            => Task.FromResult<INotionPage>(pageId == OtherPageId.ToString("D") ? _other : _main);
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult<IEnumerable<INotionPage>>([_main]);
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult<IEnumerable<INotionPage>>([_main]);
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => Task.FromResult<INotionPage>(_main);
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => Task.FromResult<INotionPage>(_main);
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId) => Task.FromResult<IEnumerable<IPageBlock>>(pageId == OtherPageId.ToString("D") ? [] : _blocks);
        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId) => Task.FromResult(block);
        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId) => Task.FromResult(blocks);
        public Task UpdateBlockAsync(IPageBlock block) => Task.CompletedTask;
        public Task DeleteBlockAsync(string blockId) => Task.CompletedTask;
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => Task.CompletedTask;
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => Task.CompletedTask;
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => Task.CompletedTask;
        public Task<IPageBlock> DuplicateBlockAsync(string blockId) => Task.FromResult(_blocks[0]);
        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType) => Task.FromResult(_blocks[0]);
        public Task<string> GetBlockLinkAsync(string blockId) => Task.FromResult($"https://localhost/notion/{MainPageId:D}#{blockId}");
    }

    private sealed class ShellWorkItems : TmWorkItemProviderBase
    {
        public override string SourceKey => "notion";
        public override string DisplayName => "Notion tasks";
        public override TmWorkItemCapabilities Capabilities => TmWorkItemCapabilities.Read;

        public override Task<Tempo.Blazor.Models.PagedResult<TmWorkItem>> SearchAsync(TmWorkItemQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new Tempo.Blazor.Models.PagedResult<TmWorkItem> { Items = [], TotalCount = 0, Page = 1, PageSize = 20 });
    }

    private sealed class ShellAudit : ITmActivityProvider
    {
        public TmActivityProviderCapabilities Capabilities => TmActivityProviderCapabilities.Read;
        TmActivityProviderCapabilities ITmCapabilityProvider<TmActivityProviderCapabilities>.Capabilities => Capabilities;

        public Task<IReadOnlyList<TmActivityEntry>> GetForEntityAsync(TmEntityRef entityRef, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmActivityEntry>>([]);

        public Task<Tempo.Blazor.Models.PagedResult<TmActivityEntry>> QueryAsync(TmActivityQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new Tempo.Blazor.Models.PagedResult<TmActivityEntry> { Items = [], TotalCount = 0, Page = 1, PageSize = 20 });

        public Task<TmActivityEntry> AppendAsync(TmActivityEntry entry, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ShellBlog : INotionBlogProvider
    {
        public Task<Tempo.Blazor.NotionEditor.Models.PagedResult<NotionBlogPostDto>> GetPostsAsync(string spaceId, NotionBlogQuery paging, CancellationToken cancellationToken = default)
            => Task.FromResult(new Tempo.Blazor.NotionEditor.Models.PagedResult<NotionBlogPostDto> { Items = [], TotalCount = 0, Page = 1, PageSize = paging.Take });

        public Task<NotionBlogPostDto?> GetPostAsync(string postId, CancellationToken cancellationToken = default)
            => Task.FromResult<NotionBlogPostDto?>(null);

        public Task<NotionBlogPostDto> CreatePostAsync(CreateNotionBlogPostRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new NotionBlogPostDto { Id = "draft", SpaceId = request.SpaceId, Title = request.Title });

        public Task<NotionBlogPostDto> PublishAsync(string postId, PublishNotionBlogPostRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new NotionBlogPostDto { Id = postId });
    }

    private sealed class ShellAnalytics : INotionAnalyticsProvider
    {
        public Task RecordViewAsync(Guid pageId, string? userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PageAnalyticsDto?> GetPageAnalyticsAsync(Guid pageId, CancellationToken cancellationToken = default)
            => Task.FromResult<PageAnalyticsDto?>(null);
        public Task<IReadOnlyList<PageAnalyticsDto>> GetTopPagesAsync(string spaceId, NotionAnalyticsRange range, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PageAnalyticsDto>>([]);
    }

    private sealed class ShellShare : INotionPublicShareProvider
    {
        public Task<PublicShareDto> CreateShareAsync(Guid pageId, PublicShareOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new PublicShareDto { PageId = pageId, Token = "share", IsEnabled = true });
        public Task RevokeAsync(Guid pageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PublicShareDto?> GetShareAsync(Guid pageId, CancellationToken cancellationToken = default)
            => Task.FromResult<PublicShareDto?>(null);
        public Task<PublicShareDto?> ResolveByTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult<PublicShareDto?>(null);
    }

    private sealed class ShellWatch : INotionWatchProvider
    {
        public Task WatchAsync(string pageId, string userId, bool includeChildren, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task UnwatchAsync(string pageId, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<NotionWatchSubscriptionDto>> GetWatchersAsync(string pageId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NotionWatchSubscriptionDto>>([]);
        public Task<bool> IsWatchingAsync(string pageId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class ShellComments : ITmCommentProvider
    {
        public TmCommentProviderCapabilities Capabilities => TmCommentProviderCapabilities.Read;

        public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
            TmEntityRef entityRef, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmCommentThread>>([]);

        public Task<TmCommentThread> CreateThreadAsync(TmCommentThread thread, CancellationToken cancellationToken = default)
            => Task.FromResult(thread);

        public Task<TmCommentEntry> ReplyAsync(string threadId, TmCommentEntry entry, CancellationToken cancellationToken = default)
            => Task.FromResult(entry);

        public Task<TmCommentEntry> UpdateEntryAsync(string threadId, string entryId, TmCommentEntry entry, CancellationToken cancellationToken = default)
            => Task.FromResult(entry);

        public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteEntryAsync(string threadId, string entryId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<TmCommentThread> ResolveAsync(string threadId, TmUserRef? resolvedBy = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new TmCommentThread { Id = threadId });

        public Task<TmCommentThread> ReopenAsync(string threadId, TmUserRef? reopenedBy = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new TmCommentThread { Id = threadId });
    }
}
