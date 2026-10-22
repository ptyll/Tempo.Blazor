using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Mobile layout of <see cref="TmWireframeEditor"/> (wireframe redesign plan ce19aa8c, phase 7, decision G2: viewing + light
/// edits): with a forced <c>LayoutMode=Mobile</c> (no CSS, no DOM measurement) the editor is a compact header (title + host
/// slots), a tool row, a full-width canvas with a floating zoom, a <c>TmMobileActionBar</c> (Components | Pages and layers |
/// Export | More) and non-modal sheets of the core shell (peek / half / full). Resize/rotate handles, rulers, the minimap,
/// align/distribute, grouping, connectors and import stay tablet+.
/// </summary>
public class TmWireframeEditorMobileTests : LocalizationTestBase
{
    private static WireframeComponentDef MakeDef(string type) => new()
    {
        Type = type, DisplayName = type, Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
        IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { }
    };

    private IRenderedComponent<TmWireframeEditor> RenderEditor(
        TmLayoutMode mode = TmLayoutMode.Mobile,
        Action<ComponentParameterCollectionBuilder<TmWireframeEditor>>? configure = null,
        WireframeDocument? document = null)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton"));
        Services.AddSingleton(registry);

        var doc = document ?? NewDocument();
        return Render<TmWireframeEditor>(p =>
        {
            p.Add(c => c.Document, doc);
            p.Add(c => c.LayoutMode, mode);
            configure?.Invoke(p);
        });
    }

    private static WireframeDocument NewDocument()
    {
        var doc = new WireframeDocument { Title = "Test", Width = 390, Height = 844 };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "Home", Width = 390, Height = 844 });
        doc.Pages.Add(new WireframePage { Id = "p2", Name = "Pricing", Width = 390, Height = 844 });
        doc.ActivePageId = "p1";
        return doc;
    }

    private static TmWireframeDesignerCanvas Canvas(IRenderedComponent<TmWireframeEditor> cut) =>
        cut.FindComponent<TmWireframeDesignerCanvas>().Instance;

    private static string AddElement(IRenderedComponent<TmWireframeEditor> cut)
    {
        var doc = Canvas(cut).Document!;
        var el = WireframeDocumentExtensions.NewElement("TmButton", 100, 100);
        doc.Elements.Add(el);
        return el.Id;
    }

    private static void Select(IRenderedComponent<TmWireframeEditor> cut, string id) =>
        cut.InvokeAsync(() => Canvas(cut).JsOnSelectionChanged([id])).GetAwaiter().GetResult();

    private static IElement Tile(IRenderedComponent<TmWireframeEditor> cut, string id) =>
        cut.Find($".tm-mobile-action-bar__action[data-action-id='{id}']");

    private static IReadOnlyList<string> MoreMenuLabels(IRenderedComponent<TmWireframeEditor> cut)
    {
        cut.Find(".tm-mobile-action-bar__more").Click();
        return cut.FindAll(".tm-overlay-panel-sheet [role='menuitem'], .tm-overlay-panel-sheet [role='menuitemcheckbox']")
            .Select(i => i.TextContent.Trim()).ToList();
    }

    // ── Load state ─────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_FirstRender_SheetClosed_NoDockedPanels_NoBarOfTabletSheets()
    {
        var cut = RenderEditor();

        cut.Find(".tm-wd-editor").GetAttribute("data-editor-layout").Should().Be("mobile");
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("the panels sheet starts closed on a phone");
        cut.FindAll(".tm-editor-shell__panel").Should().BeEmpty();
        cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty();
        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__panel-toggle--mobile").Should().BeEmpty("the action bar is the one reopen path");
        cut.FindAll(".tm-wd-editor__page-tabs").Should().BeEmpty("pages live in the Pages and layers sheet on a phone");
    }

    // ── Action bar ─────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_ActionBar_HasComponentsPagesLayersExportMore()
    {
        var cut = RenderEditor();

        var tiles = cut.FindAll(".tm-mobile-action-bar__bar .tm-mobile-action-bar__action");
        tiles.Select(t => t.GetAttribute("data-action-id")).Should().Equal("components", "pages-layers", "export");
        tiles.Select(t => t.TextContent.Trim()).Should().Equal("Add", "Pages & layers", "Export");
        cut.Find(".tm-mobile-action-bar__more").Should().NotBeNull("the overflow trigger is the More tile");
    }

    [Fact]
    public void Mobile_HostActions_FromMobileActionsSlot()
    {
        var saved = 0;
        var cut = RenderEditor(configure: p => p.Add(c => c.MobileActions, new List<TmActionItem>
        {
            new() { Id = "save", Label = "Save", Icon = "check", Overflow = ActionOverflow.Never,
                    OnClick = EventCallback.Factory.Create(this, () => saved++) },
            new() { Id = "share", Label = "Share", OnClick = EventCallback.Factory.Create(this, () => { }) },
        }));

        var tiles = cut.FindAll(".tm-mobile-action-bar__bar .tm-mobile-action-bar__action");
        tiles.Select(t => t.GetAttribute("data-action-id")).Should().Contain("save", "a host action pinned Never stays on the bar");
        Tile(cut, "save").Click();
        saved.Should().Be(1);

        // An unpinned host action leaves the bar for More like any overflow item.
        MoreMenuLabels(cut).Should().Contain("Share");
    }

    [Fact]
    public void Mobile_NoBuiltInSaveOrOpen()
    {
        var cut = RenderEditor();

        var all = cut.FindAll(".tm-mobile-action-bar button").Select(b => b.TextContent.Trim()).ToList();
        all.Should().NotContain(l => l.Contains("Save", StringComparison.OrdinalIgnoreCase));
        all.Should().NotContain(l => l.Contains("Open", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Mobile_MoreMenu_HasNoImportOrAlign()
    {
        var cut = RenderEditor();

        var labels = MoreMenuLabels(cut);

        labels.Should().Contain(["Read-only", "Show grid"]);
        labels.Should().Contain(l => l.StartsWith("iPhone 14", StringComparison.Ordinal), "the device presets live in More");
        labels.Should().Contain(l => l.StartsWith("Desktop", StringComparison.Ordinal));
        labels.Should().NotContain(l => l.Contains("Import", StringComparison.OrdinalIgnoreCase), "import is tablet+ (G2)");
        labels.Should().NotContain(l => l.Contains("Align", StringComparison.OrdinalIgnoreCase));
        labels.Should().NotContain(l => l.Contains("Distribute", StringComparison.OrdinalIgnoreCase));
        labels.Should().NotContain(l => l.Contains("Group", StringComparison.OrdinalIgnoreCase));
        cut.FindAll("input[type='file']").Should().BeEmpty("no import file picker on a phone");
    }

    [Fact]
    public void Mobile_MoreMenu_DevicePreset_ResizesTheCanvasPage_Undoable()
    {
        var cut = RenderEditor();
        var page = Canvas(cut).Document!.ActivePage!;
        page.Width.Should().Be(390);

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll(".tm-overlay-panel-sheet [role='menuitemcheckbox'], .tm-overlay-panel-sheet [role='menuitem']")
            .First(i => i.TextContent.Trim().StartsWith("Laptop", StringComparison.Ordinal)).Click();

        // The More menu closes BEFORE it invokes the action (the shared close-before-invoke contract), so the resize lands a tick later.
        cut.WaitForAssertion(() => page.Width.Should().Be(1280));
        page.Height.Should().Be(800);

        cut.Find("[data-testid='wd-mobile-tools'] button[aria-label^='Undo']").Click();
        page.Width.Should().Be(390, "the device preset goes through the undoable ResizeCanvasCommand");
    }

    [Fact]
    public void Mobile_ComponentsTile_OpensSheetOnComponents_AndTogglesItClosed()
    {
        var cut = RenderEditor();

        Tile(cut, "components").Click();

        cut.Find(".tm-editor-shell__sheet").Should().NotBeNull();
        cut.Find("[data-region='left'] .tm-wd-toolbox").Should().NotBeNull();
        Tile(cut, "components").GetAttribute("aria-pressed").Should().Be("true");

        Tile(cut, "components").Click();

        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("tapping the active tile closes the sheet");
    }

    [Fact]
    public void Mobile_PagesLayersTile_OpensPagesAndLayersSegment_AndSwitchesPage()
    {
        var cut = RenderEditor();

        Tile(cut, "pages-layers").Click();

        var extra = cut.Find("[data-region='extra']");
        extra.HasAttribute("hidden").Should().BeFalse();
        var tabs = extra.QuerySelectorAll("[role='tab']").Select(t => t.TextContent.Trim()).ToList();
        tabs.Should().Equal("Pages", "Layers");

        extra.QuerySelector("[data-page-id='p2'] .tm-wd-pages__select")!.Click();

        Canvas(cut).Document!.ActivePage!.Id.Should().Be("p2");
    }

    [Fact]
    public void Mobile_ExportTile_OpensTheExportDialogAsASheet()
    {
        var cut = RenderEditor();

        Tile(cut, "export").Click();

        cut.Find(".tm-modal--sheet").Should().NotBeNull("the export dialog is a bottom sheet on a phone");
        cut.Find(".tm-wd-export-dialog").Should().NotBeNull();
        cut.Find(".tm-modal-footer").ClassList.Should().Contain("tm-modal-footer--inline");
    }

    // ── Selection → properties sheet (peek) with quick actions ─────────────────

    [Fact]
    public void Mobile_SelectingElement_OpensPropertiesSheetPeek()
    {
        var cut = RenderEditor();
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
        var id = AddElement(cut);

        Select(cut, id);

        var sheet = cut.Find(".tm-editor-shell__sheet");
        sheet.GetAttribute("data-snap-index").Should().Be("0", "a selection opens the sheet at the PEEK snap");
        cut.Find("[data-region='right']").HasAttribute("hidden").Should().BeFalse();
        cut.Find("[data-region='right'] .tm-wd-props [data-testid='wd-element-name']").Should().NotBeNull("the sheet carries the inspector incl. Name");

        var quick = cut.Find("[data-testid='wd-quick-actions']");
        quick.TextContent.Should().Contain("TmButton", "the peek names the selected type");
        quick.QuerySelectorAll("button").Select(b => b.TextContent.Trim())
            .Should().Equal("Duplicate", "Lock", "Delete");
        sheet.QuerySelectorAll("[role='tab'][aria-selected='true']").Should().Contain(t => t.TextContent.Trim() == "Properties");
    }

    [Fact]
    public void Mobile_Selecting_FromTheComponentsSheet_SwitchesToProperties()
    {
        var cut = RenderEditor();
        Tile(cut, "components").Click();
        var id = AddElement(cut);

        Select(cut, id);

        cut.Find("[data-region='left']").HasAttribute("hidden").Should().BeTrue();
        cut.Find("[data-region='right']").HasAttribute("hidden").Should().BeFalse();
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0");
    }

    [Fact]
    public void Mobile_TapInsertingAComponent_AddsItSelectsItAndPeeksTheProperties()
    {
        var cut = RenderEditor();
        Tile(cut, "components").Click();

        cut.Find("[data-region='left'] .tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        Canvas(cut).Document!.Elements.Should().HaveCount(1);
        cut.WaitForAssertion(() => cut.Find("[data-testid='wd-quick-actions']").Should().NotBeNull());
        cut.Find("[data-region='right']").HasAttribute("hidden").Should().BeFalse();
    }

    [Fact]
    public void Mobile_DeleteFromSheet_PushesRemoveCommand_Undoable()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);
        var doc = Canvas(cut).Document!;

        cut.Find("[data-testid='wd-quick-delete']").Click();

        doc.Elements.Should().BeEmpty();
        cut.FindAll("[data-testid='wd-quick-actions']").Should().BeEmpty("nothing is selected any more");
        var undo = cut.Find("[data-testid='wd-mobile-tools'] button[aria-label^='Undo']");
        undo.HasAttribute("disabled").Should().BeFalse("the delete is one undoable step");

        undo.Click();

        doc.Elements.Should().ContainSingle(e => e.Id == id);
    }

    [Fact]
    public void Mobile_QuickLock_LocksAndUnlocksTheSelection()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);
        var element = Canvas(cut).Document!.Elements.Single();

        cut.Find("[data-testid='wd-quick-lock']").Click();
        element.IsLocked.Should().BeTrue();
        cut.Find("[data-testid='wd-quick-lock']").TextContent.Trim().Should().Be("Unlock");

        cut.Find("[data-testid='wd-quick-lock']").Click();
        element.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void Mobile_QuickDuplicate_AddsACopy_AsOneUndoableStep()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);
        var doc = Canvas(cut).Document!;

        cut.Find("[data-testid='wd-quick-duplicate']").Click();

        doc.Elements.Should().HaveCount(2);
        cut.Find("[data-testid='wd-mobile-tools'] button[aria-label^='Undo']").Click();
        doc.Elements.Should().HaveCount(1, "duplicate is undoable");
    }

    [Fact]
    public void Mobile_ReadOnly_QuickActionsAreDisabled()
    {
        var cut = RenderEditor(configure: p => p.Add(c => c.ReadOnly, true));
        var id = AddElement(cut);
        Select(cut, id);

        cut.FindAll("[data-testid='wd-quick-actions'] button").Should().OnlyContain(b => b.HasAttribute("disabled"));
    }

    // ── Chrome that stays tablet+ ──────────────────────────────────────────────

    [Fact]
    public void Mobile_HidesRulersMinimapAndResizeHandles()
    {
        var cut = RenderEditor(configure: p => p.Add(c => c.ShowRulers, true).Add(c => c.ShowMinimap, true));

        cut.FindAll(".tm-wd-ruler").Should().BeEmpty("rulers are tablet+");
        cut.FindAll(".tm-wd-editor__ruler-h, .tm-wd-editor__ruler-v").Should().BeEmpty();
        cut.FindAll(".tm-wd-editor__minimap-wrap").Should().BeEmpty("the minimap is tablet+");
        Canvas(cut).LightEditing.Should().BeTrue("the canvas drops resize / rotate handles, rubber band and edge drawing");
        cut.Find(".tm-wd-canvas-wrap").GetAttribute("data-light-editing").Should().Be("true");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void NotMobile_KeepsTheFullCanvas(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode);

        Canvas(cut).LightEditing.Should().BeFalse();
        cut.Find(".tm-wd-canvas-wrap").HasAttribute("data-light-editing").Should().BeFalse();
        cut.FindAll(".tm-wd-editor__zoom-overlay").Should().BeEmpty();
        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
    }

    // ── Header and tool row ────────────────────────────────────────────────────

    [Fact]
    public void Mobile_Header_IsTheTitleAndHostSlots()
    {
        var cut = RenderEditor(configure: p => p
            .Add(c => c.HeaderStart, builder => builder.AddMarkupContent(0, "<span data-testid='host-start'>S</span>"))
            .Add(c => c.HeaderActions, builder => builder.AddMarkupContent(0, "<button data-testid='host-action'>Open</button>")));

        var header = cut.Find("[data-region='header']");
        header.QuerySelector("[data-testid='host-start']").Should().NotBeNull();
        header.QuerySelector("input.tm-wd-editor__title").Should().NotBeNull();
        header.QuerySelector("[data-testid='host-action']").Should().NotBeNull();
        cut.FindAll("[data-testid='wd-title']").Should().HaveCount(1, "the title is rendered once");
    }

    [Fact]
    public void Mobile_Header_ExistsWithoutHostSlots_BecauseThePhoneAlwaysNamesTheDocument()
    {
        var cut = RenderEditor();

        cut.Find("[data-region='header'] input.tm-wd-editor__title").Should().NotBeNull();
        cut.FindAll("[data-region='toolbar']").Should().BeEmpty("the tool row replaces the command row on a phone");
    }

    [Fact]
    public void Mobile_ToolRow_SelectPanUndoRedoMore()
    {
        var cut = RenderEditor();

        var row = cut.Find("[data-testid='wd-mobile-tools']");
        var labels = row.QuerySelectorAll("button.tm-toolbar-btn").Select(b => b.GetAttribute("aria-label") ?? "").ToList();
        labels.Should().Contain(l => l.StartsWith("Select", StringComparison.OrdinalIgnoreCase));
        labels.Should().Contain(l => l.StartsWith("Pan", StringComparison.OrdinalIgnoreCase));
        labels.Should().Contain(l => l.StartsWith("Undo", StringComparison.OrdinalIgnoreCase));
        labels.Should().Contain(l => l.StartsWith("Redo", StringComparison.OrdinalIgnoreCase));
        labels.Should().NotContain(l => l.StartsWith("Connector", StringComparison.OrdinalIgnoreCase), "edge drawing is tablet+");
        row.QuerySelector("button.tm-toolbar-more").Should().NotBeNull("the tool row has its own More");
        row.QuerySelectorAll("[data-tm-group]").Select(g => g.GetAttribute("data-tm-group")).Should().Contain(["tools", "history"]);
    }

    [Fact]
    public void Mobile_ToolRow_Pan_PressesThePanTool()
    {
        var cut = RenderEditor();
        var pan = cut.FindAll("[data-testid='wd-mobile-tools'] button.tm-toolbar-btn")
            .First(b => (b.GetAttribute("aria-label") ?? "").StartsWith("Pan", StringComparison.OrdinalIgnoreCase));

        pan.Click();

        JSInterop.Invocations.Any(i => i.Identifier == "tmWireframeDesigner.setToolMode").Should().BeTrue();
        cut.FindAll("[data-testid='wd-mobile-tools'] button.tm-toolbar-btn")
            .First(b => (b.GetAttribute("aria-label") ?? "").StartsWith("Pan", StringComparison.OrdinalIgnoreCase))
            .GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void Mobile_ToolRowMore_HoldsArrangeAndLockForTheSelection()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        Select(cut, id);

        cut.Find("[data-testid='wd-mobile-tools'] button.tm-toolbar-more").Click();

        var labels = cut.FindAll("[role='menu'] [role='menuitem'], [role='menu'] [role='menuitemcheckbox']")
            .Select(i => i.TextContent.Trim()).ToList();
        labels.Should().Contain("Forward").And.Contain("Backward");
        labels.Should().NotContain(l => l.Contains("Align", StringComparison.OrdinalIgnoreCase));
        labels.Should().NotContain(l => l.Contains("Group", StringComparison.OrdinalIgnoreCase));
    }

    // ── Floating zoom ──────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_FloatingZoom_ZoomInOutFit()
    {
        var cut = RenderEditor();
        var overlay = cut.Find("[data-testid='wd-zoom-overlay']");
        overlay.GetAttribute("role").Should().Be("group");
        overlay.QuerySelector(".tm-wd-editor__zoom-overlay-label").Should().NotBeNull();
        cut.FindAll(".tm-wd-editor__canvas-wrap [data-testid='wd-zoom-overlay']").Should().HaveCount(1, "the pill floats over the canvas, outside the svg");
        cut.FindAll("svg [data-testid='wd-zoom-overlay']").Should().BeEmpty();

        cut.Find("[data-testid='wd-zoom-in']").Click();
        JSInterop.Invocations.Count(i => i.Identifier == "tmWireframeDesigner.zoomTo").Should().Be(1);

        cut.Find("[data-testid='wd-zoom-out']").Click();
        JSInterop.Invocations.Count(i => i.Identifier == "tmWireframeDesigner.zoomTo").Should().Be(2);

        cut.Find("[data-testid='wd-zoom-fit']").Click();
        JSInterop.Invocations.Any(i => i.Identifier == "tmWireframeDesigner.fitToView").Should().BeTrue();
    }

    // ── Context menu as an action sheet ────────────────────────────────────────

    [Fact]
    public async Task Mobile_ContextMenuLongPress_RendersAsSheet()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);

        await cut.InvokeAsync(() => Canvas(cut).OnElementContextMenu(id, 20, 20));

        cut.FindAll(".tm-wd-editor__context-menu").Should().BeEmpty("the position:fixed menu is the desktop / tablet presentation");
        var sheet = cut.Find(".tm-overlay-panel-sheet");
        sheet.GetAttribute("aria-label").Should().Be("Actions");
        var items = sheet.QuerySelectorAll("[role='menuitem']").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Duplicate").And.Contain("Delete");
        items.Should().NotContain(l => l.Contains("Align", StringComparison.OrdinalIgnoreCase), "align is tablet+");
        items.Should().NotContain(l => l.StartsWith("Group", StringComparison.OrdinalIgnoreCase));
        sheet.QuerySelectorAll(".tm-overlay-panel-sheet__done").Should().HaveCount(1);
    }

    [Fact]
    public async Task Mobile_ContextMenuSheet_DeleteRunsTheCommand_AndClosesTheSheet()
    {
        var cut = RenderEditor();
        var id = AddElement(cut);
        await cut.InvokeAsync(() => Canvas(cut).OnElementContextMenu(id, 20, 20));

        cut.FindAll(".tm-overlay-panel-sheet [role='menuitem']").First(i => i.TextContent.Trim() == "Delete").Click();

        Canvas(cut).Document!.Elements.Should().BeEmpty();
        cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty();
    }

    [Fact]
    public async Task NotMobile_ContextMenu_StaysTheFixedMenu()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        var id = AddElement(cut);

        await cut.InvokeAsync(() => Canvas(cut).OnElementContextMenu(id, 20, 20));

        cut.Find(".tm-wd-editor__context-menu").Should().NotBeNull();
        cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty();
    }

    // ── Performance: a sheet never re-renders the canvas ───────────────────────

    [Fact]
    public void Mobile_OpeningSwitchingAndClosingTheSheet_DoesNotRerenderTheCanvas()
    {
        var cut = RenderEditor();
        var canvas = Canvas(cut);
        var before = canvas.RenderCount;

        Tile(cut, "components").Click();                       // open on Components
        cut.Find("[data-region='left']").Should().NotBeNull();
        Tile(cut, "pages-layers").Click();                     // switch to Pages and layers
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")
            .First(t => t.TextContent.Trim() == "Add").Click(); // sheet tab switch
        Tile(cut, "components").Click();                       // close
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();

        canvas.RenderCount.Should().Be(before, "chrome above the canvas must not repaint the (large) SVG scene");
    }

    [Fact]
    public void Mobile_AfterASheetInteraction_ADocumentMutationStillRepaintsTheCanvas()
    {
        var cut = RenderEditor();
        var canvas = Canvas(cut);
        Tile(cut, "components").Click();
        var before = canvas.RenderCount;

        cut.Find("[data-region='left'] .tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        cut.WaitForAssertion(() => canvas.RenderCount.Should().BeGreaterThan(before));
        cut.FindAll("g[data-el-id]").Should().HaveCount(1, "the inserted element is painted although the latch was armed by the sheet");
    }

    [Fact]
    public void Mobile_UndoAfterASheetInteraction_RepaintsTheCanvas()
    {
        var cut = RenderEditor();
        var canvas = Canvas(cut);
        Tile(cut, "components").Click();
        cut.Find("[data-region='left'] .tm-wd-toolbox__item[data-component-type='TmButton']").Click();
        cut.WaitForAssertion(() => cut.FindAll("g[data-el-id]").Should().HaveCount(1));
        Tile(cut, "components").Click();                       // the insert peeked the properties: switch to Components...
        Tile(cut, "components").Click();                       // ...and close it (arms the chrome-only latch again)
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();

        cut.Find("[data-testid='wd-mobile-tools'] button[aria-label^='Undo']").Click();

        cut.WaitForAssertion(() => cut.FindAll("g[data-el-id]").Should().BeEmpty("the undo repaints the scene"));
        canvas.Document!.Elements.Should().BeEmpty();
    }

    // ── Layout flips ───────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_ToTablet_DropsTheMobileChrome()
    {
        var cut = RenderEditor();
        Tile(cut, "components").Click();

        cut.Render(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.FindAll(".tm-wd-editor__zoom-overlay").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
        cut.Find("[data-testid='wd-toolbar-row-1']").Should().NotBeNull();
        Canvas(cut).LightEditing.Should().BeFalse();
    }

    // ── Viewer ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_WithoutToolbar_IsAnEmbeddedViewer_NoActionBarNoZoomPill()
    {
        var cut = RenderEditor(configure: p => p
            .Add(c => c.ShowToolbar, false)
            .Add(c => c.ShowToolbox, false)
            .Add(c => c.ShowPropertiesPanel, false)
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.FindAll(".tm-wd-editor__zoom-overlay").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();
        cut.Find(".tm-wd-canvas-wrap").Should().NotBeNull();
    }
}
