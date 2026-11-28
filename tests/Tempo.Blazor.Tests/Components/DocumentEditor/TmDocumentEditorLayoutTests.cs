using System.Text.Json;
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

    // Phase 11: the fit is a mode of the ENGINE (zoom.autoFit re-applied by a ResizeObserver on the scroll viewport); Blazor only forwards the
    // PageFitMode and reflects what the engine reports.
    private static string? MountedAutoFit(Bunit.JSRuntimeInvocation mount)
        => JsonDocument.Parse((string)mount.Arguments[3]!).RootElement.TryGetProperty("autoFit", out var value) ? value.GetString() : null;

    [Theory]
    [InlineData(DocumentPageFitMode.Auto, "narrow")]
    [InlineData(DocumentPageFitMode.Off, "off")]
    [InlineData(DocumentPageFitMode.FitWidth, "always")]
    public void PageFitMode_IsPassedToTheEngineAtMount(DocumentPageFitMode mode, string expected)
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, parameters => parameters.Add(p => p.PageFitMode, mode));
        cut.WaitForAssertion(() => SetupDocumentCanvasModule().Invocations.Any(i => i.Identifier == "mount").Should().BeTrue());

        MountedAutoFit(SetupDocumentCanvasModule().Invocations.First(i => i.Identifier == "mount")).Should().Be(expected);
    }

    [Fact]
    public void PageFitMode_DefaultsToAuto_NarrowOnlyInEveryLayout()
    {
        new TmDocumentEditor().PageFitMode.Should().Be(DocumentPageFitMode.Auto);
        var cut = RenderEditor(TmLayoutMode.Tablet);
        cut.WaitForAssertion(() => SetupDocumentCanvasModule().Invocations.Any(i => i.Identifier == "mount").Should().BeTrue());

        MountedAutoFit(SetupDocumentCanvasModule().Invocations.First(i => i.Identifier == "mount")).Should().Be("narrow");
    }

    [Fact]
    public async Task PageFitMode_ChangedAfterMount_IsPushedToTheEngine()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);

        cut.Render(ParameterView.FromDictionary(new Dictionary<string, object?> { ["PageFitMode"] = DocumentPageFitMode.Off }));

        cut.WaitForAssertion(() => SetupDocumentCanvasModule().Invocations
            .Any(i => i.Identifier == "setOptions" && i.Arguments.Any(a => a is string text && text.Contains("\"autoFit\":\"off\"", StringComparison.Ordinal)))
            .Should().BeTrue("the engine learns the new mode through setOptions"));
    }

    [Theory]
    [InlineData(700, "tablet")]
    [InlineData(390, "mobile")]
    public async Task Fit_BlazorNoLongerFitsTheColumnItself_TheEngineDoes(double width, string mode)
    {
        var (cut, _) = await RenderAutoAsync(width, mode);

        cut.Instance.GetType().GetField("_fitPending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Should().BeNull("the duplicate Blazor fit state is gone");
        SetupDocumentCanvasModule().Invocations.Where(i => i.Identifier == "execCommand" && i.Arguments.Any(a => string.Equals(a as string, "fitWidth", StringComparison.Ordinal)))
            .Should().BeEmpty("a narrow column is fitted by the engine's own observer, never by an interop command from the component");
    }

    [Fact]
    public async Task Fit_ALayoutSwitch_AsksTheEngineToResolveTheFitAgain()
    {
        var (_, _) = await RenderAutoAsync(700, "tablet");

        SetupDocumentCanvasModule().Invocations.Any(i => i.Identifier == "execCommand" && i.Arguments.Any(a => string.Equals(a as string, "applyAutoFit", StringComparison.Ordinal)))
            .Should().BeTrue("the padding of the column changes with the layout, so the engine re-resolves the fit once the layout rendered");
    }

    private async Task PushViewStateAsync(IRenderedComponent<TmDocumentEditor> cut, int percent, string preset, string autoFit)
    {
        var formatting = new TmDocumentCanvasEngineHost.CanvasEngineFormattingState { ZoomPercent = percent, ZoomPreset = preset, ZoomAutoFit = autoFit };
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        var json = JsonSerializer.Serialize(
            new { UiState = new TmDocumentCanvasEngineHost.CanvasEngineUiState { Formatting = formatting } }, DocumentEditorJson.Options);
        await cut.InvokeAsync(() => host.Instance.OnCanvasViewChanged(json));
    }

    [Fact]
    public async Task Fit_TheEngineReportOfAFittedPage_SetsDataPageFitAndTheFitLabelWithThePercent()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);
        cut.Find(".tm-document-editor").HasAttribute("data-page-fit").Should().BeFalse("a page at 100 percent keeps the ruler");

        await PushViewStateAsync(cut, 87, "fitWidth", "narrow");

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor").GetAttribute("data-page-fit").Should().Be("true"));
        cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("Fit 87%", "the AUTOMATIC fit names itself 'Fit' and the percent it resulted in");
        cut.Find("[data-testid='document-status-zoom'] button").GetAttribute("aria-label").Should().Be("Zoom 87 percent, fitted to width");

        await PushViewStateAsync(cut, 100, "custom", "narrow");

        cut.WaitForAssertion(() => cut.Find(".tm-document-editor").HasAttribute("data-page-fit").Should().BeFalse("the column grew back, the engine released the fit"));
        cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("100%");
    }

    [Fact]
    public async Task Fit_TheReadersOwnPageWidth_IsLabelledPageWidthWithThePercent_AndOnlyThenPressesThePageWidthButton()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);

        await PushViewStateAsync(cut, 130, "fitWidth", "always");

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("Page width · 130%"));
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.Find("[data-testid='document-zoom-page-width']").GetAttribute("aria-pressed").Should().Be("true");

        await PushViewStateAsync(cut, 87, "fitWidth", "narrow");

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("Fit 87%"));
        cut.Find("[data-testid='document-zoom-page-width']").GetAttribute("aria-pressed").Should().Be("false", "the automatic fit is not the reader's Page width");
        cut.Find("[data-testid='document-zoom-automatic']").GetAttribute("aria-pressed").Should().Be("true");
    }

    private bool ExecutedCanvasCommand(string command)
        => SetupDocumentCanvasModule().Invocations.Any(i => i.Identifier == "execCommand" && i.Arguments.Any(a => string.Equals(a as string, command, StringComparison.Ordinal)));

    [Fact]
    public async Task ZoomMenu_OffersAutomaticPageWidthWholePageAndThePercents_WithTheStateChecked()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);
        await PushViewStateAsync(cut, 87, "fitWidth", "narrow");
        cut.Find("[data-testid='document-status-zoom'] .tm-dropdown-trigger").Click();

        cut.Find("[data-testid='document-status-zoom-auto']").GetAttribute("aria-checked").Should().Be("true", "the automatic fit is the active mode");
        cut.Find("[data-testid='document-status-zoom-page-width']").GetAttribute("aria-checked").Should().Be("false");
        cut.Find("[data-testid='document-status-zoom-whole-page']").GetAttribute("aria-checked").Should().Be("false");
        foreach (var percent in new[] { 50, 75, 100, 125, 150, 200 })
        {
            cut.Find($"[data-testid='document-status-zoom-{percent}']").GetAttribute("aria-checked").Should().Be("false");
        }
    }

    [Fact]
    public async Task ZoomMenu_AutomaticResetsTheZoomInTheEngine_PageWidthAndWholePageHaveTheirOwnCommands()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);
        await PushViewStateAsync(cut, 140, "custom", "off");

        foreach (var (item, command) in new[] { ("auto", "resetZoom"), ("page-width", "fitWidth"), ("whole-page", "fitPage"), ("75", "setZoom") })
        {
            cut.Find("[data-testid='document-status-zoom'] .tm-dropdown-trigger").Click();
            cut.WaitForElement($"[data-testid='document-status-zoom-{item}']").Click();
            cut.WaitForAssertion(() => ExecutedCanvasCommand(command).Should().BeTrue($"'{item}' runs {command} in the engine"));
        }
    }

    [Fact]
    public async Task ZoomMenu_WithPageFitModeOff_HidesAutomatic_AndTheRegistryCommandIsDisabled()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, parameters => parameters.Add(p => p.PageFitMode, DocumentPageFitMode.Off));
        await MarkCanvasReadyAsync(cut);
        cut.Find("[data-testid='document-status-zoom'] .tm-dropdown-trigger").Click();

        cut.FindAll("[data-testid='document-status-zoom-auto']").Should().BeEmpty("there is no automatic zoom to return to");
        cut.Find("[data-testid='document-status-zoom-page-width']").Should().NotBeNull();
        cut.Find("[data-testid='document-ribbon-tab-view']").Click();
        cut.FindAll("[data-testid='document-zoom-automatic']").Should().BeEmpty();
    }

    // Phase 11 (fix round, decision `DE-AUTO-FIT-READABLE-FLOOR`): next to a docked inspector a fit below ReadableAutoFitScale (0.75) is not readable,
    // so a desktop container that would squeeze the page that far keeps its ribbon and navigation but presents the INSPECTOR as the tablet overlay sheet.
    private async Task<IRenderedComponent<TmDocumentEditor>> RenderAutoAtWidthAsync(double width, Action<ComponentParameterCollectionBuilder<TmDocumentEditor>>? configure = null)
    {
        SetupLayoutObserverModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p =>
        {
            p.Add(c => c.DocumentId, "doc-1").Add(c => c.Provider, provider);
            configure?.Invoke(p);
        });
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(width));
        return cut;
    }

    [Theory]
    [InlineData(1048, true)]
    [InlineData(1130, true)]
    [InlineData(1180, false)]
    [InlineData(1440, false)]
    public async Task ReadableFloor_ADesktopContainerThatWouldFitBelowThreeQuarters_PresentsOnlyTheInspectorAsTheSheet(double width, bool sheet)
    {
        var cut = await RenderAutoAtWidthAsync(width);

        cut.Find(".tm-document-editor").GetAttribute("data-layout").Should().Be("desktop", "the ribbon and the navigation stay the desktop's");
        cut.Find(".tm-document-editor").HasAttribute("data-inspector-sheet").Should().Be(sheet);
        cut.Find("[data-region='inspector']").GetAttribute("data-presentation").Should().Be(sheet ? "sheet" : "docked");
        cut.FindAll("[data-region='nav']").Should().HaveCount(1);
    }

    [Fact]
    public async Task ReadableFloor_StartsTheSheetClosed_AndDocksTheInspectorAgainWhenTheContainerGrows()
    {
        var cut = await RenderAutoAtWidthAsync(1048);
        cut.Find("[data-testid='document-editor-workspace']").ClassList.Should().Contain("tm-document-editor__workspace--side-panel-closed", "an open sheet would hide half of a page the reader has not seen");
        var observer = cut.FindComponent<TmLayoutObserver>();

        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1400));

        cut.WaitForAssertion(() => cut.Find("[data-region='inspector']").GetAttribute("data-presentation").Should().Be("docked"));
        cut.Find(".tm-document-editor").HasAttribute("data-inspector-sheet").Should().BeFalse();
    }

    [Fact]
    public async Task ReadableFloor_NeverSecondGuessesAForcedLayout_OrAPageFitModeOff()
    {
        var forced = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(forced);
        var observer = forced.FindComponent<TmLayoutObserver>();
        await forced.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1048));
        forced.Find(".tm-document-editor").HasAttribute("data-inspector-sheet").Should().BeFalse("a forced LayoutMode is the host's decision");

        var off = await RenderAutoAtWidthAsync(1048, p => p.Add(c => c.PageFitMode, DocumentPageFitMode.Off));
        off.Find(".tm-document-editor").HasAttribute("data-inspector-sheet").Should().BeFalse("without a fit there is no scale to protect");
    }

    [Fact]
    public async Task Fit_AReaderZoomReportedByTheEngine_ShowsThePercentOnly()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await MarkCanvasReadyAsync(cut);

        await PushViewStateAsync(cut, 120, "custom", "off");

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-status-zoom']").TextContent.Should().Contain("120%"));
        cut.Find(".tm-document-editor").HasAttribute("data-page-fit").Should().BeFalse();
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

    // ── Phase 13: the phone bounded frame keeps its action / status bar above the on-screen keyboard ──────

    private const string VisualViewportModule = "./_content/Tempo.Blazor/js/tm-visual-viewport.js";

    private BunitJSModuleInterop SetupVisualViewport()
    {
        var module = JSInterop.SetupModule(VisualViewportModule);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        return module;
    }

    [Fact]
    public void BoundedPhoneFrame_TracksTheVirtualKeyboard_AndStopsWhenTheFrameOrTheLayoutGoesAway()
    {
        var module = SetupVisualViewport();
        var cut = RenderEditor(TmLayoutMode.Mobile, p => p.Add(c => c.Height, "640px"));

        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "attach").Should().Be(1));

        cut.Render(parameters => parameters.Add(c => c.Height, null));
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "detach"));
    }

    [Fact]
    public void BoundedPhoneFrame_AFailedTrackerAttach_IsNotRetriedOnEveryRender()
    {
        var module = JSInterop.SetupModule(VisualViewportModule);
        module.SetupVoid("attach", _ => true).SetException(new Microsoft.JSInterop.JSException("no tracker"));
        module.SetupVoid("detach", _ => true).SetVoidResult();
        var cut = RenderEditor(TmLayoutMode.Mobile, p => p.Add(c => c.Height, "640px"));
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "attach").Should().Be(1));

        cut.Render(parameters => parameters.Add(c => c.Height, "641px"));
        cut.Render(parameters => parameters.Add(c => c.Height, "642px"));

        module.Invocations.Count(i => i.Identifier == "attach").Should().Be(1, "the failure is latched, not retried per render");
    }

    [Theory]
    [InlineData(TmLayoutMode.Mobile, null)]
    [InlineData(TmLayoutMode.Desktop, "640px")]
    [InlineData(TmLayoutMode.Tablet, "640px")]
    public void OtherFrames_NeverImportTheVisualViewportTracker(TmLayoutMode mode, string? height)
    {
        var module = SetupVisualViewport();
        RenderEditor(mode, p => p.Add(c => c.Height, height));

        module.Invocations.Should().BeEmpty("only a bounded phone frame has a bottom edge a keyboard can cover");
    }

    // ── Phase 12: the viewport-bounded frame (Height) ────────────────────

    [Fact]
    public void Height_Null_KeepsTheDocumentFlow_NoBoundedFrameMarkers()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        var root = cut.Find(".tm-document-editor");
        root.HasAttribute("data-frame").Should().BeFalse("without Height the editor flows in the page exactly as before");
        (root.GetAttribute("style") ?? string.Empty).Should().NotContain("--tm-document-editor-height");
        cut.Find(".tm-document-editor-host").HasAttribute("data-frame").Should().BeFalse();
        cut.Find("[data-region='doc']").HasAttribute("data-scroll-viewport").Should().BeFalse();
    }

    [Theory]
    [InlineData("600px")]
    [InlineData("100%")]
    [InlineData("calc(100dvh - 5rem)")]
    [InlineData("min(48rem, 100dvh)")]
    [InlineData("40rem")]
    [InlineData("100svh")]
    [InlineData("clamp(20rem, 80dvh, 60rem)")]
    [InlineData("var(--host-height)")]
    public void Height_Set_RendersTheBoundedFrame_OnTheRootAndTheObserverHost(string height)
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.Height, height));

        var root = cut.Find(".tm-document-editor");
        root.GetAttribute("data-frame").Should().Be("bounded");
        root.HasAttribute("style").Should().BeFalse("the root renders no style attribute of its own - the height is inherited from the observer wrapper, so a host's own style cannot clobber it");
        var host = cut.Find(".tm-document-editor-host");
        host.GetAttribute("data-frame").Should().Be("bounded", "the observer wrapper carries the same height so a percentage resolves through it");
        host.GetAttribute("style").Should().Contain($"--tm-document-editor-height: {height}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Height_BlankOrWhitespace_IsTreatedAsNull(string height)
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.Height, height));

        cut.Find(".tm-document-editor").HasAttribute("data-frame").Should().BeFalse();
    }

    [Theory]
    [InlineData("600px; position: fixed")]
    [InlineData("600px} body{display:none")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("600px !important")]
    [InlineData("auto")]
    [InlineData("none")]
    [InlineData("inherit")]
    [InlineData("foo")]
    [InlineData("600")]
    [InlineData("calc(100dvh - 5rem")]
    [InlineData("calc(100dvh - 5rem))")]
    [InlineData("px")]
    public void Height_ThatIsNotACssLength_IsIgnored_NeverInjectedIntoTheStyle(string height)
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.Height, height));

        var root = cut.Find(".tm-document-editor");
        root.HasAttribute("data-frame").Should().BeFalse("a value that is not a CSS length must not become a declaration");
        (root.GetAttribute("style") ?? string.Empty).Should().NotContain("position");
    }

    [Fact]
    public void Height_ChangedAfterTheFirstRender_TogglesTheFrame()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Find(".tm-document-editor").HasAttribute("data-frame").Should().BeFalse();

        cut.Render(parameters => parameters.Add(c => c.Height, "520px"));
        cut.Find(".tm-document-editor").GetAttribute("data-frame").Should().Be("bounded");
        cut.Find(".tm-document-editor-host").GetAttribute("style").Should().Contain("520px");

        cut.Render(parameters => parameters.Add(c => c.Height, null));
        cut.Find(".tm-document-editor").HasAttribute("data-frame").Should().BeFalse();
        cut.Find(".tm-document-editor-host").HasAttribute("data-frame").Should().BeFalse();
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public void Bounded_TheDocRegionIsTheScrollViewport_AndTheStatusBarIsPinnedOutsideIt(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode, p => p.Add(c => c.Height, "640px"));

        var doc = cut.Find("[data-region='doc']");
        doc.GetAttribute("data-scroll-viewport").Should().Be("true", "the engine measures and scrolls this element");
        doc.QuerySelector("[data-testid='document-canvas-engine-host']").Should().NotBeNull();

        var main = cut.Find("[data-testid='document-editor-main']");
        var workspace = cut.Find("[data-testid='document-editor-workspace']");
        var statusBar = cut.Find(".tm-document-editor__status-bar");
        statusBar.ParentElement!.GetAttribute("data-testid").Should().Be("document-editor-main", "the status bar is a sibling of the workspace under main, never inside the scroller");
        statusBar.Closest("[data-testid='document-editor-workspace']").Should().BeNull();
        statusBar.Closest("[data-scroll-viewport]").Should().BeNull();
        workspace.ParentElement!.GetAttribute("data-testid").Should().Be("document-editor-main");
        main.Should().NotBeNull();
    }

    [Fact]
    public void Bounded_FloatingSurfaces_StayOutsideTheMainSizeContainer()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.Height, "640px"));

        var floating = cut.Find("[data-testid='document-floating-root']");
        floating.Closest("[data-testid='document-editor-main']").Should().BeNull(
            "main is a size container (the containing block of every fixed descendant) - sheets and floating panels must stay outside it");
        cut.Find(".tm-document-editor__ribbon").Closest("[data-testid='document-editor-main']").Should().BeNull();
    }

    [Fact]
    public void Bounded_Phone_TheActionBarIsInFlowUnderTheWorkspace_NotInsideTheScroller()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, p => p.Add(c => c.Height, "640px"));

        var bar = cut.Find("[data-testid='document-mobile-action-bar']");
        var main = cut.Find("[data-testid='document-editor-main']");
        bar.Closest("[data-testid='document-editor-main']")!.GetAttribute("data-testid").Should().Be("document-editor-main");
        bar.Closest("[data-testid='document-editor-workspace']").Should().BeNull("the action bar sits under the workspace, it never covers the document");
        bar.Closest("[data-scroll-viewport]").Should().BeNull();
        var order = main.Children.Select(child => child.GetAttribute("data-testid") ?? child.ClassName ?? string.Empty).ToList();
        order.FindIndex(id => id.Contains("document-mobile-action-bar", StringComparison.Ordinal))
            .Should().BeGreaterThan(order.IndexOf("document-editor-workspace"), "in flow, after the workspace");
    }

    [Fact]
    public void Fullscreen_UsesTheBoundedFrame_AndLeavesItWhenToggledOff()
    {
        JSInterop.SetupVoid("tmDocumentEditor.setFullscreen", _ => true).SetVoidResult();
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Find(".tm-document-editor").HasAttribute("data-frame").Should().BeFalse();

        cut.Find("[data-testid='document-status-fullscreen']").Click();

        var root = cut.Find(".tm-document-editor");
        root.GetAttribute("data-frame").Should().Be("bounded", "fullscreen is the bounded frame at 100dvh - one path");
        root.GetAttribute("data-fullscreen").Should().Be("true");
        root.HasAttribute("style").Should().BeFalse("fullscreen sets the frame height in CSS (data-fullscreen), not in an inline style");
        cut.Find("[data-region='doc']").GetAttribute("data-scroll-viewport").Should().Be("true");

        cut.Find("[data-testid='document-status-fullscreen']").Click();

        root = cut.Find(".tm-document-editor");
        root.HasAttribute("data-frame").Should().BeFalse();
        root.HasAttribute("data-fullscreen").Should().BeFalse();
    }

    [Fact]
    public void Fullscreen_WithAHeight_StillCoversTheViewport_AndRestoresTheHostHeightAfterwards()
    {
        JSInterop.SetupVoid("tmDocumentEditor.setFullscreen", _ => true).SetVoidResult();
        var cut = RenderEditor(TmLayoutMode.Desktop, p => p.Add(c => c.Height, "480px"));

        cut.Find("[data-testid='document-status-fullscreen']").Click();
        cut.Find(".tm-document-editor").GetAttribute("data-fullscreen").Should().Be("true");
        cut.Find(".tm-document-editor-host").GetAttribute("style").Should().Contain("--tm-document-editor-height: 480px", "the wrapper keeps the host's height while the root is lifted");

        cut.Find("[data-testid='document-status-fullscreen']").Click();
        cut.Find(".tm-document-editor").HasAttribute("data-fullscreen").Should().BeFalse();
        cut.Find(".tm-document-editor").GetAttribute("data-frame").Should().Be("bounded");
    }

    [Fact]
    public void Fullscreen_KeepsTheReadingPosition_CaptureBeforeAndRestoreAfterTheRender()
    {
        JSInterop.SetupVoid("tmDocumentEditor.setFullscreen", _ => true).SetVoidResult();
        var cut = RenderEditor(TmLayoutMode.Desktop);
        var module = SetupDocumentCanvasModule();

        cut.Find("[data-testid='document-status-fullscreen']").Click();

        cut.WaitForAssertion(() =>
        {
            var names = module.Invocations.Select(invocation => invocation.Identifier).ToList();
            names.Should().Contain("captureReadingPosition", "the place is remembered BEFORE the frame changes");
            names.Should().Contain("restoreReadingPosition", "and put back after the render");
            names.IndexOf("captureReadingPosition").Should().BeLessThan(names.LastIndexOf("restoreReadingPosition"));
        });
    }

    [Fact]
    public void Height_ChangedAtRuntime_KeepsTheReadingPosition()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        var module = SetupDocumentCanvasModule();
        var before = module.Invocations.Count(invocation => invocation.Identifier == "captureReadingPosition");

        cut.Render(parameters => parameters.Add(c => c.Height, "520px"));

        cut.WaitForAssertion(() =>
        {
            module.Invocations.Count(invocation => invocation.Identifier == "captureReadingPosition").Should().Be(before + 1);
            module.Invocations.Count(invocation => invocation.Identifier == "restoreReadingPosition").Should().BeGreaterThan(0);
        });
    }

    private static Task MarkCanvasReadyAsync(IRenderedComponent<TmDocumentEditor> cut, string? stateJson = "{}")
    {
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        return cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady(stateJson));
    }
}
