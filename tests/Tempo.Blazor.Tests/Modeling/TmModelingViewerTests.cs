using Bunit;
using Microsoft.AspNetCore.Components;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 6 (MODELING-READONLY-VIEWER): the <c>TmModelingViewer</c> parameter/fit/selection contract.
/// The viewer projects with the shared generator and renders through the DiagramEditor in viewer
/// mode; these tests pin the public parameters and the projection, not the editor's internals.
/// </summary>
public sealed class TmModelingViewerTests : LocalizationTestBase
{
    public TmModelingViewerTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public void Viewer_renders_the_generated_document_read_only_without_editor_chrome()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var root = cut.Find("[data-testid='modeling-viewer']");
        root.GetAttribute("data-state").Should().Be("populated");
        root.GetAttribute("data-node-count").Should().Be("4");
        root.GetAttribute("data-edge-count").Should().Be("3");
        cut.FindAll(".tm-diagram-node").Should().HaveCount(4);

        var editor = cut.FindComponent<TmDiagramEditor>().Instance;
        editor.ReadOnly.Should().BeTrue();
        editor.ToolbarPreset.Should().Be(DiagramToolbarPreset.Viewer);
        editor.ToolbarGroups.Should().Be(DiagramToolbarGroups.Zoom);
        editor.ShowToolbox.Should().BeFalse();
        editor.ShowPropertiesPanel.Should().BeFalse();
        editor.ShowLayersPanel.Should().BeFalse();
        editor.ShowMinimap.Should().BeFalse();
        editor.ShowPageTabs.Should().BeFalse();
        editor.DocumentChanged.HasDelegate.Should().BeFalse("the viewer never writes to the host's model");
    }

    [Fact]
    public void View_id_projects_the_stored_positions()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var node = cut.Find(".tm-diagram-node[data-model-element-id='e-review']");
        node.GetAttribute("transform").Should().Contain("translate(340,100)");
    }

    [Theory]
    [InlineData(ModelingViewerFitMode.Width, DiagramFitMode.Width)]
    [InlineData(ModelingViewerFitMode.Page, DiagramFitMode.Page)]
    public void Fit_mode_maps_to_the_diagram_editor_fit(ModelingViewerFitMode fitMode, DiagramFitMode expected)
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.FitMode, fitMode)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindComponent<TmDiagramEditor>().Instance.FitOnDocumentLoad.Should().Be(expected);
        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-fit-mode")
            .Should().Be(fitMode == ModelingViewerFitMode.Width ? "width" : "page");
    }

    [Fact]
    public void Zoom_parameter_sets_the_presentation_page_scale()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.Zoom, 0.75)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindComponent<TmDiagramEditor>().Instance.Document!.ActivePage.Scale.Should().Be(0.75);
        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-zoom").Should().Be("0.75");
        cut.FindComponent<TmDiagramEditor>().Instance.FitOnDocumentLoad.Should().BeNull(
            "an explicit zoom wins over fit-on-load");
    }

    [Fact]
    public void Show_zoom_controls_false_hides_the_editor_toolbar()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ShowZoomControls, false)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindComponent<TmDiagramEditor>().Instance.ShowToolbar.Should().BeFalse();
    }

    [Fact]
    public void Highlight_ids_mark_the_presentation_state_without_selecting()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.HighlightElementIds, ["e-review"])
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var node = cut.Find(".tm-diagram-node[data-model-element-id='e-review']");
        node.GetAttribute("data-viewer-state").Should().Be("highlight");
        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-selected").Should().BeNull();
    }

    [Fact]
    public void Clicking_an_element_list_row_selects_and_raises_the_click_contract()
    {
        string? selected = null;
        ModelingViewerElementEventArgs? captured = null;

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.SelectedElementIdChanged, (string? value) => selected = value)
            .Add(p => p.OnElementClick, (ModelingViewerElementEventArgs args) => captured = args));

        cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-review']").Click();

        selected.Should().Be("e-review");
        captured.Should().NotBeNull();
        captured!.ElementId.Should().Be("e-review");
        captured.IsRelationship.Should().BeFalse();
        captured.SemanticType.Should().Be("userTask");
        captured.Name.Should().Be("Validate order");
        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-selected").Should().Be("e-review");
        cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-review']")
            .GetAttribute("aria-current").Should().Be("true");
    }

    [Fact]
    public void Clicking_a_relationship_row_reports_a_relationship()
    {
        ModelingViewerElementEventArgs? captured = null;

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.OnElementClick, (ModelingViewerElementEventArgs args) => captured = args));

        cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='r1']").Click();

        captured.Should().NotBeNull();
        captured!.ElementId.Should().Be("r1");
        captured.IsRelationship.Should().BeTrue();
        captured.SemanticType.Should().Be("sequenceFlow");
    }

    [Fact]
    public void Selected_element_parameter_is_honoured_two_way()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.SelectedElementId, "e-approve")
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-approve']")
            .GetAttribute("aria-current").Should().Be("true");
        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-selected").Should().Be("e-approve");
    }

    [Fact]
    public void Element_list_is_ordered_topologically_with_relationships_after_their_source()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var ids = cut.FindAll("[data-testid='modeling-viewer-element-list'] li[data-removed='false'] [data-element-id]")
            .Select(element => element.GetAttribute("data-element-id"))
            .ToArray();

        ids.Should().Equal("e-start", "r1", "e-review", "r2", "e-approve", "r3", "e-end");
    }

    [Fact]
    public void Show_element_list_false_removes_the_text_alternative()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ShowElementList, false)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll("[data-testid='modeling-viewer-element-list']").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_layout_shows_the_list_without_a_toggle()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-layout").Should().Be("mobile");
        cut.FindAll("[data-testid='modeling-viewer-element-list-toggle']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-viewer-element-list']").GetAttribute("data-expanded").Should().Be("true");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]").Should().NotBeEmpty();
    }

    [Fact]
    public void Desktop_element_list_is_collapsible()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var toggle = cut.Find("[data-testid='modeling-viewer-element-list-toggle']");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        toggle.Click();
        cut.Find("[data-testid='modeling-viewer-element-list']").GetAttribute("data-expanded").Should().Be("false");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]").Should().BeEmpty();
    }

    [Fact]
    public void Legend_renders_only_when_requested()
    {
        using var withLegend = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));
        withLegend.FindAll("[data-testid='modeling-preview-legend']").Should().ContainSingle();
        withLegend.Find("[data-testid='modeling-viewer']").GetAttribute("data-legend").Should().Be("true");

        using var withoutLegend = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.ShowLegend, false)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));
        withoutLegend.FindAll("[data-testid='modeling-preview-legend']").Should().BeEmpty();
    }

    [Fact]
    public void Empty_model_renders_the_empty_state_without_a_canvas()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateEmptyModel())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-state").Should().Be("empty");
        cut.Find("[data-testid='modeling-viewer-empty']").Should().NotBeNull();
        cut.FindAll("[data-testid='diagram-editor']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-node-count").Should().Be("0");
    }

    [Fact]
    public void Viewer_never_mutates_the_caller_model()
    {
        var model = ModelingViewerTestModels.CreateProcessModel();
        var names = model.Elements.Select(element => element.Name).ToArray();
        var positions = model.Views[0].Nodes.Select(node => (node.X, node.Y)).ToArray();

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.SelectedElementId, "e-review")
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        model.Elements.Select(element => element.Name).Should().Equal(names);
        model.Views[0].Nodes.Select(node => (node.X, node.Y)).Should().Equal(positions);
        model.Elements.Should().Contain(element => element.Id == "e-approve");
    }

    [Fact]
    public void Print_element_list_opt_in_marks_the_root()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ShowElementListInPrint, true)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer']").ClassList
            .Should().Contain("tm-modeling-viewer--print-elements");
    }

    [Fact]
    public void Aria_label_parameter_names_the_region()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.AriaLabel, "Order process at a glance")
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer']").GetAttribute("aria-label")
            .Should().Be("Order process at a glance");
    }

    [Fact]
    public void Element_list_content_slot_replaces_the_row_content_inside_the_viewer_button()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.ElementListContent, context => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "host-row");
                builder.AddContent(2, $"host:{context.ElementId}");
                builder.CloseElement();
            })
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll(".host-row").Should().NotBeEmpty();

        // The row stays a viewer-owned button: the host slot only replaces its VISIBLE content, so
        // the roving tabindex, the aria wiring and the activation contract are not the host's job.
        var buttons = cut.FindAll("[data-testid='modeling-viewer-element-list'] button[data-list-index]");
        buttons.Should().NotBeEmpty();
        buttons.Select(button => button.QuerySelector(".host-row")).Should().NotContainNulls();
        buttons.All(button => button.GetAttribute("aria-label") is { Length: > 0 }).Should().BeTrue();
        buttons.Count(button => button.GetAttribute("tabindex") == "0").Should().Be(1);
    }

    [Fact]
    public void Element_list_content_slot_keeps_the_keyboard_contract()
    {
        ModelingViewerElementEventArgs? captured = null;

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.ElementListContent, context => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "host-row");
                builder.AddContent(2, context.Name);
                builder.CloseElement();
            })
            .Add(p => p.OnElementClick, (ModelingViewerElementEventArgs args) => captured = args)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var first = cut.Find("[data-testid='modeling-viewer-element-list'] button[data-list-index='0']");
        first.KeyDown("ArrowDown");

        cut.FindAll("[data-testid='modeling-viewer-element-list'] button[data-list-index]")[1]
            .GetAttribute("tabindex").Should().Be("0");

        cut.Find("[data-testid='modeling-viewer-element-list'] button[data-list-index='0']").Click();
        captured.Should().NotBeNull();
        captured!.ElementId.Should().Be("e-start");
    }

    [Fact]
    public void A_runtime_fit_mode_change_re_runs_the_fit()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.FitMode, ModelingViewerFitMode.Page)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        JSInterop.Invocations["tmDiagramEditor.fitToView"].Should().NotBeEmpty(
            "the page fit runs once after the first render");

        var before = JSInterop.Invocations["tmDiagramEditor.fitToView"].Count;

        cut.Render(parameters => parameters
            .Add(p => p.FitMode, ModelingViewerFitMode.Width));

        cut.Find("[data-testid='modeling-viewer']").GetAttribute("data-fit-mode").Should().Be("width");
        JSInterop.Invocations["tmDiagramEditor.fitToView"].Count.Should().BeGreaterThan(before,
            "flipping the host's fit select has to re-fit, not just repaint the attribute");
        JSInterop.Invocations["tmDiagramEditor.fitToView"][^1].Arguments
            .Select(argument => argument?.ToString())
            .Should().Contain("width");
    }

    [Fact]
    public void An_empty_model_renders_no_element_list_section()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateEmptyModel())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll("[data-testid='modeling-viewer-element-list']").Should().BeEmpty(
            "an empty model has nothing to list — and no toggle or hint describing rows that cannot exist");
        cut.FindAll("[data-testid='modeling-viewer-element-list-toggle']").Should().BeEmpty();
    }

    [Fact]
    public void Two_viewers_of_the_same_model_use_distinct_description_ids()
    {
        using var first = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var firstId = RowDescriptionId(first, "e-review");

        using var second = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var secondId = RowDescriptionId(second, "e-review");
        secondId.Should().NotBe(firstId, "GetHashCode can repeat across two instances of the same model");
    }

    [Fact]
    public void The_overlay_legend_strip_is_collapsible_with_aria_expanded()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var toggle = cut.Find("[data-testid='modeling-viewer-legends-toggle']");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        cut.FindAll("[data-testid='modeling-viewer-diff-legend']").Should().NotBeEmpty();

        toggle.Click();

        cut.Find("[data-testid='modeling-viewer-legends-toggle']").GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='modeling-viewer-diff-legend']").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_keeps_the_legend_cards_expanded_without_a_toggle()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll("[data-testid='modeling-viewer-legends-toggle']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-viewer-legends']").GetAttribute("data-expanded").Should().Be("true");
        cut.FindAll("[data-testid='modeling-viewer-diff-legend']").Should().NotBeEmpty();
    }

    [Fact]
    public void A_selection_announcement_survives_the_hosts_re_render()
    {
        var model = ModelingViewerTestModels.CreateProcessModel();
        var overlay = ModelingViewerTestModels.CreateDiffOverlay();
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, overlay)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-review']").Click();
        var announced = cut.Find("[data-testid='modeling-viewer-live-region']").TextContent;
        announced.Should().Contain("Selected").And.Contain("Validate order");

        // A host re-render with the same model and the same overlay instance must not re-announce the
        // overlay over the selection text (the "regenerate" flag used to leak into the signature).
        cut.Render(parameters => parameters
            .Add(p => p.SelectedElementId, "e-review"));

        cut.Find("[data-testid='modeling-viewer-live-region']").TextContent.Should().Be(announced,
            "a selection-only re-render keeps the selection announcement");
        cut.Find("[data-testid='modeling-viewer-live-region']").TextContent.Should().NotContain("Diff overlay");
    }

    private static string RowDescriptionId(IRenderedComponent<TmModelingViewer> cut, string elementId)
    {
        var describedBy = cut.Find($"[data-testid='modeling-viewer-element-list'] [data-element-id='{elementId}']")
            .GetAttribute("aria-describedby")!;
        return describedBy.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
    }
}
