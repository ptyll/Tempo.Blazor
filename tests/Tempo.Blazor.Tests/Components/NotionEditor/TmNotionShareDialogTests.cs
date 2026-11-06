using Bunit.Rendering;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionShareDialogTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public TmNotionShareDialogTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddSingleton<CommentNotificationOrchestrator>();

        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Loading"] = "Loading",
            ["TmNotionPageSettingsMenu_ErrorCopy"] = "Could not copy link.",
            ["Notion_Share_Title"] = "Public sharing",
            ["Notion_Share_CreateLink"] = "Create public link",
            ["Notion_Share_Revoke"] = "Revoke link",
            ["Notion_Share_Copy"] = "Copy",
            ["Notion_Share_AllowComments"] = "Allow public comments",
            ["Notion_Share_Expires"] = "Expires",
            ["Notion_Share_Disabled"] = "This public link is disabled or expired.",
            ["Notion_Share_Active"] = "Public link is active",
            ["Notion_Share_Expired"] = "Public link has expired.",
            ["Notion_Share_PublicUrl"] = "Public URL",
            ["Notion_Share_AnonymousReadOnly"] = "Anyone with the link can open this page in read-only mode.",
            ["Notion_Share_LoadError"] = "Public sharing could not be loaded.",
            ["Notion_Share_CreateError"] = "The public link could not be created.",
            ["Notion_Share_RevokeError"] = "The public link could not be revoked.",
            ["Notion_Share_Copied"] = "Copied",
            ["Notion_Share_RevokeConfirmTitle"] = "Revoke this link?",
            ["Notion_Share_RevokeConfirm"] = "Revoke link",
            ["Tm_Cancel"] = "Cancel",
            ["Notion_Share_PublicPage"] = "Shared page",
            ["Notion_Share_ReadOnlyNotice"] = "You are viewing a public read-only version.",
            ["Notion_Share_NotFoundTitle"] = "Public page unavailable",
            ["Notion_Share_ExpiresOn"] = "Expires {0}",
            ["TmNotionEditor_Loading"] = "Loading editor",
            ["TmNotionPage_WriteHint"] = "Write something",
            ["TmNotionPage_BlocksLoadError"] = "Blocks could not be loaded: {0}",
            ["Notion_PageComments_Title"] = "Page comments",
            ["Notion_PageComments_Add"] = "Comment",
            ["Notion_PageComments_Placeholder"] = "Add a comment to this page",
            ["Notion_PageComments_Empty"] = "No comments yet"
        });
    }

    [Fact]
    public void ShareDialog_CreatesPublicLink()
    {
        var provider = new FakePublicShareProvider();
        var cut = RenderDialog(provider);

        cut.Find("[data-testid='notion-share-allow-comments'] input.tm-toggle-input").Change(true);
        cut.Find("[data-testid='notion-share-create']").Click();

        cut.WaitForAssertion(() =>
        {
            provider.Share.Should().NotBeNull();
            provider.Share!.AllowComments.Should().BeTrue();
            cut.Find("[data-testid='notion-share-url']").GetAttribute("value").Should().Contain("/p/share-001");
            cut.Find("[data-testid='notion-share-active']").TextContent.Should().Contain("active");
        });
    }

    [Fact]
    public void ShareDialog_RevokesActiveLink()
    {
        var provider = new FakePublicShareProvider();
        provider.Seed(new PublicShareDto
        {
            PageId = PageId,
            Token = "share-active",
            IsEnabled = true
        });

        var cut = RenderDialog(provider);
        cut.WaitForAssertion(() => cut.Find("[data-testid='notion-share-url']").GetAttribute("value").Should().Contain("/p/share-active"));

        cut.Find("[data-testid='notion-share-revoke']").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-dialog").Should().NotBeNull());
        cut.Find(".tm-dialog .tm-dialog-btn-ok").Click();

        cut.WaitForAssertion(() =>
        {
            provider.Share.Should().NotBeNull();
            provider.Share!.IsEnabled.Should().BeFalse();
            cut.Find("[data-testid='notion-share-disabled']").TextContent.Should().Contain("disabled");
            cut.FindAll("[data-testid='notion-share-url']").Should().BeEmpty();
        });
    }

    [Fact]
    public void ShareDialog_ShowsExpiredLinkStatus()
    {
        var provider = new FakePublicShareProvider();
        provider.Seed(new PublicShareDto
        {
            PageId = PageId,
            Token = "share-expired",
            IsEnabled = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });

        var cut = RenderDialog(provider);

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='notion-share-expired']").TextContent.Should().Contain("expired");
            cut.FindAll("[data-testid='notion-share-url']").Should().BeEmpty();
            cut.Find("[data-testid='notion-share-create']").Should().NotBeNull();
        });
    }

    [Fact]
    public void PublicPage_ResolvesTokenAndRendersReadOnlyEditor()
    {
        var provider = new FakePublicShareProvider();
        provider.Seed(new PublicShareDto
        {
            PageId = PageId,
            Token = "share-public",
            IsEnabled = true,
            AllowComments = false
        });

        var data = new FakeDataProvider(PageId);
        var blocks = new FakeBlockService(PageId);

        var cut = Render(builder =>
        {
            builder.OpenComponent<TmNotionPublicPage>(0);
            builder.AddAttribute(1, nameof(TmNotionPublicPage.Token), "share-public");
            builder.AddAttribute(2, nameof(TmNotionPublicPage.PublicShareProvider), provider);
            builder.AddAttribute(3, nameof(TmNotionPublicPage.DataProvider), data);
            builder.AddAttribute(
                4,
                nameof(TmNotionPublicPage.AggregateProvider),
                new NotionAggregateTestAdapter(data, blocks));
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-notion-editor").ClassList.Should().Contain("tm-notion-editor--locked");
            cut.FindAll(".tm-notion-sidebar").Should().BeEmpty();
            cut.Find(".tm-notion-editable").GetAttribute("contenteditable").Should().Be("false");
        });
    }

    [Fact]
    public async Task ShareDialog_Close_RaisesOnCloseOnce()
    {
        // Escape is the core TmModal's own handler (CloseOnEscape default true) — the dialog keeps
        // no @onkeydown of its own, so a keydown here cannot reach it. The close button is the same
        // OnClose path that Escape takes.
        var closes = 0;
        var cut = Render<TmNotionShareDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.PageId, PageId)
            .Add(x => x.Provider, new FakePublicShareProvider())
            .Add(x => x.VisibleChanged, () => closes++));

        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-share-dialog]").Should().NotBeNull());
        await cut.Find(".tm-modal-close").ClickAsync(new MouseEventArgs());

        closes.Should().Be(1, "closing raises OnClose exactly once");
    }

    [Fact]
    public async Task ShareDialog_Copy_AnnouncesCopiedInALiveRegion()
    {
        var provider = new FakePublicShareProvider();
        provider.Seed(new PublicShareDto { PageId = PageId, Token = "share-copy", IsEnabled = true });
        var cut = Render<TmNotionShareDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.PageId, PageId)
            .Add(x => x.Provider, provider)
            .Add(x => x.ConfirmRevoke, true));
        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-share-url]").Should().NotBeNull());

        await cut.Find("[data-testid=notion-share-copy]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=notion-share-copied]").TextContent.Should().Contain("Copied"));
    }

    [Fact]
    public async Task ShareDialog_Revoke_ConfirmsBeforeCallingTheProvider()
    {
        var provider = new FakePublicShareProvider();
        provider.Seed(new PublicShareDto { PageId = PageId, Token = "share-revoke", IsEnabled = true });
        var cut = Render<TmNotionShareDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.PageId, PageId)
            .Add(x => x.Provider, provider)
            .Add(x => x.ConfirmRevoke, true));
        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-share-revoke]").Should().NotBeNull());

        await cut.Find("[data-testid=notion-share-revoke]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find(".tm-dialog").Should().NotBeNull());
        provider.RevokeCalls.Should().Be(0, "the provider is not called until the confirmation");

        await cut.Find(".tm-dialog .tm-dialog-btn-ok").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => provider.RevokeCalls.Should().Be(1));
    }

    [Fact]
    public void ShareDialog_Footer_SeparatesTheDestructiveAction()
    {
        var provider = new FakePublicShareProvider();
        provider.Seed(new PublicShareDto { PageId = PageId, Token = "share-footer", IsEnabled = true });
        var cut = RenderDialog(provider);

        cut.WaitForAssertion(() =>
        {
            var footer = cut.Find("[data-testid=notion-share-footer]");
            footer.TextContent.Should().Contain("Close");
            footer.ClassList.Should().Contain("tm-npsd__footer--split");
            cut.Find("[data-testid=notion-share-revoke]").ClassList.Should().Contain("tm-btn-danger");
        });
    }

    [Fact]
    public void ShareDialog_RidesTheCoreModal_WithNoOwnBackdrop()
    {
        var cut = RenderDialog(new FakePublicShareProvider());

        cut.WaitForAssertion(() => cut.Find(".tm-modal-overlay").Should().NotBeNull());
        cut.FindAll(".tm-npsd-backdrop").Should().BeEmpty();
    }

    private IRenderedComponent<ContainerFragment> RenderDialog(FakePublicShareProvider provider)
        => Render(builder =>
        {
            builder.OpenComponent<TmNotionShareDialog>(0);
            builder.AddAttribute(1, nameof(TmNotionShareDialog.Visible), true);
            builder.AddAttribute(2, nameof(TmNotionShareDialog.PageId), PageId);
            builder.AddAttribute(3, nameof(TmNotionShareDialog.Provider), provider);
            builder.CloseComponent();
        });

    private sealed class FakePublicShareProvider : INotionPublicShareProvider
    {
        public PublicShareDto? Share { get; private set; }
        private int _token;

        public void Seed(PublicShareDto share) => Share = Clone(share);

        public Task<PublicShareDto> CreateShareAsync(Guid pageId, PublicShareOptions options, CancellationToken cancellationToken = default)
        {
            Share = new PublicShareDto
            {
                PageId = pageId,
                Token = $"share-{Interlocked.Increment(ref _token):000}",
                IsEnabled = true,
                AllowComments = options.AllowComments,
                ExpiresAt = options.ExpiresAt
            };
            return Task.FromResult(Clone(Share));
        }

        public int RevokeCalls { get; private set; }

        public Task RevokeAsync(Guid pageId, CancellationToken cancellationToken = default)
        {
            RevokeCalls++;
            if (Share?.PageId == pageId)
                Share.IsEnabled = false;

            return Task.CompletedTask;
        }

        public Task<PublicShareDto?> GetShareAsync(Guid pageId, CancellationToken cancellationToken = default)
            => Task.FromResult(Share?.PageId == pageId ? Clone(Share) : null);

        public Task<PublicShareDto?> ResolveByTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(Share is { IsEnabled: true } && Share.Token == token ? Clone(Share) : null);

        private static PublicShareDto Clone(PublicShareDto share) => new()
        {
            PageId = share.PageId,
            Token = share.Token,
            IsEnabled = share.IsEnabled,
            AllowComments = share.AllowComments,
            ExpiresAt = share.ExpiresAt
        };
    }

    private sealed class FakeDataProvider(Guid pageId) : INotionDataProvider
    {
        private readonly NotionPage _page = new()
        {
            Id = pageId,
            Title = "Public test page",
            IconEmoji = "P",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastEditedAt = DateTime.UtcNow
        };

        public Task<INotionPage> GetPageAsync(string pageId) => Task.FromResult<INotionPage>(_page);
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult<IEnumerable<INotionPage>>([_page]);
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => throw new NotSupportedException();
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => throw new NotSupportedException();
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBlockService(Guid pageId) : INotionEditorBlockService
    {
        private readonly PageBlock _block = new()
        {
            Id = Guid.Parse("cf330000-0000-0000-0000-000000000001"),
            PageId = pageId,
            Type = BlockType.Paragraph,
            Order = 0,
            Content = new TextBlockContent { Html = "Read-only public content" }
        };

        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId) => Task.FromResult<IEnumerable<IPageBlock>>([_block]);
        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId) => throw new NotSupportedException();
        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId) => throw new NotSupportedException();
        public Task UpdateBlockAsync(IPageBlock block) => Task.CompletedTask;
        public Task DeleteBlockAsync(string blockId) => Task.CompletedTask;
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => Task.CompletedTask;
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => Task.CompletedTask;
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => Task.CompletedTask;
        public Task<IPageBlock> DuplicateBlockAsync(string blockId) => throw new NotSupportedException();
        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType) => throw new NotSupportedException();
        public Task<string> GetBlockLinkAsync(string blockId) => Task.FromResult($"#{blockId}");
    }
}
