using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.Sidebar;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 4 (N3): trash rows are cards with the page icon, a parent path resolved from the loaded
/// tree, permanently-visible actions on coarse pointers, and an inline confirm ordered
/// „Cancel | Yes, delete“ (secondary first, destructive separated last).
/// </summary>
public sealed class TmNotionSidebarTrashTests : LocalizationTestBase
{
    public TmNotionSidebarTrashTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Retry"] = "Retry",
            ["TmNotionSidebarTrash_Title"] = "Trash",
            ["TmNotionSidebarTrash_Back"] = "Back",
            ["TmNotionSidebarTrash_SearchPlaceholder"] = "Search trash",
            ["TmNotionSidebarTrash_Loading"] = "Loading",
            ["TmNotionSidebarTrash_LoadError"] = "Could not load the trash.",
            ["TmNotionSidebarTrash_Empty"] = "Trash is empty",
            ["TmNotionSidebarTrash_EmptySearch"] = "No matches",
            ["TmNotionSidebarTrash_Untitled"] = "Untitled",
            ["TmNotionSidebarTrash_DeletedToday"] = "Deleted today",
            ["TmNotionSidebarTrash_Restore"] = "Restore",
            ["TmNotionSidebarTrash_DeletePermanent"] = "Delete permanently",
            ["TmNotionSidebarTrash_DeleteConfirm"] = "Delete permanently?",
            ["TmNotionSidebarTrash_DeleteNo"] = "Cancel",
            ["TmNotionSidebarTrash_DeleteYes"] = "Yes, delete",
        });
    }

    [Fact]
    public void Trash_RowsAreCardsWithIconAndParentPath()
    {
        var provider = new TrashProvider();
        var cut = RenderTrash(provider);

        cut.WaitForAssertion(() => cut.FindAll(".tm-nst-item").Should().HaveCount(1));
        // The no-icon fallback is the lucide "file-text" glyph, not a letter/emoji stand-in.
        var icon = cut.Find(".tm-nst-item__icon");
        icon.QuerySelector("svg path[d^='M15 2H6a2 2 0 0 0-2 2v16']").Should().NotBeNull();
        icon.TextContent.Trim().Should().BeEmpty();
        cut.Find(".tm-nst-item__path").TextContent.Should().Be("Parent Page");
        cut.Find(".tm-nst-item__title").TextContent.Should().Be("Deleted Child");
    }

    [Fact]
    public void Trash_EmojiIcon_IsRenderedWhenPresent()
    {
        var provider = new TrashProvider();
        provider.SetIcon("🗑");
        var cut = RenderTrash(provider);

        cut.WaitForAssertion(() => cut.FindAll(".tm-nst-item").Should().HaveCount(1));
        cut.Find(".tm-nst-item__icon").TextContent.Trim().Should().Be("🗑");
        cut.Find(".tm-nst-item__icon").QuerySelector("svg").Should().BeNull();
    }

    [Fact]
    public void Trash_InlineConfirm_OrdersCancelBeforeYes()
    {
        var provider = new TrashProvider();
        var cut = RenderTrash(provider);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nst-item").Should().HaveCount(1));

        cut.Find(".tm-nst-item__btn--delete").Click();

        var actions = cut.Find(".tm-nst-item__confirm-actions");
        actions.Children[0].TextContent.Should().Be("Cancel");
        actions.Children[1].TextContent.Should().Be("Yes, delete");
    }

    [Fact]
    public void Trash_ConfirmYes_PermanentlyDeletes()
    {
        var provider = new TrashProvider();
        var cut = RenderTrash(provider);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nst-item").Should().HaveCount(1));

        cut.Find(".tm-nst-item__btn--delete").Click();
        cut.Find(".tm-nst-item__confirm-yes").Click();

        cut.WaitForAssertion(() =>
        {
            provider.PermanentlyDeleted.Should().Equal(TrashProvider.DeletedChildId.ToString());
            cut.FindAll(".tm-nst-item").Should().BeEmpty();
        });
    }

    [Fact]
    public void Trash_ActionsAreNotHoverOnly_OnCoarsePointers()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor", "Sidebar", "TmNotionSidebarTrash.razor.css"));

        // The actions must never be display:none — visibility may only ever be permanent or
        // coarse-promoted, never hover-gated.
        System.Text.RegularExpressions.Regex.Matches(css, @"tm-nst-item__actions[^}]*display\s*:\s*none")
            .Should().BeEmpty();
    }

    [Fact]
    public void Trash_ActionTargets_MeetMinimumHeights()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.NotionEditor",
            "Components", "NotionEditor", "Sidebar", "TmNotionSidebarTrash.razor.css"));

        // Fine pointers: ≥32px (WCAG 2.5.8). Coarse: promoted to 44px inside the coarse media query.
        var fine = System.Text.RegularExpressions.Regex.Match(css,
            @"\.tm-nst-item__btn\s*\{(?<body>[^}]*)\}");
        fine.Success.Should().BeTrue();
        fine.Groups["body"].Value.Should().Contain("min-height: 32px");

        var coarse = System.Text.RegularExpressions.Regex.Match(css,
            @"@media \(hover: none\), \(pointer: coarse\)\s*\{(?<body>.*)\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        coarse.Success.Should().BeTrue();
        coarse.Groups["body"].Value.Should().Contain("min-height: 44px");
        coarse.Groups["body"].Value.Should().Contain("opacity: 1");
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderTrash(TrashProvider provider)
        => Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = provider })
            .AddChildContent<TmNotionSidebarTrash>());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find TempoBlazor.slnx.");
    }

    private sealed class TrashProvider : INotionDataProvider
    {
        public static readonly Guid ParentPageId = Guid.Parse("cf250001-0000-0000-0000-000000000001");
        public static readonly Guid DeletedChildId = Guid.Parse("cf250002-0000-0000-0000-000000000001");

        private readonly List<NotionPage> _pages =
        [
            new() { Id = ParentPageId, Title = "Parent Page" },
            new() { Id = DeletedChildId, Title = "Deleted Child", ParentId = ParentPageId, IsDeleted = true, DeletedAt = DateTime.UtcNow.AddHours(2) },
        ];

        public List<string> PermanentlyDeleted { get; } = [];

        public void SetIcon(string emoji) => _pages[1].IconEmoji = emoji;

        public Task<INotionPage> GetPageAsync(string pageId)
            => Task.FromResult<INotionPage>(_pages.Single(p => p.Id == Guid.Parse(pageId)));
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId)
            => Task.FromResult(_pages.Where(p => p.ParentId?.ToString("D") == parentId).Cast<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetTrashAsync()
            => Task.FromResult(_pages.Where(p => p.IsDeleted).Cast<INotionPage>());
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => throw new NotSupportedException();
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId)
        {
            PermanentlyDeleted.Add(pageId);
            _pages.RemoveAll(p => p.Id.ToString("D") == pageId);
            return Task.CompletedTask;
        }
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => throw new NotSupportedException();
    }
}
