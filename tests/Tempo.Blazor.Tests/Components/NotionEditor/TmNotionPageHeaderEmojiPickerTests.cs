using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// The page-header emoji picker is a core overlay. Escape on the search field — the element that
/// used to swallow it — closes the picker and returns focus to the trigger that opened it.
/// </summary>
public sealed class TmNotionPageHeaderEmojiPickerTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    [Fact]
    public async Task EscapeOnSearch_ClosesPicker_AndReturnsFocusToTheTrigger()
    {
        var cut = RenderHeader();

        cut.Find(".tm-notion-header-icon").Click();
        cut.WaitForAssertion(() => cut.FindComponent<TmOverlayPanel>().Should().NotBeNull());

        await cut.Find(".tm-notion-emoji-picker__search").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
            cut.FindAll(".tm-notion-emoji-picker").Should().BeEmpty();
        });
        JSInterop.Invocations
            .Count(i => i.Identifier == FocusInvocation)
            .Should().BeGreaterThan(0, "closing the picker returns focus to the icon trigger");
    }

    [Fact]
    public void Mobile_PresentsThePickerAsTheCoreSheet()
    {
        var cut = RenderHeader(TmLayoutMode.Mobile);

        cut.Find(".tm-notion-header-icon").Click();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-overlay-panel-sheet, .tm-drawer").Should().NotBeNull());
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderHeader(TmLayoutMode mode = TmLayoutMode.Desktop)
    {
        var page = new NotionPage
        {
            Id = Guid.Parse("aa310000-0000-0000-0000-000000000001"),
            Title = "Header",
            IconEmoji = "📄",
        };
        return Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = new HeaderProvider() })
            .Add(p => p.IsFixed, false)
            .AddChildContent<CascadingValue<TmLayoutContext>>(layout => layout
                .Add(p => p.Value, new TmLayoutContext(mode, mode))
                .Add(p => p.IsFixed, false)
                .AddChildContent<TmNotionPageHeader>(header => header.Add(h => h.Page, page))));
    }

    private sealed class HeaderProvider : INotionDataProvider
    {
        public Task<INotionPage> GetPageAsync(string pageId) => throw new NotSupportedException();
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
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
