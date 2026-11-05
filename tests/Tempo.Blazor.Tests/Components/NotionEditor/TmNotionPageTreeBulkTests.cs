using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.Sidebar;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionPageTreeBulkTests : LocalizationTestBase
{
    public TmNotionPageTreeBulkTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Cancel"] = "Cancel",
            ["TmNotionPageTree_MoveTo"] = "Move to",
            ["TmNotionPageTree_Copy"] = "Copy",
            ["TmNotionPageTree_Delete"] = "Delete",
            ["TmNotionPageTree_BulkToolbar"] = "Bulk actions",
            ["TmNotionPageTree_BulkSelected"] = "{0} selected",
            ["TmNotionPageTree_BulkMoveTitle"] = "Move pages ({0}) to…",
            ["TmNotionPageTree_BulkCopyTitle"] = "Copy pages ({0}) to…",
            ["TmNotionPageTree_BulkDeleteTitle"] = "Delete selected pages",
            ["TmNotionPageTree_BulkDeleteMessage_One"] = "{0} selected page will be moved to trash.",
            ["TmNotionPageTree_BulkDeleteMessage_Other"] = "{0} selected pages will be moved to trash.",
            ["TmNotionPageTree_BulkTargetTitle"] = "Select target page",
            ["TmNotionPageTree_BulkSelect"] = "Select",
            ["TmNotionPageTree_BulkMoveDescendantError"] = "Cannot move into own descendants.",
            ["TmNotionPageTree_BulkOperationError"] = "Bulk operation failed.",
            ["TmNotionPageTree_MoveToRoot"] = "Top level",
            ["TmNotionPageTree_MoveToSearch"] = "Search pages…",
            ["TmNotionPageTree_Untitled"] = "Untitled",
            ["TmBulkAction_ClearSelection"] = "Clear selection",
            ["TmNotionSidebar_Pages"] = "Pages",
            ["TmNotionPageTree_DeleteNo"] = "Cancel",
            ["TmNotionPageTree_DeleteYes"] = "Delete",
            ["TmNotionPageTree_NoPages"] = "No pages",
        });
    }

    [Fact]
    public void PageTree_RendersMultiSelectToolbarAndDeletesSelectedPages()
    {
        var provider = new BulkPageProvider();
        var changedCount = 0;
        var cut = RenderPageTree(provider, () => changedCount++);

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find(SelectTestId(BulkPageProvider.DeletePageId)).Change(true);

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='notion-page-bulk-toolbar']").Should().NotBeNull();
            cut.Find("[data-testid='notion-page-bulk-count']").TextContent.Should().Contain("2 selected");
        });

        cut.Find("[data-testid='notion-page-bulk-delete']").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-dialog").Should().NotBeNull());
        cut.Find(".tm-dialog-title").TextContent.Should().Be("Delete selected pages");

        cut.Find(".tm-dialog-btn-ok").Click();

        cut.WaitForAssertion(() =>
        {
            provider.DeletedPageIds.Should().Equal(
                BulkPageProvider.SourcePageId.ToString("D"),
                BulkPageProvider.DeletePageId.ToString("D"));
            changedCount.Should().Be(1);
            cut.FindAll("[data-testid='notion-page-bulk-toolbar']").Should().BeEmpty();
        });
    }

    [Fact]
    public void PageTree_BulkDeleteDialog_EscapeCancels_AndInitialFocusIsCancel()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-delete']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-dialog").Should().NotBeNull());
        var dialog = cut.FindComponent<TmDialog>();
        dialog.Instance.IsDangerous.Should().BeTrue();
        dialog.Instance.OkButtonText.Should().Be("Delete");
        dialog.Instance.CancelButtonText.Should().Be("Cancel");

        // Escape cancels (never deletes) — the dialog raises OnResult(false).
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut.FindAll(".tm-dialog").Should().BeEmpty());
        provider.DeletedPageIds.Should().BeEmpty();
    }

    [Fact]
    public void PageTree_BulkMoveToRootUsesDataProviderBulkEndpoint()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-move']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-page-bulk-target-search']").Should().NotBeNull());

        // Two-step: pick the target, then confirm with "Select".
        cut.Find("[data-testid='notion-page-bulk-target-root']").Click();
        cut.Find("[data-testid='notion-page-bulk-target-root'][aria-selected='true']").Should().NotBeNull();
        cut.Find("[data-testid='notion-page-bulk-target-select']").Click();

        cut.WaitForAssertion(() =>
        {
            provider.MovedPageIds.Should().Equal(BulkPageProvider.SourcePageId.ToString("D"));
            provider.MoveTargetPageId.Should().BeNull();
        });
    }

    [Fact]
    public void PageTree_BulkTargetPicker_WithoutQuery_ShowsTreeRootsExcludingSelection()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        // Select Source Root — its own subtree must not be offered as a target.
        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-move']").Click();

        cut.WaitForAssertion(() =>
        {
            var options = cut.FindAll("[role='option']");
            options.Should().NotBeEmpty();
            options.Select(o => o.TextContent).Should().Contain("Top level");
            // Source (selected) and its descendant are excluded; Target stays offerable.
            options.Select(o => o.TextContent).Should().NotContain("Source Root");
            options.Select(o => o.TextContent).Should().NotContain("Source Child");
        });
    }

    [Fact]
    public void PageTree_BulkTargetPicker_TitleContainsActionAndCount()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find(SelectTestId(BulkPageProvider.TargetPageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-copy']").Click();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-modal-title").TextContent.Should().Be("Copy pages (2) to…"));
    }

    [Fact]
    public void PageTree_BulkCopySearchesTargetAndCopiesSelectedTree()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-copy']").Click();
        cut.Find("[data-testid='notion-page-bulk-target-search']").Input("Target");

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-page-bulk-target-page']").TextContent.Should().Contain("Target Space"));

        // Two-step: select, then confirm.
        cut.Find("[data-testid='notion-page-bulk-target-page']").Click();
        cut.Find("[data-testid='notion-page-bulk-target-select']").Click();

        cut.WaitForAssertion(() =>
        {
            provider.CopiedTrees.Should().ContainSingle(copy =>
                copy.PageId == BulkPageProvider.SourcePageId.ToString("D") &&
                copy.TargetPageId == BulkPageProvider.TargetPageId.ToString("D"));
        });
    }

    [Fact]
    public void PageTree_BulkTargetPicker_ArrowKeysRoveSelection_AndEnterDoesNotConfirm()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-move']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-page-bulk-target-search']").Should().NotBeNull());

        var listbox = cut.Find(".tm-npt-bulk-panel__results");
        listbox.KeyDown("ArrowDown");
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-page-bulk-target-root']").GetAttribute("tabindex").Should().Be("-1"));

        // Roving tabindex + aria-selected moved to the first page option (aria-level = depth + 2).
        var firstPage = cut.Find("[data-testid='notion-page-bulk-target-page'][tabindex='0']");
        firstPage.GetAttribute("aria-level").Should().Be("2");
        firstPage.GetAttribute("aria-selected").Should().Be("true");

        // Enter SELECTS only — it must not confirm (no double activation).
        listbox.KeyDown("Enter");
        provider.MovedPageIds.Should().BeEmpty();

        // Confirmation happens only through the sticky Select button.
        cut.Find("[data-testid='notion-page-bulk-target-select']").Click();
        cut.WaitForAssertion(() =>
            provider.MovedPageIds.Should().Equal(BulkPageProvider.SourcePageId.ToString("D")));
    }

    [Fact]
    public void PageTree_BulkTargetPicker_HomeEndMoveWithinTheVisibleOptions()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-move']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-page-bulk-target-search']").Should().NotBeNull());

        var listbox = cut.Find(".tm-npt-bulk-panel__results");
        listbox.KeyDown("End");
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='notion-page-bulk-target-page']")
                .Last().GetAttribute("tabindex").Should().Be("0"));

        listbox.KeyDown("Home");
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-page-bulk-target-root']").GetAttribute("tabindex").Should().Be("0"));
    }

    [Fact]
    public void PageTree_BulkTargetPicker_ArrowRightExpands_ArrowLeftCollapses()
    {
        var provider = new BulkPageProvider();
        var cut = RenderPageTree(provider, () => { });

        cut.Find(SelectTestId(BulkPageProvider.SourcePageId)).Change(true);
        cut.Find("[data-testid='notion-page-bulk-move']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-page-bulk-target-search']").Should().NotBeNull());

        // ArrowDown to the first page option, then ArrowDown again onto "Target Space".
        var listbox = cut.Find(".tm-npt-bulk-panel__results");
        listbox.KeyDown("ArrowDown");
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='notion-page-bulk-target-page']")
                .First().GetAttribute("tabindex").Should().Be("0"));

        // Find the Target Space option and activate it, then expand with ArrowRight.
        var options = cut.FindAll("[data-testid='notion-page-bulk-target-page']");
        var targetIdx = options
            .Select((o, i) => (o, i))
            .First(t => t.o.TextContent.Contains("Target Space")).i;
        for (var i = 0; i < targetIdx; i++)
            listbox.KeyDown("ArrowDown");

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='notion-page-bulk-target-page']")
                .First(o => o.TextContent.Contains("Target Space"))
                .GetAttribute("tabindex").Should().Be("0"));

        listbox.KeyDown("ArrowRight");
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='notion-page-bulk-target-page']")
                .Select(o => o.TextContent).Should().Contain(t => t.Contains("Target Child")));

        // The child is one level deeper.
        var child = cut.FindAll("[data-testid='notion-page-bulk-target-page']")
            .First(o => o.TextContent.Contains("Target Child"));
        child.GetAttribute("aria-level").Should().Be("3");

        listbox.KeyDown("ArrowLeft");
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='notion-page-bulk-target-page']")
                .Select(o => o.TextContent).Should().NotContain(t => t.Contains("Target Child")));
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderPageTree(BulkPageProvider provider, Action changed)
    {
        var context = new NotionEditorContext
        {
            DataProvider = provider,
            SearchProvider = provider
        };

        return Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageTree>(child => child
                .Add(component => component.RootPages, provider.RootPages)
                .Add(component => component.OnTreeChanged, EventCallback.Factory.Create(this, changed))));
    }

    private static string SelectTestId(Guid pageId)
        => $"[data-testid='notion-page-select-{pageId:D}']";

    private sealed class BulkPageProvider : INotionDataProvider, INotionSearchProvider
    {
        public static readonly Guid SourcePageId = Guid.Parse("cf240001-0000-0000-0000-000000000001");
        public static readonly Guid DeletePageId = Guid.Parse("cf240005-0000-0000-0000-000000000001");
        public static readonly Guid TargetPageId = Guid.Parse("cf240004-0000-0000-0000-000000000001");
        public static readonly Guid SourceChildPageId = Guid.Parse("cf240002-0000-0000-0000-000000000001");
        public static readonly Guid TargetChildPageId = Guid.Parse("cf240006-0000-0000-0000-000000000001");

        private readonly List<NotionPage> _pages =
        [
            new() { Id = SourcePageId, Title = "Source Root", IconEmoji = "S" },
            new() { Id = SourceChildPageId, Title = "Source Child", IconEmoji = "C", ParentId = SourcePageId },
            new() { Id = DeletePageId, Title = "Delete Candidate", IconEmoji = "D" },
            new() { Id = TargetPageId, Title = "Target Space", IconEmoji = "T" },
            new() { Id = TargetChildPageId, Title = "Target Child", IconEmoji = "t", ParentId = TargetPageId }
        ];

        public IReadOnlyList<INotionPage> RootPages => _pages
            .Where(p => p.ParentId is null)
            .Cast<INotionPage>().ToList();
        public IReadOnlyList<string> MovedPageIds { get; private set; } = [];
        public string? MoveTargetPageId { get; private set; }
        public IReadOnlyList<string> DeletedPageIds { get; private set; } = [];
        public List<(string PageId, string? TargetPageId)> CopiedTrees { get; } = [];

        public Task<INotionPage> GetPageAsync(string pageId)
            => Task.FromResult<INotionPage>(_pages.Single(page => page.Id == Guid.Parse(pageId)));

        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId)
            => Task.FromResult(_pages.Where(page => page.ParentId?.ToString("D") == parentId).Cast<INotionPage>());

        public Task<IEnumerable<INotionPage>> GetFavoritesAsync()
            => Task.FromResult(Enumerable.Empty<INotionPage>());

        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count)
            => Task.FromResult(_pages.Take(count).Cast<INotionPage>());

        public Task<IEnumerable<INotionPage>> GetTrashAsync()
            => Task.FromResult(_pages.Where(page => page.IsDeleted).Cast<INotionPage>());

        public Task<INotionPage> CreatePageAsync(string? parentId, string title)
            => throw new NotSupportedException();

        public Task UpdatePageAsync(INotionPage page)
            => throw new NotSupportedException();

        public Task DeletePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task RestorePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task PermanentlyDeletePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task ToggleFavoriteAsync(string pageId, bool isFavorite)
            => throw new NotSupportedException();

        public Task MovePageAsync(string pageId, string? newParentId)
            => throw new NotSupportedException();

        public Task<INotionPage> DuplicatePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task MovePagesAsync(IReadOnlyList<string> pageIds, string? newParentId, CancellationToken cancellationToken = default)
        {
            MovedPageIds = pageIds.ToArray();
            MoveTargetPageId = newParentId;
            return Task.CompletedTask;
        }

        public Task DeletePagesAsync(IReadOnlyList<string> pageIds, CancellationToken cancellationToken = default)
        {
            DeletedPageIds = pageIds.ToArray();
            return Task.CompletedTask;
        }

        public Task<INotionPage> CopyPageTreeAsync(string pageId, string? newParentId, CancellationToken cancellationToken = default)
        {
            CopiedTrees.Add((pageId, newParentId));
            return Task.FromResult<INotionPage>(new NotionPage { Id = Guid.NewGuid(), Title = "Copied page" });
        }

        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<INotionPage>>([]);

        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IEnumerable<INotionPage>> SearchPagesAsync(string query, NotionSearchFilter? filter)
        {
            var results = _pages
                .Where(page => page.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Cast<INotionPage>();
            return Task.FromResult(results);
        }

        public Task<IEnumerable<NotionSearchResult>> SearchBlocksAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult(Enumerable.Empty<NotionSearchResult>());

        public Task<(IEnumerable<INotionPage> Pages, IEnumerable<NotionSearchResult> Blocks)> SearchAllAsync(
            string query,
            NotionSearchFilter? filter,
            int maxResults)
            => Task.FromResult<(IEnumerable<INotionPage>, IEnumerable<NotionSearchResult>)>(([], []));
    }
}
