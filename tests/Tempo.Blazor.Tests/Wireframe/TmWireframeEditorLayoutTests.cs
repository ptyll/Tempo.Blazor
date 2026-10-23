using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Layout tests of <see cref="TmWireframeEditor"/> on the core <c>TmEditorShell</c> (wireframe redesign plan ce19aa8c,
/// phase 4): shell regions, the <c>TmToolbar</c> groups / priorities / labels, the "More" menu, the Components /
/// Pages / Layers left panel, the inspector with the canvas / page / grid sections, the header slots and the render
/// isolation of the canvas and the toolbox. The layout is forced to Desktop so no DOM measurement is needed.
/// </summary>
public class TmWireframeEditorLayoutTests : LocalizationTestBase
{
    private static WireframeComponentDef MakeDef(string type) => new()
    {
        Type = type,
        DisplayName = type,
        Category = "Test",
        DefaultWidth = 120,
        DefaultHeight = 36,
        IsBuiltIn = true,
        Props = [],
        RenderSvg = (_, _) => { }
    };

    private static WireframeDocument NewDocument()
    {
        var doc = new WireframeDocument { Title = "Test", Width = 1200, Height = 800 };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "Home", Width = 1200, Height = 800 });
        doc.Pages.Add(new WireframePage { Id = "p2", Name = "Pricing", Width = 800, Height = 600 });
        doc.ActivePageId = "p1";
        return doc;
    }

    private IRenderedComponent<TmWireframeEditor> RenderEditor(
        Action<ComponentParameterCollectionBuilder<TmWireframeEditor>>? configure = null,
        WireframeDocument? document = null,
        TmLayoutMode mode = TmLayoutMode.Desktop)
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

    private static void OpenMore(IRenderedComponent<TmWireframeEditor> cut) => cut.Find("button.tm-toolbar-more").Click();

    private static IReadOnlyList<string> MoreLabels(IRenderedComponent<TmWireframeEditor> cut) =>
        cut.FindAll("[role='menu'] [role='menuitem'], [role='menu'] [role='menuitemcheckbox']")
           .Select(i => i.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> BarLabels(IRenderedComponent<TmWireframeEditor> cut) =>
        cut.FindAll(".tm-toolbar button.tm-toolbar-btn").Select(b => b.GetAttribute("aria-label") ?? b.TextContent.Trim()).ToList();

    private static IElement Tab(IRenderedComponent<TmWireframeEditor> cut, string text) =>
        cut.FindAll("[data-region='left'] [role='tab']").First(t => t.TextContent.Trim() == text);

    // ── Shell ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RendersInsideEditorShell_WithToolbarLeftCanvasRightRegions()
    {
        var cut = RenderEditor();

        cut.Find(".tm-wd-editor .tm-editor-shell").Should().NotBeNull();
        cut.Find("[data-region='toolbar'] .tm-toolbar").Should().NotBeNull();
        cut.Find("[data-region='left'] .tm-wd-toolbox").Should().NotBeNull();
        cut.Find("[data-region='canvas'] .tm-wd-canvas__svg").Should().NotBeNull();
        cut.Find("[data-region='right'] .tm-wd-props").Should().NotBeNull();
        cut.FindAll("[data-region='header']").Should().BeEmpty("the header is opt-in (G9)");
    }

    [Fact]
    public void LayoutMode_IsPassedToTheShell()
    {
        var cut = RenderEditor(null, null, TmLayoutMode.Tablet);

        cut.Find(".tm-editor-shell").GetAttribute("data-layout").Should().Be("tablet");
    }

    // ── Toolbar ────────────────────────────────────────────────────────────────

    [Fact]
    public void ToolbarGroups_HaveRoleGroupAndAriaLabel()
    {
        var cut = RenderEditor();

        var groups = cut.FindAll(".tm-toolbar [role='group'][data-tm-group]")
            .ToDictionary(g => g.GetAttribute("data-tm-group")!, g => g.GetAttribute("aria-label"));

        groups.Keys.Should().Contain(["tools", "history", "arrange", "group", "align", "snap", "zoom", "file"]);
        groups["tools"].Should().Be("Tools");
        groups["history"].Should().Be("History");
        groups["arrange"].Should().Be("Arrange");
        groups["group"].Should().Be("Grouping");
        groups["align"].Should().Be("Alignment");
        groups["snap"].Should().Be("Snapping & rulers");
        groups["zoom"].Should().Be("Zoom");
        groups["file"].Should().Be("File");
    }

    [Fact]
    public void ToolbarLabels_Auto_ShowsVisibleLabels()
    {
        var cut = RenderEditor();

        var undo = cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']");
        undo.QuerySelector(".tm-toolbar-btn-text")!.TextContent.Trim().Should().Be("Undo");
        undo.ClassList.Should().Contain("tm-toolbar-btn--labels-auto", "Labels=Auto: the container query drops the text on a narrow toolbar");
        undo.ClassList.Should().Contain("tm-toolbar-btn--label-below");
        cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Select']")
            .QuerySelector(".tm-toolbar-btn-text")!.TextContent.Trim().Should().Be("Select");
    }

    [Fact]
    public void Export_IsPrimaryButton()
    {
        var cut = RenderEditor();

        var export = cut.Find("[data-testid='wd-export']");
        export.ClassList.Should().Contain("tm-wd-editor__export");
        export.ParentElement!.ClassList.Should().Contain("tm-toolbar-item");
        export.ParentElement.GetAttribute("data-pin").Should().Be("never", "Export never leaves the bar (Pinned)");

        export.QuerySelector("button.tm-dropdown-trigger")!.Click();
        cut.FindAll("[data-testid^='wd-export-']").Select(i => i.TextContent.Trim())
            .Should().Equal("Export JSON", "Export SVG", "Export PNG", "Export PDF");
    }

    [Fact]
    public void FormatPainterReadOnlyPanelToggles_InMoreMenu()
    {
        var cut = RenderEditor(p => p.Add(c => c.ReadOnlyChanged, EventCallback.Factory.Create<bool>(this, _ => { })));

        BarLabels(cut).Should().NotContain(l => l.StartsWith("Copy style") || l.StartsWith("Paste style") || l.StartsWith("Paste size")
            || l.Contains("Show rulers") || l.Contains("panel") || l.Contains("read-only"));

        OpenMore(cut);

        var labels = MoreLabels(cut);
        labels.Should().Contain(["Copy style", "Paste style", "Paste size", "Show rulers", "Read-only", "Components panel", "Properties panel"]);
        cut.FindAll("[role='menuitemcheckbox']").Select(i => i.TextContent.Trim()).Should().Contain(["Read-only", "Show rulers"]);
    }

    [Fact]
    public void ReadOnlyEntry_IsAbsent_WhenTheHostPinsReadOnly()
    {
        var cut = RenderEditor(p => p.Add(c => c.ReadOnly, true));

        OpenMore(cut);

        MoreLabels(cut).Should().NotContain("Read-only");
    }

    // ── Page navigation ────────────────────────────────────────────────────────

    [Fact]
    public void PageNavigationTabs_Default_KeepsTodayTabs()
    {
        var cut = RenderEditor();

        cut.FindAll(".tm-wd-editor__page-tab").Should().HaveCount(2);
        cut.FindAll(".tm-wd-pages").Should().BeEmpty();
        cut.FindAll("[data-region='left'] [role='tab']").Select(t => t.TextContent.Trim()).Should().Equal("Components", "Layers");
    }

    [Fact]
    public void PageNavigationPanel_LeftPanelHasComponentsPagesLayersTabs()
    {
        var cut = RenderEditor(p => p.Add(c => c.PageNavigation, WireframePageNavigation.Panel));

        cut.FindAll("[data-region='left'] [role='tab']").Select(t => t.TextContent.Trim()).Should().Equal("Components", "Pages", "Layers");
        cut.FindAll(".tm-wd-editor__page-tabs").Should().BeEmpty("the horizontal tabs are replaced by the Pages tab");
        cut.FindAll(".tm-wd-pages").Should().BeEmpty("inactive tabs render lazily");
        Tab(cut, "Pages").Click();
        cut.Find("[data-region='left'] .tm-wd-pages").Should().NotBeNull();
        cut.FindAll(".tm-wd-pages__item").Should().HaveCount(2);
    }

    [Fact]
    public void PagesPanel_AddRenameDuplicateDelete_SyncsDocumentAndCommandStacks()
    {
        var doc = NewDocument();
        var cut = RenderEditor(p => p.Add(c => c.PageNavigation, WireframePageNavigation.Panel), doc);

        // An element on page 1 makes Undo available there.
        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']").Click();
        cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']").HasAttribute("disabled").Should().BeFalse();
        Tab(cut, "Pages").Click();

        // Add: the new page is active and brings its own (empty) undo history.
        cut.Find("[data-testid='wd-page-add']").Click();
        doc.Pages.Should().HaveCount(3);
        doc.ActivePage!.Name.Should().Be("Page 3");
        cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']").HasAttribute("disabled").Should().BeTrue();

        // Back on page 1 the history is back.
        cut.Find("[data-testid='wd-page-p1']").Click();
        doc.ActivePageId.Should().Be("p1");
        cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']").HasAttribute("disabled").Should().BeFalse();

        // Duplicate through the row menu.
        cut.Find("[data-page-id='p2'] .tm-wd-pages__row-menu .tm-context-menu__trigger").Click();
        cut.Find("[data-page-id='p2'] .tm-wd-pages__row-menu-item[data-action='duplicate']").Click();
        cut.WaitForAssertion(() => doc.Pages.Select(p => p.Name).Should().Contain("Pricing (copy)"));

        // Delete the active duplicate: another page becomes active and the editor follows.
        var copyId = doc.Pages.First(p => p.Name == "Pricing (copy)").Id;
        cut.Find($"[data-testid='wd-page-{copyId}']").Click();
        cut.Find($"[data-page-id='{copyId}'] .tm-wd-pages__row-menu .tm-context-menu__trigger").Click();
        cut.Find($"[data-page-id='{copyId}'] .tm-wd-pages__row-menu-item[data-action='delete']").Click();
        cut.WaitForAssertion(() => doc.Pages.Should().NotContain(p => p.Id == copyId));
        doc.ActivePage!.Id.Should().NotBe(copyId);
        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.Document!.ActivePage!.Id.Should().Be(doc.ActivePage.Id);
    }

    [Fact]
    public void PagesPanel_MoveUpDown_KeyboardAndMenu()
    {
        var doc = NewDocument();
        var cut = RenderEditor(p => p.Add(c => c.PageNavigation, WireframePageNavigation.Panel), doc);
        Tab(cut, "Pages").Click();

        cut.Find("[data-page-id='p1']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        cut.WaitForAssertion(() => doc.Pages.Select(p => p.Id).Should().Equal(new[] { "p2", "p1" }));

        cut.Find("[data-page-id='p1'] .tm-wd-pages__row-menu .tm-context-menu__trigger").Click();
        cut.Find("[data-page-id='p1'] .tm-wd-pages__row-menu-item[data-action='up']").Click();
        cut.WaitForAssertion(() => doc.Pages.Select(p => p.Id).Should().Equal(new[] { "p1", "p2" }));
    }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Fact]
    public void Properties_EmptySelection_ShowsCanvasPageGridSections()
    {
        var cut = RenderEditor();

        cut.Find("[data-region='right'] [data-testid='wd-section-canvas']").Should().NotBeNull();
        cut.Find("[data-region='right'] [data-testid='wd-section-page']").Should().NotBeNull();
        cut.Find("[data-region='right'] [data-testid='wd-section-grid']").Should().NotBeNull();
    }

    [Fact]
    public void CanvasSizeApply_PushesResizeCanvasCommand()
    {
        var doc = NewDocument();
        var cut = RenderEditor(null, doc);

        cut.Find("[data-testid='wd-canvas-width']").Input("640");
        cut.Find("[data-testid='wd-canvas-height']").Input("480");
        cut.Find("[data-testid='wd-canvas-apply']").Click();

        doc.ActivePage!.Width.Should().Be(640);
        doc.ActivePage.Height.Should().Be(480);

        cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']").Click();

        doc.ActivePage.Width.Should().Be(1200);
        doc.ActivePage.Height.Should().Be(800);
    }

    [Fact]
    public void DevicePreset_iPhone_SetsCanvas390x844_Undoable()
    {
        var doc = NewDocument();
        var cut = RenderEditor(null, doc);

        cut.Find("[data-testid='wd-device-preset']").Change("iphone14");

        doc.ActivePage!.Width.Should().Be(390);
        doc.ActivePage.Height.Should().Be(844);

        cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']").Click();

        doc.ActivePage.Width.Should().Be(1200);
        doc.ActivePage.Height.Should().Be(800);
    }

    [Fact]
    public void GridControls_DriveTheCanvas_WithoutAssigningTheParameters()
    {
        var shown = new List<bool>();
        var sizes = new List<int>();
        var cut = RenderEditor(p => p
            .Add(c => c.ShowGridChanged, EventCallback.Factory.Create<bool>(this, v => shown.Add(v)))
            .Add(c => c.GridSizeChanged, EventCallback.Factory.Create<int>(this, v => sizes.Add(v))));

        cut.Find("[data-testid='wd-grid-show']").Change(false);
        cut.Find("[data-testid='wd-grid-size']").Change("16");

        shown.Should().Equal(false);
        sizes.Should().Equal(16);
        cut.Instance.ShowGrid.Should().BeTrue("the [Parameter] is never assigned");
        cut.Instance.GridSize.Should().Be(8);
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>().Instance;
        canvas.ShowGrid.Should().BeFalse();
        canvas.GridSize.Should().Be(16);
    }

    // ── Header slots (G9) ──────────────────────────────────────────────────────

    [Fact]
    public void HeaderSlots_HeaderStartHeaderActionsTitle_Render()
    {
        var cut = RenderEditor(p => p
            .Add(c => c.HeaderStart, (RenderFragment)(b => { b.OpenElement(0, "span"); b.AddAttribute(1, "id", "host-start"); b.AddContent(2, "Start"); b.CloseElement(); }))
            .Add(c => c.HeaderActions, (RenderFragment)(b => { b.OpenElement(0, "button"); b.AddAttribute(1, "id", "host-open"); b.AddContent(2, "Open…"); b.CloseElement(); }))
            .Add(c => c.Title, (RenderFragment)(b => { b.OpenElement(0, "strong"); b.AddAttribute(1, "id", "host-title"); b.AddContent(2, "My design"); b.CloseElement(); })));

        var header = cut.Find("[data-region='header']");
        header.QuerySelector("#host-start")!.TextContent.Should().Be("Start");
        header.QuerySelector("#host-open")!.TextContent.Should().Be("Open…");
        header.QuerySelector("#host-title")!.TextContent.Should().Be("My design");
        header.QuerySelector("input.tm-wd-editor__title").Should().BeNull("the Title slot replaces the built-in title input");
        cut.FindAll("[data-region='toolbar'] input.tm-wd-editor__title").Should().BeEmpty();
    }

    [Fact]
    public void ShowHeader_RendersTheBuiltInTitleInTheHeader()
    {
        var cut = RenderEditor(p => p.Add(c => c.ShowHeader, true));

        cut.Find("[data-region='header'] input.tm-wd-editor__title").GetAttribute("value").Should().Be("Test");
        cut.FindAll("[data-region='toolbar'] input.tm-wd-editor__title").Should().BeEmpty();
    }

    [Fact]
    public void Default_KeepsTheTitleInTheToolbarRow_AndRendersNoHeader()
    {
        var cut = RenderEditor();

        cut.FindAll("[data-region='header']").Should().BeEmpty();
        cut.Find("[data-region='toolbar'] input.tm-wd-editor__title").GetAttribute("value").Should().Be("Test");
    }

    // ── Left / right panel state ───────────────────────────────────────────────

    [Fact]
    public void ShowToolboxFalse_DropsTheComponentsTab_AndShowPropertiesPanelFalse_DropsTheInspector()
    {
        var cut = RenderEditor(p => p.Add(c => c.ShowToolbox, false).Add(c => c.ShowPropertiesPanel, false));

        cut.FindAll(".tm-wd-toolbox").Should().BeEmpty();
        cut.FindAll("[data-region='right']").Should().BeEmpty();
        cut.FindAll("[data-region='left'] [role='tab']").Should().BeEmpty("with both panels hidden there is no left panel either");
    }

    [Fact]
    public void ShowToolboxFalse_Alone_KeepsTheLayersTab()
    {
        var cut = RenderEditor(p => p.Add(c => c.ShowToolbox, false));

        cut.FindAll("[data-region='left'] .tm-wd-toolbox").Should().BeEmpty();
        cut.Find("[data-region='left'] .tm-wd-layers").Should().NotBeNull();
    }

    // ── Layers tab ─────────────────────────────────────────────────────────────

    [Fact]
    public void LayersTab_HostsTheLayersPanel_WithoutItsOwnCollapseToggle()
    {
        var cut = RenderEditor();

        Tab(cut, "Layers").Click();

        var layers = cut.FindComponent<TmWireframeLayersPanel>().Instance;
        layers.ShowCollapseToggle.Should().BeFalse();
        cut.Find("[data-region='left'] .tm-wd-layers").ClassList.Should().Contain("tm-wd-layers--embedded");
        Tab(cut, "Layers").GetAttribute("aria-selected").Should().Be("true");
    }

    // ── Render isolation ───────────────────────────────────────────────────────

    [Fact]
    public void LeftTabSwitch_DoesNotRerenderCanvasOrToolbox()
    {
        var cut = RenderEditor();
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>().Instance;
        var toolbox = cut.FindComponent<TmWireframeToolbox>().Instance;
        var canvasBefore = canvas.RenderCount;
        var toolboxBefore = toolbox.RenderCount;

        Tab(cut, "Layers").Click();
        Tab(cut, "Components").Click();

        canvas.RenderCount.Should().Be(canvasBefore);
        toolbox.RenderCount.Should().Be(toolboxBefore);
    }

    [Fact]
    public void OpeningTheMoreMenu_DoesNotRerenderCanvasOrToolbox()
    {
        var cut = RenderEditor();
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>().Instance;
        var toolbox = cut.FindComponent<TmWireframeToolbox>().Instance;
        var canvasBefore = canvas.RenderCount;
        var toolboxBefore = toolbox.RenderCount;

        OpenMore(cut);
        cut.FindAll("[role='menu']").Should().NotBeEmpty();

        canvas.RenderCount.Should().Be(canvasBefore);
        toolbox.RenderCount.Should().Be(toolboxBefore);
    }

    // ── Nothing got lost ───────────────────────────────────────────────────────

    [Fact]
    public void AllCommands_Reachable()
    {
        var cut = RenderEditor(p => p
            .Add(c => c.PageNavigation, WireframePageNavigation.Panel)
            .Add(c => c.ReadOnlyChanged, EventCallback.Factory.Create<bool>(this, _ => { })));

        // Toolbar bar buttons.
        var bar = BarLabels(cut);
        foreach (var name in new[]
                 {
                     "Select", "Pan", "Connector", "Undo", "Redo", "Send", "Bring", "Lock", "Group", "Ungroup",
                     "Zoom out", "Zoom in", "Fit to view", "Snap"
                 })
        {
            bar.Should().Contain(l => l.StartsWith(name, StringComparison.OrdinalIgnoreCase), $"'{name}' stays on the bar");
        }

        // Inspector (empty selection): canvas size, device preset, page name, grid; document title in the toolbar row.
        foreach (var id in new[] { "wd-canvas-width", "wd-canvas-height", "wd-canvas-apply", "wd-device-preset", "wd-page-name", "wd-grid-show", "wd-grid-size" })
            cut.Find($"[data-testid='{id}']").Should().NotBeNull();
        cut.Find("input.tm-wd-editor__title").Should().NotBeNull();

        // Dropdown menus (Align, Distribute, Export, Import). Align / Distribute need a multi selection.
        cut.FindAll("[data-testid='wd-export'] button.tm-dropdown-trigger").Should().HaveCount(1);
        cut.Find("[data-testid='wd-import-input'][type='file']").Should().NotBeNull();
        var doc = cut.FindComponent<TmWireframeDesignerCanvas>().Instance.Document!;
        for (var i = 0; i < 3; i++) doc.Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", i * 200, 0));
        var ids = doc.Elements.Select(e => e.Id).ToArray();
        cut.InvokeAsync(() => cut.FindComponent<TmWireframeDesignerCanvas>().Instance.OnMultiSelect(ids)).GetAwaiter().GetResult();
        cut.Find("[data-testid='wd-align'] button.tm-dropdown-trigger").Click();
        cut.FindAll("[data-testid^='wd-align-']").Should().HaveCount(6);
        cut.Find("[data-testid='wd-distribute'] button.tm-dropdown-trigger").Click();
        cut.FindAll("[data-testid^='wd-distribute-']").Should().HaveCount(2);

        // More menu: format painter, rulers, read-only, panel toggles.
        OpenMore(cut);
        MoreLabels(cut).Should().Contain(["Copy style", "Paste style", "Paste size", "Show rulers", "Read-only", "Components panel", "Properties panel"]);

        // Pages: add / rename / duplicate / delete / move live in the Pages panel.
        Tab(cut, "Pages").Click();
        cut.Find("[data-testid='wd-page-add']").Should().NotBeNull();
        cut.Find("[data-page-id='p2'] .tm-wd-pages__row-menu .tm-context-menu__trigger").Click();
        foreach (var action in new[] { "rename", "duplicate", "up", "down", "delete" })
        {
            cut.FindAll($"[data-page-id='p2'] .tm-wd-pages__row-menu-item[data-action='{action}']")
                .Should().NotBeEmpty($"page action '{action}'");
        }
    }

    [Fact]
    public void Dropdown_AlignAndDistribute_DisabledWithoutEnoughSelection()
    {
        var cut = RenderEditor();

        cut.Find("[data-testid='wd-align'] button.tm-dropdown-trigger").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='wd-distribute'] button.tm-dropdown-trigger").HasAttribute("disabled").Should().BeTrue();
    }
}
