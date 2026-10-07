using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public class TmNotionPageSettingsMenuExportTests : LocalizationTestBase
{
    public TmNotionPageSettingsMenuExportTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ExportMenu_RendersAllDocumentFormatsAndIncludeSubpagesToggle()
    {
        var provider = new FakeImportExportProvider();
        var cut = RenderMenu(provider);

        cut.Find(".tm-npsm-trigger").Click();
        cut.FindAll(".tm-npsm__item").First(item => item.TextContent.Contains("Export")).Click();

        cut.Find("[data-testid='notion-export-menu']").TextContent.Should().Contain("Markdown");
        cut.Find("[data-testid='notion-export-menu']").TextContent.Should().Contain("HTML");
        cut.Find("[data-testid='notion-export-menu']").TextContent.Should().Contain("PDF");
        cut.Find("[data-testid='notion-export-menu']").TextContent.Should().Contain("DOCX");
        cut.Find("[data-testid='notion-export-menu']").TextContent.Should().Contain("ODT");
        cut.Find("[data-testid='notion-export-include-subpages']").Should().NotBeNull();
    }

    [Fact]
    public void ExportMenu_IsHiddenWithoutProvider()
    {
        var cut = RenderMenu(importExportProvider: null);

        cut.Find(".tm-npsm-trigger").Click();

        cut.Markup.Should().NotContain("data-testid=\"notion-export-menu\"");
    }

    [Fact]
    public void ExportMenu_UsesSubpageExportWhenToggleIsChecked()
    {
        var provider = new FakeImportExportProvider();
        var cut = RenderMenu(provider);

        cut.Find(".tm-npsm-trigger").Click();
        cut.FindAll(".tm-npsm__item").First(item => item.TextContent.Contains("Export")).Click();
        cut.Find("[data-testid='notion-export-include-subpages']").Change(true);
        cut.Find("[data-testid='notion-export-docx']").Click();

        provider.LastFormat.Should().Be(NotionExportFormat.Docx);
        provider.IncludeSubpages.Should().BeTrue();
    }

    [Fact]
    public void ImportMenu_UploadsSelectedWordFileAndNavigatesToImportedPage()
    {
        var provider = new FakeImportExportProvider();
        string? navigatedPageId = null;
        var cut = RenderMenu(provider, pageId => navigatedPageId = pageId);

        cut.Find(".tm-npsm-trigger").Click();
        cut.FindAll(".tm-npsm__item").First(item => item.TextContent.Contains("Import")).Click();

        cut.Find("[data-testid='notion-import-menu']").TextContent.Should().Contain("Word (.docx)");
        cut.Find("[data-testid='notion-import-menu']").TextContent.Should().Contain("HTML (.html)");
        cut.Find("[data-testid='notion-import-menu']").TextContent.Should().Contain("Markdown (.md)");

        cut.Find("[data-testid='notion-import-word']").Click();
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3], "import.docx", contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));

        cut.WaitForAssertion(() =>
        {
            provider.LastImportFormat.Should().Be(NotionImportFormat.Word);
            provider.ImportedBytes.Should().Equal([1, 2, 3]);
            navigatedPageId.Should().Be(provider.ImportedPageId.ToString("D"));
        });
    }

    [Fact]
    public void PageInfoItem_RaisesPageInfoRequested()
    {
        var requested = false;
        var cut = RenderMenu(importExportProvider: null, onPageInfoRequested: () => requested = true);

        cut.Find(".tm-npsm-trigger").Click();
        cut.FindAll(".tm-npsm__item").First(item => item.TextContent.Contains("Page info")).Click();

        requested.Should().BeTrue();
        cut.FindAll(".tm-npsm").Should().BeEmpty();
    }

    [Fact]
    public void OpenMenu_ClickOutsideBackdrop_UsesScopedClassNotInlineZIndex()
    {
        var cut = RenderMenu(importExportProvider: null);

        cut.Find(".tm-npsm-trigger").Click();

        // CF10n follow-up: the click-outside layer sits one step under the popover
        // band via the scoped class. The old inline z-index:9998 escaped the CSS
        // band sweep, covered the 1030 panel and made every menu item unclickable
        // (Playwright hit-test reported an aria-hidden div intercepting).
        var backdrop = cut.Find(".tm-npsm__backdrop");
        backdrop.GetAttribute("style").Should().BeNullOrEmpty();
    }

    [Fact]
    public void NotionEditorRazor_BackdropLayers_CarryNoInlineZIndex()
    {
        // Inline z-indexes are invisible to the --tm-z-* band sweeps: the page-settings
        // backdrop kept z-index:9998 above the migrated 1030 panel. The whole tree is
        // scanned so the next inline band value cannot hide in markup again.
        var notionEditorDir = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.NotionEditor");
        var offenders = Directory
            .EnumerateFiles(notionEditorDir, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Where(line => line.Contains("style=") && line.Contains("z-index"))
                .Select(line => $"{file}: {line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderMenu(
        FakeImportExportProvider? importExportProvider,
        Action<string>? onNavigateToImportedPage = null,
        Action? onPageInfoRequested = null)
    {
        var context = new NotionEditorContext
        {
            DataProvider = new EmptyNotionProvider(),
            BlockService = new EmptyNotionProvider(),
            ImportExportProvider = importExportProvider
        };

        return Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, context)
            .AddChildContent<TmNotionPageSettingsMenu>(child => child
                .Add(p => p.Page, new NotionPage
                {
                    Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Title = "Export Page",
                    CreatedAt = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                })
                .Add(p => p.OnNavigateToImportedPage, EventCallback.Factory.Create<string>(this, onNavigateToImportedPage ?? (_ => { })))
                .Add(p => p.OnPageInfoRequested, EventCallback.Factory.Create(this, onPageInfoRequested ?? (() => { })))));
    }

    private sealed class FakeImportExportProvider : INotionImportExportProvider
    {
        public Guid ImportedPageId { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        public NotionExportFormat? LastFormat { get; private set; }

        public bool IncludeSubpages { get; private set; }

        public NotionImportFormat? LastImportFormat { get; private set; }

        public byte[] ImportedBytes { get; private set; } = [];

        public Task<Stream> ExportPageAsync(string pageId, NotionExportFormat format)
        {
            LastFormat = format;
            IncludeSubpages = false;
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }

        public Task<Stream> ExportPageWithSubpagesAsync(string pageId, NotionExportFormat format)
        {
            LastFormat = format;
            IncludeSubpages = true;
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }

        public async Task<INotionPage> ImportAsync(Stream content, NotionImportFormat format, string? targetParentPageId)
        {
            LastImportFormat = format;
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory);
            ImportedBytes = memory.ToArray();
            return new NotionPage { Id = ImportedPageId, Title = "Imported" };
        }
    }

    private sealed class EmptyNotionProvider : INotionDataProvider, INotionEditorBlockService
    {
        public Task<INotionPage> GetPageAsync(string pageId) => Task.FromResult<INotionPage>(new NotionPage { Id = Guid.Parse(pageId), Title = "Page" });
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => Task.FromResult<INotionPage>(new NotionPage { Id = Guid.NewGuid(), ParentId = Guid.TryParse(parentId, out var id) ? id : null, Title = title });
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => Task.FromResult<INotionPage>(new NotionPage { Id = Guid.NewGuid(), Title = "Duplicate" });
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId) => Task.FromResult(block);
        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId) => Task.FromResult(blocks);
        public Task UpdateBlockAsync(IPageBlock block) => Task.CompletedTask;
        public Task DeleteBlockAsync(string blockId) => Task.CompletedTask;
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => Task.CompletedTask;
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => Task.CompletedTask;
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => Task.CompletedTask;
        public Task<IPageBlock> DuplicateBlockAsync(string blockId) => Task.FromResult<IPageBlock>(new PageBlock { Id = Guid.NewGuid(), Type = BlockType.Paragraph, Content = new TextBlockContent() });
        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType) => Task.FromResult<IPageBlock>(new PageBlock { Id = Guid.Parse(blockId), Type = newType, Content = new TextBlockContent() });
        public Task<string> GetBlockLinkAsync(string blockId) => Task.FromResult($"#{blockId}");
    }
}
