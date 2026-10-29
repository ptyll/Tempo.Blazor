using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingDiagramPreviewTests : LocalizationTestBase
{
    public TmModelingDiagramPreviewTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public void Non_empty_document_renders_diagram_editor()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument(nodeCount: 2)));

        cut.Find("[data-testid='modeling-diagram-preview']").GetAttribute("data-state").Should().Be("populated");
        cut.Find("[data-testid='diagram-editor']").Should().NotBeNull();
        cut.FindAll(".tm-diagram-node").Should().HaveCount(2);
    }

    [Fact]
    public void Null_document_renders_pre_generation_empty_state()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, (DiagramDocument?)null));

        cut.Find("[data-testid='modeling-diagram-preview']").GetAttribute("data-state").Should().Be("empty");
        cut.Find("[data-testid='modeling-diagram-preview-empty']").TextContent.Should().Contain("No diagram generated");
        cut.FindAll("[data-testid='diagram-editor']").Should().BeEmpty();
    }

    [Fact]
    public void Open_in_editor_invokes_callback_with_document()
    {
        var document = CreateDocument(nodeCount: 1);
        DiagramDocument? openedDocument = null;

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.OnOpenInEditor, EventCallback.Factory.Create<DiagramDocument>(this, value => openedDocument = value)));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        openedDocument.Should().BeSameAs(document);
    }

    [Fact]
    public void Zero_node_document_renders_empty_canvas_hint_without_loading_state()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument(nodeCount: 0)));

        cut.Find("[data-testid='modeling-diagram-preview']").GetAttribute("data-state").Should().Be("empty-diagram");
        cut.Find("[data-testid='diagram-editor']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-diagram-preview-empty-diagram-hint']").TextContent.Should().Contain("generated diagram is empty");
        cut.FindAll("[data-testid='modeling-editor-loading']").Should().BeEmpty();
    }

    [Fact]
    public void Drop_on_canvas_adds_reused_modeling_node_with_source_identity()
    {
        var document = CreateDocument(nodeCount: 1);
        var element = CreateElement("task-drop", "source/task-drop");
        ModelingNodeDroppedEventArgs? dropped = null;

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.ActiveDraggedElement, element)
            .Add(p => p.OnNodeDropped, EventCallback.Factory.Create<ModelingNodeDroppedEventArgs>(this, value => dropped = value)));

        cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']")
            .TriggerEvent("ondrop", new DragEventArgs { OffsetX = 420, OffsetY = 320 });

        document.Nodes.Should().HaveCount(2);
        var node = document.Nodes.Single(item => item.Data.TryGetValue("modelElementId", out var value) && value?.ToString() == "task-drop");
        node.Data["sourceId"].Should().Be("source/task-drop");
        dropped.Should().NotBeNull();
        dropped!.Element.Id.Should().Be("task-drop");
        dropped.Point.X.Should().Be(node.X);
        dropped.NodeId.Should().Be(node.Id);
    }

    [Fact]
    public void Dropping_same_element_twice_creates_two_node_occurrences_with_same_source_id()
    {
        var document = CreateDocument(nodeCount: 0);
        var element = CreateElement("task-reuse", "source/task-reuse");

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.ActiveDraggedElement, element));

        var canvas = cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']");
        canvas.TriggerEvent("ondrop", new DragEventArgs { OffsetX = 200, OffsetY = 220 });
        canvas.TriggerEvent("ondrop", new DragEventArgs { OffsetX = 420, OffsetY = 220 });

        document.Nodes.Should().HaveCount(2);
        document.Nodes.Should().OnlyContain(node => node.Data["modelElementId"].ToString() == "task-reuse");
        document.Nodes.Select(node => node.Data["sourceId"].ToString()).Should().OnlyContain(value => value == "source/task-reuse");
    }

    [Fact]
    public void Drop_existing_canvas_element_adds_second_occurrence_instead_of_model_tree_item()
    {
        var document = CreateDocument(nodeCount: 1);
        var element = CreateElement("element-0", "source/element-0");

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.ActiveDraggedElement, element));

        cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']")
            .TriggerEvent("ondrop", new DragEventArgs { OffsetX = 120, OffsetY = 140 });

        document.Nodes.Should().HaveCount(2);
        document.Nodes.Count(node => node.Data["modelElementId"].ToString() == "element-0").Should().Be(2);
        document.Nodes.Select(node => node.Data["sourceId"].ToString()).Should().OnlyContain(value => value == "source/element-0");
        document.Nodes.Select(node => node.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Drop_outside_canvas_coordinates_is_ignored()
    {
        var document = CreateDocument(nodeCount: 1);

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.ActiveDraggedElement, CreateElement("task-drop", "source/task-drop")));

        cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']")
            .TriggerEvent("ondrop", new DragEventArgs { OffsetX = -1, OffsetY = 20 });

        document.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public void Drop_when_node_drop_is_disabled_is_ignored_without_error()
    {
        var document = CreateDocument(nodeCount: 1);

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.ActiveDraggedElement, CreateElement("task-drop", "source/task-drop"))
            .Add(p => p.AllowNodeDrop, false));

        cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']")
            .TriggerEvent("ondrop", new DragEventArgs { OffsetX = 260, OffsetY = 260 });

        document.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public void Drop_hint_and_drop_zone_render_only_while_dragging()
    {
        // Review fix (phase 5): the hint row and the dashed drop-zone border are a WHILE-DRAGGING
        // affordance — a permanent hint ate a slice of the canvas column at every viewport.
        var document = CreateDocument(nodeCount: 1);
        var element = CreateElement("task-drop", "source/task-drop");

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document));

        cut.FindAll("[data-testid='modeling-diagram-preview-drop-hint']").Should().BeEmpty("no hint without a live drag");
        cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']").GetAttribute("data-dragging").Should().Be("false");

        cut.Render(parameters => parameters.Add(p => p.ActiveDraggedElement, element));

        cut.Find("[data-testid='modeling-diagram-preview-drop-hint']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']").GetAttribute("data-dragging").Should().Be("true");

        var css = File.ReadAllText(FindPreviewCssPath());
        css.Should().Contain(".tm-modeling-diagram-preview__canvas-shell[data-drop-enabled=\"true\"][data-dragging=\"true\"]",
            "the dashed drop-zone border only paints while dragging");

        cut.Render(parameters => parameters.Add(p => p.ActiveDraggedElement, (ModelingElementDto?)null));
        cut.FindAll("[data-testid='modeling-diagram-preview-drop-hint']").Should().BeEmpty("the hint leaves with the drag");
        cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']").GetAttribute("data-dragging").Should().Be("false");
    }

    [Fact]
    public void Preview_uses_viewer_toolbar_preset()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument(nodeCount: 2)));

        // Phase 4 scattered the groups across the editor regions (search/view in the header,
        // zoom in the status bar), so the check is editor-scoped, not toolbar-scoped.
        var groupNames = cut.FindAll("[data-testid='diagram-editor'] [data-tm-group]")
            .Select(g => g.GetAttribute("data-tm-group")!)
            .ToList();

        // The preview is a read-only viewer surface keeping the ZOOM group only (final UX
        // review M1): the search + "⋯" header-bar row of the wider viewer set is gone with its
        // groups — the DE renders no empty header bar — and the status bar keeps the zoom
        // controls. The export fold-in had already cost a second row (phase 5).
        groupNames.Should().BeEquivalentTo(["zoom"]);
        groupNames.Should().NotContain("search", "the search toggle's header-bar row is dropped with the group");
        groupNames.Should().NotContain("view", "the view settings live in the header bar the preview drops");
        groupNames.Should().NotContain("export", "the preview drops the export group through ToolbarGroups");
        groupNames.Should().NotContain("import", "the preview must not offer imports");
        groupNames.Should().NotContain("templates", "the preview must not offer templates");
        groupNames.Should().NotContain("readonly", "a host-pinned read-only preview cannot be unlocked");

        // The viewer zoom readout stays reachable by the Modeling E2E selector.
        cut.Find(".tm-diagram-editor__zoom-label").Should().NotBeNull();
    }

    [Fact]
    public void Preview_header_summary_is_opt_out_through_ShowSummary()
    {
        // Final UX review (M1): the docked editor passes ShowSummary=false — the status bar
        // already carries the counts, and the subtitle row cost the canvas height.
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument(nodeCount: 2))
            .Add(p => p.ShowSummary, false));

        cut.Find(".tm-modeling-diagram-preview__title-group h3").Should().NotBeNull("the title stays");
        cut.FindAll(".tm-modeling-diagram-preview__title-group span").Should().BeEmpty(
            "the nodes/edges summary is opt-out");

        // Default true keeps the subtitle for other hosts.
        using var defaultCut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument(nodeCount: 2)));
        defaultCut.Find(".tm-modeling-diagram-preview__title-group span").Should().NotBeNull();
    }

    [Fact]
    public void Preview_hides_the_document_title_through_the_editor_api()
    {
        // F6: the preview hides the editor's document title with ShowTitle="false" — never
        // with CSS (Preview_has_no_positional_toolbar_css forbids __title there).
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument(nodeCount: 2)));

        cut.FindAll("[data-testid='diagram-editor'] .tm-diagram-editor__title").Should().BeEmpty(
            "the Modeling preview renders no document-title input");
        cut.Find(".tm-diagram-editor__zoom-label").Should().NotBeNull(
            "the viewer chrome (zoom readout) still renders");
    }

    [Fact]
    public void Open_in_editor_is_primary_and_generate_is_secondary()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument(nodeCount: 2))
            .Add(p => p.OnGenerateRequested, EventCallback.Factory.Create(this, () => { })));

        var generate = cut.FindComponents<TmButton>()
            .Single(button => button.Markup.Contains("modeling-generate-diagram-button"));
        var open = cut.FindComponents<TmButton>()
            .Single(button => button.Markup.Contains("modeling-open-in-editor-button"));

        open.Instance.Variant.Should().Be(ButtonVariant.Primary,
            "'Open in editor' is the preview header's primary action (MO3)");
        generate.Instance.Variant.Should().Be(ButtonVariant.Secondary,
            "'Generate diagram' is the secondary action (MO3)");
    }

    [Fact]
    public void Preview_header_wraps_instead_of_clipping()
    {
        var css = File.ReadAllText(FindPreviewCssPath());

        var headerRule = ExtractRule(css, ".tm-modeling-diagram-preview__header");
        headerRule.Should().Contain("flex-wrap: wrap",
            "the preview header wraps at narrow columns instead of clipping the actions (carry-forward from phase 2 review)");
        headerRule.Should().NotContain("nowrap");

        var titleRule = ExtractRule(css, ".tm-modeling-diagram-preview__title-group h3");
        titleRule.Should().NotContain("white-space: nowrap",
            "the preview title wraps instead of truncating to 'Dia…' at 1024");
        titleRule.Should().NotContain("text-overflow: ellipsis");
    }

    [Fact]
    public void Preview_has_no_positional_toolbar_css()
    {
        var css = File.ReadAllText(FindPreviewCssPath());

        css.Should().NotContain("nth-of-type",
            "positional selectors break as soon as the editor toolbar is regrouped");
        css.Should().NotContain("__toolbar-row",
            "Modeling must not style the internal toolbar rows of the diagram editor");
        css.Should().NotContain("insert-dropdown-wrapper",
            "Modeling must not hide internal editor controls by class");
        css.Should().NotContain("tm-diagram-editor__title",
            "title visibility must come from the editor API, not from Modeling CSS");
        css.Should().NotContain("toolbar-spacer",
            "spacer visibility must come from the editor API, not from Modeling CSS");
        css.Should().NotContain("tm-toolbar-btn--active",
            "Modeling must not restyle internal editor toolbar button states");
        css.Should().NotContain("tm-diagram-editor__",
            "Modeling must not style ANY diagram editor internal (extended from DiagramEditor Fáze 1 — the toolbar contract is data-tm-group + presets, never CSS)");
        css.Should().NotContain("data-tm-group",
            "the frozen group attribute is a host/E2E hook, never a Modeling CSS selector");
    }

    // ── Viewport-center insert (Fáze 6 review fix) ───────────────────────────

    [Fact]
    public async Task Insert_at_viewport_center_uses_the_embedded_editor_viewport()
    {
        var document = CreateDocument(nodeCount: 1);
        ModelingNodeDroppedEventArgs? dropped = null;
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.OnNodeDropped, EventCallback.Factory.Create<ModelingNodeDroppedEventArgs>(this, value => dropped = value)));

        // The embedded canvas reports its viewport (the JS engine's contract, driven here):
        // visible window x=100..500, y=200..500 in document space → center (300, 350).
        var canvas = cut.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramCanvas>().Single();
        await cut.InvokeAsync(() => canvas.Instance.OnViewportChanged(100, 200, 400, 300));

        await cut.InvokeAsync(() => cut.Instance.InsertAtViewportCenterAsync(CreateElement("task-vp", "source/task-vp")));

        dropped.Should().NotBeNull("the insert routes through the OnNodeDropped contract");
        // The center (300, 350) minus the default template's half-size (80, 44) — no clamp on
        // the 3000x2000 page and no collision with the node at 120..260/140..212.
        dropped!.Point.X.Should().Be(300 - 80);
        dropped.Point.Y.Should().Be(350 - 44);
    }

    [Fact]
    public async Task Insert_at_viewport_center_falls_back_to_the_page_center_without_a_viewport()
    {
        var document = CreateDocument(nodeCount: 0);
        ModelingNodeDroppedEventArgs? dropped = null;
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.OnNodeDropped, EventCallback.Factory.Create<ModelingNodeDroppedEventArgs>(this, value => dropped = value)));

        // No OnViewportChanged: the JS engine has not reported anything (bUnit/SSR) — the
        // insert lands at the page center (the pre-review fallback).
        await cut.InvokeAsync(() => cut.Instance.InsertAtViewportCenterAsync(CreateElement("task-vp", "source/task-vp")));

        dropped.Should().NotBeNull();
        dropped!.Point.X.Should().Be(1500 - 80, "the 3000px page's center minus the template half-width");
        dropped.Point.Y.Should().Be(1000 - 44, "the 2000px page's center minus the template half-height");
    }

    [Fact]
    public void Preview_is_its_own_container_and_floors_the_canvas_on_the_tablet_band()
    {
        var css = File.ReadAllText(FindPreviewCssPath()).Replace("\r\n", "\n", StringComparison.Ordinal);

        // Fáze 6 review: a STANDALONE preview (a host embedding just the preview) resolves its
        // own header stacking from its own container; the editor-container query stays for the
        // embedded case.
        css.Should().Contain("container: tm-modeling-diagram-preview / inline-size");
        css.Should().Contain("@container tm-modeling-diagram-preview (width < 640px)");
        css.Should().MatchRegex(
            @"@container tm-modeling-diagram-preview \(width < 640px\) \{[^}]*flex-direction: column;",
            "the standalone fallback stacks the header like the editor container does");

        // The tablet canvas floor lives on the embedded editor's block size (the real class is
        // the editor root the preview renders — never an editor-internal selector).
        css.Should().Contain("[data-layout='tablet'] ::deep .tm-modeling-diagram-preview__editor");
        css.Should().Contain("min-block-size: calc(var(--tm-space-16) * 5)");
    }

    /// <summary>Returns the body of the first CSS rule whose selector line starts with <paramref name="selector"/>.</summary>
    private static string ExtractRule(string css, string selector)
    {
        var index = css.IndexOf(selector, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet must style {selector}");
        var open = css.IndexOf('{', index);
        var close = css.IndexOf('}', open);
        return css[(open + 1)..close];
    }

    private static string FindPreviewCssPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent!;
        }

        directory.Should().NotBeNull("repo root with TempoBlazor.slnx must be reachable from the test output");
        return Path.Combine(
            directory!.FullName,
            "src", "Tempo.Blazor.Modeling", "Components", "Modeling", "TmModelingDiagramPreview.razor.css");
    }

    private static DiagramDocument CreateDocument(int nodeCount)
    {
        var page = new DiagramPage
        {
            Id = "preview-page",
            Name = "Preview",
            Layers =
            [
                new DiagramLayer
                {
                    Id = "default",
                    Name = "Default"
                }
            ]
        };

        for (var index = 0; index < nodeCount; index++)
        {
            page.Nodes.Add(new DiagramNode
            {
                Id = $"node-{index}",
                StencilId = "general.rectangle",
                X = 120 + index * 180,
                Y = 140,
                W = 140,
                H = 72,
                LayerId = "default",
                Data = new Dictionary<string, object>
                {
                    ["name"] = $"Node {index}",
                    ["modelElementId"] = $"element-{index}",
                    ["sourceId"] = $"source/element-{index}"
                }
            });
        }

        if (nodeCount > 1)
        {
            page.Edges.Add(new DiagramEdge
            {
                Id = "edge-0",
                SourceNodeId = "node-0",
                TargetNodeId = "node-1",
                ConnectorType = "association",
                Shape = "connector",
                LayerId = "default"
            });
        }

        return new DiagramDocument
        {
            Id = "preview-doc",
            Title = "Preview document",
            Pages = [page],
            ActivePageIndex = 0
        };
    }

    private static ModelingElementDto CreateElement(string id, string sourceId) => new()
    {
        Id = id,
        SourceId = sourceId,
        SourceType = "test-source",
        SourcePath = $"/Test/{id}",
        SemanticType = "userTask",
        Notation = "bpmn",
        Name = $"Element {id}"
    };

    [Fact]
    public void Mobile_preview_has_no_edit_actions_only_viewer_controls()
    {
        // Fáze 7 (G2): on a mobile layout the preview header carries NO edit actions — the
        // generate / open-in-editor entries live in the editor's action bar and header overflow,
        // and the drop affordances follow AllowNodeDrop=false.
        var document = CreateDocument(nodeCount: 1);
        var element = CreateElement("task-mobile", "source/task-mobile");

        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.ActiveDraggedElement, element)
            .Add(p => p.AllowNodeDrop, false)
            .Add(p => p.ShowEditActions, false));

        cut.FindAll("[data-testid='modeling-generate-diagram-button']").Should().BeEmpty(
            "generate is an editor-level action on mobile, not a preview header button");
        cut.FindAll("[data-testid='modeling-open-in-editor-button']").Should().BeEmpty(
            "open-in-editor is an editor-level action on mobile, not a preview header button");
        cut.FindAll("[data-testid='modeling-diagram-preview-drop-hint']").Should().BeEmpty(
            "no drop hint without the drag affordance");
        cut.Find("[data-testid='modeling-diagram-preview']").GetAttribute("data-drop-enabled")
            .Should().Be("false");
        // The viewer controls stay: the embedded editor keeps its toolbar.
        cut.Find("[data-testid='diagram-editor']").Should().NotBeNull();
    }
}
