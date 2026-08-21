using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.DocumentEditor.Interfaces;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentEditorTests : LocalizationTestBase
{
    [Fact]
    public void Render_RendersCanvasHostByDefault()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-paragraph-editor']").Should().BeEmpty();
    }

    [Fact]
    public void Render_RetainsBlazorShellAroundCanvasHost()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__ribbon").Should().NotBeNull());
        cut.Find("[data-testid='document-save']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel-tab-comments']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel-tab-revisions']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel-tab-versions']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel-tab-properties']").Should().NotBeNull();
        cut.Find("[data-testid='document-version-panel']").Should().NotBeNull();
    }

    [Fact]
    public void Render_ExposesFloatingPortalAndLiveRegion()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());

        cut.Find("[data-testid='document-floating-root']").ClassList
            .Should()
            .Contain("tm-document-editor__floating-root");
        var liveRegion = cut.Find("[data-testid='document-editor-live-region']");
        liveRegion.GetAttribute("role").Should().Be("status");
        liveRegion.GetAttribute("aria-live").Should().Be("polite");
        liveRegion.GetAttribute("aria-atomic").Should().Be("true");
    }

    [Fact]
    public async Task SaveSuccess_AnnouncesThroughLiveRegion()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-editor-live-region']").TextContent.Should().Contain("Saved"));
    }

    [Fact]
    public async Task FindResults_AnnouncesResultCountThroughLiveRegion()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());

        await cut.Find(".tm-document-editor").KeyDownAsync(new KeyboardEventArgs { Key = "f", CtrlKey = true });
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-find-input']").Should().NotBeNull());
        await cut.Find("[data-testid='document-find-input']").InputAsync(new ChangeEventArgs { Value = "agreement" });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-editor-live-region']").TextContent.Should().Contain("1 of"));
    }

    [Fact]
    public async Task AutoSaveFailure_AnnouncesThroughLiveRegion()
    {
        var provider = new FailingAutosaveProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.AutoSaveInterval, TimeSpan.FromMilliseconds(20)));

        await MarkCanvasReadyAsync(cut);
        await NotifyCanvasChangedAsync(cut);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-editor-live-region']").TextContent.Should().Contain("autosave-boom"),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task DisposeAsync_DisablesBeforeUnloadGuard()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        JSInterop.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "tmDocumentEditor.disableBeforeUnloadGuard");
    }

    [Fact]
    public void Render_ExposesAccessibilityLandmarkLabels()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        var root = cut.Find(".tm-document-editor");
        root.GetAttribute("role").Should().Be("application");
        root.GetAttribute("aria-label").Should().Be("Document editor");
        cut.Find("[data-testid='document-toolbar']").GetAttribute("aria-label").Should().Be("Document editor toolbar");
        cut.Find(".tm-document-editor__surface").GetAttribute("aria-label").Should().Be("Document surface");
        cut.Find("[data-testid='document-side-panel']").GetAttribute("aria-label").Should().Be("Document side panel");
        cut.Find("[data-testid='document-status-bar']").GetAttribute("aria-label").Should().Be("Document status");
    }

    [Fact]
    public void SidePanel_RendersUnifiedTabsAndSwitchesContent()
    {
        var activeTab = DocumentSidePanelTab.Comments;
        var cut = Render<TmDocumentSidePanel>(parameters => parameters
            .Add(p => p.ActiveTab, activeTab)
            .Add(p => p.ActiveTabChanged, tab => activeTab = tab)
            .Add(p => p.CommentsCount, 2)
            .Add(p => p.RevisionsCount, 1)
            .Add(p => p.VersionsCount, 3)
            .Add(p => p.CommentsContent, builder => builder.AddMarkupContent(0, "<div data-testid='side-panel-comments-content'>comments content</div>"))
            .Add(p => p.RevisionsContent, builder => builder.AddMarkupContent(0, "<div data-testid='side-panel-revisions-content'>revisions content</div>"))
            .Add(p => p.VersionsContent, builder => builder.AddMarkupContent(0, "<div data-testid='side-panel-versions-content'>versions content</div>"))
            .Add(p => p.PropertiesContent, builder => builder.AddMarkupContent(0, "<div data-testid='side-panel-properties-content'>properties content</div>"))
            .Add(p => p.ShowPages, true)
            .Add(p => p.PagesContent, builder => builder.AddMarkupContent(0, "<div data-testid='side-panel-pages-content'>pages content</div>")));

        cut.Find("[data-testid='document-side-panel']")
            .GetAttribute("data-panel-layout")
            .Should()
            .Be("docked-tabs");
        cut.Find("[data-testid='document-side-panel']")
            .GetAttribute("data-visible-panel-count")
            .Should()
            .Be("1");
        cut.Find("[data-testid='document-side-panel-body']")
            .GetAttribute("data-active-tab")
            .Should()
            .Be("comments");
        cut.Find("[data-testid='document-side-panel-tab-comments']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");
        cut.Find("[data-testid='side-panel-comments-content']").Should().NotBeNull();
        cut.FindAll("[data-testid='side-panel-revisions-content']").Should().BeEmpty();
        cut.FindAll("[data-testid='side-panel-versions-content']").Should().BeEmpty();

        cut.Find("[data-testid='document-side-panel-tab-revisions']").Click();

        activeTab.Should().Be(DocumentSidePanelTab.Revisions);
        cut.Render(parameters => parameters.Add(p => p.ActiveTab, activeTab));
        cut.Find("[data-testid='document-side-panel-body']")
            .GetAttribute("data-active-tab")
            .Should()
            .Be("revisions");
        cut.Find("[data-testid='side-panel-revisions-content']").Should().NotBeNull();
        cut.FindAll("[data-testid='side-panel-comments-content']").Should().BeEmpty();
        cut.FindAll("[data-testid='side-panel-versions-content']").Should().BeEmpty();

        cut.Find("[data-testid='document-side-panel-tab-pages']").Click();

        activeTab.Should().Be(DocumentSidePanelTab.Pages);
        cut.Render(parameters => parameters.Add(p => p.ActiveTab, activeTab));
        cut.Find("[data-testid='side-panel-pages-content']").Should().NotBeNull();
        cut.FindAll("[data-testid='side-panel-revisions-content']").Should().BeEmpty();
    }

    [Fact]
    public void SidePanel_ClosedStateShowsEdgeToggle()
    {
        var opened = false;
        var cut = Render<TmDocumentSidePanel>(parameters => parameters
            .Add(p => p.IsOpen, false)
            .Add(p => p.OnOpen, () => opened = true));

        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();
        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();

        opened.Should().BeTrue();
    }

    [Fact]
    public void Editor_CloseSidePanelFreesWorkspaceAndEdgeToggleReopens()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-close']").Click();

        cut.Find(".tm-document-editor__workspace")
            .GetAttribute("class")
            .Should()
            .Contain("tm-document-editor__workspace--side-panel-closed");
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();
        cut.Find("[data-testid='document-side-panel']").Should().NotBeNull();
    }

    [Fact]
    public void Editor_RibbonCommandsReopenSidePanelTabs()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-close']").Click();

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        cut.Find("[data-testid='document-open-revisions']").Click();

        cut.Find("[data-testid='document-side-panel-tab-revisions']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");
        cut.Find("[data-testid='document-editor-workspace']")
            .GetAttribute("data-active-side-panel-tab")
            .Should()
            .Be("revisions");
        cut.Find("[data-testid='document-revision-panel']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-version-panel']").Should().BeEmpty();

        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-open-versions']").Click();

        cut.Find("[data-testid='document-side-panel-tab-versions']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");
        cut.Find("[data-testid='document-editor-workspace']")
            .GetAttribute("data-side-panel-layout")
            .Should()
            .Be("docked-tabs");
        cut.Find("[data-testid='document-editor-workspace']")
            .GetAttribute("data-active-side-panel-tab")
            .Should()
            .Be("versions");
        cut.Find("[data-testid='document-version-panel']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-revision-panel']").Should().BeEmpty();
    }

    [Fact]
    public void RibbonTabs_SwitchVisibleCommandGroups()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.CanExportPdf, true)
            .Add(p => p.CanImportDocx, true)
            .Add(p => p.CanExportDocx, true)
            .Add(p => p.CanCompareDocuments, true)
            .Add(p => p.CanPreviewTemplate, true));

        cut.Find("[data-testid='document-ribbon-tab-home']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");
        cut.Find("[data-testid='document-toolbar']")
            .GetAttribute("data-active-ribbon-tab")
            .Should()
            .Be("home");
        cut.Find("[data-testid='document-ribbon-panel']")
            .GetAttribute("data-active-ribbon-tab")
            .Should()
            .Be("home");
        cut.Find("[data-testid='document-ribbon-tab-home']").ClassList
            .Should()
            .Contain("tm-document-editor__ribbon-tab--active");
        cut.Find("[data-testid='document-ribbon-tab-home']")
            .GetAttribute("aria-current")
            .Should()
            .Be("page");
        cut.Find("[data-testid='document-ribbon-tab-home']")
            .GetAttribute("data-active")
            .Should()
            .Be("true");
        cut.Find("[data-testid='document-save']").Should().NotBeNull();
        cut.Find("[data-testid='document-bold']").Should().NotBeNull();
        cut.Find("[data-testid='document-font-size']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-toolbar-table']").Should().BeEmpty();

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();
        cut.Find("[data-testid='document-toolbar-table']").Should().NotBeNull();
        cut.Find("[data-testid='document-toolbar-image']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-bold']").Should().BeEmpty();

        cut.Find("[data-testid='document-ribbon-tab-references']").Click();
        cut.Find("[data-testid='document-export-pdf']").Should().NotBeNull();
        cut.Find("[data-testid='document-import-docx-label']").Should().NotBeNull();

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        cut.Find("[data-testid='document-toolbar']")
            .GetAttribute("data-active-ribbon-tab")
            .Should()
            .Be("review");
        cut.Find("[data-testid='document-ribbon-panel']")
            .GetAttribute("data-active-ribbon-tab")
            .Should()
            .Be("review");
        cut.Find("[data-testid='document-ribbon-tab-review']").ClassList
            .Should()
            .Contain("tm-document-editor__ribbon-tab--active");
        cut.Find("[data-testid='document-track-changes']").Should().NotBeNull();
        cut.Find("[data-testid='document-review-display-mode']").Should().NotBeNull();
        cut.Find("[data-testid='document-open-comments']").Should().NotBeNull();
        cut.Find("[data-testid='document-open-revisions']").Should().NotBeNull();
        cut.Find("[data-testid='document-compare-open']").Should().NotBeNull();
        cut.Find("[data-testid='document-protect-document']").Should().NotBeNull();
        cut.Find("[data-testid='document-mark-editable-region']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-bold']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-font-size']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-template-preview']").Should().BeEmpty();

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-toggle-nonprinting']").Should().NotBeNull();
        cut.Find("[data-testid='document-template-preview']").Should().NotBeNull();
        cut.Find("[data-testid='document-open-versions']").Should().NotBeNull();
    }

    [Fact]
    public void TrackChangesButton_RendersAsToggleWithStateAndAriaPressed()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.CanTrackChanges, true)
            .Add(p => p.TrackChangesEnabled, false));

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        var offButton = cut.Find("[data-testid='document-track-changes']");
        offButton.GetAttribute("aria-pressed").Should().Be("false");
        offButton.GetAttribute("data-toggle").Should().Be("true");
        offButton.GetAttribute("data-state").Should().Be("off");
        offButton.ClassList.Should().Contain("tm-document-editor__track-toggle--off");
        offButton.ClassList.Should().NotContain("tm-document-editor__track-toggle--on");
        cut.Find("[data-testid='document-toolbar']")
            .GetAttribute("data-track-changes-state")
            .Should()
            .Be("off");

        cut.Render(parameters => parameters
            .Add(p => p.CanTrackChanges, true)
            .Add(p => p.TrackChangesEnabled, true));

        var onButton = cut.Find("[data-testid='document-track-changes']");
        onButton.GetAttribute("aria-pressed").Should().Be("true");
        onButton.GetAttribute("data-state").Should().Be("on");
        onButton.ClassList.Should().Contain("tm-document-editor__track-toggle--on");
        onButton.ClassList.Should().Contain("tm-document-editor__ribbon-button--active");
        cut.Find("[data-testid='document-toolbar']")
            .GetAttribute("data-track-changes-state")
            .Should()
            .Be("on");
    }

    [Fact]
    public void TrackChangesButton_ClickInvokesToggleCallback()
    {
        var toggleCount = 0;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.CanTrackChanges, true)
            .Add(p => p.OnToggleTrackChanges, () => toggleCount++));

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        cut.Find("[data-testid='document-track-changes']").Click();

        toggleCount.Should().Be(1);
    }

    [Fact]
    public void Toolbar_ViewTab_TogglesNonPrintingCharacters()
    {
        var toggled = false;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.OnToggleNonPrintingCharacters, () => toggled = true));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-toggle-nonprinting']").Click();

        toggled.Should().BeTrue();
    }

    [Fact]
    public void Toolbar_HomeTabExposesBaselineCommandsForRegistryMigration()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.CanUndo, true)
            .Add(p => p.CanRedo, true));

        cut.Find("[data-testid='document-ribbon-tab-home']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");

        var expectedHomeCommands = new[]
        {
            "document-save",
            "document-undo",
            "document-redo",
            "document-bold",
            "document-italic",
            "document-underline",
            "document-link",
            "document-clear-formatting"
        };

        foreach (var testId in expectedHomeCommands)
        {
            cut.Find($"[data-testid='{testId}']").Should().NotBeNull();
        }

        cut.FindAll("[data-testid='document-toolbar-table']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-template-preview']").Should().BeEmpty();
    }

    [Fact]
    public void Toolbar_ReferencesTabKeepsExportCommandsVisibleButDisabledWhenUnavailable()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-references']").Click();

        cut.Find("[data-testid='document-export-pdf']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-export-docx']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-import-docx-label']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Toolbar_LayoutTabExposesHeaderFooterScopeToggles()
    {
        var differentFirstPage = false;
        var differentOddEven = true;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.DifferentFirstPage, differentFirstPage)
            .Add(p => p.DifferentOddAndEvenPages, differentOddEven)
            .Add(p => p.DifferentFirstPageChanged, value => differentFirstPage = value)
            .Add(p => p.DifferentOddAndEvenPagesChanged, value => differentOddEven = value));

        cut.Find("[data-testid='document-ribbon-tab-layout']").Click();

        cut.Find("[data-testid='document-different-first-page']").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid='document-different-odd-even']").GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("[data-testid='document-different-first-page']").Click();
        cut.Find("[data-testid='document-different-odd-even']").Click();

        differentFirstPage.Should().BeTrue();
        differentOddEven.Should().BeFalse();
    }

    [Fact]
    public void Toolbar_ViewTabExposesRulerAndZoomControls()
    {
        var showRuler = true;
        var zoomPercent = 100;
        var pageWidthRequested = false;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowRuler, showRuler)
            .Add(p => p.ZoomPercent, zoomPercent)
            .Add(p => p.ShowRulerChanged, value => showRuler = value)
            .Add(p => p.ZoomPercentChanged, value => zoomPercent = value)
            .Add(p => p.OnZoomPageWidth, () => pageWidthRequested = true));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.Find("[data-testid='document-toggle-ruler']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='document-zoom-100']").TextContent.Should().Contain("100%");

        cut.Find("[data-testid='document-toggle-ruler']").Click();
        cut.Find("[data-testid='document-zoom-in']").Click();
        cut.Find("[data-testid='document-zoom-page-width']").Click();

        showRuler.Should().BeFalse();
        zoomPercent.Should().Be(110);
        pageWidthRequested.Should().BeTrue();
    }

    [Fact]
    public void StatusBar_ShowsSaveMetricsRegionAndZoom()
    {
        var cut = Render<TmDocumentEditorStatusBar>(parameters => parameters
            .Add(p => p.IsDirty, true)
            .Add(p => p.SaveMessage, "Saved")
            .Add(p => p.LastSavedAt, new DateTimeOffset(2026, 5, 15, 8, 30, 0, TimeSpan.Zero))
            .Add(p => p.WordCount, 42)
            .Add(p => p.PageCount, 3)
            .Add(p => p.ActiveRegionLabel, "body")
            .Add(p => p.ZoomLabel, "110%"));

        cut.Find("[data-testid='document-status-bar']").Should().NotBeNull();
        cut.Find("[data-testid='document-status-bar']").GetAttribute("aria-label").Should().Be("Document status");
        cut.Find("[data-testid='document-dirty-status']").TextContent.Should().Contain("Unsaved changes");
        cut.Find("[data-testid='document-save-message']").TextContent.Should().Contain("Saved");
        cut.Find("[data-testid='document-status-word-count']").TextContent.Should().Contain("42 words");
        cut.Find("[data-testid='document-status-page-count']").TextContent.Should().Contain("3 pages");
        cut.Find("[data-testid='document-status-region']").TextContent.Should().Contain("body");
        cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("110%");
    }

    [Fact]
    public async Task Editor_StatusBarReplacesRibbonSaveStatusAndCountsDocumentText()
    {
        SetDocumentCanvasAnnotationsJson("""{"comments":[],"revisions":[],"wordCount":0,"pageCount":2}""");
        SetDocumentCanvasPageMetricsJson("""{"totalPages":2,"renderedPages":2,"pages":[{"pageIndex":0,"pageNumber":1},{"pageIndex":1,"pageNumber":2}]}""");
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        seeded.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.PageBreak,
            Content = new PageBreakBlockContent()
        });
        seeded.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Paragraph,
            Order = 999,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Text = "additional words" }]
            }
        });
        await provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = "doc-1",
            Document = seeded,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-status-bar']").Should().NotBeNull());
        cut.FindAll(".tm-document-editor__ribbon-status").Should().BeEmpty();
        cut.Find("[data-testid='document-status-page-count']").TextContent.Should().Contain("2 pages");
        cut.Find("[data-testid='document-status-word-count']").TextContent.Should().Contain("words");
        cut.Find("[data-testid='document-status-region']").TextContent.Should().Contain("body");
        cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("Zoom 100%");
    }

    [Fact]
    public void Toolbar_HeaderFooterModeShowsContextualTabAndCloseCommand()
    {
        var closeCalled = false;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ActiveRegion, "Header")
            .Add(p => p.OnCloseHeaderFooter, () => closeCalled = true));

        cut.Find("[data-testid='document-ribbon-tab-header-footer']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");
        cut.Find("[data-testid='document-close-header-footer']").Should().NotBeNull();

        cut.Find("[data-testid='document-close-header-footer']").Click();

        closeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RibbonTabs_SupportKeyboardNavigationAndSelectedState()
    {
        var cut = Render<TmDocumentEditorToolbar>();
        var home = cut.Find("[data-testid='document-ribbon-tab-home']");

        await home.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.Find("[data-testid='document-ribbon-tab-insert']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");
        cut.Find("[data-testid='document-ribbon-tab-insert']")
            .GetAttribute("tabindex")
            .Should()
            .Be("0");

        await cut.Find("[data-testid='document-ribbon-tab-insert']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "End" });

        cut.Find("[data-testid='document-ribbon-tab-view']")
            .GetAttribute("aria-selected")
            .Should()
            .Be("true");
    }

    [Fact]
    public async Task Editor_EscapeClosesSidePanelAndRequestsDocumentFocus()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());

        await cut.Find(".tm-document-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();
        SetupDocumentCanvasModule().Invocations.Should().Contain(invocation => invocation.Identifier == "focus");
    }

    [Fact]
    public async Task Editor_F10EnablesRibbonKeyboardMode()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-toolbar']").Should().NotBeNull());

        await cut.Find(".tm-document-editor").KeyDownAsync(new KeyboardEventArgs { Key = "F10" });

        cut.Find(".tm-document-editor").GetAttribute("data-ribbon-keyboard-mode").Should().Be("true");
        cut.Find("[data-testid='document-toolbar']").GetAttribute("data-keyboard-mode").Should().Be("true");
    }

    [Fact]
    public async Task Toolbar_LinkDialog_LoadsExistingLinkTitleAndValidatesSafeUrl()
    {
        WysiwygLinkPayload? applied = null;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.LinkInfoProvider, () => Task.FromResult<WysiwygLinkInfo?>(new WysiwygLinkInfo
            {
                Href = "https://example.test/old",
                Title = "Old title"
            }))
            .Add(p => p.OnLinkApplied, payload =>
            {
                applied = payload;
            }));

        await cut.Find("[data-testid='document-link']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='document-link-url']").GetAttribute("value").Should().Be("https://example.test/old");
        cut.Find("[data-testid='document-link-title']").GetAttribute("value").Should().Be("Old title");

        await cut.Find("[data-testid='document-link-url']").InputAsync(new ChangeEventArgs { Value = "javascript:alert(1)" });
        await cut.Find("[data-testid='document-apply-link']").ClickAsync(new MouseEventArgs());

        applied.Should().BeNull();
        cut.Find("[data-testid='document-link-error']").TextContent.Should().Contain("safe URL");

        await cut.Find("[data-testid='document-link-url']").InputAsync(new ChangeEventArgs { Value = "https://example.test/new" });
        await cut.Find("[data-testid='document-link-title']").InputAsync(new ChangeEventArgs { Value = "New title" });
        await cut.Find("[data-testid='document-apply-link']").ClickAsync(new MouseEventArgs());

        applied.Should().NotBeNull();
        applied!.Href.Should().Be("https://example.test/new");
        applied.Title.Should().Be("New title");
    }

    [Fact]
    public async Task Toolbar_LinkDialog_EscapeClosesDialog()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        await cut.Find("[data-testid='document-link']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='document-link-dialog']").Should().NotBeNull();

        await cut.Find("[data-testid='document-link-dialog']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll("[data-testid='document-link-dialog']").Should().BeEmpty();
    }

    [Fact]
    public void Toolbar_ReadOnlyDisablesEditingCommandsButKeepsReviewAndViewNavigation()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ReadOnly, true)
            .Add(p => p.CanUndo, true)
            .Add(p => p.CanRedo, true)
            .Add(p => p.CanTrackChanges, true)
            .Add(p => p.CanPreviewTemplate, true));

        cut.Find("[data-testid='document-save']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-bold']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-bold']")
            .GetAttribute("title")
            .Should()
            .Contain("Read-only");

        cut.Find("[data-testid='document-ribbon-tab-review']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        cut.Find("[data-testid='document-review-display-mode']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='document-track-changes']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-track-changes']")
            .GetAttribute("title")
            .Should()
            .Contain("Read-only");

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-template-preview']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Toolbar_ReadOnlyDisablesDataAffectingCommandsButLeavesViewCommandsAvailable()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ReadOnly, true)
            .Add(p => p.CanUndo, true)
            .Add(p => p.CanRedo, true)
            .Add(p => p.CanPreviewTemplate, true));

        var disabledHomeCommands = new[]
        {
            "document-save",
            "document-undo",
            "document-redo",
            "document-bold",
            "document-italic",
            "document-underline",
            "document-link",
            "document-clear-formatting",
            "document-font-family",
            "document-font-size"
        };

        foreach (var testId in disabledHomeCommands)
        {
            cut.Find($"[data-testid='{testId}']").HasAttribute("disabled").Should().BeTrue(testId);
        }

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        var enabledViewCommands = new[]
        {
            "document-toggle-ruler",
            "document-zoom-out",
            "document-zoom-100",
            "document-zoom-in",
            "document-zoom-page-width",
            "document-template-preview",
            "document-open-versions"
        };

        foreach (var testId in enabledViewCommands)
        {
            cut.Find($"[data-testid='{testId}']").HasAttribute("disabled").Should().BeFalse(testId);
        }
    }

    [Fact]
    public void Toolbar_FormattingButtonsExposeActiveAndMixedStates()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.BoldState, WysiwygFormattingValue.Active)
            .Add(p => p.ItalicState, WysiwygFormattingValue.Mixed)
            .Add(p => p.UnderlineState, WysiwygFormattingValue.Inactive)
            .Add(p => p.ParagraphAlignment, DocumentTextAlignment.Center));

        cut.Find("[data-testid='document-bold']")
            .GetAttribute("class")
            .Should()
            .Contain("tm-document-editor__ribbon-button--active");
        cut.Find("[data-testid='document-bold']").GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("[data-testid='document-italic']")
            .GetAttribute("class")
            .Should()
            .Contain("tm-document-editor__ribbon-button--mixed");
        cut.Find("[data-testid='document-italic']").GetAttribute("aria-pressed").Should().Be("mixed");

        cut.Find("[data-testid='document-underline']")
            .GetAttribute("class")
            .Should()
            .NotContain("tm-document-editor__ribbon-button--active");
        cut.Find("[data-testid='document-underline']").GetAttribute("aria-pressed").Should().Be("false");

        cut.Find("[data-testid='document-align-center']")
            .GetAttribute("class")
            .Should()
            .Contain("tm-document-editor__ribbon-button--active");
        cut.Find("[data-testid='document-align-center']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='document-align-left']").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Toolbar_FontColorAndLineSpacingReflectJsSelectionState()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.FontFamilies, new[]
            {
                new DocumentFontFamily { DisplayName = "Inter", CssFamily = "Inter, sans-serif" }
            })
            .Add(p => p.CurrentFontFamily, "Inter, sans-serif")
            .Add(p => p.CurrentFontSize, "14pt")
            .Add(p => p.CurrentTextColor, "#123456")
            .Add(p => p.CurrentHighlightColor, "#abcdef")
            .Add(p => p.CurrentLineSpacing, 1.5));

        cut.Find("[data-testid='document-font-family']").GetAttribute("value").Should().Be("Inter, sans-serif");
        cut.Find("[data-testid='document-font-size']").GetAttribute("value").Should().Be("14");
        cut.Find("[data-testid='document-font-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#123456");
        cut.Find("[data-testid='document-highlight-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#abcdef");
        cut.Find("[data-testid='document-line-spacing']").GetAttribute("value").Should().Be("1.5");
    }

    [Fact]
    public async Task CanvasSelectionChanged_UsesCanvasUiStateForToolbar()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var paragraph = seeded.Blocks.First(block => block.Content is ParagraphBlockContent);
        var inline = ((ParagraphBlockContent)paragraph.Content).Inlines.First();

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 100,
            Top = 100,
            Width = 180,
            Height = 44,
            Selection = new WysiwygSelectionSnapshot
            {
                Region = "Body",
                AnchorBlockId = paragraph.Id,
                AnchorInlineId = inline.Id,
                AnchorNodeId = inline.Id,
                AnchorOffset = 0,
                FocusBlockId = paragraph.Id,
                FocusInlineId = inline.Id,
                FocusNodeId = inline.Id,
                FocusOffset = 4,
                IsCollapsed = false
            }
        }, new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState
            {
                Bold = true,
                ItalicMixed = true,
                Underline = false,
                Alignment = "right",
                FontFamily = "Inter, sans-serif",
                FontSize = "14pt",
                TextColor = "#123456",
                HighlightColor = "#abcdef",
                LineSpacing = 1.5
            }
        });

        cut.Find("[data-testid='document-bold']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='document-italic']").GetAttribute("aria-pressed").Should().Be("mixed");
        cut.Find("[data-testid='document-align-right']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='document-font-family']").GetAttribute("value").Should().Be("Inter, sans-serif");
        cut.Find("[data-testid='document-font-size']").GetAttribute("value").Should().Be("14");
        cut.Find("[data-testid='document-font-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#123456");
        cut.Find("[data-testid='document-highlight-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#abcdef");
        cut.Find("[data-testid='document-line-spacing']").GetAttribute("value").Should().Be("1.5");
    }

    [Fact]
    public async Task CanvasSelectionChanged_ImageInspectorReadsActiveDrawingObject()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var paragraph = seeded.Blocks.First(block => block.Content is ParagraphBlockContent);
        paragraph.Content = new ParagraphBlockContent
        {
            Inlines =
            [
                new TextRun { Id = "drawing-text-before", Text = "Before " },
                new DocumentDrawingRun
                {
                    Id = "drawing-run-1",
                    ObjectId = "drawing-1",
                    Url = "https://example.test/drawing.png",
                    AltText = "Selected drawing object",
                    Size = new DocumentImageSize { Width = 144, Height = 96, LockAspectRatio = true },
                    Layout = new DocumentObjectLayout
                    {
                        Kind = DocumentObjectLayoutKind.Anchored,
                        Anchor = new DocumentObjectAnchor
                        {
                            BlockId = paragraph.Id,
                            Offset = 7,
                            InlineIndex = 1
                        },
                        Wrap = new DocumentObjectWrap
                        {
                            Mode = DocumentWrapMode.Square
                        },
                        Transform = new DocumentObjectTransform
                        {
                            Width = 144,
                            Height = 96,
                            LockAspectRatio = true
                        }
                    }
                },
                new TextRun { Id = "drawing-text-after", Text = " after" }
            ]
        };
        await provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = "doc-1",
            Document = seeded,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 220,
            Top = 120,
            Width = 184,
            Height = 40,
            Reason = "canvas-object-selection",
            Selection = new WysiwygSelectionSnapshot
            {
                Region = "Body",
                SelectionMode = "Object",
                AnchorBlockId = paragraph.Id,
                FocusBlockId = paragraph.Id,
                ActiveImageBlockId = paragraph.Id,
                ActiveObjectId = "drawing-1",
                HitTargetKind = "image",
                IsCollapsed = true,
                ObjectSelection = new WysiwygObjectSelectionSnapshot
                {
                    Region = "Body",
                    Kind = "image",
                    ObjectId = "drawing-1",
                    BlockId = paragraph.Id,
                    AnchorBlockId = paragraph.Id,
                    AnchorInlineId = "drawing-run-1",
                    AnchorInlineIndex = 1,
                    InlineIndex = 1,
                    AnchorOffset = 7,
                    RunId = "drawing-run-1"
                }
            }
        }, new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState
            {
                Bold = true,
                Image = new TmDocumentCanvasEngineHost.CanvasEngineImageState
                {
                    ObjectId = "drawing-1",
                    BlockId = paragraph.Id,
                    RunId = "drawing-run-1",
                    Url = "https://example.test/drawing.png",
                    AltText = "Selected drawing object",
                    Width = 144,
                    Height = 96,
                    WrapMode = "square"
                }
            }
        });

        // Celý blok asercí v jednom WaitForAssertion — panel je conditionally-rendered a pod
        // paralelním během se mezi jednotlivými Find může přerenderovat; retry drží DOM stabilní.
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='document-image-properties-panel']")
                .GetAttribute("data-active-object-id").Should().Be("drawing-1");
            cut.Find("[data-testid='document-image-properties-panel']")
                .GetAttribute("data-active-anchor-block-id").Should().Be(paragraph.Id);
            cut.Find("[data-testid='document-image-inspector-alt']")
                .GetAttribute("value").Should().Be("Selected drawing object");
            cut.Find("[data-testid='document-image-inspector-width']")
                .GetAttribute("value").Should().Be("144");
        });
    }

    [Fact]
    public async Task CanvasUiStateChanged_UpdatesToolbarFromCanonicalEvent()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        var selection = new WysiwygSelectionSnapshot
        {
            Region = "Body",
            AnchorBlockId = paragraph.Id,
            AnchorInlineId = inline.Id,
            AnchorOffset = 0,
            FocusBlockId = paragraph.Id,
            FocusInlineId = inline.Id,
            FocusOffset = 4,
            IsCollapsed = false,
            SelectionToken = "phase-6-selection-token",
            StableSelectionToken = "phase-6-selection-token"
        };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 100,
            Top = 80,
            Width = 180,
            Height = 40,
            Selection = selection
        }, new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState
            {
                Bold = true,
                FontSize = "28pt",
                TextColor = "#2563eb",
                HighlightColor = "#fde68a"
            }
        });

        cut.Find("[data-testid='document-bold']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='document-font-size']").GetAttribute("value").Should().Be("28");
        cut.Find("[data-testid='document-font-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#2563eb");
        cut.Find("[data-testid='document-highlight-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#fde68a");
    }

    [Fact]
    public async Task ToolbarTextColorCommand_RefreshesCanonicalRuntimeStateAfterCommand()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        var selection = new WysiwygSelectionSnapshot
        {
            Region = "Body",
            AnchorBlockId = paragraph.Id,
            AnchorInlineId = inline.Id,
            AnchorOffset = 0,
            FocusBlockId = paragraph.Id,
            FocusInlineId = inline.Id,
            FocusOffset = 4,
            IsCollapsed = false,
            SelectionToken = "phase-6-selection-token",
            StableSelectionToken = "phase-6-selection-token"
        };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 100,
            Top = 80,
            Width = 180,
            Height = 40,
            Selection = selection
        }, new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState()
        });

        var textColorPicker = cut.FindComponents<TmColorPicker>()
            .Single(component => component.Markup.Contains("document-font-color-trigger", StringComparison.Ordinal));

        SetDocumentCanvasExecCommandResult("""{"handled":true,"uiState":{"formatting":{"textColor":"#2563eb","alignment":"left"}}}""");
        await cut.InvokeAsync(() => textColorPicker.Instance.ValueChanged.InvokeAsync("#2563EB"));

        var textColorInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 1
                && string.Equals(invocation.Arguments[1]?.ToString(), "textcolor", StringComparison.Ordinal));
        textColorInvocation
            .Should()
            .NotBeNull();
        textColorInvocation!.Arguments[2]?.ToString()
            .Should()
            .Contain("#2563eb")
            .And.NotContain("#2563EB");
        cut.Find("[data-testid='document-font-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#2563eb");
    }

    [Fact]
    public async Task ToolbarHighlightClearCommand_RemovesHighlightWithSelectionToken()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        var selection = new WysiwygSelectionSnapshot
        {
            Region = "Body",
            AnchorBlockId = paragraph.Id,
            AnchorInlineId = inline.Id,
            AnchorOffset = 0,
            FocusBlockId = paragraph.Id,
            FocusInlineId = inline.Id,
            FocusOffset = 4,
            IsCollapsed = false,
            SelectionToken = "phase-6-highlight-token",
            StableSelectionToken = "phase-6-highlight-token"
        };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 100,
            Top = 80,
            Width = 180,
            Height = 40,
            Selection = selection
        }, new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState
            {
                HighlightColor = "#fde68a"
            }
        });

        var highlightPicker = cut.FindComponents<TmColorPicker>()
            .Single(component => component.Markup.Contains("document-highlight-color-trigger", StringComparison.Ordinal));

        // The clear routes an execCommand whose mocked reply carries no UI snapshot, so the editor falls back
        // to PULLING getFormattingStateJson. A real engine answers that pull with the highlight already gone —
        // this line makes the fake answer the same way. Without it the fake would keep serving the pre-clear
        // colour and the pull would paint it straight back, which is a fixture that contradicts the command it
        // just accepted, not a product defect. (Before the mini-toolbar helper started keeping the pulled and
        // pushed snapshots in agreement, this test passed only because the stub happened to carry no highlight
        // at all — the right answer for the wrong reason.)
        SetDocumentCanvasFormattingStateJson("""{"bold":false,"italic":false,"underline":false,"alignment":"left"}""");

        await cut.InvokeAsync(() => highlightPicker.Instance.ValueChanged.InvokeAsync(string.Empty));

        var highlightInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 1
                && string.Equals(invocation.Arguments[1]?.ToString(), "highlight", StringComparison.Ordinal));
        highlightInvocation
            .Should()
            .NotBeNull();
        highlightInvocation!.Arguments[2]?.ToString()
            .Should()
            .NotContain("#fde68a");
        cut.Find("[data-testid='document-highlight-color-trigger'] .tm-color-picker-trigger-text").TextContent.Trim().Should().NotBe("#fde68a");
    }

    [Fact]
    public async Task Toolbar_FormattingControlsRouteToExplicitJsRuntimeCommands()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await cut.Find("[data-testid='document-bold']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-testid='document-font-size']").ChangeAsync(new ChangeEventArgs { Value = "14" });
        await cut.Find("[data-testid='document-align-right']").ClickAsync(new MouseEventArgs());

        var commands = SetupDocumentCanvasModule().Invocations
            .Where(invocation => invocation.Identifier == "execCommand")
            .Select(invocation => invocation.Arguments.Count > 1 ? invocation.Arguments[1]?.ToString() : null)
            .ToList();

        commands.Should().Contain("bold");
        commands.Should().Contain("fontsize");
        commands.Should().Contain("align");
    }

    [Fact]
    public async Task ToolbarFontSizeCommand_RejectsOutOfRangeValue()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await cut.Find("[data-testid='document-font-size']").ChangeAsync(new ChangeEventArgs { Value = "120" });

        SetupDocumentCanvasModule().Invocations
            .Where(invocation => invocation.Identifier == "execCommand")
            .Select(invocation => invocation.Arguments.Count > 1 ? invocation.Arguments[1]?.ToString() : null)
            .Should()
            .NotContain("fontsize");
    }

    [Fact]
    public void Toolbar_ParagraphControlsRenderAndExposeMixedAlignmentState()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ParagraphAlignment, DocumentTextAlignment.Left)
            .Add(p => p.ParagraphAlignmentMixed, true));

        cut.Find("[data-testid='document-align-left']")
            .GetAttribute("aria-pressed")
            .Should()
            .Be("mixed");
        cut.Find("[data-testid='document-align-justify']").Should().NotBeNull();
        cut.Find("[data-testid='document-line-spacing']").TextContent.Should().Contain("1.5");
        cut.Find("[data-testid='document-spacing-before']").Should().NotBeNull();
        cut.Find("[data-testid='document-spacing-after']").Should().NotBeNull();
        cut.Find("[data-testid='document-increase-indent']").Should().NotBeNull();
        cut.Find("[data-testid='document-decrease-indent']").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_RendersFontControlsFromProviderData()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.FontFamilies, new[]
            {
                new DocumentFontFamily { Key = "georgia", DisplayName = "Georgia", CssFamily = "Georgia, serif" }
            }));

        cut.Find("[data-testid='document-font-family']").TextContent.Should().Contain("Georgia");
        cut.Find("[data-testid='document-font-size']").TextContent.Should().Contain("12");
        cut.Find("[data-testid='document-font-color-trigger']").Should().NotBeNull();
        cut.Find("[data-testid='document-highlight-color-trigger']").Should().NotBeNull();
    }

    [Fact]
    public async Task SaveRequest_UsesStructuredProviderBoundaryDocumentWithoutDisplayOnlyImageUrl()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;

        var provider = new InMemoryDocumentEditorProvider();
        var document = CreatePhase17ProviderDocument();
        await SeedDocumentAsync(provider, document);

        var domSnapshot = Clone(document);
        GetSingleImageContent(domSnapshot, "image-1").Url = "blob:https://app.test/display-only";
        SetCanvasRuntimeDocument(domSnapshot);

        DocumentEditorSaveRequest? captured = null;
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-phase17")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.OnSaveRequested, request => captured = request));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => captured.Should().NotBeNull());
        captured!.Document.Should().NotBeNull();
        AssertPhase17Metadata(captured.Document!);
        var image = GetSingleImageContent(captured.Document!, "image-1");
        image.Source.Should().Be(DocumentImageSource.Asset);
        image.AssetId.Should().Be("asset-1");
        image.Url.Should().BeNull();
        JsonSerializer.Serialize(captured.Document, DocumentEditorJson.Options).Should().NotContain("display-only");
    }

    [Fact]
    public async Task SaveRequest_UsesCanonicalRuntimeDocumentForFormattingCommentsRevisionsAndJsonSnapshot()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;

        var provider = new InMemoryDocumentEditorProvider();
        var seeded = DocumentEditorDocument.Empty("doc-phase16");
        seeded.Blocks.Add(new DocumentBlock
        {
            Id = "phase16-paragraph",
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Id = "phase16-run-old", Text = "Old provider text" }]
            }
        });
        seeded.Comments.Add(new DocumentComment { Id = "old-comment" });
        seeded.Revisions.Add(new DocumentRevision { Id = "old-revision", Type = DocumentRevisionType.Insertion });
        await SeedDocumentAsync(provider, seeded);

        var runtimeDocument = Clone(seeded);
        var runtimeParagraph = (ParagraphBlockContent)runtimeDocument.Blocks.Single().Content;
        runtimeParagraph.Inlines =
        [
            new TextRun
            {
                Id = "phase16-run-runtime",
                Text = "Runtime persisted text",
                Marks =
                [
                    new InlineMark { Type = InlineMarkType.Bold },
                    new InlineMark { Type = InlineMarkType.FontFamily, Value = "Georgia, serif" },
                    new InlineMark { Type = InlineMarkType.FontSize, Value = "22pt" },
                    new InlineMark { Type = InlineMarkType.TextColor, Value = "#2563eb" },
                    new InlineMark { Type = InlineMarkType.Highlight, Value = "#fef08a" },
                    new InlineMark
                    {
                        Type = InlineMarkType.CommentAnchor,
                        CommentAnchor = new CommentAnchorMarkData { CommentId = "runtime-comment" }
                    },
                    new InlineMark
                    {
                        Type = InlineMarkType.Revision,
                        RevisionId = "runtime-revision",
                        Value = "Insertion"
                    }
                ]
            }
        ];
        runtimeDocument.Comments =
        [
            new DocumentComment
            {
                Id = "runtime-comment",
                Anchor = new DocumentCommentAnchor
                {
                    Type = DocumentCommentAnchorType.TextRange,
                    BlockId = "phase16-paragraph",
                    StartInlineIndex = 0,
                    StartOffset = 0,
                    EndInlineIndex = 0,
                    EndOffset = 7
                },
                Entries =
                [
                    new DocumentCommentEntry
                    {
                        Id = "runtime-comment-entry",
                        Author = new DocumentEditorAuthor { Id = "reviewer", DisplayName = "Reviewer" },
                        Text = "Runtime comment",
                        CreatedAt = DateTimeOffset.Parse("2026-05-24T10:00:00Z")
                    }
                ]
            }
        ];
        runtimeDocument.Revisions =
        [
            new DocumentRevision
            {
                Id = "runtime-revision",
                Type = DocumentRevisionType.Insertion,
                Range = new DocumentRevisionRange
                {
                    BlockId = "phase16-paragraph",
                    StartInlineIndex = 0,
                    StartOffset = 0,
                    EndInlineIndex = 0,
                    EndOffset = 22
                },
                Author = new DocumentRevisionAuthor { Id = "reviewer", DisplayName = "Reviewer" },
                CreatedAt = DateTimeOffset.Parse("2026-05-24T10:01:00Z"),
                Action = DocumentRevisionAction.Pending,
                PayloadJson = """{"text":"Runtime persisted text"}""",
                GroupId = "runtime-group"
            }
        ];
        SetCanvasRuntimeDocument(runtimeDocument);

        DocumentEditorSaveRequest? captured = null;
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-phase16")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.OnSaveRequested, request => captured = request));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => captured.Should().NotBeNull());
        captured!.Document.Should().NotBeNull();
        captured.JsonSnapshot.Should().NotBeNullOrWhiteSpace();
        GetParagraphText(captured.Document!).Should().Be("Runtime persisted text");
        var savedRun = ((ParagraphBlockContent)captured.Document!.Blocks.Single().Content).Inlines.OfType<TextRun>().Single();
        savedRun.Marks.Should().Contain(mark => mark.Type == InlineMarkType.Bold);
        savedRun.Marks.Should().Contain(mark => mark.Type == InlineMarkType.FontFamily && mark.Value == "Georgia, serif");
        savedRun.Marks.Should().Contain(mark => mark.Type == InlineMarkType.FontSize && mark.Value == "22pt");
        savedRun.Marks.Should().Contain(mark => mark.Type == InlineMarkType.TextColor && mark.Value == "#2563eb");
        savedRun.Marks.Should().Contain(mark => mark.Type == InlineMarkType.Highlight && mark.Value == "#fef08a");
        captured.Document.Comments.Should().ContainSingle(comment => comment.Id == "runtime-comment");
        captured.Document.Comments.Should().NotContain(comment => comment.Id == "old-comment");
        var revision = captured.Document.Revisions.Should().ContainSingle(revision => revision.Id == "runtime-revision").Subject;
        revision.GroupId.Should().Be("runtime-group");
        revision.Range.BlockId.Should().Be("phase16-paragraph");
        revision.PayloadJson.Should().Contain("Runtime persisted text");
        captured.JsonSnapshot.Should().Contain("runtime-comment");
        captured.JsonSnapshot.Should().Contain("runtime-revision");
        captured.JsonSnapshot.Should().Contain("Runtime persisted text");
        captured.JsonSnapshot.Should().NotContain("old-comment");

        var saved = (await provider.LoadAsync("doc-phase16")).Document!;
        GetParagraphText(saved).Should().Be("Runtime persisted text");
        saved.Comments.Should().ContainSingle(comment => comment.Id == "runtime-comment");
        saved.Revisions.Should().ContainSingle(revision => revision.Id == "runtime-revision");
    }

    [Fact]
    public async Task ExportRequests_ReceiveStructuredMetadataForDocxAndPdfProviders()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.SetupVoid("tmDocumentEditor.downloadFile", _ => true).SetVoidResult();

        var provider = new InMemoryDocumentEditorProvider();
        var document = CreatePhase17ProviderDocument();
        await SeedDocumentAsync(provider, document);

        var domSnapshot = Clone(document);
        GetSingleImageContent(domSnapshot, "image-1").Url = "https://cdn.test/display-url.png";
        SetCanvasRuntimeDocument(domSnapshot);

        var pdfProvider = new CapturingPdfExportProvider();
        var formatProvider = new CapturingDocumentFormatProvider();
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-phase17")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.PdfExportProvider, pdfProvider)
                      .Add(p => p.FormatProvider, formatProvider));

        await MarkCanvasReadyAsync(cut);
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-ribbon-tab-references']").Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-references']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-export-docx']").Should().NotBeNull());

        cut.Find("[data-testid='document-export-docx']").Click();
        cut.WaitForAssertion(() => formatProvider.LastExportRequest.Should().NotBeNull());

        cut.Find("[data-testid='document-export-pdf']").Click();
        cut.WaitForAssertion(() => pdfProvider.LastRequest.Should().NotBeNull());

        AssertPhase17Metadata(formatProvider.LastExportRequest!.Document);
        AssertPhase17Metadata(pdfProvider.LastRequest!.Document);
        GetParagraphText(formatProvider.LastExportRequest.Document).Should().StartWith("Provider export text");
        GetParagraphText(pdfProvider.LastRequest.Document).Should().StartWith("Provider export text");
        GetSingleImageContent(formatProvider.LastExportRequest.Document, "image-1")
            .Url.Should().BeNull();
        GetSingleImageContent(pdfProvider.LastRequest.Document, "image-1")
            .Url.Should().BeNull();
        pdfProvider.LastRequest.Options.PageSetup.PageSize.Name.Should().Be("A4");
        formatProvider.LastExportRequest.Format.Should().Be(DocumentFormatProviderKind.Docx);
    }

    [Fact]
    public async Task Phase19_PdfExportRequest_IncludesImageTableAndReviewDisplayOptions()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.SetupVoid("tmDocumentEditor.downloadFile", _ => true).SetVoidResult();

        var provider = new InMemoryDocumentEditorProvider();
        var document = CreatePhase19ExportDocument();
        await SeedDocumentAsync(provider, document);
        SetCanvasRuntimeDocument(Clone(document));

        var pdfProvider = new CapturingPdfExportProvider();
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, document.DocumentId)
                      .Add(p => p.Provider, provider)
                      .Add(p => p.PdfExportProvider, pdfProvider));

        await MarkCanvasReadyAsync(cut);
        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        await cut.Find("[data-testid='document-review-display-mode']").ChangeAsync(new ChangeEventArgs
        {
            Value = DocumentReviewDisplayMode.NoMarkup.ToString()
        });
        cut.Find("[data-testid='document-ribbon-tab-references']").Click();
        cut.Find("[data-testid='document-export-pdf']").Click();

        cut.WaitForAssertion(() => pdfProvider.LastRequest.Should().NotBeNull());
        var request = pdfProvider.LastRequest!;
        var image = GetSingleImageContent(request.Document, "image-1");
        image.Size.Width.Should().Be(320);
        image.Size.Height.Should().Be(180);
        image.Layout.Wrap.Mode.Should().Be(DocumentWrapMode.Square);
        image.LinkUrl.Should().Be("https://example.test/image");
        var table = request.Document.Blocks.Select(block => block.Content).OfType<TableBlockContent>().Single();
        table.Layout.Width.Should().Be(420);
        table.Layout.Alignment.Should().Be(TableHorizontalAlignment.Center);
        table.Rows[0].Cells[0].BackgroundColor.Should().Be("#ffef9a");
        request.Options.ReviewDisplayMode.Should().Be(DocumentReviewDisplayMode.NoMarkup);
        request.Options.IncludeSuggestions.Should().BeFalse();
        request.Options.IncludeComments.Should().BeTrue();
    }

    [Fact]
    public async Task PdfExport_CapturesCanvasLayoutSnapshotIntoRequest()
    {
        const string layoutSnapshotJson = """{"schemaVersion":1,"pageCount":1,"pages":[{"index":0,"width":794,"height":1123,"commands":[]}]}""";
        var module = SetupDocumentCanvasModule();
        module.Setup<string?>("getLayoutSnapshotJson", _ => true).SetResult(layoutSnapshotJson);

        var provider = new InMemoryDocumentEditorProvider();
        var document = CreatePhase17ProviderDocument();
        await SeedDocumentAsync(provider, document);
        SetCanvasRuntimeDocument(Clone(document));

        var pdfProvider = new CapturingPdfExportProvider();
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-phase17")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.PdfExportProvider, pdfProvider));

        await MarkCanvasReadyAsync(cut);
        cut.Find("[data-testid='document-ribbon-tab-references']").Click();
        cut.Find("[data-testid='document-export-pdf']").Click();

        cut.WaitForAssertion(() => pdfProvider.LastRequest.Should().NotBeNull());
        pdfProvider.LastRequest!.LayoutSnapshotJson.Should().Be(
            layoutSnapshotJson,
            "the editor must ship its live canvas layout snapshot so WYSIWYG-parity PDF renderers can reuse the exact page layout");
    }

    [Fact]
    public async Task ExportRequests_UseJsRuntimeDocumentAfterLocalRuntimeEdit()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.SetupVoid("tmDocumentEditor.downloadFile", _ => true).SetVoidResult();

        var provider = new InMemoryDocumentEditorProvider();
        var document = CreatePhase17ProviderDocument();
        await SeedDocumentAsync(provider, document);

        var runtimeDocument = Clone(document);
        var runtimeRun = ((ParagraphBlockContent)runtimeDocument.Blocks.Single(block => block.Id == "paragraph-1").Content)
            .Inlines
            .OfType<TextRun>()
            .Single();
        runtimeRun.Text = "Runtime export text";
        SetCanvasRuntimeDocument(runtimeDocument);

        var pdfProvider = new CapturingPdfExportProvider();
        var formatProvider = new CapturingDocumentFormatProvider();
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-phase17")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.PdfExportProvider, pdfProvider)
                      .Add(p => p.FormatProvider, formatProvider));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-references']").Click();
        cut.Find("[data-testid='document-export-docx']").Click();
        cut.WaitForAssertion(() => formatProvider.LastExportRequest.Should().NotBeNull());

        cut.Find("[data-testid='document-export-pdf']").Click();
        cut.WaitForAssertion(() => pdfProvider.LastRequest.Should().NotBeNull());

        GetParagraphText(formatProvider.LastExportRequest!.Document).Should().StartWith("Runtime export text");
        GetParagraphText(pdfProvider.LastRequest!.Document).Should().StartWith("Runtime export text");
        SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "getModelJson")
            .Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task VersionCreate_SavesJsRuntimeDocumentBeforeProviderVersionSnapshot()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var focusModule = JSInterop.SetupModule("./_content/Tempo.Blazor.DocumentEditor/js/document-editor/focus-management.mjs");
        focusModule.SetupVoid("trapFocus", _ => true).SetVoidResult();
        focusModule.SetupVoid("releaseFocusTrap", _ => true).SetVoidResult();

        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var runtimeDocument = Clone(seeded);
        var runtimeRun = runtimeDocument.Blocks
            .Select(block => block.Content)
            .OfType<ParagraphBlockContent>()
            .First()
            .Inlines
            .OfType<TextRun>()
            .First();
        runtimeRun.Text = "Runtime version text";
        SetCanvasRuntimeDocument(runtimeDocument);

        DocumentEditorSaveRequest? capturedSave = null;
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.OnSaveRequested, request => capturedSave = request));

        await MarkCanvasReadyAsync(cut);
        await NotifyCanvasChangedAsync(cut);

        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-version-create-open']").Should().NotBeNull());
        await cut.Find("[data-testid='document-version-create-open']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-version-dialog']").Should().NotBeNull());
        await NotifyCanvasChangedAsync(cut);
        await cut.Find("[data-testid='document-version-create-submit']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => capturedSave.Should().NotBeNull());
        GetParagraphText(capturedSave!.Document!).Should().StartWith("Runtime version text");
        var versions = await provider.GetVersionsAsync("doc-1");
        versions.Should().ContainSingle();
        DocumentEditorJson.Deserialize(versions[0].Snapshot.Json)
            .Blocks.Select(block => block.Content)
            .OfType<ParagraphBlockContent>()
            .First()
            .Inlines.OfType<TextRun>()
            .First()
            .Text.Should().Be("Runtime version text");
    }

    [Fact]
    public async Task ToolbarUndo_UsesJsRuntimeOnlyAndDoesNotRefreshSnapshotAfterLocalPatch()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        SetDocumentCanvasUndoStateJson("""{"canUndo":true,"canRedo":false}""");

        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);
        await NotifyCanvasChangedAsync(cut);

        var replaceCallsBeforeUndo = SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel");
        cut.Find("[data-testid='document-undo']").Click();

        cut.WaitForAssertion(() => HasCanvasCommand("undo").Should().BeTrue());
        SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel")
            .Should().Be(replaceCallsBeforeUndo, "canvas-owned undo must not reload a C# snapshot into the runtime");
    }

    [Fact]
    public async Task ToolbarUndo_RuntimeSnapshotSyncDoesNotEchoReloadIntoCanvasHost()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        SetDocumentCanvasUndoStateJson("""{"canUndo":true,"canRedo":false}""");

        var provider = new InMemoryDocumentEditorProvider();
        var runtimeDocument = provider.SeedContractDocument("doc-1");
        var (_, inline) = GetFirstParagraphTextRun(runtimeDocument);
        inline.Text = "Undo restored runtime text";
        SetCanvasRuntimeDocument(runtimeDocument);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);
        var host = FindCanvasHost(cut).Instance;

        var replaceCallsBeforeUndo = SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel");
        cut.Find("[data-testid='document-undo']").Click();

        cut.WaitForAssertion(() => HasCanvasCommand("undo").Should().BeTrue());
        SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel")
            .Should().Be(replaceCallsBeforeUndo, "a canvas-owned undo snapshot is already synchronized and must not be echoed back as a reload");
        FindCanvasHost(cut).Instance.Should().BeSameAs(host);
    }

    [Fact]
    public async Task ImportDocx_ReloadsImportedDocumentIntoJsRuntimeExplicitly()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;

        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var imported = DocumentEditorDocument.Empty("doc-1");
        imported.Metadata.Title = "Imported title";
        imported.Blocks.Add(new DocumentBlock
        {
            Id = "imported-paragraph",
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "Imported runtime reload" }] }
        });
        var formatProvider = new CapturingDocumentFormatProvider { ImportedDocument = imported };
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.FormatProvider, formatProvider));

        await MarkCanvasReadyAsync(cut);
        cut.Find("[data-testid='document-ribbon-tab-references']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-import-docx-label']").Should().NotBeNull());
        cut.Find("[data-testid='document-import-docx-label']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-import-docx-panel']").Should().NotBeNull());
        var replaceCallsBeforeImport = SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel");

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3], "import.docx", contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));

        cut.WaitForAssertion(() => formatProvider.LastImportRequest.Should().NotBeNull());
        SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel")
            .Should().BeGreaterThan(replaceCallsBeforeImport, "DOCX import must explicitly reload the canvas-owned runtime snapshot");
    }

    [Fact]
    public async Task HeaderFooterScopeToggle_UsesCanvasRuntimeCommandInsteadOfSnapshotRefresh()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;

        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);
        cut.Find("[data-testid='document-ribbon-tab-layout']").Click();
        var replaceCallsBeforeToggle = SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel");

        cut.Find("[data-testid='document-different-first-page']").Click();

        HasCanvasCommand("differentFirstPage").Should().BeTrue();
        SetupDocumentCanvasModule().Invocations.Count(invocation => invocation.Identifier == "replaceModel")
            .Should().Be(replaceCallsBeforeToggle, "live header/footer layout commands must not force a C# snapshot reload in the canvas-owned runtime");
    }

    [Fact]
    public async Task CanvasSelectionChanged_UpdatesRibbonFormattingState()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        inline.Text = "Bold ";
        inline.Marks.Add(new InlineMark { Type = InlineMarkType.Bold });
        var plainInline = new TextRun { Id = "plain-inline", Text = "plain" };
        ((ParagraphBlockContent)paragraph.Content).Inlines.Add(plainInline);
        await provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = "doc-1",
            Document = seeded,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = false,
            Selection = new WysiwygSelectionSnapshot
            {
                AnchorBlockId = paragraph.Id,
                AnchorInlineId = inline.Id,
                AnchorOffset = 2,
                FocusBlockId = paragraph.Id,
                FocusInlineId = inline.Id,
                FocusOffset = 2,
                IsCollapsed = true
            }
        },
        new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState
            {
                Bold = true,
                Alignment = "left"
            }
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-bold']").GetAttribute("aria-pressed").Should().Be("true"));

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = false,
            Selection = new WysiwygSelectionSnapshot
            {
                AnchorBlockId = paragraph.Id,
                AnchorInlineId = plainInline.Id,
                AnchorOffset = 7,
                FocusBlockId = paragraph.Id,
                FocusInlineId = plainInline.Id,
                FocusOffset = 7,
                IsCollapsed = true
            }
        },
        new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState
            {
                Bold = false,
                Alignment = "left"
            }
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-bold']").GetAttribute("aria-pressed").Should().Be("false"));
    }

    [Fact]
    public async Task CanvasSelectionChanged_InHeaderShowsContextualRibbonAndFormatsHeaderSelection()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        DocumentHeaderFooterResolver.EnsurePrimaryHeadersFooters(seeded);
        var header = seeded.HeadersFooters.First(headerFooter => headerFooter.Type == DocumentHeaderFooterType.Header);
        var headerParagraph = header.Blocks[0];
        var headerInline = ((ParagraphBlockContent)headerParagraph.Content).Inlines.OfType<TextRun>().Single();
        headerInline.Text = "Header";
        headerInline.Marks.Add(new InlineMark { Type = InlineMarkType.Bold });
        await provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = "doc-1",
            Document = seeded,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        });

        SetDocumentCanvasSelectionStateJson($$"""{"isCollapsed":true,"pageIndex":0,"region":"Header","headerFooterScope":"Primary","anchorBlockId":"{{headerParagraph.Id}}","focusBlockId":"{{headerParagraph.Id}}"}""");
        SetDocumentCanvasFormattingStateJson("""{"bold":true,"italic":false,"underline":false,"alignment":"left"}""");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-ribbon-tab-header-footer']").GetAttribute("aria-selected").Should().Be("true"));
        cut.Find("[data-testid='document-ribbon-tab-home']").Click();
        cut.Find("[data-testid='document-bold']").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public async Task TextContextMenuRequested_RendersMenuAndRunsBoldAgainstRestoredSelection()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        var selection = new WysiwygSelectionSnapshot
        {
            AnchorBlockId = paragraph.Id,
            AnchorInlineId = inline.Id,
            AnchorOffset = 0,
            FocusBlockId = paragraph.Id,
            FocusInlineId = inline.Id,
            FocusOffset = 4,
            IsCollapsed = false
        };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await NotifyCanvasContextMenuAsync(cut, new TmDocumentCanvasEngineHost.CanvasEngineContextMenuRequest
        {
            X = 200,
            Y = 120,
            HasSelection = true,
            Selection = selection
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-text-context-menu']").TextContent.Should().Contain("Bold"));
        cut.Find("[data-testid='document-context-comment']").TextContent.Should().Contain("Comment");
        cut.Find("[data-testid='document-context-bold']").Click();

        HasCanvasCommand("bold").Should().BeTrue();

    }

    [Fact]
    public async Task TextContextMenuRequested_ShowsTruthfulClipboardStatesAndHidesAdvancedTextCommands()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        var selection = new WysiwygSelectionSnapshot
        {
            AnchorBlockId = paragraph.Id,
            AnchorInlineId = inline.Id,
            AnchorOffset = 0,
            FocusBlockId = paragraph.Id,
            FocusInlineId = inline.Id,
            FocusOffset = 4,
            IsCollapsed = false
        };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await NotifyCanvasContextMenuAsync(cut, new TmDocumentCanvasEngineHost.CanvasEngineContextMenuRequest
        {
            X = 200,
            Y = 120,
            HasSelection = true,
            Selection = selection
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-text-context-menu']").Should().NotBeNull());

        // B11/B12 (UX fix 2026-06-11): clipboard commands were previously hard-disabled. They are now truthful —
        // with a non-empty selection in an editable document, Cut and Copy are enabled, and Paste is offered
        // (the async Clipboard API supplies the content, falling back to a Ctrl+V hint if the browser blocks it).
        cut.Find("[data-testid='document-context-cut']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='document-context-copy']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='document-context-paste']").HasAttribute("disabled").Should().BeFalse();

        var hiddenContextCommands = new[]
        {
            "document-context-font",
            "document-context-paragraph"
        };

        foreach (var testId in hiddenContextCommands)
        {
            cut.FindAll($"[data-testid='{testId}']").Should().BeEmpty(testId);
        }

        cut.Find("[data-testid='document-context-bold']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='document-context-italic']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='document-context-comment']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task KeyboardShortcuts_CtrlShiftPOpensDocumentCommandPalette()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        await MarkCanvasReadyAsync(cut);

        await cut.Find(".tm-document-editor").KeyDownAsync(new KeyboardEventArgs
        {
            Key = "p",
            CtrlKey = true,
            ShiftKey = true
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-command-palette']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-command-palette-item']")
            .Should()
            .Contain(item => item.GetAttribute("data-command") == "bold");
    }

    [Fact]
    public async Task CommandPalette_ClickEnabledCommand_ExecutesThroughRegistry()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        await MarkCanvasReadyAsync(cut);

        await cut.Find(".tm-document-editor").KeyDownAsync(new KeyboardEventArgs
        {
            Key = "p",
            CtrlKey = true,
            ShiftKey = true
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-command-palette']").Should().NotBeNull());
        cut.Find("[data-testid='document-command-palette-search']").Input("Bold");
        cut.Find("[data-command='bold'] button").Click();

        HasCanvasCommand("bold").Should().BeTrue();
        cut.FindAll("[data-testid='document-command-palette']").Should().BeEmpty();
    }

    [Fact]
    public async Task TableContextMenuRequested_RendersMenuAndRunsStructuredTableCommand()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var selection = new WysiwygSelectionSnapshot
        {
            Region = "TableCell",
            AnchorBlockId = "cell-block-1",
            AnchorInlineId = "cell-inline-1",
            ActiveTableCellId = "cell-1",
            TableCellPath = "table-1/row-0/cell-1",
            IsCollapsed = true
        };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await NotifyCanvasContextMenuAsync(cut, new TmDocumentCanvasEngineHost.CanvasEngineContextMenuRequest
        {
            X = 200,
            Y = 120,
            InTable = true,
            TableId = "table-1",
            CellId = "cell-1",
            Selection = selection
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-table-context-menu']").TextContent.Should().Contain("Add row"));
        cut.Find("[data-testid='document-table-delete-table']").TextContent.Should().Contain("Delete table");
        cut.Find("[data-testid='document-table-cell-properties']").TextContent.Should().Contain("Cell properties");
        cut.Find("[data-testid='document-table-table-properties']").TextContent.Should().Contain("Table properties");
        cut.Find("[data-testid='document-table-insert-row']").Click();

        HasCanvasCommand("insertTableRowAfter").Should().BeTrue();
    }

    [Fact]
    public async Task MiniToolbarChanged_RendersToolbarAndRunsBoldThroughCanvasCommand()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        var selection = new WysiwygSelectionSnapshot
        {
            AnchorBlockId = paragraph.Id,
            AnchorInlineId = inline.Id,
            AnchorOffset = 0,
            FocusBlockId = paragraph.Id,
            FocusInlineId = inline.Id,
            FocusOffset = 4,
            IsCollapsed = false
        };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 220,
            Top = 96,
            Width = 184,
            Height = 40,
            Selection = selection
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-mini-toolbar']").Should().NotBeNull());
        cut.Find("[data-testid='document-mini-bold']").Click();

        HasCanvasCommand("bold").Should().BeTrue();

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = false,
            Reason = "selection-collapsed"
        });
        cut.FindAll("[data-testid='document-mini-toolbar']").Should().NotBeEmpty();

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = false,
            Reason = "editable-pointerdown"
        });
        cut.FindAll("[data-testid='document-mini-toolbar']").Should().BeEmpty();
    }

    [Fact]
    public async Task MiniToolbarChanged_IgnoresCollapsedSelectionRequest()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 220,
            Top = 96,
            Width = 184,
            Height = 40,
            Selection = new WysiwygSelectionSnapshot
            {
                AnchorBlockId = paragraph.Id,
                AnchorInlineId = inline.Id,
                AnchorOffset = 4,
                FocusBlockId = paragraph.Id,
                FocusInlineId = inline.Id,
                FocusOffset = 4,
                IsCollapsed = true
            }
        });

        cut.FindAll("[data-testid='document-mini-toolbar']").Should().BeEmpty();
    }

    [Fact]
    public void Render_MissingProviderShowsError()
    {
        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1"));

        cut.Find(".tm-document-editor__error").TextContent.Should().Contain("provider");
        cut.FindAll("[data-testid='document-canvas-engine-host']").Should().BeEmpty();
    }

    [Fact]
    public async Task CanvasRuntimeChange_UpdatesDocumentAndExplicitSavePersistsIt()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var runtimeDocument = Clone(seeded);
        var (_, inline) = GetFirstParagraphTextRun(runtimeDocument);
        inline.Text = $"Draft {inline.Text}";
        SetCanvasRuntimeAndAnnotations(runtimeDocument);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        GetParagraphText(saved).Should().StartWith("Draft ");
    }

    [Fact]
    public async Task Save_RequestsJsRuntimeDocumentInsteadOfStaleCSharpDocument()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var runtimeDocument = DocumentEditorJson.Deserialize(DocumentEditorJson.Serialize(seeded));
        var runtimeParagraph = runtimeDocument.Blocks.First(block => block.Content is ParagraphBlockContent);
        var runtimeTextRun = ((ParagraphBlockContent)runtimeParagraph.Content).Inlines.OfType<TextRun>().First();
        runtimeTextRun.Text = "Runtime-only text";

        SetCanvasRuntimeAndAnnotations(runtimeDocument);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        GetParagraphText(saved).Should().StartWith("Runtime-only text");
        SetupDocumentCanvasModule().Invocations.Should().Contain(invocation => invocation.Identifier == "getModelJson");
    }

    [Fact]
    public async Task TrackChanges_InsertText_CreatesPendingInlineRevision()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        var runtimeDocument = CreateInsertedRevisionDocument(seeded);
        await SeedDocumentAsync(provider, runtimeDocument);
        SetCanvasRuntimeAndAnnotations(runtimeDocument);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(1));
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        var revision = saved.Revisions.Should().ContainSingle().Subject;
        revision.Type.Should().Be(DocumentRevisionType.Insertion);
        revision.Action.Should().Be(DocumentRevisionAction.Pending);
        revision.PayloadJson.Should().Be("Draft ");
        GetParagraphText(saved).Should().StartWith("Draft ");
        GetRevisionTextRuns(saved).Should().ContainSingle(run => run.Text == "Draft ");
    }

    [Fact]
    public async Task TrackChanges_InsertText_WithSameRevisionId_AppendsToSinglePendingRevision()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        const string revisionId = "revision-live-insert";
        var runtimeDocument = CreateInsertedRevisionDocument(seeded, "Draft ", revisionId);
        await SeedDocumentAsync(provider, runtimeDocument);
        SetCanvasRuntimeAndAnnotations(runtimeDocument);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(1));
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        var revision = saved.Revisions.Should().ContainSingle().Subject;
        revision.Id.Should().Be(revisionId);
        revision.PayloadJson.Should().Be("Draft ");
        GetRevisionTextRuns(saved).Should().ContainSingle(run => run.Text == "Draft ");
    }

    [Fact]
    public async Task TrackChanges_InsertBlock_DoesNotDropPendingInlineRevisions()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        var runtimeDocument = CreateInsertedBlockRevisionDocument(seeded);
        await SeedDocumentAsync(provider, runtimeDocument);
        SetCanvasRuntimeAndAnnotations(runtimeDocument);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-revision-item']")
                .Select(item => item.TextContent)
                .Should()
                .Contain(text => text.Contains("Insertion", StringComparison.Ordinal)));
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        saved.Revisions.Should().Contain(revision =>
            revision.Type == DocumentRevisionType.Insertion
            && revision.PayloadJson == "Draft "
            && revision.Action == DocumentRevisionAction.Pending);
        saved.Blocks.Should().Contain(block => block.Id == "tracked-enter-block");
        GetRevisionTextRuns(saved).Should().ContainSingle(run => run.Text == "Draft ");
    }

    [Fact]
    public async Task TrackChanges_AcceptInsertion_KeepsTextAndClearsRevisionMark()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        var pending = CreateInsertedRevisionDocument(seeded);
        await SeedDocumentAsync(provider, pending);
        SetCanvasRuntimeAndAnnotations(pending);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-revision-accept']").Should().NotBeNull());
        cut.Find("[data-testid='document-revision-accept']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("acceptrevision").Should().BeTrue());
        var acceptedRuntime = ReviewRevision(pending, "revision-live-insert", DocumentRevisionAction.Accepted);
        SetCanvasRuntimeAndAnnotations(acceptedRuntime);
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        saved.Revisions.Should().ContainSingle().Subject.Action.Should().Be(DocumentRevisionAction.Accepted);
        GetParagraphText(saved).Should().StartWith("Draft ");
        GetRevisionTextRuns(saved).Should().BeEmpty();
    }

    [Fact]
    public async Task TrackChanges_RejectInsertion_RemovesTextAndClearsRevisionMark()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        var pending = CreateInsertedRevisionDocument(seeded);
        await SeedDocumentAsync(provider, pending);
        SetCanvasRuntimeAndAnnotations(pending);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-revision-reject']").Should().NotBeNull());
        cut.Find("[data-testid='document-revision-reject']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("rejectrevision").Should().BeTrue());
        var rejectedRuntime = ReviewRevision(pending, "revision-live-insert", DocumentRevisionAction.Rejected, removeMarkedContent: true);
        SetCanvasRuntimeAndAnnotations(rejectedRuntime);
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        saved.Revisions.Should().ContainSingle().Subject.Action.Should().Be(DocumentRevisionAction.Rejected);
        GetParagraphText(saved).Should().StartWith("This agreement");
        GetRevisionTextRuns(saved).Should().BeEmpty();
    }

    [Fact]
    public async Task TrackChanges_AcceptDeletion_RemovesDeletedText()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        var deletionRuntime = CreateDeletionRevisionDocument(seeded);
        await SeedDocumentAsync(provider, deletionRuntime);
        SetCanvasRuntimeAndAnnotations(deletionRuntime);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(1));
        cut.Find("[data-testid='document-save']").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));

        var pending = (await provider.LoadAsync("doc-1")).Document!;
        var revision = pending.Revisions.Should().ContainSingle().Subject;
        revision.Type.Should().Be(DocumentRevisionType.Deletion);
        revision.Action.Should().Be(DocumentRevisionAction.Pending);
        revision.PayloadJson.Should().Be("s");
        GetParagraphText(pending).Should().StartWith("This agreement");
        GetRevisionTextRuns(pending).Should().ContainSingle(run => run.Text == "s");

        cut.Find("[data-testid='document-revision-accept']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("acceptrevision").Should().BeTrue());
        var acceptedRuntime = ReviewRevision(deletionRuntime, "revision-delete", DocumentRevisionAction.Accepted, removeMarkedContent: true);
        SetCanvasRuntimeAndAnnotations(acceptedRuntime);
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var accepted = (await provider.LoadAsync("doc-1")).Document!;
        accepted.Revisions.Should().ContainSingle().Subject.Action.Should().Be(DocumentRevisionAction.Accepted);
        GetParagraphText(accepted).Should().StartWith("Thi agreement");
        GetRevisionTextRuns(accepted).Should().BeEmpty();
    }

    [Fact]
    public async Task TrackChanges_ToggleMark_CreatesFormattingRevision()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        var formattingRuntime = CreateFormattingRevisionDocument(seeded);
        var (_, inline) = GetFirstPlainParagraphTextRun(seeded);
        var selectedText = inline.Text[..4];
        await SeedDocumentAsync(provider, formattingRuntime);
        SetCanvasRuntimeAndAnnotations(formattingRuntime);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-revision-item']").TextContent.Should().Contain("Formatting"));
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        var revision = saved.Revisions.Should().ContainSingle().Subject;
        revision.Type.Should().Be(DocumentRevisionType.Formatting);
        var payload = JsonSerializer.Deserialize<DocumentFormattingRevisionPayload>(revision.PayloadJson!, DocumentEditorJson.Options);
        payload!.MarkType.Should().Be(InlineMarkType.Bold);
        payload.NewActive.Should().BeTrue();
        GetRevisionTextRuns(saved).Should().ContainSingle(run =>
            run.Text == selectedText
            && run.Marks.Any(mark => mark.Type == InlineMarkType.Bold)
            && run.Marks.Any(mark => mark.Type == InlineMarkType.Revision));
    }

    [Fact]
    public async Task TrackChanges_RejectFormatting_RevertsMarkAndClearsRevisionMark()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = await SeedContractDocumentWithoutSeedRevisionsAsync(provider, "doc-1");
        var (paragraph, inline) = GetFirstPlainParagraphTextRun(seeded);
        var formattingRuntime = CreateFormattingRevisionDocument(seeded);
        await SeedDocumentAsync(provider, formattingRuntime);
        SetCanvasRuntimeAndAnnotations(formattingRuntime);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TrackChangesEnabled, true));

        await MarkCanvasReadyAsync(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-revision-reject']").Should().NotBeNull());
        cut.Find("[data-testid='document-revision-reject']").Click();
        cut.WaitForAssertion(() => HasCanvasCommand("rejectrevision").Should().BeTrue());
        var rejectedRuntime = ReviewRevision(formattingRuntime, "revision-format", DocumentRevisionAction.Rejected);
        SetCanvasRuntimeAndAnnotations(rejectedRuntime);
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        saved.Revisions.Should().ContainSingle().Subject.Action.Should().Be(DocumentRevisionAction.Rejected);
        GetRevisionTextRuns(saved).Should().BeEmpty();
        var targetParagraph = (ParagraphBlockContent)saved.Blocks.Single(block => block.Id == paragraph.Id).Content;
        targetParagraph.Inlines.OfType<TextRun>().Should().NotContain(run => run.Marks.Any(mark => mark.Type == InlineMarkType.Bold));
    }

    [Fact]
    public async Task KeyboardShortcuts_InvokeSaveThroughCanvasShell()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());

        await cut.Find(".tm-document-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
    }

    [Fact]
    public void ReadOnly_PassesReadOnlyStateToCanvasHost()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ReadOnly, true));

        cut.WaitForAssertion(() =>
            cut.FindComponent<TmDocumentCanvasEngineHost>().Instance.ReadOnly.Should().BeTrue());
    }

    [Fact]
    public async Task Collaboration_RemoteRevisionUpdateRefreshesPanelWithoutReplacingCanvasHost()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);
        var collaborationProvider = new InMemoryDocumentCollaborationProvider();
        var module = SetupDocumentCanvasModule();

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.CollaborationProvider, collaborationProvider)
                      .Add(p => p.CollaborationClientId, "client-a")
                      .Add(p => p.CollaborationSyncInterval, TimeSpan.FromMilliseconds(20)));

        var canvasHost = FindCanvasHost(cut).Instance;
        await MarkCanvasReadyAsync(cut);
        var snapshotCallsBeforeRemote = module.Invocations.Count(invocation => invocation.Identifier == "replaceModel");

        var remoteSession = await collaborationProvider.JoinAsync(new DocumentCollaborationJoinRequest
        {
            DocumentId = "doc-1",
            ClientId = "client-b",
            Author = new DocumentEditorAuthor { Id = "client-b", DisplayName = "Remote reviewer" }
        });
        await collaborationProvider.BroadcastOperationBatchAsync(remoteSession.Id, new DocumentOperationBatch
        {
            DocumentId = "doc-1",
            Operations =
            [
                CreateRemoteRevisionOperation("remote-revision", paragraph.Id, inline.Id!, "Remote ")
            ]
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-revision-item']").TextContent.Should().Contain("Remote"), TimeSpan.FromSeconds(5));
        FindCanvasHost(cut).Instance.Should().BeSameAs(canvasHost);
        module.Invocations.Count(invocation => invocation.Identifier == "replaceModel")
            .Should().BeGreaterThan(snapshotCallsBeforeRemote, "typed collaboration fallback updates the mounted canvas snapshot");
        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier.StartsWith("tmDocumentEditorRuntime.", StringComparison.Ordinal));

        cut.Find("[data-testid='document-save']").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        saved.Revisions.Should().ContainSingle(revision => revision.Id == "remote-revision");
    }

    [Fact]
    public async Task Collaboration_CanvasRelayBatchAppliesThroughCanvasModule()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var collaborationProvider = new SignalRDocumentCollaborationProvider(new InMemoryDocumentCollaborationProvider());
        var module = SetupDocumentCanvasModule();

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.CollaborationProvider, collaborationProvider)
                      .Add(p => p.CollaborationClientId, "client-a")
                      .Add(p => p.CollaborationSyncInterval, TimeSpan.FromMilliseconds(20)));

        await MarkCanvasReadyAsync(cut);
        var snapshotCallsBeforeRemote = module.Invocations.Count(invocation => invocation.Identifier == "replaceModel");

        await collaborationProvider.ReceiveRemoteOperationBatchAsync(new DocumentCollaborationOperationBatch
        {
            Sequence = 2,
            SessionId = "remote-session",
            Batch = new DocumentOperationBatch
            {
                DocumentId = "doc-1",
                CanvasOperationBatchJson = """{"sequence":2,"batch":{"operations":[{"id":"remote-canvas-op","type":"insertText","blockId":"body","offset":0,"text":"Remote "}]} }"""
            }
        });

        cut.WaitForAssertion(() =>
            module.Invocations.Any(invocation =>
                invocation.Identifier == "applyRemoteOperationBatch"
                && invocation.Arguments.Any(argument =>
                    argument != null && argument.ToString()!.Contains("remote-canvas-op", StringComparison.Ordinal)))
            .Should().BeTrue(),
            TimeSpan.FromSeconds(5));
        module.Invocations.Count(invocation => invocation.Identifier == "replaceModel")
            .Should().Be(snapshotCallsBeforeRemote, "opaque canvas relay batches are applied by the engine without typed snapshot refresh");
        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier.StartsWith("tmDocumentEditorRuntime.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collaboration_CanvasRelayApplyFailureDoesNotCallLegacyRuntime()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var collaborationProvider = new SignalRDocumentCollaborationProvider(new InMemoryDocumentCollaborationProvider());
        var module = SetupDocumentCanvasModule();
        SetDocumentCanvasRemoteApplyResult("""{"success":false,"changed":false,"failedOperationIds":["op-failed"]}""");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.CollaborationProvider, collaborationProvider)
                      .Add(p => p.CollaborationClientId, "client-a")
                      .Add(p => p.CollaborationSyncInterval, TimeSpan.FromMilliseconds(20)));

        await MarkCanvasReadyAsync(cut);

        await collaborationProvider.ReceiveRemoteOperationBatchAsync(new DocumentCollaborationOperationBatch
        {
            Sequence = 3,
            SessionId = "remote-session",
            Batch = new DocumentOperationBatch
            {
                DocumentId = "doc-1",
                CanvasOperationBatchJson = """{"sequence":3,"batch":{"operations":[{"id":"op-failed","type":"insertText","blockId":"body","offset":0,"text":"Remote "}]} }"""
            }
        });

        cut.WaitForAssertion(() =>
            module.Invocations.Should().Contain(invocation => invocation.Identifier == "applyRemoteOperationBatch"),
            TimeSpan.FromSeconds(5));
        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier.StartsWith("tmDocumentEditorRuntime.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collaboration_ProviderFailureDuringRefreshDoesNotBlockLocalTyping()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var runtimeDocument = Clone(seeded);
        var (_, inline) = GetFirstParagraphTextRun(runtimeDocument);
        inline.Text = $"Local {inline.Text}";

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.CollaborationProvider, new ThrowingReconnectCollaborationProvider())
                      .Add(p => p.CollaborationClientId, "client-a")
                      .Add(p => p.CollaborationSyncInterval, TimeSpan.FromMilliseconds(20)));

        await MarkCanvasReadyAsync(cut);
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-save-message']").TextContent.Should().Contain("Collaboration is unavailable"), TimeSpan.FromSeconds(5));

        SetCanvasRuntimeAndAnnotations(runtimeDocument);
        await NotifyCanvasChangedAsync(cut);
        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor__save-message").TextContent.Should().Contain("Saved"));
        var saved = (await provider.LoadAsync("doc-1")).Document!;
        GetParagraphText(saved).Should().StartWith("Local ");
    }

    [Fact]
    public void RuntimeRecoveryDetail_CanvasShellDoesNotRenderLegacyRuntimeMessageByDefault()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.FindComponent<TmDocumentCanvasEngineHost>().Should().NotBeNull());
        cut.FindAll("[data-testid='document-runtime-message']").Should().BeEmpty();
    }

    [Fact]
    public void RuntimeRecoveryDetail_DebugTools_DoNotShowLegacyRecoveryDetailForCanvasShell()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ShowDebugTools, true));

        cut.WaitForAssertion(() => cut.FindComponent<TmDocumentCanvasEngineHost>().Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-json']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-json-debug-modal']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-runtime-recovery-debug']").Should().BeEmpty();
    }

    [Fact]
    public void RuntimeRecoveryDetail_CanvasDebugTools_ShowRuntimeDebugSnapshot()
    {
        SetDocumentCanvasRuntimeDebugSnapshotJson("""{"runtime":"canvas","state":"ready"}""");
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ShowDebugTools, true));

        cut.WaitForAssertion(() => cut.FindComponent<TmDocumentCanvasEngineHost>().Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-json']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-runtime-debug-content']").TextContent.Should().Contain("canvas"));
    }

    [Fact]
    public async Task InsertMenu_WithTokenProvider_RoutesToCanvasTokenMenuCommand()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TokenProvider, new TestTokenProvider()));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();
        cut.Find("[data-testid='document-insert-menu']").Click();

        // Phase 9: the Insert button opens the Blazor-side token panel (decision
        // DOC-EDITOR-TOKEN-MENU-BLAZOR-SIDE) — no engine command fires until a token is picked.
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-token-insert-panel']").Should().NotBeNull());
        HasCanvasCommand("openTokenMenu").Should().BeFalse("the dead openTokenMenu engine route is removed");
        cut.FindAll("[data-testid='document-wysiwyg-token-popover']").Should().BeEmpty();
    }

    [Fact]
    public async Task ToolbarTableGrid_RoutesInsertTableCommandToCanvasEngine()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();
        cut.Find("[data-testid='document-toolbar-table']").Click();
        cut.Find("[data-testid='document-table-grid-cell-1-2']").Click();

        cut.WaitForAssertion(() => HasCanvasCommand("insertTable").Should().BeTrue());
        HasCanvasCommandArgument("insertTable", "\"rows\":2").Should().BeTrue();
        HasCanvasCommandArgument("insertTable", "\"columns\":3").Should().BeTrue();
        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier.StartsWith("tmDocumentEditorRuntime.", StringComparison.Ordinal));
    }

    private static (DocumentBlock Paragraph, TextRun Inline) GetFirstParagraphTextRun(DocumentEditorDocument document)
    {
        var paragraph = document.Blocks.First(block => block.Content is ParagraphBlockContent);
        var inline = ((ParagraphBlockContent)paragraph.Content).Inlines.OfType<TextRun>().First();
        return (paragraph, inline);
    }

    private static (DocumentBlock Paragraph, TextRun Inline) GetFirstPlainParagraphTextRun(DocumentEditorDocument document)
    {
        foreach (var paragraph in document.Blocks.Where(block => block.Content is ParagraphBlockContent))
        {
            var inline = ((ParagraphBlockContent)paragraph.Content).Inlines
                .OfType<TextRun>()
                .FirstOrDefault(run => run.Text.Length >= 4 && !run.Marks.Any(mark => mark.Type == InlineMarkType.Bold));
            if (inline is not null)
            {
                return (paragraph, inline);
            }
        }

        throw new InvalidOperationException("The test document does not contain a plain paragraph text run.");
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, DocumentEditorJson.Options);
        return JsonSerializer.Deserialize<T>(json, DocumentEditorJson.Options)!;
    }

    private static DocumentEditorDocument CreateInsertedRevisionDocument(
        DocumentEditorDocument source,
        string text = "Draft ",
        string revisionId = "revision-live-insert")
    {
        var document = Clone(source);
        var (paragraph, _) = GetFirstParagraphTextRun(document);
        var inlines = ((ParagraphBlockContent)paragraph.Content).Inlines;
        inlines.Insert(0, new TextRun
        {
            Id = $"rev-{revisionId}",
            Text = text,
            Marks = [CreateRevisionMark(revisionId, DocumentRevisionType.Insertion)]
        });
        document.Revisions =
        [
            new DocumentRevision
            {
                Id = revisionId,
                Type = DocumentRevisionType.Insertion,
                Action = DocumentRevisionAction.Pending,
                PayloadJson = text,
                Range = new DocumentRevisionRange
                {
                    BlockId = paragraph.Id,
                    StartInlineIndex = 0,
                    StartOffset = 0,
                    EndInlineIndex = 0,
                    EndOffset = text.Length
                }
            }
        ];
        return document;
    }

    private static DocumentEditorDocument CreateInsertedBlockRevisionDocument(DocumentEditorDocument source)
    {
        var document = CreateInsertedRevisionDocument(source, revisionId: "revision-before-enter");
        document.Blocks.Insert(1, new DocumentBlock
        {
            Id = "tracked-enter-block",
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Id = "tracked-enter-inline", Text = string.Empty }]
            }
        });
        return document;
    }

    private static DocumentEditorDocument CreateDeletionRevisionDocument(DocumentEditorDocument source)
    {
        var document = Clone(source);
        var (paragraph, inline) = GetFirstParagraphTextRun(document);
        var original = inline.Text;
        inline.Text = original[..3];
        var deleted = new TextRun
        {
            Id = "rev-revision-delete",
            Text = original[3].ToString(),
            Marks = [CreateRevisionMark("revision-delete", DocumentRevisionType.Deletion)]
        };
        var remainder = new TextRun { Id = "delete-remainder", Text = original[4..] };
        var inlines = ((ParagraphBlockContent)paragraph.Content).Inlines;
        inlines.Insert(1, deleted);
        inlines.Insert(2, remainder);
        document.Revisions =
        [
            new DocumentRevision
            {
                Id = "revision-delete",
                Type = DocumentRevisionType.Deletion,
                Action = DocumentRevisionAction.Pending,
                PayloadJson = deleted.Text,
                Range = new DocumentRevisionRange
                {
                    BlockId = paragraph.Id,
                    StartInlineIndex = 1,
                    StartOffset = 0,
                    EndInlineIndex = 1,
                    EndOffset = 1
                }
            }
        ];
        return document;
    }

    private static DocumentEditorDocument CreateFormattingRevisionDocument(DocumentEditorDocument source, string revisionId = "revision-format")
    {
        var document = Clone(source);
        var (paragraph, inline) = GetFirstPlainParagraphTextRun(document);
        var selected = inline.Text[..4];
        var remainder = inline.Text[4..];
        inline.Text = selected;
        inline.Marks.Add(new InlineMark { Type = InlineMarkType.Bold });
        inline.Marks.Add(CreateRevisionMark(revisionId, DocumentRevisionType.Formatting));
        ((ParagraphBlockContent)paragraph.Content).Inlines.Insert(1, new TextRun { Id = "format-remainder", Text = remainder });
        var payload = new DocumentFormattingRevisionPayload
        {
            MarkType = InlineMarkType.Bold,
            NewActive = true
        };
        document.Revisions =
        [
            new DocumentRevision
            {
                Id = revisionId,
                Type = DocumentRevisionType.Formatting,
                Action = DocumentRevisionAction.Pending,
                PayloadJson = JsonSerializer.Serialize(payload, DocumentEditorJson.Options),
                Range = new DocumentRevisionRange
                {
                    BlockId = paragraph.Id,
                    StartInlineIndex = 0,
                    StartOffset = 0,
                    EndInlineIndex = 0,
                    EndOffset = selected.Length
                }
            }
        ];
        return document;
    }

    private static DocumentEditorDocument ReviewRevision(DocumentEditorDocument source, string revisionId, DocumentRevisionAction action, bool removeMarkedContent = false)
    {
        var document = Clone(source);
        var revision = document.Revisions.Single(item => item.Id == revisionId);
        revision.Action = action;
        foreach (var block in document.Blocks.Where(block => block.Content is ParagraphBlockContent))
        {
            var inlines = ((ParagraphBlockContent)block.Content).Inlines;
            if (removeMarkedContent)
            {
                inlines.RemoveAll(inline => inline.Marks.Any(mark => mark.Type == InlineMarkType.Revision && mark.RevisionId == revisionId));
            }
            else
            {
                foreach (var inline in inlines)
                {
                    inline.Marks.RemoveAll(mark => mark.Type == InlineMarkType.Revision && mark.RevisionId == revisionId);
                    if (revision.Type == DocumentRevisionType.Formatting && action == DocumentRevisionAction.Rejected)
                    {
                        inline.Marks.RemoveAll(mark => mark.Type == InlineMarkType.Bold);
                    }
                }
            }
        }

        return document;
    }

    private static InlineMark CreateRevisionMark(string revisionId, DocumentRevisionType type)
        => new()
        {
            Type = InlineMarkType.Revision,
            RevisionId = revisionId,
            Value = type.ToString()
        };

    private static async Task SeedDocumentAsync(InMemoryDocumentEditorProvider provider, DocumentEditorDocument document)
    {
        provider.SeedEmptyDocument(document.DocumentId);
        await provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = document.DocumentId,
            Document = document,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        });
    }

    private static async Task<DocumentEditorDocument> SeedContractDocumentWithoutSeedRevisionsAsync(
        InMemoryDocumentEditorProvider provider,
        string documentId)
    {
        var document = provider.SeedContractDocument(documentId);
        document.Revisions.Clear();
        RemoveRevisionMarks(document.Blocks);
        foreach (var headerFooter in document.HeadersFooters)
        {
            RemoveRevisionMarks(headerFooter.Blocks);
        }

        await provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = documentId,
            Document = document,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        });

        return document;
    }

    private static void RemoveRevisionMarks(IEnumerable<DocumentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block.Content)
            {
                case ParagraphBlockContent paragraph:
                    RemoveRevisionMarks(paragraph.Inlines);
                    break;
                case HeadingBlockContent heading:
                    RemoveRevisionMarks(heading.Inlines);
                    break;
                case ListBlockContent list:
                    RemoveRevisionMarks(list.Inlines);
                    break;
                case QuoteBlockContent quote:
                    RemoveRevisionMarks(quote.Inlines);
                    break;
                case TableBlockContent table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            RemoveRevisionMarks(cell.Blocks);
                        }
                    }
                    break;
            }
        }
    }

    private static void RemoveRevisionMarks(IEnumerable<InlineContent> inlines)
    {
        foreach (var inline in inlines)
        {
            inline.Marks.RemoveAll(mark => mark.Type == InlineMarkType.Revision);
        }
    }

    private static DocumentEditorDocument CreatePhase17ProviderDocument()
    {
        var document = DocumentEditorDocument.Empty("doc-phase17");
        document.Metadata.Title = "Phase 17 contract";
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 12,
            BodyLineHeight = 1.3,
            ParagraphSpacingAfter = 10
        };
        document.Sections[0].Id = "section-1";
        document.Sections[0].Properties.HeaderFooterReferences =
        [
            new DocumentHeaderFooterReference
            {
                HeaderFooterId = "header-1",
                Type = DocumentHeaderFooterType.Header,
                Scope = DocumentHeaderFooterScope.Primary
            },
            new DocumentHeaderFooterReference
            {
                HeaderFooterId = "footer-1",
                Type = DocumentHeaderFooterType.Footer,
                Scope = DocumentHeaderFooterScope.Primary
            }
        ];
        document.Blocks.Add(new DocumentBlock
        {
            Id = "paragraph-1",
            Type = DocumentBlockType.Paragraph,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Right,
                LineSpacing = 1.5,
                SpacingAfter = 12
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "inline-1",
                        Text = "Provider export text",
                        Marks =
                        [
                            new InlineMark { Type = InlineMarkType.FontFamily, Value = "Georgia" },
                            new InlineMark { Type = InlineMarkType.FontSize, Value = "18pt" },
                            new InlineMark { Type = InlineMarkType.Revision, RevisionId = "revision-1", Value = "Insertion" }
                        ]
                    }
                ]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "image-1",
            Type = DocumentBlockType.Image,
            Content = new ImageBlockContent
            {
                Source = DocumentImageSource.Asset,
                AssetId = "asset-1",
                AltText = "Provider image",
                Caption = "Provider image caption",
                Size = new DocumentImageSize { Width = 300, Height = 150 },
                FloatingLayout = new DocumentFloatingLayout
                {
                    Inline = false,
                    WrapMode = DocumentWrapMode.Square,
                    X = 24,
                    Y = 12,
                    ZIndex = 3
                }
            }
        });
        document.HeadersFooters.Add(new DocumentHeaderFooter
        {
            Id = "header-1",
            Type = DocumentHeaderFooterType.Header,
            Scope = DocumentHeaderFooterScope.Primary,
            Blocks = [CreateTextBlock("header-block-1", "Header phase 17")]
        });
        document.HeadersFooters.Add(new DocumentHeaderFooter
        {
            Id = "footer-1",
            Type = DocumentHeaderFooterType.Footer,
            Scope = DocumentHeaderFooterScope.Primary,
            Blocks = [CreateTextBlock("footer-block-1", "Footer phase 17")]
        });
        document.Revisions.Add(new DocumentRevision
        {
            Id = "revision-1",
            Type = DocumentRevisionType.Insertion,
            Range = new DocumentRevisionRange { BlockId = "paragraph-1", StartInlineIndex = 0, EndInlineIndex = 0, StartOffset = 0, EndOffset = 20 },
            Author = new DocumentRevisionAuthor { Id = "reviewer-1", DisplayName = "Reviewer" },
            CreatedAt = DateTimeOffset.Parse("2026-05-14T12:00:00Z"),
            Action = DocumentRevisionAction.Pending
        });

        return document;
    }

    private static DocumentEditorDocument CreatePhase19ExportDocument()
    {
        var document = DocumentEditorDocument.Empty("doc-phase19");
        document.Metadata.Title = "Phase 19 export";
        document.Blocks.Add(new DocumentBlock
        {
            Id = "paragraph-1",
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "Phase 19 export text" }] }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "image-1",
            Type = DocumentBlockType.Image,
            Content = new ImageBlockContent
            {
                Source = DocumentImageSource.Url,
                Url = "https://cdn.test/image.png",
                AltText = "Phase 19 image",
                Caption = "Phase 19 caption",
                LinkUrl = "https://example.test/image",
                Size = new DocumentImageSize { Width = 320, Height = 180 },
                FloatingLayout = new DocumentFloatingLayout
                {
                    Inline = false,
                    WrapMode = DocumentWrapMode.Square,
                    HorizontalPosition = DocumentImageHorizontalPosition.Right,
                    DistanceLeft = 12
                }
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "table-1",
            Type = DocumentBlockType.Table,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent
                {
                    Width = 420,
                    Alignment = TableHorizontalAlignment.Center,
                    CellPadding = 8
                },
                Rows =
                [
                    new TableRowContent
                    {
                        Cells =
                        [
                            new TableCellContent
                            {
                                Id = "cell-1",
                                Width = 140,
                                BackgroundColor = "#ffef9a",
                                VerticalAlignment = TableCellVerticalAlignment.Middle,
                                Blocks = [CreateTextBlock("cell-p", "Cell text")]
                            }
                        ]
                    }
                ]
            }
        });
        document.Comments.Add(new DocumentComment { Id = "comment-1" });
        document.Revisions.Add(new DocumentRevision { Id = "revision-1", Type = DocumentRevisionType.Insertion });
        return document;
    }

    private static DocumentBlock CreateTextBlock(string id, string text)
    {
        return new DocumentBlock
        {
            Id = id,
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Text = text }]
            }
        };
    }

    private static void AssertPhase17Metadata(DocumentEditorDocument document)
    {
        document.DocumentId.Should().Be("doc-phase17");
        document.Theme.BodyFontFamily.Should().Contain("Aptos");
        document.Blocks.Single(block => block.Id == "paragraph-1")
            .ParagraphProperties.Alignment.Should().Be(DocumentTextAlignment.Right);
        var paragraph = (ParagraphBlockContent)document.Blocks.Single(block => block.Id == "paragraph-1").Content;
        var run = paragraph.Inlines.OfType<TextRun>().Single();
        run.Marks.Should().Contain(mark => mark.Type == InlineMarkType.FontFamily && mark.Value == "Georgia");
        run.Marks.Should().Contain(mark => mark.Type == InlineMarkType.FontSize && mark.Value == "18pt");
        run.Marks.Should().Contain(mark => mark.Type == InlineMarkType.Revision && mark.RevisionId == "revision-1");
        document.HeadersFooters.Should().Contain(headerFooter => headerFooter.Id == "header-1");
        document.HeadersFooters.Should().Contain(headerFooter => headerFooter.Id == "footer-1");
        document.Revisions.Should().ContainSingle(revision => revision.Id == "revision-1");
        var image = GetSingleImageContent(document, "image-1");
        image.Size.Width.Should().Be(300);
        image.Layout.Wrap.Mode.Should().Be(DocumentWrapMode.Square);
    }

    private static DocumentDrawingRun GetSingleDrawingRun(DocumentEditorDocument document, string objectId)
        => DocumentImagePersistence.EnumerateDrawingRuns(document).Single(drawing => drawing.ObjectId == objectId);

    private static ImageBlockContent GetSingleImageContent(DocumentEditorDocument document, string blockOrObjectId)
    {
        var blockImage = document.Blocks
            .Where(block => string.Equals(block.Id, blockOrObjectId, StringComparison.Ordinal))
            .Select(block => block.Content)
            .OfType<ImageBlockContent>()
            .SingleOrDefault();
        if (blockImage is not null)
        {
            return blockImage;
        }

        var drawing = GetSingleDrawingRun(document, blockOrObjectId);
        return new ImageBlockContent
        {
            Source = drawing.Source,
            Url = drawing.Url,
            AssetId = drawing.AssetId,
            AltText = drawing.AltText,
            IsDecorative = drawing.IsDecorative,
            Caption = drawing.Caption,
            Size = drawing.Size,
            NaturalSize = drawing.NaturalSize,
            Layout = drawing.Layout,
            LinkUrl = drawing.LinkUrl
        };
    }

    private static string GetParagraphText(DocumentEditorDocument document)
    {
        var paragraph = document.Blocks.Select(block => block.Content).OfType<ParagraphBlockContent>().First();
        return string.Concat(paragraph.Inlines.Select(inline => inline switch
        {
            TextRun text => text.Text,
            TokenRun token => token.DisplayName,
            _ => string.Empty
        }));
    }

    private static IReadOnlyList<TextRun> GetRevisionTextRuns(DocumentEditorDocument document)
    {
        return GetTextRuns(document.Blocks)
            .Where(run => run.Marks.Any(mark => mark.Type == InlineMarkType.Revision))
            .ToList();
    }

    private static IEnumerable<TextRun> GetTextRuns(IEnumerable<DocumentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var run in GetTextRuns(block.Content))
            {
                yield return run;
            }
        }
    }

    private static IEnumerable<TextRun> GetTextRuns(DocumentBlockContent content)
    {
        switch (content)
        {
            case ParagraphBlockContent paragraph:
                return paragraph.Inlines.OfType<TextRun>();
            case HeadingBlockContent heading:
                return heading.Inlines.OfType<TextRun>();
            case ListBlockContent list:
                return list.Inlines.OfType<TextRun>();
            case QuoteBlockContent quote:
                return quote.Inlines.OfType<TextRun>();
            case TableBlockContent table:
                return table.Rows
                    .SelectMany(row => row.Cells)
                    .SelectMany(cell => GetTextRuns(cell.Blocks));
            default:
                return [];
        }
    }

    private static DocumentOperation CreateRemoteRevisionOperation(string revisionId, string blockId, string inlineId, string text)
        => new()
        {
            Type = DocumentOperationType.CreateRevision,
            Target = new DocumentOperationTarget
            {
                BlockId = blockId,
                InlineId = inlineId,
                InlineIndex = 0,
                Offset = 0,
                Length = text.Length
            },
            Text = text,
            Revision = new DocumentRevision
            {
                Id = revisionId,
                Type = DocumentRevisionType.Insertion,
                Range = new DocumentRevisionRange
                {
                    BlockId = blockId,
                    StartInlineIndex = 0,
                    StartOffset = 0,
                    EndInlineIndex = 0,
                    EndOffset = text.Length
                },
                Author = new DocumentRevisionAuthor { Id = "client-b", DisplayName = "Remote reviewer" },
                PayloadJson = text,
                Action = DocumentRevisionAction.Pending
            },
            Metadata = new DocumentOperationMetadata
            {
                AuthorId = "client-b",
                ClientId = "client-b",
                RevisionId = revisionId,
                RevisionType = nameof(DocumentRevisionType.Insertion),
                LogicalTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        };

    private sealed class TestTokenProvider : ITokenDataProvider
    {
        public bool SupportsCreation => false;

        public void Refresh()
        {
        }

        public Task<IEnumerable<IToken>> SearchTokensAsync(string query, CancellationToken ct = default)
        {
            IEnumerable<IToken> tokens =
            [
                new TestToken
                {
                    Key = "matter.number",
                    DisplayName = "Matter number",
                    Description = "Matter reference number",
                    Category = "Matter",
                    TypeLabel = "Text"
                }
            ];

            return Task.FromResult(tokens);
        }
    }

    private sealed class TestToken : IToken
    {
        public string Key { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string? Category { get; init; }

        public string? Icon { get; init; }

        public string? ColorClass { get; init; }

        public string? TypeLabel { get; init; }
    }

    private sealed class FailingAutosaveProvider : InMemoryDocumentEditorProvider
    {
        public override Task<DocumentEditorSaveResult> SaveAsync(
            DocumentEditorSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.IsAutosave)
            {
                return Task.FromResult(new DocumentEditorSaveResult
                {
                    Success = false,
                    ErrorMessage = "autosave-boom"
                });
            }

            return base.SaveAsync(request, cancellationToken);
        }
    }

    private sealed class CapturingPdfExportProvider : IDocumentPdfExportProvider
    {
        public DocumentPdfExportRequest? LastRequest { get; private set; }

        public Task<DocumentPdfExportResult> ExportPdfAsync(
            DocumentPdfExportRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = Clone(request);
            return Task.FromResult(new DocumentPdfExportResult
            {
                Content = [1, 2, 3],
                ContentType = "application/pdf",
                FileName = "phase17.pdf"
            });
        }
    }

    private sealed class CapturingDocumentFormatProvider : IDocumentFormatProvider
    {
        public DocumentFormatExportProviderRequest? LastExportRequest { get; private set; }

        public DocumentFormatImportProviderRequest? LastImportRequest { get; private set; }

        public DocumentEditorDocument? ImportedDocument { get; set; }

        public Task<IReadOnlyList<DocumentFormatProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DocumentFormatProviderCapability> capabilities =
            [
                new DocumentFormatProviderCapability
                {
                    Format = DocumentFormatProviderKind.Docx,
                    CanImport = true,
                    CanExport = true,
                    FileExtensions = [".docx"]
                }
            ];

            return Task.FromResult(capabilities);
        }

        public Task<DocumentFormatImportProviderResult> ImportAsync(
            DocumentFormatImportProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            LastImportRequest = Clone(request);
            return Task.FromResult(new DocumentFormatImportProviderResult
            {
                Document = ImportedDocument is null
                    ? DocumentEditorDocument.Empty(request.DocumentId)
                    : Clone(ImportedDocument),
                Format = request.Format
            });
        }

        public Task<DocumentFormatExportProviderResult> ExportAsync(
            DocumentFormatExportProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            LastExportRequest = Clone(request);
            return Task.FromResult(new DocumentFormatExportProviderResult
            {
                Content = [4, 5, 6],
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileName = "phase17.docx",
                Format = request.Format
            });
        }
    }

    private sealed class ThrowingReconnectCollaborationProvider : InMemoryDocumentCollaborationProvider
    {
        public override Task<IReadOnlyList<DocumentCollaborationOperationBatch>> GetOperationBatchesAsync(
            string documentId,
            long afterSequence,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated collaboration transport failure.");
        }
    }

    // ── Phase 13.3 – Protect document / Mark editable region toolbar buttons ──

    [Fact]
    public void ReviewTab_ProtectDocumentButton_IsPresent()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.IsDocumentProtected, false));

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-protect-document']").Should().NotBeNull();
    }

    [Fact]
    public void ReviewTab_ProtectDocumentButton_HasActiveCssClassWhenProtected()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.IsDocumentProtected, true));

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-protect-document']")
           .ClassList.Should().Contain("tm-document-editor__ribbon-button--active");
    }

    [Fact]
    public void ReviewTab_ProtectDocumentButton_HasAriaPressedTrue_WhenProtected()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.IsDocumentProtected, true));

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-protect-document']")
           .GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void ReviewTab_MarkEditableRegionButton_IsPresent()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.IsDocumentProtected, true));

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        cut.Find("[data-testid='document-mark-editable-region']").Should().NotBeNull();
    }

    [Fact]
    public void ReviewTab_MarkEditableRegionButton_InvokesCallback()
    {
        var called = false;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.IsDocumentProtected, true)
            .Add(p => p.OnMarkEditableRegion, EventCallback.Factory.Create(this, () => called = true)));

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        cut.Find("[data-testid='document-mark-editable-region']").Click();

        called.Should().BeTrue();
    }

    // ── Phase 14.2 – Escape exits fullscreen ────────────────────────────────

    [Fact]
    public async Task Editor_EscapeExitsFullscreenWhenNoLayersOpen()
    {
        JSInterop.SetupVoid("tmDocumentEditor.setFullscreen", _ => true).SetVoidResult();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel-close']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-close']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-ribbon-tab-view']").Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-fullscreen']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-fullscreen']").GetAttribute("aria-pressed").Should().Be("true"));

        await cut.Find(".tm-document-editor").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-fullscreen']").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void FullscreenToggle_InvokesBrowserGlobalSetFullscreen()
    {
        // Regression: fullscreen was routed to a canvas-engine command ("setFullscreen") that the engine
        // never implemented, so the toggle flipped C# state without any visual effect. The component must
        // apply the body class through the window.tmDocumentEditor browser global instead.
        JSInterop.SetupVoid("tmDocumentEditor.setFullscreen", _ => true).SetVoidResult();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-ribbon-tab-view']").Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-fullscreen']").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Should().Contain(invocation =>
                invocation.Identifier == "tmDocumentEditor.setFullscreen"
                && invocation.Arguments.Count > 0
                && Equals(invocation.Arguments[0], true),
                "entering fullscreen must invoke the browser global with true"));

        cut.Find("[data-testid='document-fullscreen']").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Should().Contain(invocation =>
                invocation.Identifier == "tmDocumentEditor.setFullscreen"
                && invocation.Arguments.Count > 0
                && Equals(invocation.Arguments[0], false),
                "exiting fullscreen must invoke the browser global with false"));
    }

    [Fact]
    public async Task InsertMenuButton_OpensBlazorTokenPanelAndInsertsTokenThroughEngine()
    {
        // Phase 9 decision (DOC-EDITOR-TOKEN-MENU-BLAZOR-SIDE): the token menu is a Blazor-side
        // floating panel fed by TokenProvider; selecting a token routes the insertToken engine
        // command. The old openTokenMenu route (an id the engine never registered) is gone.
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var tokenProvider = new StaticTokenProvider(
            new StaticToken("user.email", "User e-mail", "Recipient address", "User"),
            new StaticToken("company.name", "Company name", null, "Company"));

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.TokenProvider, tokenProvider));
        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();
        await cut.Find("[data-testid='document-insert-menu']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-token-insert-panel']").Should().NotBeNull(
            "the Insert button must open the Blazor token panel"));
        cut.WaitForAssertion(() =>
        {
            var panelMarkup = cut.Find("[data-testid='document-token-insert-panel']").InnerHtml;
            panelMarkup.Should().Contain("{{user.email}}", "the provider tokens must be listed");
            panelMarkup.Should().Contain("{{company.name}}");
        });

        var menu = cut.FindComponent<TmDocumentTokenMenu>();
        await cut.InvokeAsync(() => menu.Instance.OnTokenSelected.InvokeAsync(new Tempo.Blazor.Components.Activity.TokenItem
        {
            Key = "user.email",
            DisplayName = "User e-mail",
            Description = "Recipient address",
            TypeLabel = "User"
        }));

        var insertInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 2
                && string.Equals(invocation.Arguments[1]?.ToString(), "insertToken", StringComparison.Ordinal));
        insertInvocation.Should().NotBeNull("selecting a token must route the insertToken engine command");
        var payload = insertInvocation!.Arguments[2]?.ToString() ?? string.Empty;
        payload.Should().Contain("\"key\":\"user.email\"");
        payload.Should().Contain("\"displayName\":\"User e-mail\"");

        // The old dead command must not be routed anywhere anymore.
        SetupDocumentCanvasModule().Invocations
            .Any(invocation => invocation.Identifier == "execCommand"
                && string.Equals(invocation.Arguments.ElementAtOrDefault(1)?.ToString(), "openTokenMenu", StringComparison.Ordinal))
            .Should().BeFalse("openTokenMenu is removed in favour of the Blazor-side panel");

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-token-insert-panel']").Should().BeEmpty(
            "the panel closes after inserting a token"));
    }

    private sealed record StaticToken(string Key, string DisplayName, string? Description, string? TypeLabel) : Tempo.Blazor.Interfaces.IToken
    {
        public string? Category => TypeLabel;
        public string? Icon => null;
        public string? ColorClass => null;
    }

    private sealed class StaticTokenProvider(params Tempo.Blazor.Interfaces.IToken[] tokens) : Tempo.Blazor.Interfaces.ITokenDataProvider
    {
        public bool SupportsCreation => false;

        public void Refresh()
        {
        }

        public Task<IEnumerable<Tempo.Blazor.Interfaces.IToken>> SearchTokensAsync(string query, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<Tempo.Blazor.Interfaces.IToken>>(
                tokens.Where(token => string.IsNullOrEmpty(query)
                    || token.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || token.Key.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    [Fact]
    public async Task DocumentProtection_TogglesAndMarkersRouteSetProtectionModePayloads()
    {
        // Phase 8 contract: the ribbon protect/mark-editable actions route setProtectionMode with
        // {isProtected, markers[]} — the engine command that stores the state in the model and
        // engages the (existing) text-edit veto and overlay.
        var provider = new InMemoryDocumentEditorProvider();
        var seeded = provider.SeedContractDocument("doc-1");
        var (paragraph, inline) = GetFirstParagraphTextRun(seeded);

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));
        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        await cut.Find("[data-testid='document-protect-document']").ClickAsync(new MouseEventArgs());

        var protectInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 2
                && string.Equals(invocation.Arguments[1]?.ToString(), "setProtectionMode", StringComparison.Ordinal));
        protectInvocation.Should().NotBeNull("protect must route setProtectionMode");
        protectInvocation!.Arguments[2]?.ToString().Should().Contain("\"isProtected\":true");

        // Give the editor a body range selection, then mark it editable.
        await NotifyCanvasMiniToolbarAsync(cut, new WysiwygMiniToolbarRequest
        {
            IsVisible = true,
            Left = 100,
            Top = 80,
            Width = 180,
            Height = 40,
            Selection = new WysiwygSelectionSnapshot
            {
                Region = "Body",
                AnchorBlockId = paragraph.Id,
                AnchorInlineId = inline.Id,
                AnchorOffset = 2,
                FocusBlockId = paragraph.Id,
                FocusInlineId = inline.Id,
                FocusOffset = 9,
                IsCollapsed = false
            }
        }, new TmDocumentCanvasEngineHost.CanvasEngineUiState
        {
            Formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState()
        });

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-protect-document']").GetAttribute("aria-pressed").Should().Be("true",
                "the protect toggle must reflect the protected state"));
        // Regression: protection administration must stay operable while protected — both buttons
        // are affectsData commands and used to be auto-disabled by the registry protection gate,
        // locking the user out of marking regions and even unprotecting.
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-mark-editable-region']").HasAttribute("disabled").Should().BeFalse(
                "mark-editable-region must enable once the document is protected"));
        cut.Find("[data-testid='document-protect-document']").HasAttribute("disabled").Should().BeFalse(
            "the protect toggle must stay enabled so protection can be turned off again");
        await cut.Find("[data-testid='document-mark-editable-region']").ClickAsync(new MouseEventArgs());

        var markerInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 2
                && string.Equals(invocation.Arguments[1]?.ToString(), "setProtectionMode", StringComparison.Ordinal)
                && (invocation.Arguments[2]?.ToString() ?? string.Empty).Contains("startBlockId"));
        markerInvocation.Should().NotBeNull("mark-editable-region must route setProtectionMode with markers");
        var payload = markerInvocation!.Arguments[2]?.ToString() ?? string.Empty;
        payload.Should().Contain("\"isProtected\":true");
        payload.Should().Contain($"\"startBlockId\":\"{paragraph.Id}\"");
        payload.Should().Contain("\"startOffset\":2");
        payload.Should().Contain("\"endOffset\":9");
    }

    [Fact]
    public async Task CanvasContextMenu_OnPageBreakBlock_OffersAndRoutesDeletePageBreak()
    {
        // Phase 6: the canvas context-menu path never set BlockType = "PageBreak" (only "Image"),
        // so the Delete-page-break menu item could not appear in canvas mode at all. The JS payload
        // now carries pageBreakBlockId; the menu must show the item and route deletePageBreak.
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));
        await MarkCanvasReadyAsync(cut);

        var host = FindCanvasHost(cut).Instance;
        await cut.InvokeAsync(() => host.OnCanvasContextMenuRequested(
            """{"x":300,"y":300,"blockId":"page-break-1","pageBreakBlockId":"page-break-1","viewportWidth":1440,"viewportHeight":1000}"""));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-page-break-delete']").Should().NotBeNull(
            "a canvas right-click on a page-break block must offer Delete page break"));

        await cut.Find("[data-testid='document-page-break-delete']").ClickAsync(new MouseEventArgs());

        var deleteInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 2
                && string.Equals(invocation.Arguments[1]?.ToString(), "deletePageBreak", StringComparison.Ordinal));
        deleteInvocation.Should().NotBeNull("the menu item must route the deletePageBreak engine command");
        deleteInvocation!.Arguments[2]?.ToString().Should().Contain("page-break-1");
    }

    [Fact]
    public async Task CanvasTableContextMenu_NearViewportBottom_ClampsIntoViewport()
    {
        // Phase 5 finding: a right-click near the bottom of the viewport rendered the table context
        // menu at the raw pointer position — its lower items (Header row, Delete table) landed
        // off-screen and were unreachable. The canvas context-menu payload must carry the viewport
        // size so FloatingStyle clamps the menu and caps it with max-block-size (the CSS scrolls).
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));
        await MarkCanvasReadyAsync(cut);

        var host = FindCanvasHost(cut).Instance;
        await cut.InvokeAsync(() => host.OnCanvasContextMenuRequested(
            """{"x":900,"y":950,"inTable":true,"tableId":"table-1","cellId":"cell-1","viewportWidth":1440,"viewportHeight":1000}"""));

        cut.WaitForAssertion(() =>
        {
            var style = cut.Find("[data-testid='document-table-context-menu']").GetAttribute("style") ?? string.Empty;
            // clamp(950, 8, 1000 - 360 - 8) → 632, plus the scroll cap.
            style.Should().Contain("top: 632px", "the menu must be clamped into the viewport");
            style.Should().Contain("max-block-size");
        });
    }

    [Fact]
    public async Task InsertTableGridPicker_RoutesInsertTableCommandWithDimensions()
    {
        // Phase 4 contract: the toolbar "Insert table" grid must route execCommand('insertTable',
        // { rows, columns, appendToBodyEnd: true }) — the engine registers this command since the
        // command-layer plan; a payload/name drift would make the grid a silent no-op again.
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));
        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-toolbar-table']").Should().NotBeNull());
        await cut.Find("[data-testid='document-toolbar-table']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-table-grid-cell-1-2']").Should().NotBeNull());

        // Grid cell (row index 1, column index 2) → a 2×3 table.
        await cut.Find("[data-testid='document-table-grid-cell-1-2']").ClickAsync(new MouseEventArgs());

        var insertInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 2
                && string.Equals(invocation.Arguments[1]?.ToString(), "insertTable", StringComparison.Ordinal));
        insertInvocation.Should().NotBeNull("the grid picker must route the insertTable engine command");
        var payload = insertInvocation!.Arguments[2]?.ToString() ?? string.Empty;
        payload.Should().Contain("\"rows\":2");
        payload.Should().Contain("\"columns\":3");
        payload.Should().Contain("\"appendToBodyEnd\":true");
    }

    [Fact]
    public async Task HeaderFooterToggles_RouteEngineRegisteredCommandIdsWithTargetState()
    {
        // Regression: the ribbon toggles routed "toggleDifferentFirstPage"/"toggleDifferentOddEven",
        // command ids the engine never registered (it registers differentFirstPage/differentOddEven),
        // so both buttons were silent no-ops. The route must use the registered id AND send the target
        // state ({enabled}) so the C# checkbox and the engine section flag cannot diverge.
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));
        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-layout']").Click();
        await cut.Find("[data-testid='document-different-first-page']").ClickAsync(new MouseEventArgs());

        var firstPageInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 2
                && string.Equals(invocation.Arguments[1]?.ToString(), "differentFirstPage", StringComparison.Ordinal));
        firstPageInvocation.Should().NotBeNull("the toggle must route the engine-registered differentFirstPage id");
        firstPageInvocation!.Arguments[2]?.ToString().Should().Contain("\"enabled\":true");

        await cut.Find("[data-testid='document-different-odd-even']").ClickAsync(new MouseEventArgs());

        var oddEvenInvocation = SetupDocumentCanvasModule().Invocations
            .LastOrDefault(invocation => invocation.Identifier == "execCommand"
                && invocation.Arguments.Count > 2
                && string.Equals(invocation.Arguments[1]?.ToString(), "differentOddEven", StringComparison.Ordinal));
        oddEvenInvocation.Should().NotBeNull("the toggle must route the engine-registered differentOddEven id");
        oddEvenInvocation!.Arguments[2]?.ToString().Should().Contain("\"enabled\":true");
    }

    [Fact]
    public async Task CanvasCommand_UnhandledByEngine_ReturnsFalseAndLogsWarningOncePerCommand()
    {
        // Systemic regression guard: RouteToCanvasEngineAsync used to return true whenever the canvas
        // engine was mounted without reading result.Handled, so an unknown command id looked like a
        // success, optimistic local state was applied, and nothing warned about the dead command.
        var logSink = new CapturingLoggerProvider();
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(
            new Microsoft.Extensions.Logging.LoggerFactory([logSink]));

        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-1")
                      .Add(p => p.Provider, provider));
        await MarkCanvasReadyAsync(cut);

        SetDocumentCanvasExecCommandResult("""{"handled":false}""");

        // Unhandled command: the optimistic paragraph-alignment state must NOT be applied.
        await cut.Find("[data-testid='document-align-right']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='document-align-right']").GetAttribute("aria-pressed").Should().Be("false",
            "an engine-unhandled command must not flip the optimistic toolbar state");

        // The warning is logged once per command id per session, not per invocation.
        await cut.Find("[data-testid='document-align-right']").ClickAsync(new MouseEventArgs());
        logSink.Warnings.Count(message => message.Contains("align", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "repeated unhandled invocations of one command must warn only once");

        // A different unhandled command id gets its own single warning.
        await cut.Find("[data-testid='document-bold']").ClickAsync(new MouseEventArgs());
        logSink.Warnings.Count(message => message.Contains("bold", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);

        // Control: a handled command applies the optimistic state and warns nothing.
        SetDocumentCanvasExecCommandResult("""{"handled":true}""");
        await cut.Find("[data-testid='document-align-right']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-align-right']").GetAttribute("aria-pressed").Should().Be("true"));
        logSink.Warnings.Count(message => message.Contains("align", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "handled commands must not add warnings");
    }

    private sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get { lock (_warnings) { return _warnings.ToArray(); } }
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                {
                    lock (owner._warnings)
                    {
                        owner._warnings.Add(formatter(state, exception));
                    }
                }
            }
        }
    }

    [Fact]
    public void ViewTab_FullscreenButton_IsPresent()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.Find("[data-testid='document-fullscreen']").Should().NotBeNull();
    }

    [Fact]
    public void ViewTab_FullscreenButton_HasAriaPressedTrue_WhenFullscreen()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.IsFullscreen, true));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.Find("[data-testid='document-fullscreen']")
           .GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void ViewTab_FullscreenButton_InvokesCallback()
    {
        var called = false;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.OnFullscreenToggle, EventCallback.Factory.Create(this, () => called = true)));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-fullscreen']").Click();

        called.Should().BeTrue();
    }

    // ── Phase 15 – Debug tools / viewDocumentJson ────────────────────────

    [Fact]
    public void ViewTab_ViewDocumentJsonButton_NotPresentByDefault()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.FindAll("[data-testid='document-view-json']").Should().BeEmpty();
    }

    [Fact]
    public void ViewTab_ViewDocumentJsonButton_PresentWhenShowDebugToolsEnabled()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowDebugTools, true));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.Find("[data-testid='document-view-json']").Should().NotBeNull();
    }

    [Fact]
    public void ViewTab_ViewDocumentJsonButton_InvokesCallback()
    {
        var called = false;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowDebugTools, true)
            .Add(p => p.OnViewDocumentJson, EventCallback.Factory.Create(this, () => called = true)));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-json']").Click();

        called.Should().BeTrue();
    }

    // ── Phase 15 – View clipboard HTML ──────────────────────────────────────

    [Fact]
    public void ViewTab_ViewClipboardHtmlButton_NotPresentByDefault()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.FindAll("[data-testid='document-view-clipboard-html']").Should().BeEmpty();
    }

    [Fact]
    public void ViewTab_ViewClipboardHtmlButton_PresentWhenShowDebugToolsEnabled()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowDebugTools, true));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        cut.Find("[data-testid='document-view-clipboard-html']").Should().NotBeNull();
    }

    [Fact]
    public void ViewTab_ViewClipboardHtmlButton_InvokesCallback()
    {
        var called = false;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowDebugTools, true)
            .Add(p => p.OnViewClipboardHtml, EventCallback.Factory.Create(this, () => called = true)));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-clipboard-html']").Click();

        called.Should().BeTrue();
    }

    // ── Phase 18 – Developer-only source/debug workflow ───────────────────

    [Fact]
    public void DebugJsonInspector_IsNotRenderedWithoutShowDebugTools()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-1")
            .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());

        cut.FindAll("[data-testid='document-json-debug-modal']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-view-json']").Should().BeEmpty();
    }

    [Fact]
    public void DebugJsonInspector_ShowsCanonicalDocumentAndRuntimeDebugState()
    {
        SetDocumentCanvasRuntimeDebugSnapshotJson("""{"HasRuntimeDocument":true,"RuntimeAuthority":"JsCanonicalBoundary"}""");
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-1")
            .Add(p => p.Provider, provider)
            .Add(p => p.ShowDebugTools, true));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-json']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-json-debug-modal']").Should().NotBeNull());
        cut.Find("[data-testid='document-json-debug-content']").TextContent.Should().Contain("doc-1");
        cut.Find("[data-testid='document-runtime-debug-content']").TextContent.Should().Contain("JsCanonicalBoundary");
    }

    [Fact]
    public void DebugJsonInspector_CopyButtonWritesCombinedPayloadToClipboard()
    {
        JSInterop.Setup<string>("tmDocumentEditorDebug.getRuntimeStateJson", _ => true)
            .SetResult("""{"HasRuntimeDocument":true}""");
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");

        var cut = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-1")
            .Add(p => p.Provider, provider)
            .Add(p => p.ShowDebugTools, true));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-json']").Click();
        cut.Find("[data-testid='document-json-debug-copy']").Click();

        JSInterop.Invocations.Any(invocation =>
        {
            var argument = invocation.Arguments.Count > 0 ? invocation.Arguments[0]?.ToString() : null;
            return invocation.Identifier == "navigator.clipboard.writeText"
                && argument is not null
                && argument.Contains("canonicalDocument", StringComparison.Ordinal);
        }).Should().BeTrue();
    }

    [Fact]
    public async Task DebugClipboardModal_ShowsLastRawNormalizedAndWarnings()
    {
        SetDocumentCanvasClipboardDebugSnapshotJson(
            """
            {
              "rawHtml": "<p>Phase 18</p><script>alert(1)</script>",
              "plainText": "Phase 18",
              "normalizedJson": "[{\"type\":\"paragraph\",\"text\":\"Phase 18\"}]",
              "warnings": ["stripped-element"]
            }
            """);
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var cut = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-1")
            .Add(p => p.Provider, provider)
            .Add(p => p.ShowDebugTools, true));

        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-clipboard-html']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-clipboard-html-debug-modal']").Should().NotBeNull());
        cut.Find("[data-testid='document-clipboard-html-debug-content']").TextContent.Should().Contain("Phase 18");
        cut.Find("[data-testid='document-clipboard-normalized-debug-content']").TextContent.Should().Contain("Phase 18");
        cut.Find("[data-testid='document-clipboard-warnings-debug-content']").TextContent.Should().Contain("stripped-element");
    }

    [Fact]
    public void PublicHtmlSourceEditing_IsNotExposedInDebugModal()
    {
        SetDocumentCanvasRuntimeDebugSnapshotJson("{}");
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var cut = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-1")
            .Add(p => p.Provider, provider)
            .Add(p => p.ShowDebugTools, true));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-view-json']").Click();

        cut.FindAll("[data-testid='document-html-source-editor']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-json-debug-import']").Should().BeEmpty();
        cut.FindAll("textarea").Should().OnlyContain(element =>
            element.GetAttribute("data-testid") == "document-canvas-hidden-input");
    }

    private static IRenderedComponent<TmDocumentCanvasEngineHost> FindCanvasHost(IRenderedComponent<TmDocumentEditor> cut)
    {
        cut.WaitForAssertion(() =>
            cut.FindComponent<TmDocumentCanvasEngineHost>().Should().NotBeNull());

        return cut.FindComponent<TmDocumentCanvasEngineHost>();
    }

    private static Task MarkCanvasReadyAsync(IRenderedComponent<TmDocumentEditor> cut, string? stateJson = "{}")
    {
        var host = FindCanvasHost(cut);
        return cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady(stateJson));
    }

    private static Task NotifyCanvasChangedAsync(IRenderedComponent<TmDocumentEditor> cut, string? stateJson = """{"isDirty":true,"modelVersion":1}""")
    {
        var host = FindCanvasHost(cut);
        return cut.InvokeAsync(() => host.Instance.OnCanvasEngineChanged(stateJson));
    }

    private void SetCanvasRuntimeDocument(DocumentEditorDocument document)
        => SetDocumentCanvasRuntimeDocument(document);

    private void SetCanvasRuntimeAndAnnotations(DocumentEditorDocument document)
    {
        SetCanvasRuntimeDocument(document);
        SetDocumentCanvasAnnotationsJson(JsonSerializer.Serialize(new
        {
            comments = document.Comments,
            revisions = document.Revisions,
            wordCount = GetParagraphText(document).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            pageCount = 1
        }, DocumentEditorJson.Options));
    }

    /// <summary>
    /// Raises the canvas mini-toolbar callback, optionally carrying the pushed UI snapshot (B2).
    /// </summary>
    /// <remarks>
    /// WHY THIS ALSO WRITES THE PULLED FORMATTING SNAPSHOT, AND WHY LEAVING IT OUT WAS A RACE.
    /// <para>
    /// <c>TmDocumentCanvasEngineHost.OnCanvasMiniToolbarChanged</c> dispatches TWO handlers for one call:
    /// <c>OnUiStateChanged</c> applies the pushed snapshot straight into the editor's formatting state, and
    /// <c>OnMiniToolbarChanged</c> arms <c>TmDocumentEditor.ScheduleCanvasToolbarSync</c> — a detached
    /// 200&#160;ms debounce that afterwards PULLS <c>getFormattingStateJson</c> and applies it over the very
    /// same state through the same <c>ApplyCanvasFormattingState</c>. Both writers are legitimate; in the
    /// product they agree, because the pull reads the engine that pushed. In these tests the module mock
    /// answered that pull with its default stub, so the two writers DISAGREED, and which one the assertions
    /// read depended on whether the debounce elapsed before them. That is the whole mechanism behind this
    /// file's flaky toolbar test, and it explains why the failing assertion moved: the pull resets the whole
    /// snapshot at once, so whichever assertion happens to run after the re-render is the one that reports.
    /// </para>
    /// <para>
    /// MEASURED, not reasoned: with a deliberate 250&#160;ms pause after this call the test failed on its
    /// FIRST assertion every time (<c>aria-pressed</c> "false"); with 150&#160;ms — under the debounce — it
    /// passed. Keeping the pulled snapshot equal to the pushed one removes the losing side of that race and
    /// changes nothing about what any test asserts: the later pull now applies the same values the push did.
    /// A test whose subject IS the pulled snapshot still sets it itself, and must do so AFTER this call.
    /// </para>
    /// </remarks>
    private Task NotifyCanvasMiniToolbarAsync(
        IRenderedComponent<TmDocumentEditor> cut,
        WysiwygMiniToolbarRequest request,
        TmDocumentCanvasEngineHost.CanvasEngineUiState? uiState = null)
    {
        if (uiState?.Formatting is not null)
        {
            SetDocumentCanvasFormattingStateJson(
                JsonSerializer.Serialize(uiState.Formatting, DocumentEditorJson.Options));
        }

        var host = FindCanvasHost(cut);
        var payload = new
        {
            request.IsVisible,
            request.Left,
            request.Top,
            request.Width,
            request.Height,
            request.Reason,
            request.Placement,
            request.Selection,
            UiState = uiState
        };
        var json = JsonSerializer.Serialize(payload, DocumentEditorJson.Options);
        return cut.InvokeAsync(() => host.Instance.OnCanvasMiniToolbarChanged(json));
    }

    private static Task NotifyCanvasContextMenuAsync(
        IRenderedComponent<TmDocumentEditor> cut,
        TmDocumentCanvasEngineHost.CanvasEngineContextMenuRequest request)
    {
        var host = FindCanvasHost(cut);
        var json = JsonSerializer.Serialize(request, DocumentEditorJson.Options);
        return cut.InvokeAsync(() => host.Instance.OnCanvasContextMenuRequested(json));
    }

    private bool HasCanvasCommand(string commandId) =>
        SetupDocumentCanvasModule().Invocations.Any(invocation =>
            invocation.Identifier == "execCommand"
            && invocation.Arguments.Count >= 2
            && string.Equals(invocation.Arguments[1]?.ToString(), commandId, StringComparison.Ordinal));

    private bool HasCanvasCommandArgument(string commandId, string expected) =>
        SetupDocumentCanvasModule().Invocations.Any(invocation =>
            invocation.Identifier == "execCommand"
            && invocation.Arguments.Count >= 3
            && string.Equals(invocation.Arguments[1]?.ToString(), commandId, StringComparison.Ordinal)
            && invocation.Arguments[2]?.ToString()?.Contains(expected, StringComparison.Ordinal) == true);

    private int CountCanvasCommand(string commandId) =>
        SetupDocumentCanvasModule().Invocations.Count(invocation =>
            invocation.Identifier == "execCommand"
            && invocation.Arguments.Count >= 2
            && string.Equals(invocation.Arguments[1]?.ToString(), commandId, StringComparison.Ordinal));
}
