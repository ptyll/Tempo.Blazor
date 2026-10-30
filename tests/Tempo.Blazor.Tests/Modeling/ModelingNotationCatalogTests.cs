using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Fáze 15: the shared notation catalog — the ONE mapping of an active notation profile's
/// semantic types to diagram stencils that both the "Open in editor" palette (pinned stencil
/// library) and the preview legend consume. The palette lists every supported type of the
/// profile (profile order preserved); the legend keeps only the types actually present in the
/// generated document.
/// </summary>
public sealed class ModelingNotationCatalogTests : LocalizationTestBase
{
    public ModelingNotationCatalogTests()
    {
        // The catalog labels resolve through TmModelingSemanticType_{type} keys — register a
        // localizer that carries them (the shared English mock does not know the vocabulary).
        Services.AddSingleton<ITmLocalizer>(new MockTmLocalizer(new Dictionary<string, string>
        {
            ["TmModelingSemanticType_userTask"] = "User task",
            ["TmModelingSemanticType_sequenceFlow"] = "Sequence flow"
        }));
    }

    private ModelingNotationCatalog CreateCatalog()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "bpmn2.task.user",
            Name = "User task",
            Category = "bpmn2",
            SetId = "bpmn2",
            PaletteId = "bpmn2.activities",
            Kind = DiagramStencilKind.Node,
            IconSvg = "<rect x='1' y='1' width='30' height='30' />",
            Origin = DiagramStencilOrigin.TempoOriginal
        });
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "bpmn2.flow.sequence",
            Name = "Sequence Flow",
            Category = "bpmn2",
            SetId = "bpmn2",
            PaletteId = "bpmn2.relationships",
            Kind = DiagramStencilKind.Edge,
            EdgeDefaults = new DiagramEdgeStencilDefaults { ConnectorType = "bpmn-sequence-flow" },
            Origin = DiagramStencilOrigin.TempoOriginal
        });

        return new ModelingNotationCatalog(
            new SingleProfileProvider(new TestProfile()),
            new TestStencilMapper(),
            registry,
            Services.GetRequiredService<ITmLocalizer>());
    }

    [Fact]
    public void Catalog_maps_supported_element_types_to_stencils_via_mapper()
    {
        var catalog = CreateCatalog();

        var palette = catalog.GetPalette(TestProfile.Notation);

        palette.Should().Contain(entry => entry.SemanticType == "userTask" && entry.StencilId == "bpmn2.task.user");
        palette.Should().Contain(entry => entry.SemanticType == "startEvent" && entry.StencilId == "not.in.registry");
        // Profile order is preserved so the palette reads like the profile, not alphabetically.
        var ordered = palette.ToList();
        ordered[0].SemanticType.Should().Be("userTask");
        ordered[1].SemanticType.Should().Be("startEvent");
        // The palette drives NODE insertion — relationship types never appear in it.
        palette.Should().NotContain(entry => entry.IsRelationship);
    }

    [Fact]
    public void Unmapped_types_are_skipped_with_fallback_flag()
    {
        var catalog = CreateCatalog();

        var palette = catalog.GetPalette(TestProfile.Notation);

        // A type the mapper does not map is skipped entirely — it cannot offer a shape.
        palette.Should().NotContain(entry => entry.SemanticType == "dataStore");
        // A type mapped to a stencil MISSING from the registry still lists (it can render the
        // generic fallback icon) but carries the fallback flag.
        palette.Should().Contain(entry => entry.SemanticType == "startEvent" && !entry.HasMapping);
        palette.Should().Contain(entry => entry.SemanticType == "userTask" && entry.HasMapping);
    }

    [Fact]
    public void Legend_entries_only_for_types_present_in_document()
    {
        var catalog = CreateCatalog();
        var document = CreateDocument(
            nodeSemanticTypes: ["userTask", "userTask"],
            edgeConnectorTypes: ["bpmn-sequence-flow"]);

        var legend = catalog.GetLegend(TestProfile.Notation, document);

        legend.Should().Contain(entry => entry.SemanticType == "userTask" && !entry.IsRelationship);
        // Types the profile supports but the document does not use never appear.
        legend.Should().NotContain(entry => entry.SemanticType == "startEvent");
        legend.Should().NotContain(entry => entry.SemanticType == "dataStore");
        legend.Should().NotContain(entry => entry.SemanticType == "messageFlow");
    }

    [Fact]
    public void Relationship_types_map_to_edge_stencils()
    {
        var catalog = CreateCatalog();
        var document = CreateDocument(
            nodeSemanticTypes: ["userTask"],
            edgeConnectorTypes: ["bpmn-sequence-flow"]);

        var legend = catalog.GetLegend(TestProfile.Notation, document);

        legend.Should().Contain(entry =>
            entry.SemanticType == "sequenceFlow"
            && entry.StencilId == "bpmn2.flow.sequence"
            && entry.IsRelationship);
    }

    [Fact]
    public void Labels_use_TmModelingSemanticType_keys()
    {
        var catalog = CreateCatalog();

        var palette = catalog.GetPalette(TestProfile.Notation);

        palette.First(entry => entry.SemanticType == "userTask").Label.Should().Be("User task");
        // A type without a TmModelingSemanticType_{type} key falls back to the humanized key.
        palette.First(entry => entry.SemanticType == "startEvent").Label.Should().Be("Start event");

        var document = CreateDocument(["userTask"], ["bpmn-sequence-flow"]);
        catalog.GetLegend(TestProfile.Notation, document)
            .First(entry => entry.SemanticType == "sequenceFlow").Label.Should().Be("Sequence flow");
    }

    [Fact]
    public void Legend_without_document_or_notation_is_empty()
    {
        var catalog = CreateCatalog();

        catalog.GetLegend(TestProfile.Notation, null).Should().BeEmpty();
        catalog.GetLegend(string.Empty, CreateDocument(["userTask"], [])).Should().BeEmpty();
    }

    private static DiagramDocument CreateDocument(string[] nodeSemanticTypes, string[] edgeConnectorTypes)
    {
        var page = new DiagramPage();
        for (var index = 0; index < nodeSemanticTypes.Length; index++)
        {
            page.Nodes.Add(new DiagramNode
            {
                Id = $"node-{index}",
                StencilId = "bpmn2.task.user",
                Data = new Dictionary<string, object> { ["semanticType"] = nodeSemanticTypes[index] }
            });
        }

        for (var index = 0; index < edgeConnectorTypes.Length; index++)
        {
            page.Edges.Add(new DiagramEdge { Id = $"edge-{index}", ConnectorType = edgeConnectorTypes[index] });
        }

        return new DiagramDocument { Pages = { page } };
    }

    private sealed class SingleProfileProvider(IModelingNotationProfile profile) : IModelingNotationProfileProvider
    {
        public IModelingNotationProfile? GetProfile(string notationKey)
            => string.Equals(notationKey?.Trim(), TestProfile.Notation, StringComparison.OrdinalIgnoreCase)
                ? profile
                : null;
    }

    private sealed class TestProfile : IModelingNotationProfile
    {
        public const string Notation = "test-notation";

        public string NotationKey => Notation;

        public string DisplayName => "Test Notation";

        public IReadOnlyCollection<string> SupportedElementTypes { get; } = ["userTask", "startEvent", "dataStore"];

        public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } = ["sequenceFlow", "messageFlow"];

        public IReadOnlyCollection<string> SupportedViewpointKeys { get; } = [];
    }

    private sealed class TestStencilMapper : IModelingStencilMapper
    {
        public string? GetStencilId(string notationKey, string semanticType) => semanticType switch
        {
            "userTask" => "bpmn2.task.user",
            "startEvent" => "not.in.registry",
            _ => null
        };

        public string? GetEdgeStencilId(string notationKey, string relationshipType) => relationshipType switch
        {
            "sequenceFlow" => "bpmn2.flow.sequence",
            _ => null
        };
    }
}
