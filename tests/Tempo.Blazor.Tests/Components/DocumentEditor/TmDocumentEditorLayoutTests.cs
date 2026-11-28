using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.DocumentEditor.Interfaces;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign plan 146c7e1b, phase 3 (DE-1.1): the editor decides its layout from its own container. bUnit cannot
/// evaluate a container query, so every mode is forced through <see cref="TmDocumentEditor.LayoutMode"/>; the CSS
/// side is covered by <see cref="TmDocumentEditorCssTests"/> and the 1440/1024/390/320 Playwright specs.
/// </summary>
public class TmDocumentEditorLayoutTests : LocalizationTestBase
{
    private IRenderedComponent<TmDocumentEditor> RenderEditor(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmDocumentEditor>>? configure = null,
        Action<DocumentEditorDocument>? seed = null)
    {
        var provider = new InMemoryDocumentEditorProvider();
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
            configure?.Invoke(parameters);
        });

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        return cut;
    }

    // ── regions ────────────────────────────────────────────────────────────

    [Fact]
    public void Desktop_RendersNavDocInspectorRegions()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        var regions = cut.FindAll("[data-region]").Select(region => region.GetAttribute("data-region")).ToList();
        regions.Should().Equal("nav", "doc", "inspector");

        cut.Find("[data-region='nav']").QuerySelector("[data-testid='document-navigation-panel']").Should().NotBeNull();
        cut.Find("[data-region='doc']").QuerySelector("[data-testid='document-canvas-engine-host']").Should().NotBeNull();
        cut.Find("[data-region='inspector']").QuerySelector("[data-testid='document-side-panel']").Should().NotBeNull();
        cut.Find("[data-testid='document-editor-workspace']").QuerySelectorAll("[data-region]").Should().HaveCount(3);
    }

    [Fact]
    public void Desktop_OutlineAndPages_InNavColumn_NotInSidePanelTabs()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        var nav = cut.Find("[data-region='nav']");
        nav.QuerySelector("[data-testid='document-outline-panel']").Should().NotBeNull();
        nav.QuerySelector("[data-testid='document-page-navigator']").Should().NotBeNull();

        cut.FindAll("[data-testid='document-side-panel-tab-outline']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-side-panel-tab-pages']").Should().BeEmpty();
        cut.Find("[data-region='inspector']").QuerySelector("[data-testid='document-outline-panel']").Should().BeNull();
    }

    [Fact]
    public void Desktop_InspectorTabs_AreTextTabs_InTheReferenceOrder()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        var tabs = cut.FindAll("[data-testid^='document-side-panel-tab-']")
            .Select(tab => tab.GetAttribute("data-testid")!["document-side-panel-tab-".Length..])
            .ToList();
        tabs.Should().Equal("properties", "comments", "revisions", "versions");
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-tab-style").Should().Be("text");
        cut.FindAll(".tm-document-side-panel__tab svg").Should().BeEmpty("the desktop tabs are text-only like the reference");
    }

    [Fact]
    public void Tablet_OutlinePagesAsTabs()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);
        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();

        cut.FindAll("[data-region='nav']").Should().BeEmpty("DE1: the left column exists on a desktop container only");
        cut.FindAll("[data-testid='document-navigation-panel']").Should().BeEmpty();
        cut.Find("[data-testid='document-side-panel-tab-outline']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel-tab-pages']").Should().NotBeNull();
        cut.FindAll("[data-region]").Select(region => region.GetAttribute("data-region"))
            .Should().Equal("doc", "inspector");
    }

    [Fact]
    public void Mobile_OutlinePagesAsTabs_OfTheInspectorSheet_NoRegionsBesideTheDocument()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);
        cut.FindAll("[data-region]").Select(region => region.GetAttribute("data-region")).Should().Equal("doc");

        cut.Find("[data-testid='document-mobile-comments']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());

        cut.Find("[data-testid='document-side-panel-tab-outline']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel-tab-pages']").Should().NotBeNull();
    }

    [Theory]
    [InlineData(DocumentSidePanelTab.Outline)]
    [InlineData(DocumentSidePanelTab.Pages)]
    public async Task Desktop_CompatibilityTabs_ExpandTheLeftColumn_AndLeaveTheInspectorAlone(DocumentSidePanelTab compatibilityTab)
    {
        // DocumentSidePanelTab.Outline / Pages stay for compatibility: opening one on a desktop container leads to the left
        // column (it unfolds) and the inspector keeps the tab it showed.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        var inspectorTab = cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab");

        cut.Find("[data-testid='document-navigation-collapse']").Click();
        cut.FindAll("[data-testid='document-outline-panel']").Should().BeEmpty("a collapsed column keeps only its rail");
        cut.Find("[data-testid='document-navigation-expand']").Should().NotBeNull();

        var openSidePanel = typeof(TmDocumentEditor).GetMethod("OpenSidePanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await cut.InvokeAsync(() => openSidePanel.Invoke(cut.Instance, [compatibilityTab, true, true]));
        cut.Render();

        cut.Find("[data-region='nav'] [data-testid='document-outline-panel']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab").Should().Be(inspectorTab);
        cut.FindAll("[data-testid='document-side-panel-tab-outline']").Should().BeEmpty();
    }

    [Fact]
    public void Desktop_NavigationColumn_FoldsAndUnfolds()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Find("[data-region='nav'] [data-testid='document-navigation-collapse']").Click();
        cut.Find("[data-testid='document-editor-workspace']").ClassList.Should().Contain("tm-document-editor__workspace--nav-collapsed");
        cut.Find("[data-region='nav']").GetAttribute("data-collapsed").Should().Be("true");

        cut.Find("[data-testid='document-navigation-expand']").Click();
        cut.Find("[data-testid='document-editor-workspace']").ClassList.Should().NotContain("tm-document-editor__workspace--nav-collapsed");
        cut.Find("[data-region='nav'] [data-testid='document-outline-panel']").Should().NotBeNull();
    }

    [Fact]
    public async Task Auto_NarrowDesktopContainer_FoldsTheNavigation_UntilTheReaderUnfolds()
    {
        SetupLayoutObserverModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p => p
            .Add(c => c.DocumentId, "doc-1")
            .Add(c => c.Provider, provider));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        var observer = cut.FindComponent<TmLayoutObserver>();

        cut.Find("[data-region='nav']").GetAttribute("data-collapsed").Should().Be("false", "before a measurement the column is open");

        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1100));
        cut.Find("[data-region='nav']").GetAttribute("data-collapsed").Should().Be("true", "a desktop container too narrow for column + page + inspector folds the column into its rail");
        cut.Find("[data-testid='document-editor-workspace']").ClassList.Should().Contain("tm-document-editor__workspace--nav-collapsed");

        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1700));
        cut.Find("[data-region='nav']").GetAttribute("data-collapsed").Should().Be("false", "widening the container opens it again");

        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1100));
        cut.Find("[data-testid='document-navigation-expand']").Click();
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1050));
        cut.Find("[data-region='nav']").GetAttribute("data-collapsed").Should().Be("false", "the reader's choice wins over the width");
    }

    [Theory]
    [InlineData(1400, true)]
    [InlineData(1545, true)]
    [InlineData(1546, false)]
    [InlineData(1900, false)]
    public async Task Auto_FoldThreshold_IsDerivedFromThePageWidth(double width, bool folded)
    {
        // navigation (272) + inspector (384) + an A4 page (794) + the gutters (96) = 1546 px.
        SetupLayoutObserverModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p => p.Add(c => c.DocumentId, "doc-1").Add(c => c.Provider, provider));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());

        await cut.InvokeAsync(() => cut.FindComponent<TmLayoutObserver>().Instance.OnContainerWidthChanged(width));

        cut.Find("[data-region='nav']").GetAttribute("data-collapsed").Should().Be(folded ? "true" : "false");
    }

    [Fact]
    public async Task Auto_WidthReportInsideTheSameStep_DoesNotReRenderTheEditor()
    {
        SetupLayoutObserverModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p => p
            .Add(c => c.DocumentId, "doc-1")
            .Add(c => c.Provider, provider));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1100));
        var renders = () => int.Parse(cut.Find(".tm-document-editor").GetAttribute("data-blazor-render-count")!);
        var before = renders();

        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1090));
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1080));

        renders().Should().Be(before, "a resize inside one width step must not rebuild the editor chrome");
    }

    // ── fit to the column (phase 8) ─────────────────────────────────────────

    private async Task<(IRenderedComponent<TmDocumentEditor> Cut, Bunit.JSRuntimeInvocation[] Fits)> RenderAutoAsync(double width, string mode)
    {
        SetupLayoutObserverModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var canvas = SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p => p.Add(c => c.DocumentId, "doc-1").Add(c => c.Provider, provider));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        await MarkCanvasReadyAsync(cut);
        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(width));
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged(mode));
        cut.WaitForAssertion(() => cut.Find(".tm-document-editor").GetAttribute("data-layout").Should().Be(mode));
        await Task.Delay(50);
        return (cut, canvas.Invocations.Where(i => i.Identifier == "execCommand").ToArray());
    }

    [Theory]
    [InlineData(700, "tablet")]
    [InlineData(390, "mobile")]
    public async Task Fit_ANonDesktopContainerNarrowerThanThePage_FitsItAfterTheLayoutRendered(double width, string mode)
    {
        var (cut, _) = await RenderAutoAsync(width, mode);

        cut.WaitForAssertion(() => cut.Instance.GetType().GetField("_fitPending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cut.Instance).Should().Be(false));
        var canvas = SetupDocumentCanvasModule();
        canvas.Invocations.Where(i => i.Identifier == "execCommand" && i.Arguments.Any(a => string.Equals(a as string, "fitWidth", StringComparison.Ordinal)))
            .Should().NotBeEmpty("the page is wider than the column, so the engine fits it");
    }

    [Fact]
    public async Task Fit_AColumnThatHoldsThePage_IsLeftAlone()
    {
        var (_, _) = await RenderAutoAsync(1000, "tablet");

        SetupDocumentCanvasModule().Invocations.Where(i => i.Identifier == "execCommand" && i.Arguments.Any(a => string.Equals(a as string, "fitWidth", StringComparison.Ordinal)))
            .Should().BeEmpty("a 1000 px column holds an A4 page (794 px plus the gutters): no fit, the page keeps its zoom");
    }

    // ── header ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Header_ShowsTitleAndSaveStatus()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);

        var header = cut.Find("[data-testid='document-header']");
        header.QuerySelector(".tm-document-editor__document-title")!.TextContent.Should().Contain("Service agreement");

        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-header'] [data-testid='document-header-status']").TextContent
                .Should().Contain("Last saved"));
        cut.FindAll("[data-testid='document-status-bar'] [data-testid='document-last-saved']")
            .Should().BeEmpty("with the header the last-saved text lives in the header only");
    }

    [Fact]
    public async Task ShowHeaderFalse_HidesHeader_AndKeepsQuickAccessInRibbon()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.ShowHeader, false));

        cut.FindAll("[data-testid='document-header']").Should().BeEmpty();
        cut.FindAll(".tm-document-editor__page-header").Should().BeEmpty("the in-surface page header is gone in every mode");
        foreach (var id in new[] { "document-save", "document-undo", "document-redo" })
        {
            cut.FindAll($"[data-testid='{id}']").Should().ContainSingle();
            cut.Find($"[data-testid='{id}']").Closest(".tm-document-editor__ribbon-group--quick-access")
                .Should().NotBeNull($"{id} stays in the ribbon quick-access group when the header is off");
        }

        await MarkCanvasReadyAsync(cut);
        cut.Find("[data-testid='document-save']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-status-bar'] [data-testid='document-last-saved']")
            .Should().NotBeNull("without the header the status bar keeps the last-saved text - it is the only place left"));
    }

    [Fact]
    public void HeaderStart_And_HeaderActions_Render()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p
            .Add(c => c.HeaderStart, (RenderFragment)(builder => builder.AddMarkupContent(0, "<span data-testid='host-start'>start</span>")))
            .Add(c => c.HeaderActions, (RenderFragment)(builder => builder.AddMarkupContent(0, "<button data-testid='host-share'>Share</button>"))));

        var header = cut.Find("[data-testid='document-header']");
        header.QuerySelector("[data-testid='host-start']").Should().NotBeNull();
        header.QuerySelector("[data-testid='host-share']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-header-actions'] [data-testid='host-share']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-header-start'] [data-testid='host-start']").Should().NotBeNull();
    }

    [Fact]
    public void Title_And_Subtitle_OverrideDocumentTitle()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p
            .Add(c => c.Title, "Project Aurora")
            .Add(c => c.Subtitle, "Draft proposal"));

        var header = cut.Find("[data-testid='document-header']");
        header.QuerySelector(".tm-document-editor__document-title")!.TextContent.Trim().Should().Be("Project Aurora");
        header.QuerySelector("[data-testid='document-header-subtitle']")!.TextContent.Trim().Should().Be("Draft proposal");
    }

    [Fact]
    public void NoSubtitle_RendersNoSubtitleElement()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.FindAll("[data-testid='document-header-subtitle']").Should().BeEmpty();
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop, true)]
    [InlineData(TmLayoutMode.Tablet, true)]
    [InlineData(TmLayoutMode.Mobile, true)]
    [InlineData(TmLayoutMode.Desktop, false)]
    [InlineData(TmLayoutMode.Tablet, false)]
    [InlineData(TmLayoutMode.Mobile, false)]
    public void UndoRedoSave_RenderExactlyOnce_PerMode(TmLayoutMode mode, bool showHeader)
    {
        var cut = RenderEditor(mode, p => p.Add(c => c.ShowHeader, showHeader));

        foreach (var id in new[] { "document-save", "document-undo", "document-redo" })
        {
            var matches = cut.FindAll($"[data-testid='{id}']");
            matches.Should().ContainSingle($"{id} must render exactly once ({mode}, header={showHeader})");
            if (mode == TmLayoutMode.Mobile && id == "document-save")
            {
                // Phase 8 (decision G2): a phone's Save is the primary tile of the action bar, with or without the header.
                matches[0].Closest("[data-testid='document-mobile-action-bar']").Should().NotBeNull("Save is the action bar's primary action on a phone");
                continue;
            }

            var inHeader = matches[0].Closest("[data-testid='document-header']") is not null;
            inHeader.Should().Be(showHeader, $"{id} belongs to the header exactly when the header is shown");
        }
    }

    [Fact]
    public void ShowToolbarFalse_WithHeader_KeepsTheHeaderActions()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.ShowToolbar, false));

        cut.FindAll(".tm-document-editor__ribbon").Should().BeEmpty();
        cut.Find("[data-testid='document-header'] [data-testid='document-save']").Should().NotBeNull();
    }

    [Fact]
    public void PrintPdf_InHeader_OnlyWhenCanExportPdf()
    {
        var withoutProvider = RenderEditor(TmLayoutMode.Desktop);
        withoutProvider.FindAll("[data-testid='document-header'] [data-testid='document-header-export-pdf']")
            .Should().BeEmpty("G9: a built-in action is offered only when the capability exists");

        var provider = Substitute.For<IDocumentPdfExportProvider>();
        var withProvider = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.PdfExportProvider, provider));
        withProvider.Find("[data-testid='document-header'] [data-testid='document-header-export-pdf']").Should().NotBeNull();
        withProvider.Find("[data-testid='document-header'] [data-testid='document-header-print-preview']").Should().NotBeNull();
        withProvider.Find("[data-testid='document-header'] [data-testid='document-header-search']").Should().NotBeNull();
    }

    [Fact]
    public async Task HeaderSearch_OpensTheFindPanel()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-header-search']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-find-panel']").Should().NotBeNull());
    }

    [Fact]
    public async Task NavigationSearch_OpensTheFindPanel_WithoutNewLogic()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);

        cut.Find("[data-testid='document-navigation-search']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-find-panel']").Should().NotBeNull());
    }

    // ── root contract ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(TmLayoutMode.Desktop, "desktop")]
    [InlineData(TmLayoutMode.Tablet, "tablet")]
    [InlineData(TmLayoutMode.Mobile, "mobile")]
    public void Root_HasDataLayout_FromLayoutMode(TmLayoutMode mode, string expected)
    {
        var cut = RenderEditor(mode);

        cut.Find(".tm-document-editor").GetAttribute("data-layout").Should().Be(expected);
    }

    [Fact]
    public void Workspace_And_Surface_KeepTheirTestIds()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        var workspace = cut.Find("[data-testid='document-editor-workspace']");
        workspace.GetAttribute("data-side-panel-state").Should().Be("open");
        workspace.QuerySelector("[data-region='doc'] [data-testid='document-canvas-engine-host']").Should().NotBeNull();
    }

    [Fact]
    public void Auto_RegistersLayoutObserver_AndReRendersOnlyOnModeChange()
    {
        var module = SetupLayoutObserverModule();

        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        var canvas = SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p => p
            .Add(c => c.DocumentId, "doc-1")
            .Add(c => c.Provider, provider));

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "observe").Should().Be(1,
            "Auto measures the editor root through the shared observer — no private observer"));
        cut.Find(".tm-document-editor").GetAttribute("data-layout").Should().Be("desktop", "the first frame is Desktop");

        var observer = cut.FindComponent<TmLayoutObserver>();
        var canvasCallsBefore = canvas.Invocations.Count(i => i.Identifier is "replaceModel" or "mount");
        var renderCount = () => int.Parse(cut.Find(".tm-document-editor").GetAttribute("data-blazor-render-count")!);

        // The same measurement again is not a change: nothing re-renders.
        var before = renderCount();
        cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("desktop"));
        renderCount().Should().Be(before, "an unchanged mode is ignored");

        cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("tablet"));
        cut.WaitForAssertion(() => cut.Find(".tm-document-editor").GetAttribute("data-layout").Should().Be("tablet"));
        cut.FindAll("[data-region='nav']").Should().BeEmpty();
        var afterChange = renderCount();
        afterChange.Should().BeGreaterThan(before);

        cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("tablet"));
        renderCount().Should().Be(afterChange, "a repeated tablet report does not re-render");

        canvas.Invocations.Count(i => i.Identifier is "replaceModel" or "mount")
            .Should().Be(canvasCallsBefore, "a layout switch never re-mounts or re-pushes the canvas model");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public void ForcedMode_IsMarked_SoTheCssNeverSecondGuessesIt(TmLayoutMode mode)
    {
        RenderEditor(mode).Find(".tm-document-editor").GetAttribute("data-layout-forced").Should().Be("true");
    }

    [Fact]
    public void AutoMode_IsNotMarkedForced()
    {
        SetupLayoutObserverModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p => p.Add(c => c.DocumentId, "doc-1").Add(c => c.Provider, provider));

        cut.Find(".tm-document-editor").HasAttribute("data-layout-forced").Should().BeFalse();
    }

    [Fact]
    public void InspectorVersionPanel_HasNoSecondCloseButton_WhileHostedInTheDockedSidePanel()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.Find("[data-testid='document-side-panel-close']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-version-panel-close']").Should().BeEmpty("the side panel's own X does the same thing");
    }

    [Fact]
    public async Task Desktop_OpeningACompatibilityTab_MovesFocusToTheNavigationColumn()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        var before = JSInterop.Invocations.Count(i => i.Identifier.EndsWith("domWrapper.focus", StringComparison.Ordinal));

        var setTab = typeof(TmDocumentEditor).GetMethod("SetSidePanelTabAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await cut.InvokeAsync(() => (Task)setTab.Invoke(cut.Instance, [DocumentSidePanelTab.Outline])!);

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier.EndsWith("domWrapper.focus", StringComparison.Ordinal))
            .Should().BeGreaterThan(before, "FocusAsync on the column's toggle"));
        cut.Find("[data-region='nav'] [data-testid='document-navigation-collapse']").Should().NotBeNull();
    }

    [Fact]
    public void IconTabs_OnAPhoneKeepTheirLabelAsTheAccessibleName_AndTextTabsShowIt()
    {
        // A phone's bottom sheet is as narrow as the screen: six labels do not fit, so the tabs are icons with the label as the accessible name.
        var phone = RenderEditor(TmLayoutMode.Mobile);
        phone.Find("[data-testid='document-mobile-comments']").Click();
        var tab = phone.Find("[data-testid='document-side-panel-tab-comments']");
        tab.GetAttribute("title").Should().Be("Comments", "a tooltip names the icon");
        tab.QuerySelector("span.tm-document-editor__sr-only")!.TextContent.Should().Be("Comments");

        // A tablet's side sheet is 24rem wide (the container is at least 768 px): the tabs read as text like the reference, and scroll if a long label needs it.
        var tablet = RenderEditor(TmLayoutMode.Tablet);
        tablet.Find("[data-testid='document-side-panel-edge-toggle']").Click();
        tablet.Find("[data-testid='document-side-panel']").GetAttribute("data-tab-style").Should().Be("text");

        var desktop = RenderEditor(TmLayoutMode.Desktop);
        var textTab = desktop.Find("[data-testid='document-side-panel-tab-comments']");
        textTab.HasAttribute("title").Should().BeFalse();
        textTab.QuerySelector("span.tm-document-editor__sr-only").Should().BeNull();
    }

    [Fact]
    public void ForcedMode_NeverImportsTheObserverModule()
    {
        var module = SetupLayoutObserverModule();

        var cut = RenderEditor(TmLayoutMode.Tablet);

        cut.Find(".tm-document-editor").GetAttribute("data-layout").Should().Be("tablet");
        module.Invocations.Count(i => i.Identifier == "observe").Should().Be(0);
    }

    // ── F6 region cycle ────────────────────────────────────────────────────

    [Fact]
    public void RegionCycle_IsAttachedToTheRoot_AndEveryRegionIsMarked()
    {
        var cycle = SetupRegionCycleModule();

        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cycle.Invocations.Count(i => i.Identifier == "attachRegionCycle").Should().Be(1,
            "F6 must cancel the browser default synchronously, so the handler is a JS module on the root"));
        cut.FindAll("[data-focus-region]").Select(e => e.GetAttribute("data-focus-region"))
            .Should().Equal("header", "ribbon", "nav", "doc", "inspector");
    }

    [Fact]
    public void RegionCycle_TabletRegionsSkipTheNavigationColumn()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);

        cut.FindAll("[data-focus-region]").Select(e => e.GetAttribute("data-focus-region"))
            .Should().Equal("header", "ribbon", "doc", "inspector");
    }

    // ── doc region banner slot ─────────────────────────────────────────────

    [Fact]
    public void DocRegionBannerSlot_RendersAboveCanvas()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, seed: document =>
            document.Revisions.Add(new DocumentRevision { Id = "revision-1", Type = DocumentRevisionType.Insertion }));

        var doc = cut.Find("[data-region='doc']");
        var banners = doc.QuerySelector("[data-testid='document-doc-banners']");
        banners.Should().NotBeNull("the doc region owns an internal banner slot");
        banners!.QuerySelector("[data-testid='document-review-summary']").Should().NotBeNull(
            "the review summary is a doc-region banner now, not a full-width strip above the workspace");

        var html = doc.InnerHtml;
        html.IndexOf("document-doc-banners", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("document-canvas-engine-host", StringComparison.Ordinal), "the banner sits above the canvas");
        cut.Find(".tm-document-editor").QuerySelectorAll("[data-testid='document-review-summary']").Should().ContainSingle();
    }

    // ── G8 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Header_AddsNoApplyOrCancelFooter()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.FindAll("[data-testid='document-header'] [data-testid*='apply'], [data-region='inspector'] [data-testid*='apply'], [data-region='inspector'] [data-testid*='ok']")
            .Should().BeEmpty("G8: changes apply immediately and undo covers them - no Apply / OK footers");
    }

    private static Task MarkCanvasReadyAsync(IRenderedComponent<TmDocumentEditor> cut, string? stateJson = "{}")
    {
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        return cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady(stateJson));
    }
}
