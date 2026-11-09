using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Round-trip tests over the whole corpus (docs/modeling-bpmn-xml.md §5, phase 4):
/// import → export → import must reproduce the normalized model (WithHostExtensions profile keeps
/// every property, so nothing may drift), and export(import(export(x))) must be byte-identical to
/// export(x) — the exporter is idempotent.
/// </summary>
public sealed class BpmnRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in BpmnImportGoldenFixtures.All)
        {
            data.Add(fixture);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Import_export_import_preserves_the_normalized_model(string fixture)
    {
        var first = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture));
        first.HasErrors.Should().BeFalse(first.ErrorMessage ?? $"import of {fixture} failed");

        var exported = new BpmnXmlExporter().Export(first.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
        exported.Success.Should().BeTrue(exported.ErrorMessage);

        var second = new BpmnXmlImporter().Import(exported.Xml!);
        second.HasErrors.Should().BeFalse(second.ErrorMessage ?? $"re-import of the exported {fixture} failed");
        second.Models.Should().HaveCount(first.Models.Count, "export must not merge or drop models");

        for (var index = 0; index < first.Models.Count; index++)
        {
            var before = CanonicalJson(Normalized(first.Models[index]));
            var after = CanonicalJson(Normalized(second.Models[index]));
            after.Should().Be(before,
                $"model '{first.Models[index].Id}' of {fixture} must survive the export round trip (normalized comparison)");
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Export_is_idempotent_export_import_export_yields_the_same_document(string fixture)
    {
        var first = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture));
        var exporter = new BpmnXmlExporter();
        var options = new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions };

        var once = exporter.Export(first.Models, options);
        var reimported = new BpmnXmlImporter().Import(once.Xml!);
        var twice = exporter.Export(reimported.Models, options);

        twice.Success.Should().BeTrue(twice.ErrorMessage);
        twice.Xml.Should().Be(once.Xml,
            "export(import(export(x))) must equal export(x) — the exporter must be a fixed point");
    }

    [Fact]
    public void Standard_profile_round_trip_preserves_everything_except_the_documented_unmappable_properties()
    {
        var imported = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("camunda-export.bpmn"));

        var exported = new BpmnXmlExporter().Export(imported.Models, new BpmnExportOptions());
        exported.Success.Should().BeTrue();
        exported.Losses.Should().BeEmpty(
            "every property the importer produces must have a native BPMN representation — " +
            "otherwise the loss report must say why (docs/modeling-bpmn-xml.md §4/§6)");

        var second = new BpmnXmlImporter().Import(exported.Xml!);
        second.HasErrors.Should().BeFalse(second.ErrorMessage);
        second.Models.Should().HaveCount(imported.Models.Count);

        for (var index = 0; index < imported.Models.Count; index++)
        {
            var before = CanonicalJson(Normalized(imported.Models[index]));
            var after = CanonicalJson(Normalized(second.Models[index]));
            after.Should().Be(before,
                "the Standard profile must round-trip loss-free for natively representable importer output");
        }
    }

    [Fact]
    public void Standard_profile_reports_losses_for_host_authored_properties_without_a_BPMN_representation()
    {
        var model = new ModelingModelDto { Id = "Process_1", Title = "T", Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto { Id = "Task_1", SemanticType = BpmnSemanticTypes.Task });
        model.Elements[0].Properties["internalRating"] = JsonSerializer.SerializeToElement(3);

        var result = new BpmnXmlExporter().Export(model);

        result.Success.Should().BeTrue();
        result.Losses.Should().ContainSingle(loss =>
            loss.ElementId == "Task_1" && loss.Construct == "Property" &&
            loss.Reason.Contains("internalRating", StringComparison.Ordinal));
    }

    [Fact]
    public void Properties_cannot_break_out_of_the_XML_they_are_serialized_into()
    {
        var model = new ModelingModelDto { Id = "Process_1", Title = "T", Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto { Id = "Task_1", SemanticType = BpmnSemanticTypes.Task });
        model.Elements[0].Properties["payload"] = JsonSerializer.SerializeToElement("</task><evil/>");
        model.Elements[0].Properties["bpmn:marker"] = JsonSerializer.SerializeToElement("\" onload=\"x");

        var result = new BpmnXmlExporter().Export(model,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
        result.Success.Should().BeTrue();

        // The output must stay well-formed AND keep the values as DATA (double-parse equality).
        var reimported = new BpmnXmlImporter().Import(result.Xml!);
        reimported.HasErrors.Should().BeFalse(reimported.ErrorMessage);
        var task = reimported.Models.Single().Elements.Single();
        task.Properties["payload"].GetString().Should().Be("</task><evil/>");
        task.Properties["bpmn:marker"].GetString().Should().Be("\" onload=\"x");
        result.Xml.Should().NotContain("<evil/>");
    }

    /// <summary>
    /// Normalization removes the comparison noise the export is ALLOWED to add (docs/modeling-bpmn-xml.md
    /// §5.4): DI geometry is re-derived from the emitted shapes/edges, issues are validation
    /// results of the specific document, and source paths describe the transient XML layout.
    /// Everything else (elements, relationships, properties, view membership, DI flags, label
    /// bounds, positions) must match exactly.
    /// </summary>
    private static ModelingModelDto Normalized(ModelingModelDto model)
    {
        var clone = new ModelingModelDto
        {
            Id = model.Id,
            Title = model.Title,
            Notation = model.Notation,
            Metadata = model.Metadata,
        };
        clone.SupportedNotations.AddRange(model.SupportedNotations);
        foreach (var (key, value) in model.Properties)
        {
            clone.Properties[key] = value;
        }

        foreach (var element in model.Elements)
        {
            var properties = new Dictionary<string, JsonElement>(element.Properties);
            // The legacy `multiple` trigger flag has no BPMN 2.0 schema representation (the
            // event definitions themselves carry the semantics); the exporter reports it as an
            // approximation instead of writing a schema-invalid construct.
            properties.Remove("multiple");
            properties.Remove("bpmn:multiple");

            clone.Elements.Add(new ModelingElementDto
            {
                Id = element.Id,
                SourceId = element.SourceId,
                SourceType = element.SourceType,
                Notation = element.Notation,
                SemanticType = element.SemanticType,
                Name = element.Name,
                ParentId = element.ParentId,
                Description = element.Description,
                Properties = properties,
            });
        }

        foreach (var relationship in model.Relationships)
        {
            var properties = new Dictionary<string, JsonElement>(relationship.Properties);
            properties.Remove("bpmndi:waypoints");
            properties.Remove("bpmndi:planeId");
            clone.Relationships.Add(new ModelingRelationshipDto
            {
                Id = relationship.Id,
                SourceId = relationship.SourceId,
                SourceType = relationship.SourceType,
                SourceElementId = relationship.SourceElementId,
                TargetElementId = relationship.TargetElementId,
                RelationshipType = relationship.RelationshipType,
                Name = relationship.Name,
                Properties = properties,
            });
        }

        foreach (var view in model.Views)
        {
            var cloneView = new ModelingViewDto { Id = view.Id, Name = view.Name, Notation = view.Notation };
            cloneView.Nodes.AddRange(view.Nodes.Select(node => new ModelingViewNodeDto
            {
                ElementId = node.ElementId,
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height,
                ParentNodeId = node.ParentNodeId,
            }));
            cloneView.Connections.AddRange(view.Connections.Select(connection => new ModelingViewConnectionDto
            {
                RelationshipId = connection.RelationshipId,
                SourceNodeId = connection.SourceNodeId,
                TargetNodeId = connection.TargetNodeId,
                Waypoints = [],
            }));
            clone.Views.Add(cloneView);
        }

        // Element order is not part of the contract the export must preserve: the exporter may
        // legitimately re-derive positions (e.g. synthesized <dataObject> declarations lead the
        // process content, finding 4). Compare order-insensitively, content-exactly.
        var ordered = clone.Elements.OrderBy(element => element.Id, StringComparer.Ordinal).ToList();
        clone.Elements.Clear();
        clone.Elements.AddRange(ordered);

        return clone;
    }

    /// <summary>Canonical JSON: sorted object keys recursively, compact — member order must not matter.</summary>
    private static string CanonicalJson<T>(T value)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return Canonicalize(node)!.ToJsonString(JsonOptions);
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var property in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    sorted[property.Key] = Canonicalize(property.Value);
                }

                return sorted;
            case JsonArray array:
                var sortedArray = new JsonArray();
                foreach (var item in array)
                {
                    sortedArray.Add(Canonicalize(item));
                }

                return sortedArray;
            default:
                return node?.DeepClone();
        }
    }
}
