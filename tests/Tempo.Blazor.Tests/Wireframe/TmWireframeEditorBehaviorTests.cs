using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Behaviour tests for <see cref="TmWireframeEditor"/> (wireframe redesign plan, phase 1):
/// the PDF export preselect, the never-assigned <c>[Parameter]</c> contract for
/// <c>ReadOnly</c> / <c>ShowRulers</c>, and tap/click/keyboard insertion from the toolbox at the
/// centre of the visible area (the non-drag alternative to HTML5 drag-and-drop, WCAG 2.5.7).
/// </summary>
public class TmWireframeEditorBehaviorTests : LocalizationTestBase
{
    // Read-only, rulers and the panel toggles live in the toolbar's "More" menu (phase 4): the entries are checkable
    // menu items (menuitemcheckbox) that exist while the menu is open.
    private const string ReadOnlyEntrySelector = "[role='menuitemcheckbox'][data-action-id*='ReadOnly'], [role='menuitemcheckbox']";
    private const string RulersSelector = "[role='menuitemcheckbox']";

    private static void OpenMore(IRenderedComponent<TmWireframeEditor> cut) => cut.Find("button.tm-toolbar-more").Click();

    private static AngleSharp.Dom.IElement MoreEntry(IRenderedComponent<TmWireframeEditor> cut, string text)
    {
        if (cut.FindAll("button.tm-toolbar-more[aria-expanded='false']").Count > 0) OpenMore(cut);
        return cut.FindAll("[role='menu'] [role='menuitemcheckbox'], [role='menu'] [role='menuitem']")
                  .First(i => i.TextContent.Trim() == text);
    }

    private static WireframeComponentDef MakeDef(string type, double w = 120, double h = 36)
        => new()
        {
            Type = type,
            DisplayName = type,
            Category = "Test",
            DefaultWidth = w,
            DefaultHeight = h,
            IsBuiltIn = true,
            Props = [],
            RenderSvg = (_, _) => { }
        };

    private static WireframeDocument NewDocument()
    {
        var doc = new WireframeDocument { Title = "Test", Width = 1200, Height = 800 };
        doc.EnsureActivePage();
        return doc;
    }

    private IRenderedComponent<TmWireframeEditor> RenderEditor(
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
            configure?.Invoke(p);
        });
    }

    // ── Export PDF ─────────────────────────────────────────────────────────────

    [Fact]
    public void ExportPdfMenuItem_OpensDialogWithPdfSelected()
    {
        var cut = RenderEditor();

        cut.Find("[data-testid='wd-export'] button.tm-dropdown-trigger").Click();
        cut.Find("[data-testid='wd-export-pdf']").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-wd-export-dialog").Should().NotBeNull());
        cut.Find("#wd-export-format option[selected]").GetAttribute("value").Should().Be("pdf");
    }

    [Fact]
    public void ExportPngMenuItem_OpensDialogWithPngSelected()
    {
        var cut = RenderEditor();

        cut.Find("[data-testid='wd-export'] button.tm-dropdown-trigger").Click();
        cut.Find("[data-testid='wd-export-png']").Click();

        cut.WaitForAssertion(() => cut.Find("#wd-export-format option[selected]").GetAttribute("value").Should().Be("png"));
    }

    // ── Read-only toggle ───────────────────────────────────────────────────────

    [Fact]
    public void ReadOnlyToggle_RaisesReadOnlyChanged_DoesNotMutateParameter()
    {
        var raised = new List<bool>();
        var cut = RenderEditor(p => p.Add(c => c.ReadOnlyChanged,
            EventCallback.Factory.Create<bool>(this, v => raised.Add(v))));

        MoreEntry(cut, "Read-only").Click();
        cut.WaitForAssertion(() => raised.Should().Equal(true));

        cut.Instance.ReadOnly.Should().BeFalse("the [Parameter] is never assigned by the component");
        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.ReadOnly.Should().BeTrue();

        MoreEntry(cut, "Read-only").Click();
        cut.WaitForAssertion(() => raised.Should().Equal(true, false));
        cut.Instance.ReadOnly.Should().BeFalse();
        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void ReadOnlyToggle_PropagatesToInspectorLayersAndContextMenu()
    {
        var cut = RenderEditor();

        MoreEntry(cut, "Read-only").Click();
        cut.WaitForAssertion(() => cut.FindComponent<TmWireframePropertiesPanel>().Instance.ReadOnly.Should().BeTrue());

        // The Layers tab mounts lazily: open it, then it follows the editor state too.
        cut.FindAll("[data-region='left'] [role='tab']").First(t => t.TextContent.Trim() == "Layers").Click();
        cut.FindComponent<TmWireframeLayersPanel>().Instance.ReadOnly.Should().BeTrue();
        cut.FindComponent<TmWireframeContextMenu>().Instance.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void HostReadOnlyTrue_CannotBeUnlockedByToggle()
    {
        var cut = RenderEditor(p => p.Add(c => c.ReadOnly, true));

        OpenMore(cut);
        cut.FindAll("[role='menuitemcheckbox']").Select(i => i.TextContent.Trim()).Should().NotContain("Read-only", "a host-pinned read-only editor has no toggle");
        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.ReadOnly.Should().BeTrue();
        cut.FindAll("[data-region='left'] [role='tab']").First(t => t.TextContent.Trim() == "Layers").Click();
        cut.FindComponent<TmWireframeLayersPanel>().Instance.ReadOnly.Should().BeTrue();
        cut.Instance.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void HostReadOnlyTrue_DisablesImportJson_OnTheBarAndInTheMoreMenu()
    {
        // Import replaces the whole document, so it is an editing path: the file button, its hidden input and its
        // "More" entry are all disabled while the editor is read-only.
        var cut = RenderEditor(p => p.Add(c => c.ReadOnly, true));

        cut.Find("[data-testid='wd-import']").HasAttribute("disabled").Should().BeTrue("the bar button is disabled");
        cut.Find("[data-testid='wd-import-input']").HasAttribute("disabled").Should().BeTrue("a disabled input can never open a chooser");

        // Force the file button into the "More" menu (the real fit is measured in the browser).
        cut.InvokeAsync(() => cut.FindComponent<Tempo.Blazor.Components.Toolbar.TmToolbar>().Instance.OnFitChanged(0));
        OpenMore(cut);
        cut.WaitForAssertion(() => cut.FindAll("[role='menu'] [role='menuitem']")
            .First(i => i.TextContent.Trim().StartsWith("Import JSON")).HasAttribute("disabled").Should().BeTrue());
    }

    [Fact]
    public void WritableEditor_KeepsImportJsonEnabled()
    {
        var cut = RenderEditor();

        cut.Find("[data-testid='wd-import']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='wd-import-input']").HasAttribute("disabled").Should().BeFalse();
    }
    [Fact]
    public void BoundReadOnly_KeepsToggle_AndFollowsHostValue()
    {
        // @bind-ReadOnly: the host accepts the change, so the toggle stays usable in both directions.
        var cut = RenderEditor(p => p.Add(c => c.ReadOnlyChanged,
            EventCallback.Factory.Create<bool>(this, _ => { })));

        cut.Render(p => p.Add(c => c.ReadOnly, true));

        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.ReadOnly.Should().BeTrue();
        OpenMore(cut);
        cut.FindAll("[role='menuitemcheckbox'][aria-checked='true']").Select(i => i.TextContent.Trim()).Should().Contain("Read-only");
    }

    [Fact]
    public async Task ReadOnly_BlocksToolboxInsertion()
    {
        var cut = RenderEditor(p => p.Add(c => c.ReadOnly, true));
        var doc = cut.FindComponent<TmWireframeDesignerCanvas>().Instance.Document!;

        cut.Find(".tm-wd-toolbox__item").Click();
        await Task.Yield();

        doc.Elements.Should().BeEmpty();
    }

    [Fact]
    public void ReadOnlyToggle_SyncsTheJsCanvas_SoDragIsBlockedClientSide()
    {
        var cut = RenderEditor();

        MoreEntry(cut, "Read-only").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Where(i => i.Identifier == "tmWireframeDesigner.setReadOnly")
                .Select(i => i.Arguments[1]).Should().Equal(true));

        MoreEntry(cut, "Read-only").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Where(i => i.Identifier == "tmWireframeDesigner.setReadOnly")
                .Select(i => i.Arguments[1]).Should().Equal(true, false));
    }
    // ── Rulers toggle ──────────────────────────────────────────────────────────

    [Fact]
    public void RulersToggle_RaisesShowRulersChanged_DoesNotMutateParameter()
    {
        var raised = new List<bool>();
        var cut = RenderEditor(p => p.Add(c => c.ShowRulersChanged,
            EventCallback.Factory.Create<bool>(this, v => raised.Add(v))));
        cut.FindAll(".tm-wd-ruler").Should().BeEmpty();

        MoreEntry(cut, "Show rulers").Click();
        cut.WaitForAssertion(() => raised.Should().Equal(true));

        cut.Instance.ShowRulers.Should().BeFalse("the [Parameter] is never assigned by the component");
        cut.FindAll(".tm-wd-ruler").Should().HaveCount(2);

        MoreEntry(cut, "Show rulers").Click();
        cut.WaitForAssertion(() => raised.Should().Equal(true, false));
        cut.FindAll(".tm-wd-ruler").Should().BeEmpty();
    }

    [Fact]
    public void ShowRulersParameter_SeedsAndFollowsHost()
    {
        var cut = RenderEditor(p => p.Add(c => c.ShowRulers, true));
        cut.FindAll(".tm-wd-ruler").Should().HaveCount(2);

        cut.Render(p => p.Add(c => c.ShowRulers, false));
        cut.FindAll(".tm-wd-ruler").Should().BeEmpty();
    }

    // ── Toolbox activation → viewport centre ───────────────────────────────────

    [Fact]
    public void ToolboxActivation_AddsElementAtViewportCenter()
    {
        var doc = NewDocument();
        var cut = RenderEditor(p => p.Add(c => c.GridSize, 0), doc);
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>();

        // JS reports the visible area: x 200..1000, y 100..700 → centre (600, 400).
        cut.InvokeAsync(() => canvas.Instance.OnViewBoxChanged(200, 100, 800, 600)).GetAwaiter().GetResult();

        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']")
           .KeyDown(new KeyboardEventArgs { Key = "Enter" });

        var el = doc.Elements.Should().ContainSingle().Subject;
        el.Type.Should().Be("TmButton");
        // The element is centred on the viewport centre (120×36 default size).
        el.X.Should().BeApproximately(600 - 60, 0.01);
        el.Y.Should().BeApproximately(400 - 18, 0.01);
    }

    [Fact]
    public void ToolboxClick_AddsElementAtViewportCenter_SnappedToGrid()
    {
        var doc = NewDocument();
        var cut = RenderEditor(p => p.Add(c => c.GridSize, 8), doc);
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>();
        cut.InvokeAsync(() => canvas.Instance.OnViewBoxChanged(203, 101, 800, 600)).GetAwaiter().GetResult();

        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        var el = doc.Elements.Should().ContainSingle().Subject;
        (el.X % 8).Should().Be(0);
        (el.Y % 8).Should().Be(0);
        // Snapping moves the origin by at most half a cell.
        el.X.Should().BeInRange(603 - 60 - 4, 603 - 60 + 4);
        el.Y.Should().BeInRange(401 - 18 - 4, 401 - 18 + 4);
    }

    [Fact]
    public void ToolboxActivation_BeforeAnyViewBoxReport_UsesInitialViewBoxCentre()
    {
        var doc = NewDocument();
        var cut = RenderEditor(p => p.Add(c => c.GridSize, 0), doc);

        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        var el = doc.Elements.Should().ContainSingle().Subject;
        // Initial editor view box is 0 0 1200 800 → centre (600, 400).
        el.X.Should().BeApproximately(600 - 60, 0.01);
        el.Y.Should().BeApproximately(400 - 18, 0.01);
    }

    [Fact]
    public void ToolboxActivation_SelectsTheNewElement_AndSupportsUndo()
    {
        var doc = NewDocument();
        var cut = RenderEditor(null, doc);

        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']").Click();
        doc.Elements.Should().ContainSingle();

        cut.Find(".tm-toolbar button.tm-toolbar-btn[aria-label^='Undo']").Click();

        doc.Elements.Should().BeEmpty();
    }

    // ── Phase 2: TmIcon instead of emoji / unicode glyphs ──────────────────────

    private static readonly string[] EmojiGlyphs =
    [
        "↖", "✋", "↩", "↪", "🔒", "📁", "📂", "🧲", "📏", "🎨", "🖌", "📐", "◧", "◨",
        "→", "⬇", "⬆", "⬅", "➡", "⇹", "⇔", "↔", "↕", "✏", "✓", "✕", "▾", "◻"
    ];

    [Fact]
    public void Toolbar_RendersTmIcons_NotEmoji()
    {
        var doc = NewDocument();
        var cut = RenderEditor(null, doc);

        var buttons = cut.FindAll(".tm-wd-editor__toolbar .tm-toolbar-btn");
        buttons.Should().NotBeEmpty();

        foreach (var button in buttons)
        {
            foreach (var glyph in EmojiGlyphs)
                button.TextContent.Should().NotContain(glyph, $"'{button.GetAttribute("aria-label")}' must render a TmIcon, not a glyph");
        }

        // Every icon-only button of the toolbar row carries an svg icon (and keeps its accessible name).
        var iconOnly = cut.FindAll(".tm-wd-editor__toolbar .tm-toolbar-btn--icon-only");
        iconOnly.Should().NotBeEmpty();
        foreach (var button in iconOnly)
        {
            button.QuerySelector("svg.tm-icon").Should().NotBeNull($"'{button.GetAttribute("aria-label")}' is icon-only");
            (button.GetAttribute("aria-label") ?? button.GetAttribute("title")).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Toolbar_ZoomAndPanelToggles_AreIconButtons()
    {
        var cut = RenderEditor();

        cut.Find("button[aria-label='Zoom out'] svg.tm-icon").Should().NotBeNull();
        cut.Find("button[aria-label='Zoom in'] svg.tm-icon").Should().NotBeNull();
        OpenMore(cut);
        cut.FindAll("[role='menu'] [role='menuitemcheckbox']").Select(i => i.TextContent.Trim())
            .Should().Contain(["Components panel", "Properties panel"]);
    }

    [Fact]
    public void PropertiesPanelArrangeButtons_RenderTmIcons_NotArrowGlyphs()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(MakeDef("TmButton"));
        Services.AddSingleton(registry);

        var doc = NewDocument();
        for (var i = 0; i < 3; i++)
            doc.Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", i * 200, 0));
        var ids = doc.Elements.Select(e => e.Id).ToArray();

        var cut = Render<TmWireframePropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, ids));

        var panelButtons = cut.FindAll(".tm-wd-props .tm-wd-editor__btn--icon");
        panelButtons.Count.Should().BeGreaterThanOrEqualTo(6, "six alignment buttons render for a multi selection");
        foreach (var button in panelButtons)
        {
            button.QuerySelector("svg.tm-icon").Should().NotBeNull();
            (button.GetAttribute("aria-label") ?? "").Should().NotBeNullOrWhiteSpace("an icon-only button needs an accessible name");
            foreach (var glyph in EmojiGlyphs)
                button.TextContent.Should().NotContain(glyph);
        }

        cut.Find(".tm-wd-props__empty-icon, .tm-wd-props").Should().NotBeNull();
    }
    // ── Phase 2: insertion centre is clamped into the page ─────────────────────

    [Fact]
    public void ToolboxActivation_ClampsTheOriginInsideThePage()
    {
        var doc = NewDocument();   // page 1200 x 800
        var cut = RenderEditor(p => p.Add(c => c.GridSize, 0), doc);
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>();

        // The user panned far right/below the page: the viewport centre lies outside the page.
        cut.InvokeAsync(() => canvas.Instance.OnViewBoxChanged(2000, 1500, 800, 600)).GetAwaiter().GetResult();
        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        var el = doc.Elements.Should().ContainSingle().Subject;
        el.X.Should().Be(1200 - 120, "the 120 px wide element pins to the right page edge (viewport centre 2400 clamps to 1080)");
        el.Y.Should().Be(800 - 36, "the 36 px high element pins to the bottom page edge (viewport centre 1800 clamps to 764)");
        el.X.Should().BeGreaterThanOrEqualTo(0);
        el.Y.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ToolboxActivation_NegativeViewportCentre_ClampsToZero()
    {
        var doc = NewDocument();
        var cut = RenderEditor(p => p.Add(c => c.GridSize, 0), doc);
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>();

        cut.InvokeAsync(() => canvas.Instance.OnViewBoxChanged(-900, -700, 400, 300)).GetAwaiter().GetResult();
        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        var el = doc.Elements.Should().ContainSingle().Subject;
        el.X.Should().Be(0);
        el.Y.Should().Be(0);
    }

    [Fact]
    public void ToolboxActivation_GridSizeZero_DoesNotSnap()
    {
        var doc = NewDocument();
        var cut = RenderEditor(p => p.Add(c => c.GridSize, 0), doc);
        var canvas = cut.FindComponent<TmWireframeDesignerCanvas>();
        cut.InvokeAsync(() => canvas.Instance.OnViewBoxChanged(203, 101, 800, 600)).GetAwaiter().GetResult();

        cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']").Click();

        var el = doc.Elements.Should().ContainSingle().Subject;
        el.X.Should().BeApproximately(603 - 60, 0.001, "GridSize=0 disables snapping, so the centre is exact");
        el.Y.Should().BeApproximately(401 - 18, 0.001);
    }

    // ── Phase 2: the toolbox follows the editor read-only state ────────────────

    [Fact]
    public void ReadOnlyEditor_ToolboxItemsAreAriaDisabled()
    {
        var cut = RenderEditor(p => p.Add(c => c.ReadOnly, true));

        var item = cut.Find(".tm-wd-toolbox__item[data-component-type='TmButton']");
        item.GetAttribute("aria-disabled").Should().Be("true");
        item.ClassList.Should().Contain("tm-wd-toolbox__item--disabled");
        item.GetAttribute("draggable").Should().Be("false");
    }
}
