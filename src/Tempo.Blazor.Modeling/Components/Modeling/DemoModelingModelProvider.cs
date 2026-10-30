using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Demo modeling provider with deterministic BPMN and ArchiMate sample data.</summary>
public sealed class DemoModelingModelProvider : IModelingModelProvider
{
    /// <summary>Provider key used for the built-in demo modeling data.</summary>
    public const string ProviderKeyValue = "tempo.demo.modeling";

    /// <summary>Identifier of the default demo model (shared with <see cref="DemoModelingModelWriter"/>
    /// so the in-memory demo writer can resolve the persisted copy).</summary>
    public const string ModelIdValue = "demo-modeling-model";

    private static readonly DateTimeOffset LoadedAt = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public string ProviderKey => ProviderKeyValue;

    /// <inheritdoc />
    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateModel());
    }

    /// <summary>
    /// The <c>?scenario=hierarchy</c> demo model (Fáze 12): the default mixed model enriched
    /// with a BPMN subprocess that parents the order activities through
    /// <see cref="ModelingElementDto.ParentId"/> — the model tree flips to the nested hierarchy
    /// view while the default model above stays flat (a model without parents must look and
    /// behave exactly as before).
    /// </summary>
    public static ModelingModelDto CreateHierarchyModel()
    {
        var model = CreateModel();
        model.Elements.Add(Element(
            id: "bpmn-subprocess-fulfillment",
            sourceId: "demo/bpmn/subprocess-fulfillment",
            sourceType: "bpmn-subprocess",
            sourcePath: "/Order fulfillment/Order fulfillment",
            notation: "bpmn",
            semanticType: "subprocess",
            name: "Order fulfillment",
            description: "BPMN subprocess containing the order activities.",
            tags: ["bpmn", "subprocess", "order"],
            properties: new()
            {
                ["stencilId"] = Json("bpmn2.subprocess")
            }));
        foreach (var element in model.Elements)
        {
            if (element.Id is "bpmn-start-order" or "bpmn-validate-order" or "bpmn-ship-order")
            {
                element.ParentId = "bpmn-subprocess-fulfillment";
            }
        }

        model.Views[0].Nodes.Add(new ModelingViewNodeDto
        {
            ElementId = "bpmn-subprocess-fulfillment",
            X = 40,
            Y = 60,
            Width = 620,
            Height = 190
        });
        return model;
    }

    /// <summary>The default demo model. Public so the demo writer can build its in-memory
    /// persisted copy from the same deterministic source.</summary>
    public static ModelingModelDto CreateModel()
    {
        return new ModelingModelDto
        {
            Id = ModelIdValue,
            Title = "Demo order fulfillment model",
            Notation = "mixed",
            SupportedNotations = ["bpmn", "archimate"],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Tempo.Blazor Demo",
                SourceVersion = "2026.06",
                LoadedAt = LoadedAt,
                IsFresh = true
            },
            Elements =
            [
                Element(
                    id: "bpmn-start-order",
                    sourceId: "demo/bpmn/start-order",
                    sourceType: "bpmn-event",
                    sourcePath: "/Order fulfillment/Start order",
                    notation: "bpmn",
                    semanticType: "startEvent",
                    name: "Order received",
                    description: "Customer order has been received.",
                    tags: ["bpmn", "event", "order"],
                    properties: new()
                    {
                        ["stencilId"] = Json("bpmn2.event.start"),
                        ["lane"] = Json("Sales")
                    }),
                Element(
                    id: "bpmn-validate-order",
                    sourceId: "demo/bpmn/validate-order",
                    sourceType: "bpmn-task",
                    sourcePath: "/Order fulfillment/Validate order",
                    notation: "bpmn",
                    semanticType: "userTask",
                    name: "Validate order",
                    description: "Sales checks order details and availability.",
                    tags: ["bpmn", "task", "sales"],
                    properties: new()
                    {
                        ["stencilId"] = Json("bpmn2.task.user"),
                        ["owner"] = Json("Sales")
                    }),
                Element(
                    id: "bpmn-ship-order",
                    sourceId: "demo/bpmn/ship-order",
                    sourceType: "bpmn-task",
                    sourcePath: "/Order fulfillment/Ship order",
                    notation: "bpmn",
                    semanticType: "serviceTask",
                    name: "Ship order",
                    description: "Warehouse service prepares and ships the package.",
                    tags: ["bpmn", "task", "warehouse"],
                    properties: new()
                    {
                        ["stencilId"] = Json("bpmn2.task.service"),
                        ["owner"] = Json("Warehouse")
                    }),
                Element(
                    id: "arch-customer-portal",
                    sourceId: "demo/arch/customer-portal",
                    sourceType: "archimate-application",
                    sourcePath: "/Architecture/Application/Customer portal",
                    notation: "archimate",
                    semanticType: "applicationComponent",
                    name: "Customer portal",
                    description: "Application component used by customers to place orders.",
                    tags: ["archimate", "application"],
                    properties: new()
                    {
                        ["stencilId"] = Json("archimate3.application.component"),
                        ["layer"] = Json("Application")
                    }),
                Element(
                    id: "arch-order-service",
                    sourceId: "demo/arch/order-service",
                    sourceType: "archimate-application-service",
                    sourcePath: "/Architecture/Application/Order service",
                    notation: "archimate",
                    semanticType: "applicationService",
                    name: "Order service",
                    description: "Application service that orchestrates fulfillment.",
                    tags: ["archimate", "service"],
                    properties: new()
                    {
                        ["stencilId"] = Json("archimate3.application.service"),
                        ["layer"] = Json("Application")
                    }),
                Element(
                    id: "arch-warehouse-team",
                    sourceId: "demo/arch/warehouse-team",
                    sourceType: "archimate-business-actor",
                    sourcePath: "/Architecture/Business/Warehouse team",
                    notation: "archimate",
                    semanticType: "businessActor",
                    name: "Warehouse team",
                    description: "Business actor responsible for physical fulfillment.",
                    tags: ["archimate", "business"],
                    properties: new()
                    {
                        ["stencilId"] = Json("archimate3.business.actor"),
                        ["layer"] = Json("Business")
                    })
            ],
            Relationships =
            [
                Relationship(
                    id: "rel-start-to-validate",
                    sourceId: "demo/rel/start-to-validate",
                    sourceType: "bpmn-sequence-flow",
                    sourceElementId: "bpmn-start-order",
                    targetElementId: "bpmn-validate-order",
                    relationshipType: "sequenceFlow",
                    name: "Start validation",
                    tags: ["bpmn", "sequence"],
                    properties: new() { ["stencilId"] = Json("bpmn2.flow.sequence") }),
                Relationship(
                    id: "rel-validate-to-ship",
                    sourceId: "demo/rel/validate-to-ship",
                    sourceType: "bpmn-sequence-flow",
                    sourceElementId: "bpmn-validate-order",
                    targetElementId: "bpmn-ship-order",
                    relationshipType: "sequenceFlow",
                    name: "Validated order",
                    tags: ["bpmn", "sequence"],
                    properties: new() { ["stencilId"] = Json("bpmn2.flow.sequence") }),
                Relationship(
                    id: "rel-portal-serves-order-service",
                    sourceId: "demo/rel/portal-serves-order-service",
                    sourceType: "archimate-serving",
                    sourceElementId: "arch-customer-portal",
                    targetElementId: "arch-order-service",
                    relationshipType: "serving",
                    name: "Uses order service",
                    tags: ["archimate", "serving"],
                    properties: new() { ["stencilId"] = Json("archimate3.relationship.serving") }),
                Relationship(
                    id: "rel-order-service-triggers-validation",
                    sourceId: "demo/rel/order-service-triggers-validation",
                    sourceType: "cross-model-trace",
                    sourceElementId: "arch-order-service",
                    targetElementId: "bpmn-validate-order",
                    relationshipType: "realization",
                    name: "Supports validation",
                    tags: ["archimate", "traceability"],
                    properties: new() { ["stencilId"] = Json("archimate3.relationship.realization") }),
                Relationship(
                    id: "rel-warehouse-assigned-ship",
                    sourceId: "demo/rel/warehouse-assigned-ship",
                    sourceType: "cross-model-trace",
                    sourceElementId: "arch-warehouse-team",
                    targetElementId: "bpmn-ship-order",
                    relationshipType: "assignment",
                    name: "Performs shipping",
                    tags: ["archimate", "traceability"],
                    properties: new() { ["stencilId"] = Json("archimate3.relationship.assignment") })
            ],
            Views =
            [
                new ModelingViewDto
                {
                    Id = "demo-fulfillment-overview",
                    Name = "Fulfillment overview",
                    Notation = "mixed",
                    ViewpointKey = "overview",
                    Nodes =
                    [
                        new() { ElementId = "bpmn-start-order", X = 80, Y = 120, Width = 70, Height = 70 },
                        new() { ElementId = "bpmn-validate-order", X = 220, Y = 100, Width = 160, Height = 92 },
                        new() { ElementId = "bpmn-ship-order", X = 460, Y = 100, Width = 160, Height = 92 },
                        new() { ElementId = "arch-customer-portal", X = 80, Y = 320, Width = 170, Height = 95 },
                        new() { ElementId = "arch-order-service", X = 320, Y = 320, Width = 170, Height = 95 },
                        new() { ElementId = "arch-warehouse-team", X = 560, Y = 320, Width = 170, Height = 95 }
                    ],
                    Connections =
                    [
                        new() { RelationshipId = "rel-start-to-validate", SourceNodeId = "bpmn-start-order", TargetNodeId = "bpmn-validate-order" },
                        new() { RelationshipId = "rel-validate-to-ship", SourceNodeId = "bpmn-validate-order", TargetNodeId = "bpmn-ship-order" },
                        new() { RelationshipId = "rel-portal-serves-order-service", SourceNodeId = "arch-customer-portal", TargetNodeId = "arch-order-service" },
                        new() { RelationshipId = "rel-order-service-triggers-validation", SourceNodeId = "arch-order-service", TargetNodeId = "bpmn-validate-order" },
                        new() { RelationshipId = "rel-warehouse-assigned-ship", SourceNodeId = "arch-warehouse-team", TargetNodeId = "bpmn-ship-order" }
                    ]
                }
            ]
        };
    }

    private static ModelingElementDto Element(
        string id,
        string sourceId,
        string sourceType,
        string sourcePath,
        string notation,
        string semanticType,
        string name,
        string description,
        List<string> tags,
        Dictionary<string, JsonElement> properties)
        => new()
        {
            Id = id,
            SourceId = sourceId,
            SourceType = sourceType,
            SourcePath = sourcePath,
            Notation = notation,
            SemanticType = semanticType,
            Name = name,
            Description = description,
            Tags = tags,
            Properties = properties,
            Governance = new ModelingGovernanceDto
            {
                TrustLevel = "demo",
                ReviewState = "approved",
                SyncState = "fresh",
                DataSource = "Tempo.Blazor Demo"
            }
        };

    private static ModelingRelationshipDto Relationship(
        string id,
        string sourceId,
        string sourceType,
        string sourceElementId,
        string targetElementId,
        string relationshipType,
        string name,
        List<string> tags,
        Dictionary<string, JsonElement> properties)
        => new()
        {
            Id = id,
            SourceId = sourceId,
            SourceType = sourceType,
            SourceElementId = sourceElementId,
            TargetElementId = targetElementId,
            RelationshipType = relationshipType,
            Name = name,
            Tags = tags,
            Properties = properties
        };

    private static JsonElement Json<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}
