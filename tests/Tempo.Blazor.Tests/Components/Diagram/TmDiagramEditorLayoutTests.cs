using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramEditorLayoutTests : LocalizationTestBase
{
    public TmDiagramEditorLayoutTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LayoutDropdown_ContainsAllLayoutOptions()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });
        doc.Edges.Add(new DiagramEdge { SourceNodeId = doc.Nodes[0].Id, TargetNodeId = doc.Nodes[1].Id });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        // Open layout menu
        var layoutBtn = cut.FindAll("button").FirstOrDefault(b =>
            b.TextContent.Contains("Layout", StringComparison.OrdinalIgnoreCase) ||
            b.TextContent.Contains("Rozložit", StringComparison.OrdinalIgnoreCase));
        layoutBtn.Should().NotBeNull();
        layoutBtn!.Click();
        cut.Render();

        var menuItems = cut.FindAll(".tm-dropdown-item");
        var texts = menuItems.Select(m => m.TextContent).ToList();

        // Localized through MockTmLocalizer — a raw resource key must never reach the UI
        texts.Should().ContainEquivalentOf("Hierarchical Top-Down");
        texts.Should().ContainEquivalentOf("Hierarchical Left-Right");
        texts.Should().ContainEquivalentOf("Tree");
        texts.Should().ContainEquivalentOf("Tree Left-Right");
        texts.Should().ContainEquivalentOf("Force");
        texts.Should().ContainEquivalentOf("Circle");
        texts.Should().ContainEquivalentOf("Grid");
        texts.Should().NotContain(t => t.StartsWith('['), "every layout entry is localized");
    }

    [Fact]
    public async Task RunLayoutAsync_Dagre_InvokesRunDagreLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("dagre", "TB");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runDagreLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Tree_InvokesRunTreeLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("tree", "TB");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runTreeLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Force_InvokesRunForceLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("force");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runForceLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Circle_InvokesRunCircleLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("circle");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runCircleLayout");
        invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunLayoutAsync_Grid_InvokesRunGridLayout()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 300, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, false));

        await cut.Instance.SetSelection(doc.Nodes[0].Id, doc.Nodes[1].Id);
        await cut.Instance.RunLayoutAsync("grid");

        var invocations = JSInterop.Invocations.Where(i => i.Identifier == "tmDiagramEditor.runGridLayout");
        invocations.Should().ContainSingle();
    }

    // ── Phase 4 (desktop restructure) ─────────────────────────────────────────
    // The desktop reference (output/imagegen/tempo-reimagined/Tempo.Blazor.DiagramEditor/
    // diagram-editor--desktop.png): a header with the document title, NAMED panel toggles and
    // the "⋯" view-settings menu; a single command row built from the core TmToolbar (overflow
    // to "More"); a status bar with the zoom controls; an inspector with Properties | Layers
    // tabs (TmTabs) and a collapsible minimap card. TmEditorShell owns the slots.

    [Fact]
    public void Header_ContainsTitleAndPanelToggles()
    {
        using var cut = RenderDefaultEditor();

        var header = cut.Find(".tm-diagram-editor__header-bar");
        header.Should().NotBeNull("the header is a distinct region above the command row");

        // Title.
        header.QuerySelector(".tm-diagram-editor__title").Should().NotBeNull();

        // Named panel toggles: the search toggle carries its own group, the panel toggles the
        // panels group — with VISIBLE labels next to the icons (desktop mockup).
        var searchToggle = cut.Find(".tm-diagram-editor__header-bar [data-tm-group='search'] button");
        searchToggle.GetAttribute("title").Should().Be("Search");

        var panelsGroup = cut.Find(".tm-diagram-editor__header-bar [data-tm-group='panels']");
        foreach (var title in new[] { "Toggle toolbox", "Toggle inspector", "Toggle minimap" })
        {
            panelsGroup.QuerySelectorAll($"button[title='{title}']").Should().HaveCount(1, title);
        }

        // WCAG 2.5.3: the accessible name carries the visible label; the tooltip stays the action.
        foreach (var (title, label) in new[]
                 {
                     ("Toggle toolbox", "Tools"), ("Toggle inspector", "Inspector"), ("Toggle minimap", "Minimap"),
                 })
        {
            var toggle = panelsGroup.QuerySelector($"button[title='{title}']")!;
            toggle.GetAttribute("aria-label").Should().Be(label,
                "the aria-label repeats the visible label text, never the tooltip action");
        }

        foreach (var label in new[] { "Search", "Tools", "Inspector", "Minimap" })
        {
            header.TextContent.Should().Contain(label, "desktop panel toggles show visible labels");
        }
    }

    [Fact]
    public void HeaderSearchToggle_ReportsPressedWhileThePanelIsOpen()
    {
        using var cut = RenderDefaultEditor();

        var search = cut.Find(".tm-diagram-editor__header-bar [data-tm-group='search'] button");
        search.GetAttribute("aria-pressed").Should().Be("false", "the search panel starts closed");

        search.Click();
        cut.Find(".tm-diagram-editor__header-bar [data-tm-group='search'] button")
            .GetAttribute("aria-pressed").Should().Be("true", "opening the search panel presses the toggle");
    }

    [Fact]
    public void ShowToolbar_True_IsTheDefault()
    {
        using var cut = RenderDefaultEditor();

        cut.Find(".tm-diagram-editor__toolbar").Should().NotBeNull();
        cut.Find(".tm-diagram-editor__statusbar").Should().NotBeNull();
    }

    [Fact]
    public void ShowToolbar_False_HidesHeaderControlsCommandRowAndStatusBar()
    {
        // ShowToolbar=false keeps its pre-redesign meaning: everything that lived in the old
        // toolbar hides — the header's built-in controls, the command row and the status bar —
        // while the page tabs, the canvas, the docked panels and the minimap stay.
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowToolbar, false));

        cut.FindAll(".tm-diagram-editor__toolbar").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__statusbar").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__title").Should().BeEmpty();
        cut.FindAll("[data-testid='diagram-editor-more']").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__header-bar").Should().BeEmpty(
            "without built-in controls and without host slots no empty bar stays behind");
        cut.FindAll("[data-testid='diagram-editor'] [data-tm-group]").Should().BeEmpty(
            "no toolbar group of any region renders");

        cut.FindAll(".tm-diagram-editor__page-tab").Should().NotBeEmpty("page tabs never hide");
        cut.FindComponent<TmDiagramCanvas>().Should().NotBeNull("the canvas still renders");
        cut.Find(".tm-diagram-editor__minimap-card").Should().NotBeNull(
            "the docked panels and the minimap stay");
        cut.Find(".tm-diagram-editor__inspector").Should().NotBeNull();
    }

    [Fact]
    public void ShowToolbar_False_KeepsHostHeaderSlots()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowToolbar, false)
            .Add(e => e.HeaderActions, "<button class='host-save'>Save</button>"));

        cut.Find(".tm-diagram-editor__header-actions .host-save").Should().NotBeNull(
            "the host slots still render without the built-in chrome");
    }

    [Fact]
    public void HeaderStart_RendersBeforeTheTitle_AndHeaderActions_Last()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.HeaderStart, "<span class=\"hdr-start-sentinel\">Start</span>")
            .Add(e => e.HeaderActions, "<span class=\"hdr-actions-sentinel\">Actions</span>"));

        var header = cut.Find(".tm-diagram-editor__header-bar");
        header.QuerySelector(".hdr-start-sentinel").Should().NotBeNull("HeaderStart opens the header bar");
        header.QuerySelector(".hdr-actions-sentinel").Should().NotBeNull("HeaderActions close the header bar");

        var classes = header.Children.Select(c => c.ClassName ?? string.Empty).ToList();
        classes.FindIndex(c => c.Contains("tm-diagram-editor__header-start"))
            .Should().BeLessThan(classes.FindIndex(c => c.Contains("tm-diagram-editor__title")));
        classes.FindIndex(c => c.Contains("tm-diagram-editor__header-actions"))
            .Should().BeGreaterThan(classes.FindIndex(c => c.Contains("tm-diagram-editor__view-menu")),
                "HeaderActions render after the built-in header controls");
    }

    [Fact]
    public void CommandBar_RendersGroupsInNewSingleRowOrder()
    {
        using var cut = RenderDefaultEditor();

        var toolbar = cut.Find(".tm-diagram-editor__toolbar");
        toolbar.QuerySelector(".tm-toolbar").Should().NotBeNull(
            "the command row is a core TmToolbar (overflow capable, no flex-wrap)");

        var names = toolbar.QuerySelectorAll("[data-tm-group]")
            .Select(g => g.GetAttribute("data-tm-group")!)
            .ToList();

        names.Should().Equal(new[]
        {
            "history", "clipboard", "tools", "format", "insert", "arrange",
            "layout", "export", "import", "templates",
        });
    }

    [Fact]
    public void CommandBar_ShowsVisibleLabels_UnderIcons()
    {
        using var cut = RenderDefaultEditor();

        var toolbar = cut.Find(".tm-diagram-editor__toolbar");
        toolbar.TextContent.Should().Contain("Undo").And.Contain("Redo")
            .And.Contain("Cut").And.Contain("Copy").And.Contain("Paste")
            .And.Contain("Duplicate").And.Contain("Delete")
            .And.Contain("Select").And.Contain("Pan").And.Contain("Edge")
            .And.Contain("Copy style").And.Contain("Paste style").And.Contain("Paste Size")
            .And.Contain("Insert").And.Contain("Distribute")
            .And.Contain("Layout").And.Contain("Export").And.Contain("Import")
            .And.Contain("Templates");
    }

    [Fact]
    public void StatusBar_ContainsZoomControls()
    {
        using var cut = RenderDefaultEditor();

        var statusbar = cut.Find(".tm-diagram-editor__statusbar");
        var zoomGroup = statusbar.QuerySelector("[data-tm-group='zoom']");
        zoomGroup.Should().NotBeNull("zoom lives in the status bar (desktop mockup)");

        // The zoom-label selector is a Modeling E2E contract — it survives the move.
        zoomGroup!.QuerySelector(".tm-diagram-editor__zoom-label").Should().NotBeNull();
        zoomGroup.QuerySelector("button[aria-label='Zoom in']").Should().NotBeNull();
        zoomGroup.QuerySelector("button[aria-label='Zoom out']").Should().NotBeNull();
        zoomGroup.QuerySelector("button[aria-label='Fit to view']").Should().NotBeNull();
    }

    [Fact]
    public void StatusBar_ContainsReadOnlyToggle_AndGroupOffRemovesIt()
    {
        using var cut = RenderDefaultEditor();
        cut.Find(".tm-diagram-editor__statusbar [data-tm-group='readonly']")
            .Should().NotBeNull("the read-only toggle lives in the status bar in the new layout");

        using var withoutGroup = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ToolbarGroups, DiagramToolbarGroups.History));
        withoutGroup.FindAll(".tm-diagram-editor__statusbar [data-tm-group='readonly']").Should().BeEmpty();
    }

    [Fact]
    public void ShowStatusBar_False_HidesTheStatusBar()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowStatusBar, false));

        cut.FindAll(".tm-diagram-editor__statusbar").Should().BeEmpty();
    }

    [Fact]
    public void ShowTitle_False_HidesTheTitleInput()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowTitle, false));

        cut.FindAll(".tm-diagram-editor__title").Should().BeEmpty(
            "hosts hide the title through the editor API, never through CSS selectors");
    }

    [Fact]
    public void ViewSettings_InMoreMenu()
    {
        using var cut = RenderDefaultEditor();

        var more = cut.Find(".tm-diagram-editor__view-menu");
        var trigger = more.QuerySelector(".tm-diagram-editor__more-btn")!;
        trigger.Should().NotBeNull("the header carries the '⋯' view-settings trigger");
        trigger.GetAttribute("aria-expanded").Should().Be("false");

        // The groups live in the DOM even while the menu is closed (stable data-tm-group contract).
        var panel = more.QuerySelector(".tm-diagram-editor__view-menu-panel")!;
        panel.Should().NotBeNull();
        panel.GetAttribute("hidden").Should().NotBeNull("the menu starts closed");

        // The trigger points at the panel through a per-instance aria-controls id.
        trigger.GetAttribute("aria-controls").Should().Be(panel.GetAttribute("id"),
            "aria-controls replaces the bare aria-haspopup and names the panel it opens");
        trigger.GetAttribute("aria-label").Should().Be("View settings");

        var names = panel.QuerySelectorAll("[data-tm-group]")
            .Select(g => g.GetAttribute("data-tm-group")!)
            .ToList();
        names.Should().BeEquivalentTo(["view", "canvas-size"]);

        var viewGroup = panel.QuerySelector("[data-tm-group='view']")!;
        viewGroup.QuerySelectorAll("button").Should().HaveCount(4, "grid, snap, page view, rulers");
        viewGroup.TextContent.Should().Contain("Grid").And.Contain("Snap")
            .And.Contain("Page View").And.Contain("ruler");

        trigger.Click();
        cut.Find(".tm-diagram-editor__view-menu-panel")
            .GetAttribute("hidden").Should().BeNull("clicking ⋯ opens the view settings menu");
    }

    [Fact]
    public void Inspector_HasTabs_PropertiesAndLayers()
    {
        using var cut = RenderDefaultEditor();

        var inspector = cut.Find(".tm-diagram-editor__inspector");
        inspector.GetAttribute("data-inspector-layout").Should().Be("tabs");

        var tabs = inspector.QuerySelectorAll(".tm-tabs [role='tab']").Select(t => t.TextContent.Trim()).ToList();
        tabs.Should().Equal(["Properties", "Layers"]);

        // The active tab panel renders the properties panel.
        inspector.QuerySelector(".tm-tabs__panel .tm-diagram-properties").Should().NotBeNull();

        // Switching to the Layers tab renders the layers panel.
        inspector.QuerySelectorAll(".tm-tabs [role='tab']").Last().Click();
        inspector.QuerySelector(".tm-tabs__panel .tm-diagram-layers").Should().NotBeNull();
    }

    [Fact]
    public void Inspector_StackedOptIn_RendersBothPanels()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.InspectorLayout, DiagramInspectorLayout.Stacked));

        var inspector = cut.Find(".tm-diagram-editor__inspector");
        inspector.GetAttribute("data-inspector-layout").Should().Be("stacked");
        inspector.QuerySelectorAll(".tm-diagram-editor__props-wrap .tm-diagram-properties").Should().HaveCount(1);
        inspector.QuerySelectorAll(".tm-diagram-editor__layers-wrap .tm-diagram-layers").Should().HaveCount(1);
    }

    [Fact]
    public void LayersTab_MarksTheActiveLayer()
    {
        using var cut = RenderDefaultEditor();

        // Open the Layers tab of the inspector.
        var layersTab = cut.FindAll(".tm-diagram-editor__inspector [role='tab']")
            .FirstOrDefault(t => t.TextContent.Contains("Layers"));
        layersTab.Should().NotBeNull("the inspector presents Properties | Layers");
        layersTab!.Click();

        // F3: the ActiveLayerId binding used to receive the literal field name, so no layer was
        // ever marked active.
        cut.FindAll(".tm-diagram-layers__item--active").Should().ContainSingle(
            "exactly one layer row is the active one");
        cut.FindAll(".tm-diagram-layers__active[aria-checked='true']").Should().ContainSingle(
            "the active-layer radio reflects the same state");
    }

    [Fact]
    public void Header_HasNoSeparateLayersToggle()
    {
        // X3: the header presents ONE Inspector toggle — the Properties | Layers tabs inside
        // choose the content. The old per-panel toggles are gone (breaking). (Scoped to the
        // header: the panels themselves carry their own localized collapse toggles.)
        using var cut = RenderDefaultEditor();

        cut.FindAll(".tm-diagram-editor__header-bar button[title='Toggle layers']").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__header-bar button[title='Toggle properties panel']").Should().BeEmpty();
    }

    [Fact]
    public void InspectorToggle_HidesAndRestoresTheWholeInspector()
    {
        using var cut = RenderDefaultEditor();

        var toggle = cut.Find("[data-testid='diagram-editor-toggle-inspector']");
        toggle.GetAttribute("aria-pressed").Should().Be("true", "the inspector starts expanded");

        toggle.Click();
        cut.FindAll(".tm-diagram-editor__inspector").Should().BeEmpty(
            "the toggle hides the whole inspector");
        cut.Find("[data-testid='diagram-editor-toggle-inspector']")
            .GetAttribute("aria-pressed").Should().Be("false");

        cut.Find("[data-testid='diagram-editor-toggle-inspector']").Click();
        var inspector = cut.Find(".tm-diagram-editor__inspector");
        inspector.QuerySelectorAll("[role='tab']").Should().HaveCount(2,
            "restoring brings back both Properties | Layers tabs");
        cut.Find("[data-testid='diagram-editor-toggle-inspector']")
            .GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void InspectorToggle_OnlyLayersPanel_IsNamedLayers()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowPropertiesPanel, false));

        var toggle = cut.Find("[data-testid='diagram-editor-toggle-inspector']");
        toggle.TextContent.Trim().Should().Be("Layers",
            "a lone panel names the toggle after its content");
        toggle.QuerySelectorAll("svg").Should().NotBeEmpty(
            "the toggle carries the panel's icon");
    }

    [Fact]
    public void InspectorToggle_OnlyLayersPanel_AccessibleNameMatchesVisibleLabel()
    {
        // WCAG 2.5.3 (label in name): the visible label text must be part of the accessible
        // name — the tooltip ("Toggle inspector") stays in title/aria-description only.
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowPropertiesPanel, false));

        var toggle = cut.Find("[data-testid='diagram-editor-toggle-inspector']");
        toggle.GetAttribute("aria-label").Should().Be(toggle.TextContent.Trim(),
            "the aria-label repeats the visible Layers label");
        toggle.GetAttribute("aria-label").Should().NotBe(toggle.GetAttribute("title"),
            "the title keeps the Toggle-inspector tooltip wording");
    }

    [Fact]
    public void Inspector_ShowLayersPanelFalse_RendersLonePropertiesPanelWithoutTabs()
    {
        // F9: the Properties | Layers tab strip renders only when BOTH panels are available —
        // a lone panel renders alone, without a dead tab.
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowLayersPanel, false));

        cut.FindAll(".tm-diagram-editor__inspector [role='tab']").Should().BeEmpty();
        cut.Find(".tm-diagram-editor__props-wrap .tm-diagram-properties").Should().NotBeNull();
        cut.FindAll(".tm-diagram-layers").Should().BeEmpty();
    }

    [Fact]
    public void Inspector_ShowPropertiesPanelFalse_RendersLoneLayersPanel()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowPropertiesPanel, false));

        cut.FindAll(".tm-diagram-editor__inspector [role='tab']").Should().BeEmpty();
        cut.Find(".tm-diagram-editor__layers-wrap .tm-diagram-layers").Should().NotBeNull();
        cut.FindAll(".tm-diagram-properties").Should().BeEmpty();
    }

    [Fact]
    public void InspectorTitle_FollowsTheEffectiveTab()
    {
        // The dock's accessible name must reflect the tab actually shown (the effective one),
        // not the user's stale tab choice.
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowPropertiesPanel, false));

        cut.Find("aside.tm-editor-shell__panel--right")
            .GetAttribute("aria-label").Should().Be("Layers");
    }

    [Fact]
    public void Minimap_RendersAsCollapsibleCard()
    {
        using var cut = RenderDefaultEditor();

        var card = cut.Find(".tm-diagram-editor__minimap-card");
        card.QuerySelector(".tm-diagram-minimap").Should().NotBeNull();

        var collapse = card.QuerySelector(".tm-diagram-editor__minimap-card-toggle")!;
        collapse.GetAttribute("aria-expanded").Should().Be("true");
        collapse.Click();
        cut.FindAll(".tm-diagram-editor__minimap-card .tm-diagram-minimap").Should().BeEmpty(
            "the minimap card collapses to its header");
    }

    /// <summary>
    /// The phase-4 command checklist: every command the old two-row toolbar offered must stay
    /// reachable — in the command row, the header, the view-settings menu, the status bar or the
    /// context menu. bUnit never measures, so bar items render on the bar and the menu contents
    /// render (hidden) in the DOM; presence, not visibility, is asserted here.
    /// </summary>
    [Fact]
    public void CommandChecklist_AllCommandsReachable()
    {
        using var cut = RenderDefaultEditor();
        var root = cut.Find("[data-testid='diagram-editor']");

        string? AttributeFor(string selector, string attribute)
        {
            var element = root.QuerySelector(selector);
            return element?.GetAttribute(attribute);
        }

        // 1. Command row (history, clipboard, tools, format + dropdowns). The undo/redo tooltips
        //    carry the dynamic command name, so the aria-label matches by prefix.
        foreach (var aria in new[]
        {
            "Undo", "Redo", "Cut", "Copy", "Paste", "Duplicate", "Delete",
            "Select tool (V)", "Pan tool (H)",
            "Copy style (Ctrl+Shift+C)", "Paste Style", "Paste Size",
        })
        {
            root.QuerySelectorAll($"button[aria-label^='{aria}']").Should().NotBeEmpty(aria);
        }

        root.QuerySelectorAll("button[aria-label^='Edge tool']").Should().NotBeEmpty("Edge tool");

        foreach (var text in new[] { "Insert", "Distribute", "Layout", "Export", "Import", "Templates" })
        {
            root.QuerySelectorAll(".tm-diagram-editor__toolbar .tm-dropdown-trigger, .tm-diagram-editor__toolbar button")
                .Should().Contain(b => b.TextContent.Contains(text), text);
        }

        // 2. Header: search + named panel toggles.
        AttributeFor("[data-tm-group='search'] button", "title").Should().Be("Search");
        foreach (var title in new[] { "Toggle toolbox", "Toggle inspector", "Toggle minimap" })
        {
            root.QuerySelectorAll($"[data-tm-group='panels'] button[title='{title}']").Should().NotBeEmpty(title);
        }

        // 3. View settings menu: grid, snap, page view, rulers, ruler unit, canvas size.
        var viewGroup = root.QuerySelector("[data-tm-group='view']")!;
        viewGroup.QuerySelectorAll("button").Length.Should().BeGreaterThanOrEqualTo(4, "grid, snap, page view, rulers");
        AttributeFor("[data-tm-group='canvas-size'] select", "aria-label").Should().Be("Ruler unit");
        root.QuerySelector("[data-tm-group='canvas-size'] button").Should().NotBeNull("canvas size editor");

        // 4. Status bar: zoom controls + read-only toggle.
        root.QuerySelector("[data-tm-group='zoom'] .tm-diagram-editor__zoom-label").Should().NotBeNull();
        root.QuerySelector("[data-tm-group='readonly'] button").Should().NotBeNull();
    }

    private IRenderedComponent<TmDiagramEditor> RenderDefaultEditor() =>
        Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));
}
