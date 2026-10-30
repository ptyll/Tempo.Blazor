using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Fáze 15: the toolbox's additive preferred-stencil API — the Modeling "Open in editor" dialog
/// pins the active notation's stencils as the FIRST library (its label = the profile display
/// name) while every other library starts collapsed; search still spans the whole stencil
/// library. A null preferred set keeps today's toolbox byte-for-byte.
/// </summary>
public sealed class TmDiagramToolboxPreferredStencilsTests : LocalizationTestBase
{
    private const string LibraryJson = """
        {
          "setId": "phase15.diagram",
          "nameResourceKey": "DiagramLibrary_Phase15",
          "palettes": [
            {
              "paletteId": "phase15.diagram.core",
              "nameResourceKey": "DiagramPalette_Phase15Core",
              "order": 10,
              "stencils": [
                {
                  "id": "phase15.node.a",
                  "nameResourceKey": "DiagramStencil_Phase15A",
                  "category": "phase15.diagram",
                  "origin": "tempoOriginal"
                },
                {
                  "id": "phase15.node.b",
                  "nameResourceKey": "DiagramStencil_Phase15B",
                  "category": "phase15.diagram",
                  "origin": "tempoOriginal"
                }
              ]
            },
            {
              "paletteId": "phase15.diagram.extra",
              "nameResourceKey": "DiagramPalette_Phase15Extra",
              "order": 20,
              "stencils": [
                {
                  "id": "phase15.node.c",
                  "nameResourceKey": "DiagramStencil_Phase15C",
                  "category": "phase15.diagram",
                  "origin": "tempoOriginal"
                }
              ]
            }
          ]
        }
        """;

    public TmDiagramToolboxPreferredStencilsTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmDiagramToolbox_Toggle"] = "Toggle toolbox",
            ["TmDiagramToolbox_Title"] = "Toolbox",
            ["TmDiagramToolbox_SearchPlaceholder"] = "Search stencils",
            ["TmDiagramToolbox_SearchAriaLabel"] = "Search diagram stencils",
            ["TmDiagramToolbox_NoResults"] = "No matching stencils",
            ["TmDiagramToolbox_DragStencil"] = "Drag {0} onto the canvas",
            ["TmDiagramToolbox_InsertStencil"] = "Insert {0} onto the canvas",
            ["TmDiagramToolbox_PreferredDefault"] = "Preferred stencils",
            ["DiagramLibrary_Phase15"] = "Phase 15 Library",
            ["DiagramPalette_Phase15Core"] = "Core",
            ["DiagramPalette_Phase15Extra"] = "Extra",
            ["DiagramStencil_Phase15A"] = "Stencil A",
            ["DiagramStencil_Phase15B"] = "Stencil B",
            ["DiagramStencil_Phase15C"] = "Stencil C"
        });

        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new JsonDiagramStencilProvider(
            [JsonDiagramStencilLibrarySource.Required("phase15", () => LibraryJson)]));
        Services.AddSingleton(registry);
    }

    [Fact]
    public void PreferredStencilIds_render_as_first_pinned_library_with_label()
    {
        using var cut = Render<TmDiagramToolbox>(parameters => parameters
            .Add(p => p.PreferredStencilIds, ["phase15.node.b", "phase15.node.a"])
            .Add(p => p.PreferredStencilsLabel, "BPMN 2.0"));

        var libraries = cut.FindAll(".tm-diagram-toolbox__library");
        libraries.Should().HaveCount(2, "the pinned library plus the rest of the stencil library");

        var pinned = libraries[0];
        pinned.GetAttribute("data-library-id").Should().Be("notation-palette");
        pinned.GetAttribute("data-pinned").Should().Be("true");
        pinned.QuerySelector(".tm-diagram-toolbox__library-header")!.TextContent.Trim().Should().Be("BPMN 2.0");

        // The preferred ORDER is preserved inside the pinned library.
        pinned.QuerySelectorAll("[data-stencil-id]")
            .Select(node => node.GetAttribute("data-stencil-id"))
            .Should().Equal("phase15.node.b", "phase15.node.a");
    }

    [Fact]
    public void Other_libraries_start_collapsed_when_preferred_set()
    {
        using var cut = Render<TmDiagramToolbox>(parameters => parameters
            .Add(p => p.PreferredStencilIds, ["phase15.node.a"]));

        // The pinned palette body renders; the OTHER libraries' palettes start collapsed.
        cut.Find("[data-library-id='notation-palette'] [data-palette-id='notation-palette'] .tm-diagram-toolbox__category-body")
            .Should().NotBeNull("the pinned palette renders expanded");

        var collapsedHeader = cut.Find("[data-palette-id='phase15.diagram.core'] .tm-diagram-toolbox__category-header");
        collapsedHeader.GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-palette-id='phase15.diagram.core'] .tm-diagram-toolbox__category-body")
            .Should().BeEmpty("a collapsed palette renders no body");
        cut.FindAll("[data-palette-id='phase15.diagram.extra'] .tm-diagram-toolbox__category-body")
            .Should().BeEmpty();
    }

    [Fact]
    public void Search_spans_whole_library()
    {
        using var cut = Render<TmDiagramToolbox>(parameters => parameters
            .Add(p => p.PreferredStencilIds, ["phase15.node.a"]));

        // "Stencil C" lives in a collapsed non-pinned palette — the search must still surface it.
        cut.Find(".tm-diagram-toolbox__search input").Input("Stencil C");

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-palette-id='phase15.diagram.extra'] .tm-diagram-toolbox__category-body")
                .QuerySelector("[data-stencil-id='phase15.node.c']")
                .Should().NotBeNull("the search expands every palette that has matches");
        });
    }

    [Fact]
    public void Null_keeps_todays_toolbox()
    {
        using var cut = Render<TmDiagramToolbox>();

        cut.FindAll("[data-library-id='notation-palette']").Should().BeEmpty();
        // Every palette starts expanded — the pre-Fáze-15 default.
        cut.FindAll(".tm-diagram-toolbox__category-body").Should().HaveCount(2);
        cut.Find("[data-palette-id='phase15.diagram.core'] .tm-diagram-toolbox__category-header")
            .GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Unknown_preferred_ids_are_ignored()
    {
        using var cut = Render<TmDiagramToolbox>(parameters => parameters
            .Add(p => p.PreferredStencilIds, ["missing.stencil", "phase15.node.a"]));

        var pinned = cut.Find("[data-library-id='notation-palette']");
        pinned.QuerySelectorAll("[data-stencil-id]")
            .Select(node => node.GetAttribute("data-stencil-id"))
            .Should().Equal("phase15.node.a");
    }

    [Fact]
    public void Preferred_label_falls_back_to_localized_default()
    {
        using var cut = Render<TmDiagramToolbox>(parameters => parameters
            .Add(p => p.PreferredStencilIds, ["phase15.node.a"]));

        cut.Find("[data-library-id='notation-palette'] .tm-diagram-toolbox__library-header")
            .TextContent.Trim()
            .Should().Be("Preferred stencils");
    }
}
