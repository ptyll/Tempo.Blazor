using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 6: the diff and heatmap overlays. The viewer restyles its presentation copy only; these
/// tests pin the states, the legend counts, the scale mapping, the non-colour cues and the text
/// equivalents (the picture is never the only carrier of the information).
/// </summary>
public sealed class TmModelingViewerOverlayTests : LocalizationTestBase
{
    public TmModelingViewerOverlayTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public void Diff_overlay_marks_added_changed_and_layout_nodes_and_hides_removed_ones()
    {
        using var cut = RenderDiff();

        cut.Find(".tm-diagram-node[data-model-element-id='e-notify']")
            .GetAttribute("data-viewer-state").Should().Be("diff-added");
        cut.Find(".tm-diagram-node[data-model-element-id='e-review']")
            .GetAttribute("data-viewer-state").Should().Be("diff-changed");
        cut.Find(".tm-diagram-node[data-model-element-id='e-end']")
            .GetAttribute("data-viewer-state").Should().Be("diff-layout");

        cut.FindAll(".tm-diagram-node[data-model-element-id='e-approve']").Should().BeEmpty(
            "a removed element no longer exists in the model, so it is never drawn");
    }

    [Fact]
    public void Diff_overlay_uses_dedicated_high_contrast_stroke_tokens()
    {
        using var cut = RenderDiff();

        var added = Node(cut, "e-notify");
        added.Style.Stroke.Should().Be("var(--tm-color-diff-added-stroke)");
        added.Style.StrokeWidth.Should().Be(3);
        added.Style.StrokeDasharray.Should().BeNull("an added item is a solid, heavier outline");

        var changed = Node(cut, "e-review");
        changed.Style.Stroke.Should().Be("var(--tm-color-diff-changed-stroke)");
        changed.Style.StrokeDasharray.Should().Be("2,2", "a changed item is finely dotted");

        var layout = Node(cut, "e-end");
        layout.Style.Stroke.Should().Be("var(--tm-color-diff-layout-stroke)");
        layout.Style.StrokeDasharray.Should().Be("6,3", "a layout-only move gets the long dash");
    }

    [Fact]
    public void The_three_diff_states_carry_three_distinct_non_colour_cues()
    {
        using var cut = RenderDiff();

        var dashes = new[]
        {
            Node(cut, "e-notify").Style.StrokeDasharray,
            Node(cut, "e-review").Style.StrokeDasharray,
            Node(cut, "e-end").Style.StrokeDasharray
        };
        dashes.Should().OnlyHaveUniqueItems("a reader who cannot separate the hues still separates the states");

        var badges = new[]
        {
            Node(cut, "e-notify").Data["viewerBadge"]?.ToString(),
            Node(cut, "e-review").Data["viewerBadge"]?.ToString(),
            Node(cut, "e-end").Data["viewerBadge"]?.ToString()
        };
        badges.Should().Equal("+", "~", "↔");
    }

    [Fact]
    public void Diff_legend_lists_every_state_with_its_count()
    {
        using var cut = RenderDiff();

        var legend = cut.Find("[data-testid='modeling-viewer-diff-legend']");
        legend.TextContent.Should().Contain("Version diff");

        legend.QuerySelector("[data-state='diff-added']")!.GetAttribute("data-count").Should().Be("3");
        legend.QuerySelector("[data-state='diff-removed']")!.GetAttribute("data-count").Should().Be("3");
        legend.QuerySelector("[data-state='diff-changed']")!.GetAttribute("data-count").Should().Be("1");
        legend.QuerySelector("[data-state='diff-layout']")!.GetAttribute("data-count").Should().Be("1");

        legend.QuerySelector("[data-state='diff-added']")!.TextContent.Should().Contain("Added",
            "the legend carries a word, not only a colour");
        legend.QuerySelector("[data-state='diff-layout']")!.TextContent.Should().Contain("Layout only");
    }

    [Fact]
    public void The_removed_legend_row_says_where_its_items_are()
    {
        using var cut = RenderDiff();

        var removed = cut.Find("[data-testid='modeling-viewer-diff-legend'] [data-state='diff-removed']");
        removed.TextContent.Should().Contain("listed below",
            "a removed item is never drawn, so the row must point at the list that names it");
    }

    [Fact]
    public void Removed_items_are_named_from_the_baseline_and_listed_in_a_removed_group()
    {
        using var cut = RenderDiff();

        var removed = cut.FindAll("[data-testid='modeling-viewer-element-list'] li[data-removed='true']");
        removed.Should().HaveCount(3, "one element and two relationships were removed");

        var names = removed.Select(row => row.TextContent).ToArray();
        names.Should().Contain(text => text.Contains("Approve order"));
        names.Should().Contain(text => text.Contains("Sequence flow: validate → approve"));
        names.Should().Contain(text => text.Contains("Sequence flow: approve → ship"));
        cut.Find("[data-testid='modeling-viewer-element-list']").TextContent.Should().Contain("Removed");
    }

    [Fact]
    public void Removed_items_fall_back_to_their_ids_without_a_baseline_model()
    {
        var overlay = ModelingViewerTestModels.CreateDiffOverlay();
        overlay.BaselineModel = null;

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, overlay)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var removed = cut.FindAll("[data-testid='modeling-viewer-element-list'] li[data-removed='true']");
        removed.Select(row => row.TextContent).Should().Contain(text => text.Contains("e-approve"));
    }

    [Fact]
    public void Collapsing_the_element_list_also_hides_the_removed_group()
    {
        using var cut = RenderDiff();

        cut.FindAll("[data-testid='modeling-viewer-element-list'] li[data-removed='true']").Should().NotBeEmpty();

        cut.Find("[data-testid='modeling-viewer-element-list-toggle']").Click();

        cut.FindAll("[data-testid='modeling-viewer-element-list'] li[data-removed='true']")
            .Should().BeEmpty("the removed group lives inside the collapsible body");
    }

    [Fact]
    public void Heatmap_tints_nodes_with_the_four_step_scale_tokens()
    {
        var heatmap = new ModelingHeatmapDto
        {
            Values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["e-start"] = 0.1,
                ["e-review"] = 0.3,
                ["e-approve"] = 0.6,
                ["e-end"] = 0.9
            }
        };

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, heatmap)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        Node(cut, "e-start").Style.Fill.Should().Be("var(--tm-color-heat-0)");
        Node(cut, "e-review").Style.Fill.Should().Be("var(--tm-color-heat-1)");
        Node(cut, "e-approve").Style.Fill.Should().Be("var(--tm-color-heat-2)");
        Node(cut, "e-end").Style.Fill.Should().Be("var(--tm-color-heat-3)");
        cut.Find(".tm-diagram-node[data-model-element-id='e-review']")
            .GetAttribute("data-viewer-state").Should().Be("heatmap");
    }

    [Fact]
    public void The_high_heat_band_flips_its_label_to_white_and_the_pale_bands_keep_the_paper_ink()
    {
        var heatmap = new ModelingHeatmapDto
        {
            Values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["e-start"] = 0.1,
                ["e-end"] = 0.9
            }
        };

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, heatmap)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        Node(cut, "e-start").Style.Color.Should().Be("var(--tm-color-heat-ink)");
        Node(cut, "e-end").Style.Color.Should().Be("var(--tm-color-heat-3-text)");
    }

    [Fact]
    public void Heatmap_legend_carries_the_scale_labels_and_the_selected_value()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.SelectedElementId, "e-review")
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var legend = cut.Find("[data-testid='modeling-viewer-heatmap-legend']");
        legend.TextContent.Should().Contain("SLA risk");
        legend.TextContent.Should().Contain("Low");
        legend.TextContent.Should().Contain("High");
        legend.TextContent.Should().Contain("Moderate");
        legend.TextContent.Should().Contain("Elevated");
        legend.TextContent.Should().Contain("80", "the selected element's value is a text equivalent");

        cut.Find("[data-testid='modeling-viewer-heatmap-selected']").TextContent.Should().Contain("SLA risk");
    }

    [Fact]
    public void Every_heat_step_swatch_carries_the_scale_class_the_stylesheet_answers_to()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll(".tm-modeling-viewer__scale-step").Should().HaveCount(8,
            "four gradient steps plus four scale swatches");
        for (var step = 0; step < 4; step++)
        {
            cut.FindAll($".tm-modeling-viewer__scale-step--tm-color-heat-{step}").Should().HaveCount(2,
                $"heat-{step} is one gradient step and one swatch — a class name cannot keep the token's dashes");
        }

        cut.FindAll(".tm-modeling-viewer__scale-step--").Should().BeEmpty(
            "the old interpolation produced scale-step----tm-color-heat-N and every swatch lost its fill");
    }

    [Fact]
    public void Heatmap_value_is_visible_and_available_as_a_tooltip_in_the_element_list()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var row = cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-review']");
        row.TextContent.Should().Contain("80");
        row.GetAttribute("title").Should().Contain("SLA risk").And.Contain("80").And.Contain("High");
    }

    [Fact]
    public void A_heat_only_row_renders_no_empty_state_chip()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-state='heatmap']").Should().BeEmpty(
            "a heat tint has no diff state word, and an empty chip rendered as a stray dash");
        cut.FindAll(".tm-modeling-viewer__element-state").Should().BeEmpty();
    }

    [Fact]
    public void The_canvas_node_and_edge_carry_the_overlay_reading_as_their_title()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var nodeTitle = cut.Find(".tm-diagram-node[data-model-element-id='e-review'] title");
        nodeTitle.TextContent.Should().Contain("SLA risk").And.Contain("80").And.Contain("High (75–100 %)");

        var edgeTitle = cut.Find(".tm-diagram-edge-group[data-edge-id='r1'] title");
        edgeTitle.TextContent.Should().Contain("SLA risk").And.Contain("40").And.Contain("Moderate (25–50 %)");
    }

    [Fact]
    public void A_diff_state_is_the_canvas_title_of_a_node_without_a_value()
    {
        using var cut = RenderDiff();

        cut.Find(".tm-diagram-node[data-model-element-id='e-review'] title").TextContent.Should().Be("Changed");
        cut.FindAll(".tm-diagram-node[data-model-element-id='e-start'] title").Should().BeEmpty(
            "an unmarked element has nothing to announce");
    }

    [Fact]
    public void Heatmap_without_a_value_for_the_selection_says_so()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.SelectedElementId, "e-approve")
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer-heatmap-selected']").TextContent
            .Should().Contain("Select an element to see its value.");
        Node(cut, "e-approve").Style.Fill.Should().BeNull(
            "an element without a value gets no tint");
    }

    [Fact]
    public void Diff_and_heatmap_combine_stroke_and_fill()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.Heatmap, new ModelingHeatmapDto
            {
                Values = new Dictionary<string, double>(StringComparer.Ordinal) { ["e-review"] = 0.8 }
            })
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var node = Node(cut, "e-review");
        node.Style.Fill.Should().Be("var(--tm-color-heat-3)");
        node.Style.Stroke.Should().Be("var(--tm-color-diff-changed-stroke)");
        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-overlay").Should().Be("diff-heatmap");
    }

    [Fact]
    public void Empty_overlay_collections_render_no_legend()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, new ModelingDiffOverlayDto())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer-diff-legend']").QuerySelectorAll("[data-count='0']")
            .Should().HaveCount(4, "a diff legend still describes the complete scale");
    }

    [Fact]
    public void A_pass_without_the_overlay_restores_the_generated_paint()
    {
        var document = new DiagramDocument();
        var page = new DiagramPage();
        var edge = new DiagramEdge { Id = "r1", Style = new DiagramStyle { Stroke = "#123456", StrokeWidth = 4 } };
        var node = new DiagramNode { Id = "n1", Style = new DiagramStyle { Fill = "#abcdef" } };
        node.Data["modelElementId"] = "e1";
        page.Nodes.Add(node);
        page.Edges.Add(edge);
        document.Pages.Add(page);

        var heatmap = new ModelingHeatmapDto
        {
            Values = new Dictionary<string, double>(StringComparer.Ordinal) { ["r1"] = 0.8, ["e1"] = 0.8 }
        };
        ModelingViewerProjection.ApplyPresentation(document, null, heatmap, null, null);
        edge.Style.Stroke.Should().Be("var(--tm-color-heat-3)");

        // Two passes without the overlay: the generator's own paint must come back, not null.
        ModelingViewerProjection.ApplyPresentation(document, null, null, null, null);
        ModelingViewerProjection.ApplyPresentation(document, null, null, null, null);

        edge.Style.Stroke.Should().Be("#123456", "the overlay restores the presentation copy's own style");
        edge.Style.StrokeWidth.Should().Be(4);
        node.Style.Fill.Should().Be("#abcdef");
    }

    [Fact]
    public void The_notation_legend_docks_into_the_legend_strip()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var strip = cut.Find("[data-testid='modeling-viewer-legends']");
        strip.QuerySelector("[data-testid='modeling-preview-legend']").Should().NotBeNull(
            "the notation card is one more docked card, never a floating pill over the canvas");
        cut.FindAll(".tm-modeling-viewer__notation-legend [data-testid='modeling-preview-legend']")
            .Should().ContainSingle();
    }

    [Fact]
    public void The_legend_strip_is_absent_when_it_has_no_card()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.ShowLegend, false)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll("[data-testid='modeling-viewer-legends']").Should().BeEmpty(
            "an empty strip would only eat canvas room");
        cut.FindAll("[data-testid='modeling-viewer-legends-toggle']").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_drops_the_notation_card_and_its_strip()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll("[data-testid='modeling-preview-legend']").Should().BeEmpty(
            "the element list already names every type on a phone");
        cut.FindAll("[data-testid='modeling-viewer-legends']").Should().BeEmpty();
    }

    [Fact]
    public void A_changed_relationship_gets_the_changed_stroke_and_counts_once()
    {
        var current = ModelingViewerTestModels.CreateProcessModel();
        current.Relationships.Find(relationship => relationship.Id == "r1")!.Name = "Reworded flow";
        var overlay = ModelingModelDiff.Compute(ModelingViewerTestModels.CreateProcessModel(), current);

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, current)
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, overlay)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var edge = Edge(cut, "r1");
        edge.Style.Stroke.Should().Be("var(--tm-color-diff-changed-stroke)");
        edge.Style.StrokeWidth.Should().Be(2.5, "an edge keeps its own dash pattern; the weight carries the cue");
        edge.Style.StrokeDasharray.Should().BeNull("the generator's edge dash is never overwritten");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-state='diff-changed']")
            .Should().ContainSingle("the relationship row is the edge's text equivalent");
        cut.Find("[data-testid='modeling-viewer-diff-legend'] [data-state='diff-changed']")
            .GetAttribute("data-count").Should().Be("1");
    }

    private IRenderedComponent<TmModelingViewer> RenderDiff()
        => Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

    private static DiagramEdge Edge(IRenderedComponent<TmModelingViewer> cut, string relationshipId)
        => cut.FindComponent<Tempo.Blazor.Components.Diagram.TmDiagramEditor>().Instance.Document!
            .Pages.SelectMany(page => page.Edges)
            .Single(edge => string.Equals(edge.Id, relationshipId, StringComparison.Ordinal));

    private static DiagramNode Node(IRenderedComponent<TmModelingViewer> cut, string elementId)
        => cut.FindComponent<Tempo.Blazor.Components.Diagram.TmDiagramEditor>().Instance.Document!
            .Pages.SelectMany(page => page.Nodes)
            .Single(node => node.Data.TryGetValue("modelElementId", out var id)
                && string.Equals(id?.ToString(), elementId, StringComparison.Ordinal));
}
