using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// Demo fixtures for <c>/modeling-viewer</c> (plan eadbf967 phase 6): one BPMN order-fulfillment
/// process, the "after" version of the same process plus the diff overlay, the risk heatmap and the
/// empty-model edge case. The page owns these models; the viewer never writes to them.
/// </summary>
internal static class ModelingViewerPageSamples
{
    internal const string ViewId = "viewer-flow";

    internal static ModelingModelDto CreateBaseline()
    {
        var model = new ModelingModelDto
        {
            Id = "demo-viewer-process",
            Title = "Order fulfillment (v1)",
            Notation = "bpmn",
            Elements =
            [
                Element("e-start", "startEvent", "Order received", "Customer order has been received."),
                Element("e-check", "userTask", "Check stock", "Sales verifies availability."),
                Element("e-approve", "userTask", "Approve order", "Manager approves the order."),
                Element("e-gateway", "exclusiveGateway", "Approved?", "Decision after approval."),
                Element("e-ship", "serviceTask", "Ship order", "Warehouse ships the goods."),
                Element("e-end", "endEvent", "Order completed", "Process finished.")
            ],
            Relationships =
            [
                Relationship("r1", "e-start", "e-check", "Received → check"),
                Relationship("r2", "e-check", "e-approve", "Check → approve"),
                Relationship("r3", "e-approve", "e-gateway", "Approve → decision"),
                Relationship("r4", "e-gateway", "e-ship", "Yes → ship"),
                Relationship("r5", "e-gateway", "e-end", "No → completed")
            ]
        };

        model.Views.Add(new ModelingViewDto
        {
            Id = ViewId,
            Name = "Order fulfillment",
            Notation = "bpmn",
            Nodes =
            [
                new() { ElementId = "e-start", X = 60, Y = 100, Width = 110, Height = 70 },
                new() { ElementId = "e-check", X = 340, Y = 100, Width = 150, Height = 70 },
                new() { ElementId = "e-approve", X = 620, Y = 100, Width = 150, Height = 70 },
                new() { ElementId = "e-gateway", X = 900, Y = 100, Width = 90, Height = 90 },
                new() { ElementId = "e-ship", X = 1080, Y = 250, Width = 150, Height = 70 },
                new() { ElementId = "e-end", X = 1080, Y = 40, Width = 150, Height = 70 }
            ],
            Connections =
            [
                new() { RelationshipId = "r1" },
                new() { RelationshipId = "r2" },
                new() { RelationshipId = "r3" },
                new() { RelationshipId = "r4" },
                new() { RelationshipId = "r5" }
            ]
        });

        return model;
    }

    internal static ModelingModelDto CreateAfter()
    {
        var model = CreateBaseline();
        model.Title = "Order fulfillment (v2)";
        model.Elements.RemoveAll(element => element.Id == "e-end");
        model.Elements.Find(element => element.Id == "e-check")!.Name = "Check availability";
        model.Elements.Add(Element("e-notify", "sendTask", "Notify customer", "Customer gets the shipping confirmation."));
        model.Relationships.RemoveAll(relationship => relationship.Id == "r5");
        model.Relationships.Add(Relationship("r6", "e-ship", "e-notify", "Ship → notify"));

        var view = model.Views[0];
        view.Nodes.RemoveAll(node => node.ElementId == "e-end");
        view.Nodes.Find(node => node.ElementId == "e-ship")!.Y = 250;
        view.Nodes.Add(new ModelingViewNodeDto { ElementId = "e-notify", X = 1340, Y = 250, Width = 150, Height = 70 });
        view.Connections.RemoveAll(connection => connection.RelationshipId == "r5");
        view.Connections.Add(new ModelingViewConnectionDto { RelationshipId = "r6" });
        return model;
    }

    internal static ModelingDiffOverlayDto CreateDiffOverlay()
        => new()
        {
            AddedElementIds = ["e-notify"],
            RemovedElementIds = ["e-end"],
            ChangedElementIds = ["e-check"],
            LayoutOnlyElementIds = ["e-ship"],
            AddedRelationshipIds = ["r6"],
            RemovedRelationshipIds = ["r5"],
            BaselineModel = CreateBaseline(),
            LegendTitle = "Order fulfillment: v1 → v2"
        };

    internal static ModelingHeatmapDto CreateHeatmap()
        => new()
        {
            Values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["e-start"] = 0.15,
                ["e-check"] = 0.35,
                ["e-approve"] = 0.55,
                ["e-gateway"] = 0.7,
                ["e-ship"] = 0.9,
                ["r1"] = 0.2,
                ["r4"] = 0.8
            },
            ValueLabel = "SLA risk",
            LegendMinLabel = "Low risk",
            LegendMaxLabel = "High risk",
            Format = "{0:P0}"
        };

    internal static ModelingModelDto CreateEmpty()
        => new()
        {
            Id = "demo-viewer-empty",
            Title = "New process (empty)",
            Notation = "bpmn"
        };

    private static ModelingElementDto Element(string id, string semanticType, string name, string description)
        => new()
        {
            Id = id,
            SourceId = $"demo/viewer/{id}",
            Notation = "bpmn",
            SemanticType = semanticType,
            Name = name,
            Description = description
        };

    private static ModelingRelationshipDto Relationship(string id, string source, string target, string name)
        => new()
        {
            Id = id,
            SourceId = $"demo/viewer/{id}",
            SourceElementId = source,
            TargetElementId = target,
            RelationshipType = "sequenceFlow",
            Name = name
        };
}
