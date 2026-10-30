using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Fáze 15: the preview legend — a collapsible card over the preview canvas (a SIBLING of the
/// embedded diagram editor, never styled through its internals) explaining ONLY the notation
/// shapes the generated document actually uses, plus the mobile band's "⋯" → Legenda sheet.
/// </summary>
public sealed class TmModelingDiagramLegendTests : LocalizationTestBase
{
    private const string ModelingModulePath = "./_content/Tempo.Blazor.Modeling/js/modeling-editor.js";

    public TmModelingDiagramLegendTests()
    {
        Services.AddTempoBlazorModeling();
    }

    private static ModelingNotationCatalogEntry Element(string semanticType, string stencilId, string label = "Element")
        => new(semanticType, stencilId, label, IsRelationship: false, HasMapping: true);

    private static ModelingNotationCatalogEntry Relationship(string semanticType, string stencilId, string label = "Relationship")
        => new(semanticType, stencilId, label, IsRelationship: true, HasMapping: true);

    // ── The presenter ────────────────────────────────────────────────────────

    [Fact]
    public void Legend_lists_present_element_and_relationship_types_with_icons()
    {
        using var cut = Render<TmModelingDiagramLegend>(parameters => parameters
            .Add(p => p.Entries,
            [
                Element("userTask", "bpmn2.task.user", "User task"),
                Relationship("sequenceFlow", "bpmn2.flow.sequence", "Sequence flow")
            ]));

        var lists = cut.FindAll(".tm-modeling-diagram-legend__list");
        lists.Should().HaveCount(2, "one list per group: elements and relationships");
        foreach (var list in lists)
        {
            list.GetAttribute("role").Should().Be("list", "the legend is an accessible list");
        }

        var items = cut.FindAll(".tm-modeling-diagram-legend__list > li");
        items.Should().HaveCount(2);
        items[0].TextContent.Should().Contain("User task");
        items[1].TextContent.Should().Contain("Sequence flow");
        // The icons are decorative — the text label owns the accessible name.
        items[0].QuerySelector("svg")!.GetAttribute("aria-hidden").Should().Be("true");
        cut.Find(".tm-modeling-diagram-legend__group-title").TextContent.Should().NotBeEmpty();
    }

    [Fact]
    public void Legend_is_collapsible_with_aria_expanded()
    {
        using var cut = Render<TmModelingDiagramLegend>(parameters => parameters
            .Add(p => p.Entries, [Element("userTask", "bpmn2.task.user")])
            .Add(p => p.DefaultExpanded, true));

        var toggle = cut.Find(".tm-modeling-diagram-legend__toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        cut.Find(".tm-modeling-diagram-legend__list").Should().NotBeNull();

        toggle.Click();

        toggle.GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll(".tm-modeling-diagram-legend__list").Should().BeEmpty(
            "a collapsed legend renders no list body");
    }

    [Fact]
    public void Legend_shows_empty_text_when_document_has_no_known_types()
    {
        using var cut = Render<TmModelingDiagramLegend>(parameters => parameters
            .Add(p => p.Entries, Array.Empty<ModelingNotationCatalogEntry>()));

        cut.Find(".tm-modeling-diagram-legend__empty").Should().NotBeNull();
        cut.FindAll(".tm-modeling-diagram-legend__list > li").Should().BeEmpty();
    }

    // ── The preview slot ─────────────────────────────────────────────────────

    [Fact]
    public void Preview_renders_the_legend_as_a_sibling_of_the_editor_when_enabled()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument())
            .Add(p => p.ShowLegend, true)
            .Add(p => p.LegendEntries,
            [
                Element("userTask", "bpmn2.task.user", "User task")
            ]));

        var shell = cut.Find("[data-testid='modeling-diagram-preview-canvas-shell']");
        shell.QuerySelector("[data-testid='modeling-preview-legend']").Should().NotBeNull(
            "the legend card lives in the canvas shell next to the editor");
        cut.FindComponent<TmDiagramEditor>().Should().NotBeNull();
    }

    [Fact]
    public void Legend_hidden_when_ShowLegend_false()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument()));

        cut.FindAll("[data-testid='modeling-preview-legend']").Should().BeEmpty(
            "ShowLegend defaults to false — the pre-Fáze-15 preview shows no legend");
    }

    [Fact]
    public void Legend_without_entries_is_not_rendered_even_when_enabled()
    {
        using var cut = Render<TmModelingDiagramPreview>(parameters => parameters
            .Add(p => p.Document, CreateDocument())
            .Add(p => p.ShowLegend, true));

        cut.FindAll("[data-testid='modeling-preview-legend']").Should().BeEmpty(
            "no notation catalog data — no legend card");
    }

    // ── The mobile sheet ─────────────────────────────────────────────────────

    [Fact]
    public async Task Mobile_legend_opens_from_overflow_menu_sheet()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.NotationKey, "bpmn")
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more .tm-dropdown-trigger").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-modeling-editor__more").InnerHtml.Should().Contain("Legend"));

        cut.FindAll(".tm-modeling-editor__more .tm-dropdown-item")
            .Single(item => item.TextContent.Contains("Legend"))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var sheet = cut.Find("[data-testid='modeling-legend-sheet']");
            sheet.QuerySelector("[data-testid='modeling-preview-legend']").Should().NotBeNull(
                "the mobile sheet hosts the same legend component");
        });
    }

    private static DiagramDocument CreateDocument()
    {
        var page = new DiagramPage
        {
            Id = "legend-page",
            Name = "Legend",
            Layers = [new DiagramLayer { Id = "default", Name = "Default" }]
        };
        page.Nodes.Add(new DiagramNode
        {
            Id = "legend-node",
            StencilId = "bpmn2.task.user",
            X = 40,
            Y = 40,
            W = 140,
            H = 72,
            LayerId = "default",
            Data = new Dictionary<string, object> { ["semanticType"] = "userTask" }
        });

        return new DiagramDocument { Pages = { page } };
    }
}
