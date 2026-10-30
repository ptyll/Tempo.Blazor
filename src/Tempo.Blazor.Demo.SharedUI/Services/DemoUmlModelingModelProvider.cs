using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Demo.SharedUI.Services;

/// <summary>
/// Second demo modeling provider (modeling redesign phase 3): a small UML 2.5 class model so the
/// demo's provider selector lists more than one registered source. The default
/// <c>tempo.demo.modeling</c> provider keeps serving the mixed BPMN/ArchiMate model.
/// </summary>
public sealed class DemoUmlModelingModelProvider : IModelingModelProvider
{
    /// <summary>Provider key of the demo UML source.</summary>
    public const string ProviderKeyValue = "tempo.demo.modeling.uml";

    private static readonly DateTimeOffset LoadedAt = new(2026, 6, 6, 9, 30, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public string ProviderKey => ProviderKeyValue;

    /// <inheritdoc />
    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateModel());
    }

    private static ModelingModelDto CreateModel()
    {
        return new ModelingModelDto
        {
            Id = "demo-uml-modeling-model",
            Title = "Demo order domain model",
            Notation = "uml25",
            SupportedNotations = ["uml25"],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Tempo.Blazor Demo",
                SourceVersion = "2026.06-uml",
                LoadedAt = LoadedAt,
                IsFresh = true
            },
            Elements =
            [
                Element("uml-package-order", "Package", "Order domain", "demo/uml/order-domain",
                    description: "UML package containing the order domain classifiers.",
                    properties: new()
                    {
                        ["stencilId"] = Json("uml25.package")
                    }),
                Element("uml-order", "Class", "Order", "demo/uml/order",
                    parentId: "uml-package-order",
                    properties: new()
                    {
                        ["stencilId"] = Json("uml2.class"),
                        ["attributes"] = Json("- id: Guid\n- number: string\n- total: decimal"),
                        ["operations"] = Json("+ Submit(): void")
                    }),
                Element("uml-customer", "Class", "Customer", "demo/uml/customer",
                    parentId: "uml-package-order",
                    properties: new()
                    {
                        ["stencilId"] = Json("uml2.class"),
                        ["attributes"] = Json("- name: string\n- email: string")
                    }),
                Element("uml-order-repository", "Interface", "IOrderRepository", "demo/uml/order-repository",
                    parentId: "uml-package-order",
                    properties: new()
                    {
                        ["stencilId"] = Json("uml2.interface"),
                        ["operations"] = Json("+ Get(id: Guid): Order")
                    })
            ],
            Relationships =
            [
                Relationship("uml-customer-places-order", "uml-customer", "uml-order", "Association", "places"),
                Relationship("uml-order-realizes-repository", "uml-order", "uml-order-repository", "Realization", string.Empty)
            ],
            Views =
            [
                new ModelingViewDto
                {
                    Id = "demo-uml-overview",
                    Name = "Domain overview",
                    Notation = "uml25",
                    ViewpointKey = "ClassDiagram",
                    Nodes =
                    [
                        new() { ElementId = "uml-customer", X = 80, Y = 120, Width = 190, Height = 120 },
                        new() { ElementId = "uml-order", X = 360, Y = 110, Width = 220, Height = 150 },
                        new() { ElementId = "uml-order-repository", X = 660, Y = 130, Width = 220, Height = 100 }
                    ],
                    Connections =
                    [
                        new() { RelationshipId = "uml-customer-places-order", SourceNodeId = "uml-customer", TargetNodeId = "uml-order" },
                        new() { RelationshipId = "uml-order-realizes-repository", SourceNodeId = "uml-order", TargetNodeId = "uml-order-repository" }
                    ]
                }
            ]
        };
    }

    private static ModelingElementDto Element(
        string id,
        string semanticType,
        string name,
        string sourcePath,
        string? parentId = null,
        string? description = null,
        Dictionary<string, JsonElement>? properties = null)
        => new()
        {
            Id = id,
            SourceId = $"demo/uml/{id}",
            SourceType = $"uml-{semanticType.ToLowerInvariant()}",
            SourcePath = sourcePath,
            Notation = "uml25",
            SemanticType = semanticType,
            Name = name,
            Description = description ?? $"{name} from the demo UML source.",
            Tags = ["uml", "demo"],
            ParentId = parentId ?? string.Empty,
            Properties = properties ?? [],
            Governance = new ModelingGovernanceDto
            {
                TrustLevel = "demo",
                ReviewState = "reviewed",
                SyncState = "fresh"
            }
        };

    private static ModelingRelationshipDto Relationship(
        string id,
        string sourceElementId,
        string targetElementId,
        string relationshipType,
        string name)
        => new()
        {
            Id = id,
            SourceId = $"demo/uml/{id}",
            SourceType = "uml-relationship",
            SourceElementId = sourceElementId,
            TargetElementId = targetElementId,
            RelationshipType = relationshipType,
            Name = string.IsNullOrWhiteSpace(name) ? relationshipType : name,
            Tags = ["uml", "demo"],
            Properties = new Dictionary<string, JsonElement>()
        };

    private static JsonElement Json(string value) => JsonSerializer.SerializeToElement(value);
}
