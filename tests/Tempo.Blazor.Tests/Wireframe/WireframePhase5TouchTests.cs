using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 5 — touch and Pointer Events on the canvas, plus the carry-forward fixes of
/// the phase 4 review. The pointer gestures themselves run in <c>wireframe-designer.js</c> (node tests:
/// <c>npm run test:wireframe</c>); these tests pin the Blazor contract around them: the <c>[JSInvokable]</c> surface the
/// script calls is unchanged, the canvas claims the touch (<c>touch-action: none</c>), the handle hit areas have a CSS
/// token, and the commands behind the gestures stay undoable.
/// </summary>
public class WireframePhase5TouchTests : LocalizationTestBase
{
    private static WireframeComponentDef MakeDef(string type) => new()
    {
        Type = type, DisplayName = type, Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
        IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { },
    };

    private static string RepoPath(params string[] parts) =>
        Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(parts).ToArray());

    private static string WireframeCss() =>
        File.ReadAllText(RepoPath("src", "Tempo.Blazor.Wireframe", "wwwroot", "css", "components", "_wireframe-editor.css"));

    private IRenderedComponent<TmWireframeEditor> RenderEditor(Action<ComponentParameterCollectionBuilder<TmWireframeEditor>>? configure = null)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton"));
        Services.AddSingleton(registry);
        var doc = new WireframeDocument { Title = "Test", Width = 1200, Height = 800 };
        doc.EnsureActivePage();
        return Render<TmWireframeEditor>(p =>
        {
            p.Add(c => c.Document, doc);
            p.Add(c => c.LayoutMode, TmLayoutMode.Desktop);
            configure?.Invoke(p);
        });
    }

    // ── The canvas claims the touch ───────────────────────────────────────────

    [Fact]
    public void CanvasSvg_ClaimsTheTouch_ThroughCss()
    {
        // touch-action decides, before any script runs, whether a finger scrolls the page or reaches the canvas: it has to
        // live in CSS on the svg (the shared helpers are attached with touchAction: null and never write it).
        var css = Regex.Match(WireframeCss(), @"\.tm-wd-canvas__svg\s*\{[^}]*\}").Value;

        css.Should().Contain("touch-action: none");
    }

    [Fact]
    public void CanvasWrap_DoesNotCarryAnInlineTouchAction()
    {
        Services.AddSingleton(new WireframeComponentRegistry());

        var cut = Render<TmWireframeDesignerCanvas>();

        cut.Find("svg.tm-wd-canvas__svg").GetAttribute("style").Should().NotContain("touch-action",
            "the rule lives in the stylesheet so a host can override it, not inline");
    }

    [Fact]
    public void WaypointHandles_GetAFingerSizedHitCircle_WhenAConnectorIsSelected()
    {
        Services.AddSingleton(new WireframeComponentRegistry());
        var doc = new WireframeDocument { Width = 800, Height = 600 };
        var a = WireframeDocumentExtensions.NewElement("TmButton", 0, 0);
        var b = WireframeDocumentExtensions.NewElement("TmButton", 400, 0);
        doc.Elements.AddRange([a, b]);
        var connector = new WireframeConnector { FromId = a.Id, ToId = b.Id, Waypoints = [new DiagramPoint(200, 100)] };
        doc.Connectors.Add(connector);

        var cut = Render<TmWireframeDesignerCanvas>(p => p.Add(x => x.Document, doc));
        cut.FindAll("circle.tm-wd-connector__waypoint-hit").Should().BeEmpty("a hit area only exists while the connector is selected");

        cut.InvokeAsync(() => cut.Instance.JsOnConnectorSelectionChanged([connector.Id]));

        var hit = cut.Find("circle.tm-wd-connector__waypoint-hit");
        hit.GetAttribute("data-waypoint-index").Should().Be("0");
        hit.GetAttribute("fill").Should().Be("transparent");
        hit.HasAttribute("pointer-events").Should().BeFalse("CSS owns it: none for a mouse (unchanged behaviour), all on a coarse pointer");
        cut.Find("circle.tm-wd-connector__waypoint").GetAttribute("data-waypoint-index").Should().Be("0",
            "the visible dot keeps its selector and index");
    }

    [Fact]
    public void WaypointHitCircle_IsNotPartOfTheExport()
    {
        // The export strips interactive chrome by class; the transparent hit circle must be on that list.
        var js = File.ReadAllText(RepoPath("src", "Tempo.Blazor.Wireframe", "wwwroot", "js", "wireframe-designer.js"));

        js.Should().Contain(".tm-wd-connector__waypoint-hit", "exportSvg must strip the hit circle with the rest of the chrome");
        Regex.Match(js, @"exportSvg:\s*function[\s\S]*?\n    \},").Value.Should().Contain("tm-wd-connector__waypoint-hit");
    }

    [Fact]
    public void WaypointHitCircle_CarriesNoPaintToBake()
    {
        // data-wd-paint nodes are baked by index in the export; a fully transparent hit circle must not enter that pairing.
        Services.AddSingleton(new WireframeComponentRegistry());
        var doc = new WireframeDocument { Width = 800, Height = 600 };
        var a = WireframeDocumentExtensions.NewElement("TmButton", 0, 0);
        var b = WireframeDocumentExtensions.NewElement("TmButton", 400, 0);
        doc.Elements.AddRange([a, b]);
        var connector = new WireframeConnector { FromId = a.Id, ToId = b.Id, Waypoints = [new DiagramPoint(200, 100)] };
        doc.Connectors.Add(connector);
        var cut = Render<TmWireframeDesignerCanvas>(p => p.Add(x => x.Document, doc));
        cut.InvokeAsync(() => cut.Instance.JsOnConnectorSelectionChanged([connector.Id]));

        cut.Find("circle.tm-wd-connector__waypoint-hit").HasAttribute("data-wd-paint").Should().BeFalse();
    }

    // ── The [JSInvokable] contract is unchanged ───────────────────────────────

    [Theory]
    [InlineData("OnDragStarted")]
    [InlineData("OnElementMoved")]
    [InlineData("OnElementsMoved")]
    [InlineData("OnElementResized")]
    [InlineData("OnElementRotated")]
    [InlineData("OnElementDropped")]
    [InlineData("OnMultiSelect")]
    [InlineData("OnSelectionChanged")]
    [InlineData("OnConnectorSelectionChanged")]
    [InlineData("OnConnectorCreated")]
    [InlineData("OnConnectorWaypointDragged")]
    [InlineData("OnConnectorWaypointAdded")]
    [InlineData("OnConnectorDragged")]
    [InlineData("OnZoomChanged")]
    [InlineData("OnViewBoxChanged")]
    [InlineData("OnElementContextMenu")]
    [InlineData("OnConnectorContextMenu")]
    [InlineData("OnCanvasContextMenu")]
    [InlineData("OnCanvasMouseMoved")]
    [InlineData("OnDeleteSelected")]
    [InlineData("OnDeleteConnectors")]
    [InlineData("OnToolModeChanged")]
    [InlineData("CloseContextMenu")]
    public void JsInvokableMethod_StillExists(string identifier)
    {
        var found = typeof(TmWireframeDesignerCanvas).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(Microsoft.JSInterop.JSInvokableAttribute), false)
                .Cast<Microsoft.JSInterop.JSInvokableAttribute>()
                .Any(a => (a.Identifier ?? m.Name) == identifier));

        found.Should().NotBeEmpty($"wireframe-designer.js calls {identifier} by name (the gestures changed, the contract did not)");
    }

    [Fact]
    public async Task CloseContextMenu_RaisesTheCloseCallback_SoAnOutsidePressDismissesTheMenu()
    {
        // wireframe-designer.js dismisses the open menu on a press outside it (a tap on a tablet) by calling CloseContextMenu.
        var closed = 0;
        Services.AddSingleton(new WireframeComponentRegistry());
        var cut = Render<TmWireframeDesignerCanvas>(p => p
            .Add(c => c.OnCloseContextMenu, EventCallback.Factory.Create(this, () => closed++)));

        await cut.InvokeAsync(() => cut.Instance.JsCloseContextMenu());

        closed.Should().Be(1);
    }

    [Fact]
    public async Task OnZoomChanged_ReportsTheNewScale_SoAPinchUpdatesTheZoomLabel()
    {
        var reported = new List<double>();
        var cut = RenderEditor();
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>();

        await cut.InvokeAsync(() => canvas.Instance.OnZoomChanged(2.0));

        cut.Find(".tm-wd-editor__zoom-label").TextContent.Trim().Should().Be("200%",
            "a two-finger pinch commits through OnZoomChanged; the toolbar zoom read-out must follow it");
        reported.Should().BeEmpty();
    }

    [Fact]
    public async Task OnZoomChanged_ClampsToTheCanvasLimits()
    {
        var cut = RenderEditor();
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>();

        await cut.InvokeAsync(() => canvas.Instance.OnZoomChanged(9.0));
        cut.Find(".tm-wd-editor__zoom-label").TextContent.Trim().Should().Be("500%", "5x is the ceiling");

        await cut.InvokeAsync(() => canvas.Instance.OnZoomChanged(0.01));
        cut.Find(".tm-wd-editor__zoom-label").TextContent.Trim().Should().Be("15%", "0.15x is the floor");
    }

    // ── Handle hit-area token ─────────────────────────────────────────────────

    [Fact]
    public void Css_DeclaresTheHandleHitToken_ForCoarsePointers()
    {
        var css = WireframeCss();

        css.Should().Contain("--tm-wd-handle-hit");
        css.Should().MatchRegex(@"@media \(pointer: coarse\)[\s\S]*--tm-wd-handle-hit");
        css.Should().MatchRegex(@"--tm-wd-handle-hit:\s*var\(--tm-space-6\)");
        css.Should().MatchRegex(@"--tm-wd-handle-hit:\s*var\(--tm-touch-target\)");
    }

    [Fact]
    public void Css_TheLongPressContextMenuEntries_ReachTheTouchTarget_OnACoarsePointer()
    {
        WireframeCss().Should().MatchRegex(@"@media \(pointer: coarse\)\s*\{\s*\.tm-wd-editor__context-item\s*\{[^}]*min-block-size:\s*var\(--tm-touch-target\)");
        WireframeCss().Should().MatchRegex(@"\.tm-wd-editor__context-menu\s*\{[^}]*max-height:\s*calc\(100vh - var\(--tm-space-4\)\)",
            "the taller touch menu must not push Delete below the fold");
    }

    [Fact]
    public void Css_TheHandleHitAreasAreInvisibleButHittable()
    {
        var css = WireframeCss();

        css.Should().MatchRegex(@"\.tm-wd-connector__waypoint-hit\s*\{[^}]*fill:\s*transparent[^}]*pointer-events:\s*none");
        css.Should().MatchRegex(@"@media \(pointer: coarse\)\s*\{\s*\.tm-wd-connector__waypoint-hit\s*\{[^}]*pointer-events:\s*all");
    }

    // ── Non-drag alternatives (WCAG 2.5.7) ────────────────────────────────────

    [Fact]
    public void Inspector_ResizesAnElement_WithoutDragging_ThroughTheWidthHeightFields()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton"));
        Services.AddSingleton(registry);
        var doc = new WireframeDocument { Width = 800, Height = 600 };
        var el = WireframeDocumentExtensions.NewElement("TmButton", 10, 10, 120, 36);
        doc.Elements.Add(el);
        var stack = new WireframeCommandStack();

        var cut = Render<TmWireframePropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, new[] { el.Id })
            .AddCascadingValue(stack));

        cut.FindAll(".tm-wd-props__section-body--grid2 input").Should().HaveCount(4, "X, Y, W and H");
        var width = cut.FindAll(".tm-wd-props__section-body--grid2 input")[2];
        width.Input("200");

        cut.WaitForAssertion(() => el.W.Should().Be(200));
        stack.Undo();
        el.W.Should().Be(120, "the field edit is one undoable step like the drag resize");
    }

    // ── Carry-forward: commands ───────────────────────────────────────────────

    [Fact]
    public void RenameElementCommand_UndoStillFindsTheElement_AfterThePageChanged()
    {
        var doc = new WireframeDocument { Title = "T" };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "One" });
        doc.Pages.Add(new WireframePage { Id = "p2", Name = "Two" });
        doc.ActivePageId = "p1";
        var el = new WireframeElement { Type = "TmButton", W = 10, H = 10, Name = "Old" };
        doc.Pages[0].Elements.Add(el);
        var cmd = new RenameElementCommand(doc, el.Id, "Old", "New");
        cmd.Execute();

        doc.ActivePageId = "p2";   // the user switched page; the command still belongs to page one
        cmd.Undo();

        el.Name.Should().Be("Old", "the command looks the element up on the page it was created on");
    }

    [Fact]
    public void ResizeCanvasCommand_UndoRestoresThePageThatWasResized_NotTheActiveOne()
    {
        var doc = new WireframeDocument { Title = "T" };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "p1", Name = "One", Width = 1440, Height = 900 });
        doc.Pages.Add(new WireframePage { Id = "p2", Name = "Two", Width = 800, Height = 600 });
        doc.ActivePageId = "p1";
        var cmd = new ResizeCanvasCommand(doc, 1440, 900, 390, 844);
        cmd.Execute();

        doc.ActivePageId = "p2";
        cmd.Undo();

        doc.Pages[0].Width.Should().Be(1440);
        doc.Pages[0].Height.Should().Be(900);
        doc.Pages[1].Width.Should().Be(800, "the other page is untouched");
    }

    [Fact]
    public async Task UndoOfACanvasResize_UpdatesTheJsCanvasSize()
    {
        var cut = RenderEditor();
        var panel = cut.FindComponent<TmWireframePropertiesPanel>();
        cut.Find("select[data-testid='wd-device-preset']").Change("iphone14");
        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == "tmWireframeDesigner.updateCanvasSize").Should().Be(1));

        // Undo from the toolbar (the same command stack the Ctrl+Z shortcut drives).
        await cut.InvokeAsync(() => cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']").Click());

        cut.WaitForAssertion(() =>
        {
            var calls = JSInterop.Invocations.Where(i => i.Identifier == "tmWireframeDesigner.updateCanvasSize").ToList();
            calls.Should().HaveCount(2, "undo of a canvas resize tells the JS canvas the size changed back");
            calls[1].Arguments[1].Should().Be(1200d);
            calls[1].Arguments[2].Should().Be(800d);
        });
        panel.Should().NotBeNull();
    }

    [Fact]
    public void CanvasApply_BelowTheMinimum_ShowsValidationFeedback_InsteadOfSilentlyRejecting()
    {
        var cut = RenderEditor();

        cut.Find("[data-testid='wd-canvas-width']").Input("50");
        cut.Find("[data-testid='wd-canvas-apply']").Click();

        var message = cut.Find("[data-testid='wd-canvas-error']");
        message.GetAttribute("role").Should().Be("alert");
        message.TextContent.Should().Contain("100");
        cut.Find("[data-testid='wd-canvas-width']").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void CanvasApply_AfterAValidSize_ClearsTheFeedback()
    {
        var cut = RenderEditor();
        cut.Find("[data-testid='wd-canvas-width']").Input("50");
        cut.Find("[data-testid='wd-canvas-apply']").Click();
        cut.FindAll("[data-testid='wd-canvas-error']").Should().HaveCount(1);

        cut.Find("[data-testid='wd-canvas-width']").Input("500");
        cut.Find("[data-testid='wd-canvas-apply']").Click();

        cut.FindAll("[data-testid='wd-canvas-error']").Should().BeEmpty();
    }

    // ── Carry-forward: toolbar, panels, toolbox ───────────────────────────────

    [Fact]
    public void ToolbarWrapper_HasNoDuplicateGroupLabel()
    {
        var cut = RenderEditor();

        var wrapper = cut.Find(".tm-wd-editor__toolbar");
        wrapper.HasAttribute("role").Should().BeFalse("TmToolbar carries the accessible name; a second labelled group duplicates it");
        wrapper.HasAttribute("aria-label").Should().BeFalse();
        cut.FindAll("[aria-label='Wireframe editor toolbar']").Should().HaveCount(1);
    }

    [Fact]
    public void SidePanel_PagesTab_DoesNotRepeatThePagesHeading()
    {
        var cut = RenderEditor(p => p.Add(c => c.PageNavigation, WireframePageNavigation.Panel));
        cut.FindAll("[data-region='left'] [role='tab']").First(t => t.TextContent.Trim() == "Pages").Click();

        cut.FindAll(".tm-wd-pages__title").Should().BeEmpty("the tab already says Pages");
    }

    [Fact]
    public void PagesPanel_ShowTitle_DefaultsToTrueForStandaloneUse()
    {
        Services.AddSingleton(new WireframeComponentRegistry());
        var doc = new WireframeDocument { Title = "T" };
        doc.EnsureActivePage();

        var standalone = Render<TmWireframePagesPanel>(p => p.Add(c => c.Document, doc));
        standalone.Find(".tm-wd-pages__title").TextContent.Trim().Should().Be("Pages");

        var hidden = Render<TmWireframePagesPanel>(p => p.Add(c => c.Document, doc).Add(c => c.ShowTitle, false));
        hidden.FindAll(".tm-wd-pages__title").Should().BeEmpty();
    }

    [Fact]
    public void Inspector_NoSelection_DoesNotRenderAnEmptySelectionSection()
    {
        var cut = RenderEditor();

        cut.FindAll("[data-testid='wd-section-selection']").Should().BeEmpty();
        cut.Find(".tm-wd-props__empty").TextContent.Should().Contain("Select an element",
            "the localized hint stays, without an empty collapsible around it");
    }

    [Fact]
    public void ToolboxTile_IsAnEquallyWideGridCell_WithAnEllipsisLabelAndTheFullNameInTheTitle()
    {
        var css = WireframeCss();

        css.Should().MatchRegex(@"\.tm-wd-toolbox__category-items\s*\{[^}]*grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\)");
        css.Should().MatchRegex(@"\.tm-wd-toolbox__item\s*\{[^}]*min-width:\s*0");
        Regex.Match(css, @"\.tm-wd-toolbox__item-name\s*\{[^}]*\}").Value.Should().Contain("text-overflow: ellipsis");

        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(new WireframeComponentDef
        {
            Type = "TmAVeryLongComponentName", DisplayName = "A very long component name", Category = "Test",
            DefaultWidth = 100, DefaultHeight = 40, IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { },
        });
        Services.AddSingleton(registry);
        var cut = Render<TmWireframeToolbox>();
        cut.Find(".tm-wd-toolbox__item").GetAttribute("title").Should().StartWith("A very long component name");
    }

    // ── Carry-forward: XML docs ───────────────────────────────────────────────

    [Fact]
    public void ShowPropertiesPanel_XmlDoc_StatesItAlsoHidesTheLayersTab()
    {
        var xml = File.ReadAllText(RepoPath("src", "Tempo.Blazor.Wireframe", "Components", "Wireframe", "TmWireframeEditor.razor.cs"));

        var summary = Regex.Match(xml, @"/// <summary>(?:(?!</summary>)[\s\S])*</summary>\s*\[Parameter\] public bool ShowPropertiesPanel").Value;
        summary.Should().Contain("Layers");
        summary.Should().MatchRegex("hid|hide|hides|drop", "the doc says the Layers tab goes with the inspector");
    }
}
