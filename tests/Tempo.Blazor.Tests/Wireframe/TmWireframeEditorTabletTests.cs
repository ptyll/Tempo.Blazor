using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Tablet layout of <see cref="TmWireframeEditor"/> (wireframe redesign plan ce19aa8c, phase 6): with a forced
/// <c>LayoutMode=Tablet</c> (no CSS, no DOM measurement) the left panel (Components / Pages / Layers) is an overlay
/// drawer, the inspector is a non-modal side sheet with a close ×, at most ONE of them is open, the minimap is
/// hidden, the toolbox is a single column with names and the command row is split into two toolbars.
/// </summary>
public class TmWireframeEditorTabletTests : LocalizationTestBase
{
    private static WireframeComponentDef MakeDef(string type) => new()
    {
        Type = type, DisplayName = type, Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
        IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { }
    };

    private IRenderedComponent<TmWireframeEditor> RenderEditor(
        TmLayoutMode mode = TmLayoutMode.Tablet,
        Action<ComponentParameterCollectionBuilder<TmWireframeEditor>>? configure = null)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton"));
        Services.AddSingleton(registry);

        var doc = new WireframeDocument { Title = "Test", Width = 1200, Height = 800 };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "Home", Width = 1200, Height = 800 });
        doc.ActivePageId = "p1";
        return Render<TmWireframeEditor>(p =>
        {
            p.Add(c => c.Document, doc);
            p.Add(c => c.LayoutMode, mode);
            configure?.Invoke(p);
        });
    }

    private static IElement ToggleLeft(IRenderedComponent<TmWireframeEditor> cut) => cut.Find("[data-testid='wd-toggle-left']");

    private static IElement ToggleInspector(IRenderedComponent<TmWireframeEditor> cut) => cut.Find("[data-testid='wd-toggle-inspector']");

    private static void SelectElement(IRenderedComponent<TmWireframeEditor> cut)
    {
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>().Instance;
        var doc = canvas.Document!;
        var el = WireframeDocumentExtensions.NewElement("TmButton", 100, 100);
        doc.Elements.Add(el);
        cut.InvokeAsync(() => canvas.JsOnSelectionChanged([el.Id])).GetAwaiter().GetResult();
    }

    // ── One sheet at a time ────────────────────────────────────────────────────

    [Fact]
    public void Tablet_PanelsStartClosed_AndTheShellDocksNothing()
    {
        var cut = RenderEditor();

        cut.Find(".tm-wd-editor").GetAttribute("data-editor-layout").Should().Be("tablet");
        cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty();
        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__panel").Should().BeEmpty("on a tablet the panels are sheets, not docked columns");
        cut.FindAll(".tm-editor-shell__rail").Should().BeEmpty();
    }

    [Fact]
    public void Tablet_OpeningInspector_ClosesLeftDrawer()
    {
        var cut = RenderEditor();

        ToggleLeft(cut).Click();
        cut.Find("[data-testid='wd-left-drawer'] .tm-wd-toolbox").Should().NotBeNull();

        ToggleInspector(cut).Click();

        cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty("at most one sheet is open");
        cut.Find("[data-testid='wd-inspector-sheet'] .tm-wd-props").Should().NotBeNull();
    }

    [Fact]
    public void Tablet_OpeningLeftDrawer_ClosesInspectorSheet()
    {
        var cut = RenderEditor();
        ToggleInspector(cut).Click();

        ToggleLeft(cut).Click();

        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty();
        cut.Find("[data-testid='wd-left-drawer']").Should().NotBeNull();
    }

    [Fact]
    public void Tablet_SelectingElement_OpensInspectorSheet()
    {
        var cut = RenderEditor();
        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty();

        SelectElement(cut);

        cut.Find("[data-testid='wd-inspector-sheet'] [data-testid='wd-element-name']").Should().NotBeNull();
    }

    [Fact]
    public void Tablet_SelectingElement_ClosesAnOpenLeftDrawer()
    {
        var cut = RenderEditor();
        ToggleLeft(cut).Click();

        SelectElement(cut);

        cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty();
        cut.Find("[data-testid='wd-inspector-sheet']").Should().NotBeNull();
    }

    [Fact]
    public void Tablet_SelectionBecomingEmpty_ClosesInspectorSheet()
    {
        var cut = RenderEditor();
        SelectElement(cut);
        cut.Find("[data-testid='wd-inspector-sheet']").Should().NotBeNull();

        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>().Instance;
        cut.InvokeAsync(() => canvas.JsOnSelectionChanged([])).GetAwaiter().GetResult();

        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty("nothing is selected: the inspector sheet has nothing to show");
        ToggleInspector(cut).GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Tablet_ConnectorSelectionBecomingEmpty_ClosesInspectorSheet()
    {
        var cut = RenderEditor();
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>().Instance;
        cut.InvokeAsync(() => canvas.JsOnConnectorSelectionChanged(["c1"])).GetAwaiter().GetResult();
        cut.Find("[data-testid='wd-inspector-sheet']").Should().NotBeNull();

        cut.InvokeAsync(() => canvas.JsOnConnectorSelectionChanged([])).GetAwaiter().GetResult();

        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty();
    }

    [Fact]
    public void Tablet_InspectorOpenedByToggleWithoutSelection_StaysOpen()
    {
        var cut = RenderEditor();
        ToggleInspector(cut).Click();

        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>().Instance;
        cut.InvokeAsync(() => canvas.JsOnSelectionChanged([])).GetAwaiter().GetResult();

        cut.Find("[data-testid='wd-inspector-sheet']").Should().NotBeNull("an already empty selection does not close a sheet the user opened");
    }

    [Fact]
    public void Tablet_Sheet_IsNamedByItsVisibleTitle_AndTheTitleIsNotAHeading()
    {
        var cut = RenderEditor();
        ToggleLeft(cut).Click();

        var sheet = cut.Find("[data-testid='wd-left-drawer']");
        var titleId = sheet.GetAttribute("aria-labelledby");
        titleId.Should().NotBeNullOrWhiteSpace();
        var title = cut.Find("#" + titleId);
        title.ClassList.Should().Contain("tm-editor-shell__overlay-title");
        title.TagName.Should().NotBe("H2", "a host page outline must not gain a heading from the editor");
    }

    [Fact]
    public void Tablet_PressOnCanvasBackground_ClosesLeftDrawer_ButNotTheInspectorSheet()
    {
        var cut = RenderEditor();
        ToggleLeft(cut).Click();

        cut.Find(".tm-wd-editor__canvas-wrap").TriggerEvent("onpointerdown", new Microsoft.AspNetCore.Components.Web.PointerEventArgs());

        cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty();

        ToggleInspector(cut).Click();
        cut.Find(".tm-wd-editor__canvas-wrap").TriggerEvent("onpointerdown", new Microsoft.AspNetCore.Components.Web.PointerEventArgs());
        cut.Find("[data-testid='wd-inspector-sheet']").Should().NotBeNull("the inspector is not a transient picker");
    }

    [Fact]
    public void Tablet_ToolboxInsert_ClosesLeftDrawer()
    {
        var cut = RenderEditor();
        ToggleLeft(cut).Click();

        cut.Find("[data-testid='wd-left-drawer'] .tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty());
        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.Document!.Elements.Should().HaveCount(1);
    }

    [Fact]
    public void Tablet_ToggleButtons_ReflectTheOpenSheet()
    {
        var cut = RenderEditor();
        ToggleLeft(cut).GetAttribute("aria-pressed").Should().Be("false");

        ToggleLeft(cut).Click();

        ToggleLeft(cut).GetAttribute("aria-pressed").Should().Be("true");
        ToggleInspector(cut).GetAttribute("aria-pressed").Should().Be("false");
    }

    // ── Close ×, Escape and focus ──────────────────────────────────────────────

    [Fact]
    public void Tablet_Sheets_HaveALocalizedCloseButton()
    {
        var cut = RenderEditor();
        ToggleInspector(cut).Click();

        var close = cut.Find("[data-testid='wd-inspector-sheet'] [data-testid='wd-sheet-close']");
        close.GetAttribute("aria-label").Should().Be("Close panel");

        close.Click();

        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty();
        ToggleInspector(cut).GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Tablet_Sheets_AreNonModalAndNamed()
    {
        var cut = RenderEditor();
        ToggleLeft(cut).Click();

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        scope.Modal.Should().BeFalse("the canvas stays operable while a sheet is open");
        var sheet = cut.Find("[data-testid='wd-left-drawer']");
        sheet.GetAttribute("aria-modal").Should().Be("false");
        sheet.GetAttribute("aria-labelledby").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Escape_ClosesSheet_AndRestoresFocusToTheOpener()
    {
        var cut = RenderEditor();
        ToggleInspector(cut).Click();
        var scope = cut.FindComponent<TmFocusScope>().Instance;
        scope.RestoreFocus.Should().BeTrue("closing a sheet hands the focus back to the control that opened it (decision EDITOR-SHELL-TABLET-PRESENTATION)");
        scope.RestoreFocusTarget.Should().BeNull("the opener is recorded by the focus scope itself: a toolbar toggle, or the canvas when a selection opened the sheet");

        cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        cut.FindAll("[data-testid='wd-inspector-sheet']").Should().BeEmpty();
        ToggleInspector(cut).GetAttribute("aria-pressed").Should().Be("false");
    }

    // ── Canvas viewport ────────────────────────────────────────────────────────

    [Fact]
    public void Tablet_OpeningAndClosingASheet_AsksTheCanvasToRelayout_ThroughScript()
    {
        var cut = RenderEditor();
        int Relayouts() => JSInterop.Invocations.Count(i => i.Identifier == "tmWireframeDesigner.relayout");
        var calls = Relayouts();

        ToggleInspector(cut).Click();
        cut.WaitForAssertion(() => Relayouts().Should().BeGreaterThan(calls));
        calls = Relayouts();
        cut.Find("[data-testid='wd-sheet-close']").Click();
        cut.WaitForAssertion(() => Relayouts().Should().BeGreaterThan(calls));

    }

    [Fact]
    public void Tablet_InspectorSheet_ReservesRoomBesideTheCanvas()
    {
        var cut = RenderEditor();
        cut.Find(".tm-wd-editor__canvas-region").ClassList.Should().NotContain("tm-wd-editor__canvas-region--sheet-right");

        ToggleInspector(cut).Click();

        cut.Find(".tm-wd-editor__canvas-region").ClassList.Should().Contain("tm-wd-editor__canvas-region--sheet-right");
    }

    // ── Minimap, toolbox, toolbar ──────────────────────────────────────────────

    [Fact]
    public void Tablet_MinimapHidden()
    {
        RenderEditor().FindAll(".tm-wd-editor__minimap-wrap").Should().BeEmpty();
    }

    [Fact]
    public void Desktop_MinimapStaysVisible()
    {
        RenderEditor(TmLayoutMode.Desktop).FindAll(".tm-wd-editor__minimap-wrap").Should().HaveCount(1, "the desktop minimap is unchanged");
    }

    [Fact]
    public void Tablet_ToolboxSingleColumnWithNames()
    {
        var cut = RenderEditor();
        ToggleLeft(cut).Click();

        var toolbox = cut.Find("[data-testid='wd-left-drawer'] .tm-wd-toolbox");
        toolbox.ClassList.Should().Contain("tm-wd-toolbox--single-column");
        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton'] .tm-wd-toolbox__item-name").TextContent.Trim().Should().Be("TmButton");
        cut.FindAll(".tm-wd-toolbox__item-name").Should().OnlyContain(n => n.TextContent.Trim().Length > 0);
    }

    [Fact]
    public void Desktop_ToolboxStaysMultiColumn()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.Find(".tm-wd-toolbox").ClassList.Should().NotContain("tm-wd-toolbox--single-column");
        cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty();
        cut.FindAll("[data-testid='wd-toggle-left']").Should().BeEmpty("on desktop the toggles are More-menu entries of the one toolbar");
    }

    [Fact]
    public void Tablet_ToolbarIsTwoRows_SplitByGroup()
    {
        var cut = RenderEditor();

        var rows = cut.FindAll("[data-testid^='wd-toolbar-row-']");
        rows.Should().HaveCount(2);
        string[] Groups(IElement row) => row.QuerySelectorAll("[data-tm-group]").Select(g => g.GetAttribute("data-tm-group")!).ToArray();
        Groups(rows[0]).Should().Contain(["tools", "history", "arrange", "group", "align", "snap"]);
        Groups(rows[1]).Should().Contain(["view", "zoom", "file"]);
        Groups(rows[0]).Should().NotContain(["zoom", "file"]);
    }

    [Fact]
    public void Desktop_ToolbarStaysOneRow()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.FindAll("[data-testid^='wd-toolbar-row-']").Should().BeEmpty();
        cut.FindAll(".tm-toolbar").Should().HaveCount(1);
    }

    [Fact]
    public void Tablet_Export_StaysPinned_AndImportIsASecondaryFileButton()
    {
        var cut = RenderEditor();

        cut.Find("[data-testid='wd-export']").ParentElement!.GetAttribute("data-pin").Should().Be("never");
        // Decision TOOLBAR-FILE-BUTTON: Import JSON is a Secondary TmToolbarFileButton - it may leave the bar into "More".
        var import = cut.Find("button[data-testid='wd-import']");
        import.GetAttribute("data-pin").Should().Be("auto");
        import.GetAttribute("data-rank").Should().Be("1");
        cut.Find("input[type='file'][data-testid='wd-import-input']").GetAttribute("accept").Should().Be(".json,.wireframe.json");
    }
    [Fact]
    public void Tablet_AllCommands_StayReachable()
    {
        var cut = RenderEditor();

        var bar = cut.FindAll(".tm-toolbar button.tm-toolbar-btn").Select(b => b.GetAttribute("aria-label") ?? "").ToList();
        foreach (var name in new[] { "Select", "Pan", "Connector", "Undo", "Redo", "Zoom out", "Zoom in", "Fit to view", "Toggle toolbox", "Toggle properties" })
            bar.Should().Contain(l => l.StartsWith(name, StringComparison.OrdinalIgnoreCase), $"'{name}' is on a tablet bar");
        cut.FindAll("[data-testid='wd-title']").Should().HaveCount(1);
    }

    [Fact]
    public void Tablet_ReadOnly_KeepsNavigationButInspectorStillOpens()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet, p => p.Add(c => c.ReadOnly, true));

        ToggleInspector(cut).Click();

        cut.Find("[data-testid='wd-inspector-sheet'] .tm-wd-props").Should().NotBeNull();
    }

    [Fact]
    public void Mobile_KeepsTheShellSheetPresentation_NoTabletSheets()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='wd-left-drawer']").Should().BeEmpty();
        cut.FindAll("[data-testid='wd-toggle-left']").Should().BeEmpty();
        cut.Find(".tm-wd-editor").GetAttribute("data-editor-layout").Should().Be("mobile");
    }
}
