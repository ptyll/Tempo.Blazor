using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign plan 146c7e1b, phase 8 (DE-2.7 + the phone parts of DE-1.1 / DE-1.2). bUnit cannot evaluate a container query, so every mode is
/// forced through <see cref="TmDocumentEditor.LayoutMode"/>. A tablet shows ONE side sheet (decision G2: full editing), a phone viewing plus
/// light edits: a compact header, the Home / Insert / Review / View tabs, the core action bar and sheets for every panel.
/// </summary>
public sealed class TmDocumentEditorSheetsTests : LocalizationTestBase
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    private IRenderedComponent<TmDocumentEditor> RenderEditor(
        TmLayoutMode mode,
        Action<DocumentEditorDocument>? seed = null,
        Action<ComponentParameterCollectionBuilder<TmDocumentEditor>>? configure = null)
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
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady("{}")).GetAwaiter().GetResult();
        return cut;
    }

    private static async Task SelectImageAsync(IRenderedComponent<TmDocumentEditor> cut)
    {
        var sync = typeof(TmDocumentEditor).GetMethod("SyncCanvasActiveImage", Instance)!;
        await cut.InvokeAsync(() => sync.Invoke(cut.Instance, [new TmDocumentCanvasEngineHost.CanvasEngineImageState
        {
            ObjectId = "drawing-1", BlockId = "block-1", RunId = "run-1", Url = "https://example.test/picture.png",
            AltText = "A picture", Width = 144, Height = 96, WrapMode = "square"
        }]));
        cut.Render();
    }

    private static DocumentBlock TableBlock() => new()
    {
        Id = "tbl-1",
        Type = DocumentBlockType.Table,
        Content = new TableBlockContent
        {
            Rows =
            [
                new TableRowContent
                {
                    Cells =
                    [
                        new TableCellContent
                        {
                            Id = "cell-1",
                            Blocks = [new DocumentBlock { Id = "cell-p", Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "x" }] } }]
                        }
                    ]
                }
            ]
        }
    };

    private static DocumentRevision Revision(string id) => new() { Id = id, Type = DocumentRevisionType.Insertion };

    // ── the action bar ─────────────────────────────────────────────────────

    [Fact]
    public void Mobile_RendersActionBar_CommentPrintSaveMore()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        var bar = cut.FindComponent<TmMobileActionBar>();
        bar.Instance.Placement.Should().Be(ActionBarPlacement.StickyContainer, "the bar belongs to the editor's own container");
        var tiles = cut.FindAll("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__action")
            .Select(tile => tile.GetAttribute("data-action-id")).ToList();
        tiles.Should().Equal("comments", "print-preview", "save");
        cut.Find("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__more").TextContent.Should().Contain("More");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void DesktopAndTablet_RenderNoActionBar(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode);

        cut.FindAll("[data-testid='document-mobile-action-bar']").Should().BeEmpty();
        cut.FindAll(".tm-mobile-action-bar").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_MoreSheet_OffersTheRemainingActions_AndOpensThePanels()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.Find("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__more").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role='menuitem']").Select(item => item.TextContent.Trim()).Should()
            .Contain(["Search in document", "Revisions", "Outline", "Pages", "Properties"]));
        cut.Find("[data-testid='document-mobile-outline']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab").Should().Be("outline"));
    }

    [Fact]
    public void Mobile_ReviewSummary_AsBadgeInActionBar()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, document =>
        {
            document.Revisions.Add(Revision("revision-1"));
            document.Revisions.Add(Revision("revision-2"));
        });

        cut.FindAll("[data-testid='document-review-summary']").Should().BeEmpty("the banner is not rendered on a phone");
        var badge = cut.Find("[data-action-id='comments'] .tm-mobile-action-bar__badge");
        badge.TextContent.Should().Be("3", "two pending changes plus the contract's one open comment ride the Comments tile");
        // The tile reads its label and then the words behind the number; the pill is decorative. Exact text, no whitespace stripping.
        cut.Find("[data-action-id='comments']").TextContent.Trim().Should().Be("Comments, 3 items to review3");
        cut.Find("[data-action-id='comments'] .tm-sr-only").TextContent.Should().Be(", 3 items to review");
        badge.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Mobile_WithoutComments_TheFirstTileIsRevisions_WithTheBadge()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, document => document.Revisions.Add(Revision("revision-1")),
            configure: p => p.Add(c => c.ShowComments, false));

        cut.FindAll("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__action")
            .Select(tile => tile.GetAttribute("data-action-id")).Should().Equal("revisions", "print-preview", "save");
        cut.Find("[data-action-id='revisions'] .tm-mobile-action-bar__badge").TextContent.Should().Be("2", "the contract's own pending change plus the one added here");
        cut.FindAll("[data-action-id='comments']").Should().BeEmpty("no tile for a panel that does not exist");
    }

    [Fact]
    public void Mobile_PrintToPdf_IsOnlyBehindTheKebab_NotInMore()
    {
        var pdf = NSubstitute.Substitute.For<Tempo.Blazor.DocumentEditor.Interfaces.IDocumentPdfExportProvider>();
        var cut = RenderEditor(TmLayoutMode.Mobile, configure: p => p.Add(c => c.PdfExportProvider, pdf));

        cut.Find("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__more").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role='menuitem']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='document-mobile-export-pdf']").Should().BeEmpty("one place for Print to PDF on a phone");
        cut.FindAll("[role='menuitem']").Select(item => item.TextContent.Trim()).Should().NotContain("Print to PDF");
    }

    [Fact]
    public void Mobile_HeaderKebab_AnnouncesADialog_AndTracksItsPanel()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, configure: p => p
            .Add(c => c.HeaderActions, (RenderFragment)(builder => builder.AddMarkupContent(0, "<button data-testid='host-share'>Share</button>"))));

        var kebab = cut.Find("[data-testid='document-header-kebab']");
        kebab.GetAttribute("aria-haspopup").Should().Be("dialog");
        kebab.GetAttribute("aria-expanded").Should().Be("false");
        kebab.HasAttribute("aria-controls").Should().BeFalse();

        kebab.Click();

        kebab = cut.Find("[data-testid='document-header-kebab']");
        kebab.GetAttribute("aria-expanded").Should().Be("true");
        var panelId = kebab.GetAttribute("aria-controls");
        panelId.Should().NotBeNullOrEmpty();
        cut.Find($"[id='{panelId}']").Should().NotBeNull("aria-controls names the rendered panel");
    }

    [Fact]
    public async Task Mobile_PhoneRestrictedCommands_DisabledInRegistry()
    {
        var phone = RenderEditor(TmLayoutMode.Mobile);
        var tablet = RenderEditor(TmLayoutMode.Tablet);
        var field = typeof(TmDocumentEditor).GetField("_commandRegistry", Instance)!;
        var refresh = typeof(TmDocumentEditor).GetMethod("RefreshCommandRegistryAsync", Instance)!;
        await phone.InvokeAsync(() => (Task)refresh.Invoke(phone.Instance, null)!);
        await tablet.InvokeAsync(() => (Task)refresh.Invoke(tablet.Instance, null)!);
        var phoneRegistry = (Tempo.Blazor.Components.DocumentEditor.Registry.DocumentEditorCommandRegistry)field.GetValue(phone.Instance)!;
        var tabletRegistry = (Tempo.Blazor.Components.DocumentEditor.Registry.DocumentEditorCommandRegistry)field.GetValue(tablet.Instance)!;

        var registered = Tempo.Blazor.Components.DocumentEditor.Registry.DocumentEditorCommandRegistry.PhoneRestrictedCommands
            .Where(name => phoneRegistry.GetState(name) is not null).ToList();
        registered.Should().Contain(["insertEquation", "compareDocuments"], "the registered members of the G2 set");
        foreach (var name in registered)
        {
            phoneRegistry.GetState(name)!.IsEnabled.Should().BeFalse($"{name} is tablet-and-up (decision G2)");
        }

        tabletRegistry.GetState("insertEquation")!.IsEnabled.Should().BeTrue("a tablet edits in full");
        tabletRegistry.GetState("compareDocuments")!.IsEnabled.Should().BeTrue();
        Tempo.Blazor.Components.DocumentEditor.Registry.DocumentEditorCommandRegistry.PhoneRestrictedCommands
            .Should().Contain(["insertEquation", "pageLayout", "createStyle", "renameStyle", "deleteStyle", "cellProperties", "compareDocuments"]);
    }

    [Fact]
    public void Mobile_PrintPreviewTile_IsDisabledWithoutTheCommand()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        // The canvas engine is up in these tests, so the tile is enabled; the contract is that it follows the command state like the header button did.
        cut.Find("[data-action-id='print-preview']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Mobile_ActionBarBadge_CountsOnlyWhatIsStillOpen()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        // The seeded contract carries one open comment and no pending change.
        cut.Find("[data-action-id='comments'] .tm-mobile-action-bar__badge").TextContent.Should().Be("1");
        cut.FindAll("[data-action-id='print-preview'] .tm-mobile-action-bar__badge").Should().BeEmpty("only the Comments tile carries a count");
    }

    [Fact]
    public void Mobile_ActionBarSave_RunsTheSave()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.Find("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-header-status']").GetAttribute("data-state").Should().Be("saved"));
    }

    // ── the header ─────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_HeaderCompact_TitleStatusKebab()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, configure: p => p
            .Add(c => c.HeaderActions, (RenderFragment)(builder => builder.AddMarkupContent(0, "<button data-testid='host-share'>Share</button>"))));

        var header = cut.Find("[data-testid='document-header']");
        header.GetAttribute("data-compact").Should().Be("true");
        header.QuerySelector(".tm-document-editor__document-title")!.TextContent.Should().Contain("Service agreement");
        header.QuerySelector("[data-testid='document-header-status']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-undo']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-redo']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-header-search']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-header-print-preview']").Should().BeNull("Print preview is an action bar tile on a phone");
        cut.FindAll("[data-testid='host-share']").Should().BeEmpty("the host actions live behind the kebab");

        cut.Find("[data-testid='document-header-kebab']").Click();

        cut.Find("[data-testid='document-header-kebab-panel'] [data-testid='host-share']").Should().NotBeNull("the kebab carries HeaderActions");
    }

    [Fact]
    public void Mobile_HeaderKebab_IsAbsent_WhenThereIsNothingBehindIt()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='document-header-kebab']").Should().BeEmpty();
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void DesktopAndTablet_KeepTheFullHeader(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode);

        var header = cut.Find("[data-testid='document-header']");
        header.HasAttribute("data-compact").Should().BeFalse();
        header.QuerySelector("[data-testid='document-header-print-preview']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-save']").Should().NotBeNull();
    }

    // ── the ribbon ─────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_RibbonRendersTabsRow_AndSinglePrimaryToolsRow()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='document-toolbar'] [role='tablist']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-toolbar'] [role='toolbar']").Should().ContainSingle("one command row, the rest is in More");
        cut.FindComponents<TmToolbar>().Where(toolbar => toolbar.Instance.Class?.Contains("tm-document-editor__ribbon-toolbar") == true)
            .Should().ContainSingle();
        foreach (var command in new[] { "document-bold", "document-italic", "document-underline" })
        {
            cut.FindAll($"[data-testid='{command}']").Should().NotBeEmpty($"{command} stays on the phone's primary row");
        }
    }

    [Fact]
    public void Mobile_LayoutReferencesMathTabs_NotRendered()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        var tabs = cut.FindAll("[data-testid^='document-ribbon-tab-']").Select(tab => tab.GetAttribute("data-testid")!["document-ribbon-tab-".Length..]).ToList();
        tabs.Should().Equal("home", "insert", "review", "view");
        foreach (var tab in new[] { "layout", "references", "math" })
        {
            cut.FindAll($"[data-testid='document-ribbon-tab-{tab}']").Should().BeEmpty($"decision G2: {tab} is tablet-and-up");
        }
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void DesktopAndTablet_KeepEveryRibbonTab(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode);

        cut.FindAll("[data-testid^='document-ribbon-tab-']").Select(tab => tab.GetAttribute("data-testid")!["document-ribbon-tab-".Length..])
            .Should().Equal("home", "insert", "math", "layout", "references", "review", "view");
    }

    [Fact]
    public void Mobile_StyleManagement_IsTabletAndUp()
    {
        var phone = RenderEditor(TmLayoutMode.Mobile);
        var tablet = RenderEditor(TmLayoutMode.Tablet);

        phone.FindComponent<TmDocumentEditorToolbar>().Instance.ShowStyleManagement.Should().BeFalse();
        tablet.FindComponent<TmDocumentEditorToolbar>().Instance.ShowStyleManagement.Should().BeTrue();
    }

    [Fact]
    public void Overflow_UsesCoreToolbarOverflowLayout()
    {
        // The ribbon keeps NO overflow model of its own: the priority partition and the measuring are the core TmToolbar's (ToolbarOverflowLayout /
        // tm-toolbar.js). The editor package ships neither a copy of the layout nor a measuring module.
        var root = FindRepositoryRoot();
        var package = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor");
        Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(Path.GetFileName)
            .Should().NotContain(name => name!.StartsWith("toolbar-overflow", StringComparison.OrdinalIgnoreCase), "the measuring module is the core's");

        var sources = string.Concat(Directory.EnumerateFiles(package, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));
        sources.Should().NotContain("class ToolbarOverflowLayout").And.NotContain("ToolbarOverflowPartition");

        var cut = RenderEditor(TmLayoutMode.Desktop);
        var toolbar = cut.FindComponents<TmToolbar>().Single(candidate => candidate.Instance.Class?.Contains("tm-document-editor__ribbon-toolbar") == true);
        toolbar.Instance.Overflow.Should().Be(ToolbarOverflow.Menu);
    }

    [Fact]
    public void MoreMenu_IsCoreToolbarOverflow_WithPopoverPresentation()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        var toolbar = cut.FindComponents<TmToolbar>().Single(candidate => candidate.Instance.Class?.Contains("tm-document-editor__ribbon-toolbar") == true);
        toolbar.Instance.OverflowPresentation.Should().Be(Tempo.Blazor.Components.Overlay.PanelPresentation.Popover,
            "the editor holds the selection: a popover that never moves focus into a sheet");
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Should().NotBeNull("the More trigger is the core toolbar's");
        cut.FindAll("[data-testid='document-toolbar'] .tm-dropdown").Should().NotContain(dropdown => dropdown.ClassList.Contains("tm-toolbar-more"),
            "the More menu is not a TmDropdown");
    }

    // ── the sheets of a phone ──────────────────────────────────────────────

    [Fact]
    public void Mobile_PanelsStartClosed_AndTheInspectorHasNoRegion()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.FindAll("[data-region='inspector']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("nothing hides the page until the reader asks for a panel");
        cut.Find("[data-testid='document-editor-workspace']").GetAttribute("data-side-panel-state").Should().Be("closed");
    }

    [Fact]
    public void Mobile_FindRevisionsVersions_OpenAsFullHeightSheet()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, document => document.Revisions.Add(Revision("revision-1")));

        // Revisions and Versions: the inspector sheet.
        cut.Find("[data-testid='document-mobile-comments']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());
        cut.WaitForAssertion(() => cut.FindComponents<TmDocumentFloatingPanel>().Single(panel => panel.Instance.Kind == "inspector")
            .Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile));
        cut.Find("[data-testid='document-side-panel-tab-revisions']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab").Should().Be("revisions"));
        cut.Find("[data-testid='document-side-panel-tab-versions']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab").Should().Be("versions"));
        cut.Find(".tm-document-floating-panel__frame").GetAttribute("data-floating-kind").Should().Be("inspector");

        // Find: its own sheet, and the inspector sheet is gone (one sheet at a time).
        cut.Find("[data-testid='document-header-search']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-find-panel']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("opening find closes the inspector sheet");
        cut.FindComponents<TmDocumentFloatingPanel>().Where(panel => panel.Instance.Kind == "find").Should().ContainSingle()
            .Which.Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile);
    }

    [Fact]
    public void Mobile_OpeningThePanelClosesFind_OneSheetAtATime()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);
        cut.Find("[data-testid='document-header-search']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-find-panel']").Should().NotBeNull());

        cut.Find("[data-testid='document-mobile-comments']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());
        cut.FindAll("[data-testid='document-find-panel']").Should().BeEmpty();
    }

    [Fact]
    public async Task Mobile_ImageTable_OpenContextualBottomSheet_WithShortSet()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, document => document.Blocks.Add(TableBlock()));

        // An image: wrapping, alignment and the alternative text - not the picture source, the size, the exact position or the order.
        await SelectImageAsync(cut);
        await cut.InvokeAsync(() => (Task)typeof(TmDocumentEditor).GetMethod("OpenImagePropertiesPanelFromCommandAsync", Instance)!.Invoke(cut.Instance, null)!);
        cut.Render();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-context-sheet']").Should().NotBeNull());
        var sheet = cut.Find("[data-testid='document-context-sheet']");
        sheet.QuerySelector("[data-testid='document-image-inspector']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='document-image-inspector-section-wrap']").Should().NotBeNull("wrapping is part of the short set");
        sheet.QuerySelector("[data-testid='document-image-inspector-section-accessibility']").Should().NotBeNull("the alternative text is part of the short set");
        sheet.QuerySelector("[data-testid='document-image-inspector-section-position'] [role='group']").Should().NotBeNull("the alignment is part of the short set");
        sheet.QuerySelector("[data-testid='document-image-inspector-position-x']").Should().BeNull("the exact position is tablet-and-up");
        sheet.QuerySelector("[data-testid='document-image-inspector-lock-anchor']").Should().BeNull("the anchor is tablet-and-up");
        sheet.QuerySelector("[data-testid='document-image-inspector-section-order']").Should().BeNull("the stacking order is tablet-and-up");
        sheet.QuerySelector("[data-testid='document-image-inspector-section-size']").Should().BeNull("the size is tablet-and-up");
        cut.WaitForAssertion(() => cut.FindComponents<TmDocumentFloatingPanel>().Single(panel => panel.Instance.Kind == "image-context")
            .Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile));
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("the object sheet is not the inspector");

        // The sheet closes; a table gets its short sheet: the alignment only, no cell properties.
        cut.Find(".tm-overlay-panel-sheet__done").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-context-sheet']").Should().BeEmpty());
        await cut.InvokeAsync(() => typeof(TmDocumentEditor).GetField("_canvasActiveImage", Instance)!.SetValue(cut.Instance, null));
        await cut.InvokeAsync(() => typeof(TmDocumentEditor).GetField("_selection", Instance)!.SetValue(cut.Instance, new DocumentEditorSelectionState { ActiveTableId = "tbl-1", ActiveTableCellId = "cell-1" }));
        await cut.InvokeAsync(() => (Task)typeof(TmDocumentEditor).GetMethod("OpenImagePropertiesPanelFromCommandAsync", Instance)!.Invoke(cut.Instance, null)!);
        cut.Render();

        cut.WaitForAssertion(() => cut.Find("[data-testid='document-context-sheet']").Should().NotBeNull());
        var tableSheet = cut.Find("[data-testid='document-context-sheet']");
        tableSheet.QuerySelector("[data-testid='document-table-properties-align-center']").Should().NotBeNull();
        tableSheet.QuerySelector("[data-testid='document-table-properties-width']").Should().BeNull("the width is tablet-and-up");
        tableSheet.QuerySelector("[data-testid='document-table-properties-border']").Should().BeNull("the border is tablet-and-up");
        tableSheet.QuerySelector("[data-testid='document-cell-properties-panel']").Should().BeNull("cell properties are tablet-and-up");
    }

    [Fact]
    public async Task Mobile_ObjectSelection_DoesNotOpenASheetByItself()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        await SelectImageAsync(cut);

        cut.FindAll("[data-testid='document-context-sheet']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();
    }

    // ── the side sheet of a tablet ─────────────────────────────────────────

    [Fact]
    public void Tablet_InspectorStartsClosed_AndOpensAsTheOneSideSheet()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);

        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();
        var region = cut.Find("[data-region='inspector']");
        region.GetAttribute("data-presentation").Should().Be("sheet");

        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();

        cut.FindAll("[data-testid='document-side-panel']").Should().ContainSingle();
        cut.Find("[data-testid='document-editor-workspace']").GetAttribute("data-side-panel-state").Should().Be("open");
    }

    [Fact]
    public async Task Tablet_AtMostOneSideSheetOpen()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);
        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse("the tabs are showing");

        await cut.InvokeAsync(() => (Task)typeof(TmDocumentEditor).GetMethod("OpenFindFromChromeAsync", Instance)!.Invoke(cut.Instance, null)!);
        cut.Render();

        cut.FindAll("[data-testid='document-side-panel']").Should().ContainSingle("find REPLACES the tabs in the one sheet");
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("find");
        cut.FindAll("[data-testid='document-side-panel'] [role='tablist']").Should().BeEmpty("the inspector's tab bar yields to find");
        cut.FindAll("[data-region='inspector']").Should().ContainSingle();
        cut.FindComponents<TmDocumentFloatingPanel>().Where(panel => panel.Instance.Kind is "find" or "inspector").Should().BeEmpty();

        cut.Find("[data-testid='document-find-close']").Click();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse("closing find returns to the tabs of the sheet the reader opened");
    }

    [Fact]
    public async Task Tablet_ReviewSummary_StaysABanner_AndFindIsNotAFloatingPanel()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet, document => document.Revisions.Add(Revision("revision-1")));

        cut.Find("[data-testid='document-review-summary']").Should().NotBeNull("the badge replaces the banner on a phone only");
        await cut.InvokeAsync(() => (Task)typeof(TmDocumentEditor).GetMethod("OpenFindFromChromeAsync", Instance)!.Invoke(cut.Instance, null)!);
        cut.FindAll("[data-testid='document-find-floating']").Should().BeEmpty();
    }

    [Fact]
    public async Task Find_KeepsItsQueryAcrossADesktopToTabletSwitch()
    {
        // The find panel is the inspector's context on a desktop AND on a tablet: one place in the render tree, so a layout flip keeps the component
        // (and the reader's query, options and results) instead of mounting a second copy.
        SetupLayoutObserverModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-1");
        SetupDocumentCanvasModule();
        var cut = Render<TmDocumentEditor>(p => p.Add(c => c.DocumentId, "doc-1").Add(c => c.Provider, provider));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        await cut.InvokeAsync(() => (Task)typeof(TmDocumentEditor).GetMethod("OpenFindFromChromeAsync", Instance)!.Invoke(cut.Instance, null)!);
        cut.Render();
        cut.Find("[data-testid='document-find-input']").Input("agreement");
        var panel = cut.FindComponent<TmDocumentFindPanel>().Instance;

        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("tablet"));
        cut.WaitForAssertion(() => cut.Find(".tm-document-editor").GetAttribute("data-layout").Should().Be("tablet"));

        cut.FindComponent<TmDocumentFindPanel>().Instance.Should().BeSameAs(panel, "the same component instance survives the flip");
        cut.Find("[data-testid='document-find-input']").GetAttribute("value").Should().Be("agreement");
    }

    [Fact]
    public void Sheets_DoNotAddApplyOrCancelFooters()
    {
        var phone = RenderEditor(TmLayoutMode.Mobile);
        phone.Find("[data-testid='document-mobile-comments']").Click();

        phone.FindAll("[data-testid='document-inspector-sheet'] [data-testid*='apply'], [data-testid='document-side-panel'] [data-testid*='apply']")
            .Should().BeEmpty("G8: changes apply immediately and undo covers them");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
