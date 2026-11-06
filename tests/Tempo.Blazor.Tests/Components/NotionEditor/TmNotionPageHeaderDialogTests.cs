using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 4 (N1): the page-header dialogs ride the core <see cref="TmModal"/> — a visible title, ×,
/// Escape close, focus return to the trigger and <c>aria-pressed</c> on the active cover preset.
/// No own backdrop or z-index.
/// </summary>
public sealed class TmNotionPageHeaderDialogTests : LocalizationTestBase
{
    public TmNotionPageHeaderDialogTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Cancel"] = "Cancel",
            ["Tm_Close"] = "Close",
            ["TmNotionPage_AddIcon"] = "Add icon",
            ["TmNotionPage_AddCover"] = "Add cover",
            ["TmNotionPage_ChangeCover"] = "Change cover",
            ["TmNotionPage_RemoveCover"] = "Remove cover",
            ["TmNotionPage_IconLabel"] = "Page icon",
            ["TmNotionPage_TitlePlaceholder"] = "Untitled",
            ["TmNotionPageHeader_CoverDialogTitle"] = "Change cover",
            ["TmNotionPageHeader_CoverUrl"] = "Image URL",
            ["TmNotionPageHeader_CoverPresets"] = "Preset covers",
            ["TmNotionPageHeader_SetCover"] = "Set cover",
            ["TmNotionPageHeader_EmojiPickerTitle"] = "Choose icon",
            ["TmNotionPageHeader_EmojiSearch"] = "Search icons",
            ["TmNotionPageHeader_RemoveIcon"] = "Remove icon",
            ["TmNotionEmojiPicker_RecentlyUsed"] = "Recently used",
        });
    }

    [Fact]
    public void CoverDialog_HasVisibleTitleAndAccessibleUrl()
    {
        var cut = RenderHeader(new NotionPage { Id = Guid.NewGuid(), Title = "T" });

        cut.FindAll(".tm-notion-header-meta__add-btn")[1].Click();

        var modal = cut.FindComponent<TmModal>();
        modal.Instance.Title.Should().Be("Change cover");
        cut.Find(".tm-modal-title").TextContent.Should().Be("Change cover");
        cut.Find(".tm-modal-close").Should().NotBeNull();

        var urlInput = cut.Find(".tm-notion-cover-dialog__input");
        urlInput.GetAttribute("type").Should().Be("url");
        urlInput.Id.Should().Be("tm-notion-cover-url");
        cut.Find("label[for='tm-notion-cover-url']").TextContent.Should().Be("Image URL");
    }

    [Fact]
    public void CoverDialog_EscapeFromAnyElement_ClosesTheDialog()
    {
        var cut = RenderHeader(new NotionPage { Id = Guid.NewGuid(), Title = "T" });

        cut.FindAll(".tm-notion-header-meta__add-btn")[1].Click();
        cut.FindComponents<TmModal>().Should().Contain(m => m.Instance.Show);

        // The focus trap owns Escape at the document level — the trap callback is what any focused
        // element inside the dialog (here the URL input) triggers.
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut.FindAll(".tm-notion-cover-dialog__input").Should().BeEmpty());
    }

    [Fact]
    public void CoverDialog_CurrentPreset_HasPressedStateAndCheck()
    {
        var page = new NotionPage
        {
            Id = Guid.NewGuid(),
            Title = "T",
            CoverImageUrl = "linear-gradient(135deg,#667eea 0%,#764ba2 100%)",
        };
        var cut = RenderHeader(page);

        cut.Find(".tm-notion-header-cover__btn").Click();

        var pressed = cut.Find(".tm-notion-cover-dialog__preset[aria-pressed='true']");
        pressed.Should().NotBeNull();
        pressed.TextContent.Should().Contain("✓");
        cut.FindAll(".tm-notion-cover-dialog__preset[aria-pressed='false']").Should().HaveCount(7);
    }

    [Fact]
    public void CoverDialog_UsesCoreModal_WithoutOwnBackdropOrZIndex()
    {
        var css = File.ReadAllText(Path.Combine(FindNotionRoot(), "Page", "TmNotionPageHeader.razor.css"));
        css.Should().NotContain("tm-notion-cover-dialog-backdrop");
        var dialogBlock = css.Split(["tm-notion-cover-dialog"], StringSplitOptions.None);
        dialogBlock.Should().HaveCountGreaterThan(1, "the cover dialog keeps its content classes");
    }

    [Fact]
    public void CoverDialog_InitialFocusLandsOnTheUrlInput()
    {
        var cut = RenderHeader(new NotionPage { Id = Guid.NewGuid(), Title = "T" });

        cut.FindAll(".tm-notion-header-meta__add-btn")[1].Click();

        // Opening the dialog must not park focus on the × or a preset — the URL field is the
        // primary action (UX review of the phase-4 cover capture).
        cut.FindComponent<TmModal>().Instance.InitialFocusSelector.Should().Be("#tm-notion-cover-url");
    }

    [Fact]
    public void CoverDialog_DisabledSetCover_UsesDisabledSurfaces_NotPrimaryOpacity()
    {
        var css = File.ReadAllText(Path.Combine(FindNotionRoot(), "..", "..", "wwwroot", "css", "tempo-blazor-notion-editor.css"));

        // A disabled primary must read as disabled — core disabled tokens, never opacity on the
        // primary colour (worst in dark mode). The rule lives in the package global CSS, scoped to
        // the cover dialog class the TmModal carries, because scoped CSS cannot reach the portal.
        var match = System.Text.RegularExpressions.Regex.Match(css,
            @"\.tm-notion-cover-dialog \.tm-modal-footer \.tm-btn-primary:disabled\s*\{(?<body>[^}]*)\}");
        match.Success.Should().BeTrue("the cover footer overrides the disabled primary");
        match.Groups["body"].Value.Should().Contain("var(--tm-bg-muted)");
        match.Groups["body"].Value.Should().Contain("var(--tm-text-disabled)");
        match.Groups["body"].Value.Should().Contain("opacity: 1");
    }

    [Fact]
    public void EmojiPicker_IsATitledModalWithCloseButton()
    {
        var cut = RenderHeader(new NotionPage { Id = Guid.NewGuid(), Title = "T", IconEmoji = "📄" });

        cut.Find(".tm-notion-header-icon").Click();

        var modal = cut.FindComponents<TmModal>().Single(m => m.Instance.Show);
        modal.Instance.Title.Should().Be("Choose icon");
        cut.Find(".tm-modal-title").TextContent.Should().Be("Choose icon");
        cut.Find(".tm-modal-close").Should().NotBeNull();
        cut.Find(".tm-notion-emoji-picker__search").Should().NotBeNull();
    }

    [Fact]
    public void EmojiPicker_Escape_ClosesTheModal()
    {
        var cut = RenderHeader(new NotionPage { Id = Guid.NewGuid(), Title = "T", IconEmoji = "📄" });

        cut.Find(".tm-notion-header-icon").Click();
        cut.FindComponents<TmModal>().Should().Contain(m => m.Instance.Show);

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut.FindAll(".tm-notion-emoji-picker__search").Should().BeEmpty());
    }

    [Fact]
    public void EmojiPicker_Mobile_PresentsAsCoreSheet()
    {
        var cut = RenderHeader(new NotionPage { Id = Guid.NewGuid(), Title = "T", IconEmoji = "📄" }, TmLayoutMode.Mobile);

        cut.Find(".tm-notion-header-icon").Click();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-modal--sheet").Should().NotBeNull());
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderHeader(NotionPage page, TmLayoutMode mode = TmLayoutMode.Desktop)
        => Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = new HeaderProvider() })
            .Add(p => p.IsFixed, false)
            .AddChildContent<CascadingValue<TmLayoutContext>>(layout => layout
                .Add(p => p.Value, new TmLayoutContext(mode, mode))
                .Add(p => p.Name, TmLayoutScopes.Viewport)
                .AddChildContent<CascadingValue<TmLayoutContext>>(viewport => viewport
                    .Add(p => p.Value, new TmLayoutContext(mode, mode))
                    .AddChildContent<TmNotionPageHeader>(header => header.Add(h => h.Page, page)))));

    private static string FindNotionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "Tempo.Blazor.NotionEditor", "Components", "NotionEditor");
    }

    private sealed class HeaderProvider : INotionDataProvider
    {
        public Task<INotionPage> GetPageAsync(string pageId) => throw new NotSupportedException();
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => throw new NotSupportedException();
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => throw new NotSupportedException();
    }
}
