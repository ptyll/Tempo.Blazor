using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The shared semantic models for the phase-6 <c>TmModelingViewer</c> tests: a small BPMN process
/// with a stored view (so positions and the legend are deterministic) plus the diff/heatmap
/// fixtures the overlay tests reuse.
/// </summary>
internal static class ModelingViewerTestModels
{
    internal const string ViewId = "flow-view";

    internal static ModelingModelDto CreateProcessModel()
    {
        var model = new ModelingModelDto
        {
            Id = "viewer-process",
            Title = "Order fulfillment",
            Notation = "bpmn",
            Elements =
            [
                Element("e-start", "startEvent", "Order received"),
                Element("e-review", "userTask", "Validate order"),
                Element("e-approve", "userTask", "Approve order"),
                Element("e-end", "endEvent", "Order shipped")
            ],
            Relationships =
            [
                Relationship("r1", "e-start", "e-review", "Sequence flow: received → validate"),
                Relationship("r2", "e-review", "e-approve", "Sequence flow: validate → approve"),
                Relationship("r3", "e-approve", "e-end", "Sequence flow: approve → ship")
            ]
        };

        model.Views.Add(new ModelingViewDto
        {
            Id = ViewId,
            Name = "Flow",
            Notation = "bpmn",
            Nodes =
            [
                new() { ElementId = "e-start", X = 100, Y = 100, Width = 150, Height = 70 },
                new() { ElementId = "e-review", X = 340, Y = 100, Width = 150, Height = 70 },
                new() { ElementId = "e-approve", X = 580, Y = 100, Width = 150, Height = 70 },
                new() { ElementId = "e-end", X = 820, Y = 100, Width = 150, Height = 70 }
            ],
            Connections =
            [
                new() { RelationshipId = "r1" },
                new() { RelationshipId = "r2" },
                new() { RelationshipId = "r3" }
            ]
        });

        return model;
    }

    /// <summary>The "after" model of the diff scenario: <c>e-approve</c> was removed, <c>e-notify</c>
    /// added, <c>e-review</c> renamed and <c>e-end</c> moved.</summary>
    internal static ModelingModelDto CreateDiffModel()
    {
        var model = CreateProcessModel();
        model.Elements.RemoveAll(element => element.Id == "e-approve");
        model.Elements.Find(element => element.Id == "e-review")!.Name = "Validate and verify order";
        model.Elements.Add(Element("e-notify", "serviceTask", "Notify customer"));
        model.Relationships.RemoveAll(relationship => relationship.Id is "r2" or "r3");
        model.Relationships.Add(Relationship("r4", "e-review", "e-notify", "Sequence flow: validate → notify"));
        model.Relationships.Add(Relationship("r5", "e-notify", "e-end", "Sequence flow: notify → ship"));

        var view = model.Views[0];
        view.Nodes.RemoveAll(node => node.ElementId == "e-approve");
        view.Nodes.Find(node => node.ElementId == "e-end")!.Y = 260;
        view.Nodes.Add(new ModelingViewNodeDto { ElementId = "e-notify", X = 580, Y = 260, Width = 150, Height = 70 });
        view.Connections.RemoveAll(connection => connection.RelationshipId is "r2" or "r3");
        view.Connections.Add(new ModelingViewConnectionDto { RelationshipId = "r4" });
        view.Connections.Add(new ModelingViewConnectionDto { RelationshipId = "r5" });
        return model;
    }

    internal static ModelingDiffOverlayDto CreateDiffOverlay()
        => new()
        {
            AddedElementIds = ["e-notify"],
            RemovedElementIds = ["e-approve"],
            ChangedElementIds = ["e-review"],
            LayoutOnlyElementIds = ["e-end"],
            AddedRelationshipIds = ["r4", "r5"],
            RemovedRelationshipIds = ["r2", "r3"],
            BaselineModel = CreateProcessModel(),
            LegendTitle = "Version diff"
        };

    internal static ModelingHeatmapDto CreateHeatmap()
        => new()
        {
            Values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["e-start"] = 0.2,
                ["e-review"] = 0.8,
                ["r1"] = 0.4
            },
            ValueLabel = "SLA risk",
            LegendMinLabel = "Low",
            LegendMaxLabel = "High",
            Format = "{0:P0}"
        };

    /// <summary>A model without any elements — the viewer's empty-model edge case.</summary>
    internal static ModelingModelDto CreateEmptyModel()
        => new()
        {
            Id = "viewer-empty",
            Title = "Empty model",
            Notation = "bpmn"
        };

    private static ModelingElementDto Element(string id, string semanticType, string name)
        => new()
        {
            Id = id,
            SourceId = $"demo/{id}",
            Notation = "bpmn",
            SemanticType = semanticType,
            Name = name
        };

    private static ModelingRelationshipDto Relationship(string id, string source, string target, string name)
        => new()
        {
            Id = id,
            SourceId = $"demo/{id}",
            SourceElementId = source,
            TargetElementId = target,
            RelationshipType = "sequenceFlow",
            Name = name
        };
}
