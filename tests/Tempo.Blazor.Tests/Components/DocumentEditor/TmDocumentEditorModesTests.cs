using System.Text.Json;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Interfaces;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 7: the doc region has two modes besides editing. The print preview (DE-1.8) has no ribbon, page thumbnails on the
/// left, a page / zoom bar and the actions and the document information on the right; the comparison (DE-2.6, decision DE3) shows the diff
/// as text aligned by block, with "Back to document", and is opened from the version panel ("Show differences").
/// </summary>
public class TmDocumentEditorModesTests : LocalizationTestBase
{
    private static readonly Action<DocumentEditorDocument> NoSeed = _ => { };

    private IRenderedComponent<TmDocumentEditor> RenderEditor(
        TmLayoutMode mode = TmLayoutMode.Desktop,
        bool withPdf = true,
        Action<DocumentEditorDocument>? seed = null,
        InMemoryDocumentEditorProvider? provider = null)
    {
        provider ??= new InMemoryDocumentEditorProvider();
        var document = provider.SeedContractDocument("doc-1");
        if (seed is not null)
        {
            seed(document);
            provider.SaveAsync(new DocumentEditorSaveRequest
            {
                DocumentId = document.DocumentId,
                Document = document,
                ConcurrencyMode = DocumentEditorConcurrencyMode.Force
            }).GetAwaiter().GetResult();
        }

        var cut = RenderDocumentEditor(parameters =>
        {
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.LayoutMode, mode);
            if (withPdf)
            {
                parameters.Add(p => p.PdfExportProvider, new PdfProvider());
            }
        });

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        return cut;
    }

    // The engine reports the print preview through the pushed UI state of the mini-toolbar callback, exactly as in the browser.
    private async Task SetPrintPreviewAsync(IRenderedComponent<TmDocumentEditor> cut, bool active, int zoomPercent = 100, string zoomPreset = "fitWidth", string zoomAutoFit = "off")
    {
        var formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState
        {
            PrintPreviewActive = active,
            ZoomPercent = zoomPercent,
            ZoomPreset = zoomPreset,
            ZoomAutoFit = zoomAutoFit
        };
        SetDocumentCanvasFormattingStateJson(JsonSerializer.Serialize(formatting, DocumentEditorJson.Options));
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        var json = JsonSerializer.Serialize(new
        {
            IsVisible = false,
            UiState = new TmDocumentCanvasEngineHost.CanvasEngineUiState { Formatting = formatting }
        }, DocumentEditorJson.Options);
        await cut.InvokeAsync(() => host.Instance.OnCanvasMiniToolbarChanged(json));
    }

    private bool HasCanvasCommand(string commandId) =>
        SetupDocumentCanvasModule().Invocations.Any(invocation =>
            invocation.Identifier == "execCommand"
            && invocation.Arguments.Count >= 2
            && string.Equals(invocation.Arguments[1]?.ToString(), commandId, StringComparison.Ordinal));

    // ── print preview ──────────────────────────────────────────────────────

    [Fact]
    public async Task PrintPreview_HasNoRibbon_ButKeepsTheHeader()
    {
        var cut = RenderEditor();
        cut.FindAll("[data-testid='document-toolbar']").Should().HaveCount(1);

        await SetPrintPreviewAsync(cut, true);

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-toolbar']").Should().BeEmpty("the print preview has no editing ribbon"));
        cut.FindAll("[data-testid='document-header']").Should().HaveCount(1);
        cut.Find(".tm-document-editor").ClassList.Should().Contain("tm-document-editor--canvas-print-preview");

        await SetPrintPreviewAsync(cut, false);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-toolbar']").Should().HaveCount(1));
    }

    [Fact]
    public async Task PrintPreview_StatusBar_NamesTheMode_AndLeavesTheZoomToThePreview()
    {
        var cut = RenderEditor();
        cut.Find("[data-testid='document-status-region']").TextContent.Should().Contain("body", "editing: the active region");
        cut.FindAll("[data-testid='document-status-zoom']").Should().HaveCount(1);

        await SetPrintPreviewAsync(cut, true);

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='document-status-region']").TextContent.Trim().Should().Be("Print preview", "the printout is not being edited");
            cut.FindAll("[data-testid='document-status-zoom']").Should().BeEmpty("the print preview has its own zoom in its toolbar");
        });
        cut.FindAll("[data-testid='document-status-fullscreen']").Should().HaveCount(1);
    }

    [Fact]
    public async Task PrintPreview_KeepsTheStableActionTestIds_InTheInspectorRegion()
    {
        var cut = RenderEditor();
        await SetPrintPreviewAsync(cut, true);

        cut.WaitForAssertion(() => cut.Find("[data-region='inspector'] [data-testid='document-print-preview-actions']").Should().NotBeNull());
        cut.Find("[data-testid='document-print-preview-browser-print']").Should().NotBeNull();
        cut.Find("[data-testid='document-print-preview-export-pdf']").Should().NotBeNull();
        cut.Find("[data-testid='document-print-preview-close']").Should().NotBeNull();
    }

    [Fact]
    public async Task PrintPreview_PrintToPdf_IsOfferedOnlyWithAnExportProvider()
    {
        var cut = RenderEditor(withPdf: false);
        await SetPrintPreviewAsync(cut, true);

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-actions']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-print-preview-export-pdf']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-print-preview-browser-print']").Should().HaveCount(1);
    }

    [Fact]
    public async Task PrintPreview_ActionsRunTheEngineCommands()
    {
        var cut = RenderEditor();
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        await cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady("{}"));
        await SetPrintPreviewAsync(cut, true);
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-browser-print']").Should().NotBeNull());

        cut.Find("[data-testid='document-print-preview-browser-print']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("printDocument").Should().BeTrue());

        cut.Find("[data-testid='document-print-preview-close']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("closePrintPreview").Should().BeTrue());
    }

    [Fact]
    public async Task PrintPreview_LeftRegionHoldsThePageThumbnails()
    {
        var cut = RenderEditor();
        await SetPrintPreviewAsync(cut, true);

        cut.WaitForAssertion(() =>
            cut.Find("[data-region='nav'][data-testid='document-print-preview-pages'] [data-testid='document-page-navigator']").Should().NotBeNull());
        cut.FindAll("[data-region='nav'] [data-testid='document-navigation-panel']").Should().BeEmpty("the outline stays out of the print preview");
        cut.FindAll("[data-region='nav'] [data-testid='document-page-navigator-count']").Should().BeEmpty("the region's own header carries the total");
        cut.Find("[data-testid='document-print-preview-page-total']").TextContent.Should().Be("1 page");
    }

    [Fact]
    public async Task PrintPreview_InfoShowsPaperOrientationAndScale_FromThePageSettings()
    {
        var cut = RenderEditor(seed: document =>
        {
            document.PageSettings = new DocumentPageSettings { Size = DocumentPageSize.A4, Landscape = true };
            document.Sections[0].Properties.PageSettings = new DocumentPageSettings { Size = DocumentPageSize.A4, Landscape = true };
        });
        await SetPrintPreviewAsync(cut, true, zoomPercent: 125, zoomPreset: "custom");

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-info']").Should().NotBeNull());
        cut.Find("[data-testid='document-print-preview-info-paper']").TextContent.Should().Be("A4 (210 × 297 mm)");
        cut.Find("[data-testid='document-print-preview-info-orientation']").TextContent.Should().Be("Landscape");
        cut.Find("[data-testid='document-print-preview-info-scale']").TextContent.Should().Be("125%");
        cut.Find("[data-testid='document-print-preview-info-pages']").TextContent.Should().Be("1");
    }

    [Fact]
    public async Task PrintPreview_ToolbarOffersPagingAndZoom()
    {
        var cut = RenderEditor();
        await SetPrintPreviewAsync(cut, true, zoomPercent: 100, zoomPreset: "custom");

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-toolbar']").Should().NotBeNull());
        cut.Find("[data-testid='document-print-preview-previous']").HasAttribute("disabled").Should().BeTrue("a one-page document has no previous page");
        cut.Find("[data-testid='document-print-preview-next']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-print-preview-page-of']").TextContent.Should().Contain("1");

        cut.Find("[data-testid='document-print-preview-zoom-in']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("setZoom").Should().BeTrue());
    }

    [Fact]
    public async Task PrintPreview_OnATablet_KeepsTheThumbnailsAndMovesTheActionsIntoTheDocRegion()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);
        await SetPrintPreviewAsync(cut, true);

        cut.WaitForAssertion(() => cut.Find("[data-region='nav'][data-testid='document-print-preview-pages'] [data-testid='document-page-navigator']").Should().NotBeNull());
        cut.Find("[data-region='doc'] [data-testid='document-print-preview-actions']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-print-preview-info']").Should().BeEmpty("the information block is a desktop inspector card");
    }

    [Fact]
    public async Task PrintPreview_OnAPhone_HasNoLeftRegion_ButTheActionsAndThePagingBar()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);
        await SetPrintPreviewAsync(cut, true);

        cut.WaitForAssertion(() => cut.Find("[data-region='doc'] [data-testid='document-print-preview-actions']").Should().NotBeNull());
        cut.FindAll("[data-region='nav']").Should().BeEmpty();
        cut.Find("[data-testid='document-print-preview-toolbar']").Should().NotBeNull();
    }

    // ── status bar ─────────────────────────────────────────────────────────

    [Fact]
    public void StatusBar_ShowsPageXOfY_AndTheZoomControlsOfTheEditor()
    {
        var cut = RenderEditor();

        cut.Find("[data-testid='document-status-page-count']").TextContent.Should().Be("Page 1 of 1");
        cut.Find("[data-testid='document-status-zoom-in']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("setZoom").Should().BeTrue());
        cut.FindAll("[data-testid='document-status-fullscreen']").Should().HaveCount(1);
    }

    [Fact]
    public void StatusBar_SaveState_LivesInTheHeader_WhileTheHeaderIsShown()
    {
        var withHeader = RenderEditor();
        withHeader.FindAll("[data-testid='document-status-bar'] [data-testid='document-last-saved']").Should().BeEmpty();
    }

    // ── comparison mode ────────────────────────────────────────────────────

    [Fact]
    public async Task VersionPanel_ShowDifferences_OpensTheComparisonMode_AndBackReturnsToTheDocument()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var document = provider.SeedContractDocument("doc-1");
        var first = await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Draft", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        var edited = DocumentEditorJson.Deserialize(DocumentEditorJson.Serialize(document));
        edited.Blocks.Add(new DocumentBlock
        {
            Id = "added-block",
            Order = edited.Blocks.Count + 1,
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "A brand new closing paragraph" }] }
        });
        await provider.SaveAsync(new DocumentEditorSaveRequest { DocumentId = "doc-1", Document = edited, ConcurrencyMode = DocumentEditorConcurrencyMode.Force });
        var second = await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Final", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1").Add(p => p.Provider, provider).Add(p => p.LayoutMode, TmLayoutMode.Desktop));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-versions']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-version-item']").Should().HaveCount(2));

        FindVersionButton(cut, "document-version-diff-base", "Draft").Click();
        FindVersionButton(cut, "document-version-diff-compare", "Final").Click();
        cut.Find("[data-testid='document-version-show-diff']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-region='doc'] [data-testid='document-diff-mode'] [data-testid='document-diff-viewer']").Should().NotBeNull());
        cut.Find("[data-testid='document-diff-old-head']").TextContent.Should().Contain("Draft");
        cut.Find("[data-testid='document-diff-new-head']").TextContent.Should().Contain("Final");
        cut.Find("[data-testid='document-diff-new-meta']").TextContent.Should().Contain("Ada");
        cut.Find("[data-change-kind='added'] [data-diff-kind='added']").TextContent.Should().Contain("A brand new closing paragraph");
        cut.Find("[data-testid='document-editor-workspace']").ClassList.Should().Contain("tm-document-editor__workspace--diff");
        cut.Find(".tm-document-editor__page-surface").HasAttribute("hidden").Should().BeTrue("two copies of the document are never drawn - the canvas is hidden, not duplicated");
        cut.FindAll("[data-testid='document-canvas-engine-host']").Should().HaveCount(1);

        cut.Find("[data-testid='document-diff-back']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-viewer']").Should().BeEmpty());
        cut.Find(".tm-document-editor__page-surface").HasAttribute("hidden").Should().BeFalse();
        cut.Find("[data-testid='document-editor-workspace']").ClassList.Should().NotContain("tm-document-editor__workspace--diff");
    }

    [Fact]
    public async Task Tablet_VersionPanel_ShowDifferences_ClosesTheSideSheet_SoTheComparisonIsSeen()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Draft", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Final", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1").Add(p => p.Provider, provider).Add(p => p.LayoutMode, TmLayoutMode.Tablet));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-edge-toggle']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-version-item']").Should().HaveCount(2));

        FindVersionButton(cut, "document-version-diff-base", "Draft").Click();
        FindVersionButton(cut, "document-version-diff-compare", "Final").Click();
        cut.Find("[data-testid='document-version-show-diff']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-diff-viewer']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("the side sheet overlays the document and would hide the right column of the comparison it asked for");
    }

    [Fact]
    public async Task Mobile_VersionPanel_ShowDifferences_ClosesTheSheet_SoTheComparisonIsSeen()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var document = provider.SeedContractDocument("doc-1");
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Draft", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        var edited = DocumentEditorJson.Deserialize(DocumentEditorJson.Serialize(document));
        edited.Blocks.Add(new DocumentBlock
        {
            Id = "added-block",
            Order = edited.Blocks.Count + 1,
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "A brand new closing paragraph" }] }
        });
        await provider.SaveAsync(new DocumentEditorSaveRequest { DocumentId = "doc-1", Document = edited, ConcurrencyMode = DocumentEditorConcurrencyMode.Force });
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Final", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1").Add(p => p.Provider, provider).Add(p => p.LayoutMode, TmLayoutMode.Mobile));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-mobile-comments']").Should().NotBeNull());
        cut.Find("[data-testid='document-mobile-comments']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-versions']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-version-item']").Should().HaveCount(2));

        FindVersionButton(cut, "document-version-diff-base", "Draft").Click();
        FindVersionButton(cut, "document-version-diff-compare", "Final").Click();
        cut.Find("[data-testid='document-version-show-diff']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-diff-viewer']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("a phone's sheet covers the page: it closes when the comparison it asked for opens");
    }

    [Fact]
    public async Task ComparisonMode_Escape_ReturnsToTheDocument()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Draft", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Final", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1").Add(p => p.Provider, provider).Add(p => p.LayoutMode, TmLayoutMode.Desktop));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-versions']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-version-item']").Should().HaveCount(2));
        FindVersionButton(cut, "document-version-diff-base", "Draft").Click();
        FindVersionButton(cut, "document-version-diff-compare", "Final").Click();
        cut.Find("[data-testid='document-version-show-diff']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-diff-mode']").Should().NotBeNull());
        cut.Find("[data-testid='document-diff-summary']").TextContent.Should().Be("No changes", "identical snapshots say so instead of leaving a hole");

        await cut.Find(".tm-document-editor").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty());
    }

    private async Task<IRenderedComponent<TmDocumentEditor>> OpenComparisonModeAsync(InMemoryDocumentEditorProvider? provider = null)
    {
        provider ??= new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Draft", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Final", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1").Add(p => p.Provider, provider).Add(p => p.LayoutMode, TmLayoutMode.Desktop));
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        await cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady("{}"));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-versions']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-version-item']").Should().HaveCount(2));
        FindVersionButton(cut, "document-version-diff-base", "Draft").Click();
        FindVersionButton(cut, "document-version-diff-compare", "Final").Click();
        cut.Find("[data-testid='document-version-show-diff']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-diff-mode']").Should().NotBeNull());
        return cut;
    }

    [Fact]
    public async Task ComparisonMode_ADataCommandOfTheRibbon_LeavesTheComparisonFirst_ThenRuns()
    {
        var cut = await OpenComparisonModeAsync();
        HasCanvasCommand("bold").Should().BeFalse();

        cut.Find("[data-testid='document-bold']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty("the comparison closes before an editing command runs"));
        cut.WaitForAssertion(() => HasCanvasCommand("bold").Should().BeTrue());
    }

    [Fact]
    public async Task ComparisonMode_UndoFromTheHeader_LeavesTheComparisonFirst()
    {
        SetDocumentCanvasUndoStateJson("""{"canUndo":true,"canRedo":false}""");
        var cut = await OpenComparisonModeAsync();

        cut.Find("[data-testid='document-undo']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty());
        cut.WaitForAssertion(() => HasCanvasCommand("undo").Should().BeTrue());
    }

    [Fact]
    public async Task ComparisonMode_AViewCommand_KeepsTheComparisonOpen()
    {
        var cut = await OpenComparisonModeAsync();

        cut.Find("[data-testid='document-status-zoom-in']").Click();

        cut.WaitForAssertion(() => HasCanvasCommand("setZoom").Should().BeTrue());
        cut.FindAll("[data-testid='document-diff-mode']").Should().HaveCount(1, "a zoom changes the view, not the document");
    }

    private static Task RunPaletteCommandAsync(IRenderedComponent<TmDocumentEditor> cut, string command)
    {
        var method = typeof(TmDocumentEditor).GetMethod("ExecuteCommandPaletteCommandAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [command])!);
    }

    [Fact]
    public async Task ComparisonMode_ADataCommandOfThePalette_LeavesTheComparisonFirst_ThenRuns()
    {
        var cut = await OpenComparisonModeAsync();
        HasCanvasCommand("bold").Should().BeFalse();

        await RunPaletteCommandAsync(cut, "bold");

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty("the palette follows the same rule as the ribbon"));
        cut.WaitForAssertion(() => HasCanvasCommand("bold").Should().BeTrue());
    }

    private async Task<IRenderedComponent<TmDocumentEditor>> OpenComparisonModeAsync(TmLayoutMode layout)
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Draft", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Final", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1").Add(p => p.Provider, provider).Add(p => p.LayoutMode, layout));
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        await cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady("{}"));
        if (layout == TmLayoutMode.Mobile)
        {
            cut.WaitForAssertion(() => cut.Find("[data-testid='document-mobile-comments']").Should().NotBeNull());
            cut.Find("[data-testid='document-mobile-comments']").Click();
        }
        else
        {
            cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-edge-toggle']").Should().NotBeNull());
            cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();
        }

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-versions']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-version-item']").Should().HaveCount(2));
        FindVersionButton(cut, "document-version-diff-base", "Draft").Click();
        FindVersionButton(cut, "document-version-diff-compare", "Final").Click();
        cut.Find("[data-testid='document-version-show-diff']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-diff-mode']").Should().NotBeNull());
        return cut;
    }

    [Fact]
    public async Task Tablet_ComparisonMode_APanelCommand_ClosesTheComparison_AndOpensTheSideSheet()
    {
        var cut = await OpenComparisonModeAsync(TmLayoutMode.Tablet);

        await RunPaletteCommandAsync(cut, "openRevisions");

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty());
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-revisions']").GetAttribute("aria-selected").Should().Be("true"));
    }

    [Fact]
    public async Task Mobile_ComparisonMode_APanelCommand_KeepsTheComparison_AndOpensTheSheet()
    {
        var cut = await OpenComparisonModeAsync(TmLayoutMode.Mobile);

        await RunPaletteCommandAsync(cut, "openRevisions");

        cut.FindAll("[data-testid='document-diff-mode']").Should().HaveCount(1, "a phone's panels are sheets over the page, the comparison stays under the sheet");
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-revisions']").GetAttribute("aria-selected").Should().Be("true"));
    }

    [Fact]
    public async Task ComparisonMode_CtrlAltV_LeavesTheComparison_AndShowsTheVersionsPanel()
    {
        var cut = await OpenComparisonModeAsync();

        cut.Find("[data-testid='document-diff-mode']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "v", CtrlKey = true, AltKey = true });

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty("the shortcut used to be swallowed while the comparison was shown"));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-versions']").GetAttribute("aria-selected").Should().Be("true"));
    }

    [Theory]
    [InlineData("openRevisions", "revisions")]
    [InlineData("openComments", "comments")]
    [InlineData("openVersions", "versions")]
    public async Task ComparisonMode_APanelCommandOfThePalette_LeavesTheComparison_AndShowsThePanelBesideTheDocument(string command, string tab)
    {
        var cut = await OpenComparisonModeAsync();

        await RunPaletteCommandAsync(cut, command);

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty("the docked inspector steps aside for the comparison, so the panel needs the document back"));
        cut.WaitForAssertion(() => cut.Find($"[data-testid='document-side-panel-tab-{tab}']").GetAttribute("aria-selected").Should().Be("true"));
    }

    [Fact]
    public async Task VersionWithABadSnapshot_FailsTheComparisonInsteadOfThrowing()
    {
        var provider = new BrokenSnapshotProvider();
        provider.SeedContractDocument("doc-1");
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Draft", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });
        await provider.CreateVersionAsync(new DocumentVersionCreateRequest { DocumentId = "doc-1", Label = "Final", Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" } });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1").Add(p => p.Provider, provider).Add(p => p.LayoutMode, TmLayoutMode.Desktop));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-tab-versions']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-version-item']").Should().HaveCount(2));
        FindVersionButton(cut, "document-version-diff-base", "Draft").Click();
        FindVersionButton(cut, "document-version-diff-compare", "Final").Click();
        cut.Find("[data-testid='document-version-show-diff']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-version-status']").TextContent.Should().Be("Document comparison failed."));
        cut.FindAll("[data-testid='document-diff-mode']").Should().BeEmpty();
    }

    [Fact]
    public async Task PrintPreview_ClosedAfterAnAutomaticallyFittedEditingZoom_ResetsTheZoomInsteadOfPinningThePercent()
    {
        var cut = RenderEditor();
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        await cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady("{}"));
        // The engine's automatic fit left the page at 100 percent (the column holds it): the reader never chose a zoom.
        await SetPrintPreviewAsync(cut, false, zoomPercent: 100, zoomPreset: "custom", zoomAutoFit: "narrow");
        await SetPrintPreviewAsync(cut, true, zoomPercent: 100, zoomPreset: "custom", zoomAutoFit: "narrow");
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-close']").Should().NotBeNull());

        cut.Find("[data-testid='document-print-preview-close']").Click();

        cut.WaitForAssertion(() => HasCanvasCommand("resetZoom").Should().BeTrue("the automatic fit comes back with the editing view"));
        SetupDocumentCanvasModule().Invocations.Any(i => i.Identifier == "execCommand" && i.Arguments.Count >= 3 && i.Arguments[1]?.ToString() == "setZoom")
            .Should().BeFalse("a zoom that was never the reader's is not pinned as one");
    }

    [Fact]
    public async Task PrintPreview_FitsThePageOnEntry_AndRestoresTheEditingZoomOnClose()
    {
        var cut = RenderEditor();
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        await cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady("{}"));
        await SetPrintPreviewAsync(cut, false, zoomPercent: 125, zoomPreset: "custom");
        HasCanvasCommand("fitWidth").Should().BeFalse();

        await SetPrintPreviewAsync(cut, true, zoomPercent: 125, zoomPreset: "custom");
        cut.WaitForAssertion(() => HasCanvasCommand("fitWidth").Should().BeTrue("the page is fitted into the narrowed region on entry"));

        var before = SetupDocumentCanvasModule().Invocations.Count;
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-close']").Should().NotBeNull());
        cut.Find("[data-testid='document-print-preview-close']").Click();
        cut.WaitForAssertion(() => SetupDocumentCanvasModule().Invocations.Skip(before)
            .Any(i => i.Identifier == "execCommand" && i.Arguments.Count >= 3 && i.Arguments[1]?.ToString() == "setZoom" && i.Arguments[2]!.ToString()!.Contains("125")).Should().BeTrue("the zoom the reader was editing at comes back"));
    }

    [Fact]
    public async Task PrintPreview_PageField_TakesTheClampedValueBack()
    {
        var cut = RenderEditor();
        await SetPrintPreviewAsync(cut, true);
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-page-input']").Should().NotBeNull());

        cut.Find("[data-testid='document-print-preview-page-input']").Change("99");

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-page-input']").GetAttribute("value").Should().Be("1"));
    }

    [Fact]
    public async Task PrintPreview_CloseCardCarriesALongAndAShortWord()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);
        await SetPrintPreviewAsync(cut, true);
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-print-preview-close']").Should().NotBeNull());

        var close = cut.Find("[data-testid='document-print-preview-close']");
        close.QuerySelector(".tm-document-print-actions__label--full")!.TextContent.Should().Be("Close print preview");
        close.QuerySelector(".tm-document-print-actions__label--short")!.TextContent.Should().Be("Close");
    }

    private sealed class BrokenSnapshotProvider : InMemoryDocumentEditorProvider
    {
        public override async Task<IReadOnlyList<DocumentVersion>> GetVersionsAsync(string documentId, CancellationToken cancellationToken = default)
        {
            var versions = await base.GetVersionsAsync(documentId, cancellationToken);
            foreach (var version in versions.Where(v => v.Label == "Final"))
            {
                version.Snapshot.Json = "{bad";
            }

            return versions;
        }
    }

    private static AngleSharp.Dom.IElement FindVersionButton(IRenderedComponent<TmDocumentEditor> cut, string testId, string label)
        => cut.FindAll($"[data-testid='{testId}']")
              .First(button => button.ParentElement?.ParentElement?.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase) == true);

    private sealed class PdfProvider : IDocumentPdfExportProvider
    {
        public Task<DocumentPdfExportResult> ExportPdfAsync(DocumentPdfExportRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new DocumentPdfExportResult { Content = [1, 2, 3], ContentType = "application/pdf", FileName = "x.pdf" });
    }
}
