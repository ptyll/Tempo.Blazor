using System.Text.Json;
using System.Text.Json.Nodes;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class ModelingDtoSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static TheoryData<object, Type> DtoSamples => new()
    {
        {
            new ModelingGovernanceDto
            {
                TrustLevel = "trusted",
                ReviewState = "approved",
                SyncState = "current",
                DataSource = "prompthelper"
            },
            typeof(ModelingGovernanceDto)
        },
        {
            new ModelingElementDto
            {
                Id = "element-1",
                SourceId = "src-1",
                SourceType = "requirement",
                SourcePath = "/process/order",
                Notation = "bpmn",
                SemanticType = "task",
                Name = "Approve order",
                Description = "Human approval step",
                ParentId = "element-0",
                Properties =
                {
                    ["priority"] = JsonSerializer.SerializeToElement("high"),
                    ["slaHours"] = JsonSerializer.SerializeToElement(24),
                    ["manual"] = JsonSerializer.SerializeToElement(true)
                },
                Tags = ["ops", "approval"],
                Governance = new ModelingGovernanceDto
                {
                    TrustLevel = "trusted",
                    ReviewState = "reviewed",
                    SyncState = "synced",
                    DataSource = "demo"
                }
            },
            typeof(ModelingElementDto)
        },
        {
            new ModelingRelationshipDto
            {
                Id = "relationship-1",
                SourceId = "rel-src-1",
                SourceType = "edge",
                SourceElementId = "element-1",
                TargetElementId = "element-2",
                RelationshipType = "sequenceFlow",
                Name = "continues",
                Properties =
                {
                    ["condition"] = JsonSerializer.SerializeToElement("approved")
                },
                Tags = ["happy-path"]
            },
            typeof(ModelingRelationshipDto)
        },
        {
            new ModelingViewNodeDto
            {
                ElementId = "element-1",
                X = 10,
                Y = 20,
                Width = 180,
                Height = 80,
                ParentNodeId = "pool-1"
            },
            typeof(ModelingViewNodeDto)
        },
        {
            new ModelingViewWaypointDto
            {
                X = 32,
                Y = 64
            },
            typeof(ModelingViewWaypointDto)
        },
        {
            new ModelingViewConnectionDto
            {
                RelationshipId = "relationship-1",
                SourceNodeId = "node-1",
                TargetNodeId = "node-2",
                Waypoints =
                [
                    new ModelingViewWaypointDto { X = 120, Y = 40 },
                    new ModelingViewWaypointDto { X = 160, Y = 90 }
                ]
            },
            typeof(ModelingViewConnectionDto)
        },
        {
            new ModelingViewDto
            {
                Id = "view-1",
                Name = "Process overview",
                Notation = "bpmn",
                ViewpointKey = "operations",
                Nodes =
                [
                    new ModelingViewNodeDto
                    {
                        ElementId = "element-1",
                        X = 10,
                        Y = 20,
                        Width = 180,
                        Height = 80
                    }
                ],
                Connections =
                [
                    new ModelingViewConnectionDto
                    {
                        RelationshipId = "relationship-1",
                        SourceNodeId = "element-1",
                        TargetNodeId = "element-2"
                    }
                ]
            },
            typeof(ModelingViewDto)
        },
        {
            new ModelingMetadataDto
            {
                SourceSystem = "PromptHelper",
                SourceVersion = "2026.6",
                LoadedAt = new DateTimeOffset(2026, 6, 6, 8, 30, 0, TimeSpan.Zero),
                IsFresh = true
            },
            typeof(ModelingMetadataDto)
        },
        {
            new ModelingIssueDto
            {
                Id = "issue-1",
                Severity = ModelingIssueSeverity.Warning,
                Category = "mapping",
                SourceElementId = "element-1",
                SourceRelationshipId = "relationship-1",
                Message = "Unsupported semantic type.",
                SuggestedFix = "Choose a supported notation profile."
            },
            typeof(ModelingIssueDto)
        },
        {
            new ModelingModelDto
            {
                Id = "model-1",
                Title = "Order process",
                Notation = "bpmn",
                SupportedNotations = ["bpmn", "archimate"],
                Elements =
                [
                    new ModelingElementDto
                    {
                        Id = "element-1",
                        SourceId = "src-1",
                        SourceType = "task",
                        Notation = "bpmn",
                        SemanticType = "task",
                        Name = "Approve order"
                    }
                ],
                Relationships =
                [
                    new ModelingRelationshipDto
                    {
                        Id = "relationship-1",
                        SourceElementId = "element-1",
                        TargetElementId = "element-2",
                        RelationshipType = "sequenceFlow"
                    }
                ],
                Views =
                [
                    new ModelingViewDto
                    {
                        Id = "view-1",
                        Name = "Main",
                        Notation = "bpmn"
                    }
                ],
                Issues =
                [
                    new ModelingIssueDto
                    {
                        Id = "issue-1",
                        Severity = ModelingIssueSeverity.Info,
                        Category = "load",
                        Message = "Loaded from demo provider."
                    }
                ],
                Metadata = new ModelingMetadataDto
                {
                    SourceSystem = "PromptHelper",
                    SourceVersion = "2026.6",
                    LoadedAt = new DateTimeOffset(2026, 6, 6, 8, 30, 0, TimeSpan.Zero),
                    IsFresh = true
                }
            },
            typeof(ModelingModelDto)
        }
    };

    [Theory]
    [MemberData(nameof(DtoSamples))]
    public void Dto_roundtrip_preserves_json_data(object dto, Type dtoType)
    {
        var json = JsonSerializer.Serialize(dto, dtoType, Options);
        var restored = JsonSerializer.Deserialize(json, dtoType, Options);

        restored.Should().NotBeNull();
        var restoredJson = JsonSerializer.Serialize(restored, dtoType, Options);
        JsonNode.DeepEquals(JsonNode.Parse(restoredJson), JsonNode.Parse(json)).Should().BeTrue();
    }

    [Fact]
    public void Empty_collections_deserialize_as_empty_arrays_not_null()
    {
        const string json = """
            {
              "supportedNotations": null,
              "elements": null,
              "relationships": null,
              "views": null,
              "issues": null
            }
            """;

        var model = JsonSerializer.Deserialize<ModelingModelDto>(json, Options)!;

        model.SupportedNotations.Should().BeEmpty();
        model.Elements.Should().BeEmpty();
        model.Relationships.Should().BeEmpty();
        model.Views.Should().BeEmpty();
        model.Issues.Should().BeEmpty();

        var serialized = JsonSerializer.Serialize(model, Options);
        using var document = JsonDocument.Parse(serialized);
        document.RootElement.GetProperty(nameof(ModelingModelDto.Elements)).ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetProperty(nameof(ModelingModelDto.Relationships)).ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetProperty(nameof(ModelingModelDto.Views)).ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetProperty(nameof(ModelingModelDto.Issues)).ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Theory]
    [InlineData(ModelingIssueSeverity.Info)]
    [InlineData(ModelingIssueSeverity.Warning)]
    [InlineData(ModelingIssueSeverity.Error)]
    public void Issue_severity_values_roundtrip(ModelingIssueSeverity severity)
    {
        var issue = new ModelingIssueDto
        {
            Id = severity.ToString(),
            Severity = severity,
            Category = "test",
            Message = "Roundtrip check"
        };

        var json = JsonSerializer.Serialize(issue, Options);
        var restored = JsonSerializer.Deserialize<ModelingIssueDto>(json, Options)!;

        restored.Severity.Should().Be(severity);
        json.Should().Contain(severity.ToString());
    }

    [Fact]
    public void Element_with_null_properties_deserializes_to_empty_dictionary()
    {
        const string json = """
            {
              "id": "element-1",
              "name": "Element",
              "properties": null
            }
            """;

        var element = JsonSerializer.Deserialize<ModelingElementDto>(json, Options)!;

        element.Properties.Should().NotBeNull();
        element.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Element_with_null_name_deserializes_to_empty_string()
    {
        const string json = """
            {
              "id": "element-1",
              "name": null
            }
            """;

        var element = JsonSerializer.Deserialize<ModelingElementDto>(json, Options)!;

        element.Name.Should().BeEmpty();
    }

    [Fact]
    public void ParentId_roundtrips_and_defaults_to_empty()
    {
        // Default: no parent (the additive phase-12 field starts empty for every existing model).
        new ModelingElementDto().ParentId.Should().BeEmpty();

        var element = new ModelingElementDto { Id = "child", ParentId = "parent-1" };
        element.ParentId.Should().Be("parent-1");

        var json = JsonSerializer.Serialize(element, Options);
        var restored = JsonSerializer.Deserialize<ModelingElementDto>(json, Options)!;
        restored.ParentId.Should().Be("parent-1");

        // The wire contract is camelCase (the shape the modeling providers emit).
        const string camelJson = """{"id":"child","parentId":"parent-1"}""";
        JsonSerializer.Deserialize<ModelingElementDto>(camelJson, Options)!
            .ParentId.Should().Be("parent-1");
    }

    [Fact]
    public void ParentId_null_normalizes_to_empty_string()
    {
        var element = new ModelingElementDto { Id = "child", ParentId = null };

        element.ParentId.Should().BeEmpty("the setter normalizes null to the empty string");
    }

    [Fact]
    public void Legacy_payload_without_parentId_deserializes()
    {
        const string json = """
            {
              "id": "legacy-1",
              "name": "Legacy element",
              "semanticType": "userTask"
            }
            """;

        var element = JsonSerializer.Deserialize<ModelingElementDto>(json, Options)!;

        element.Id.Should().Be("legacy-1");
        element.Name.Should().Be("Legacy element");
        element.ParentId.Should().BeEmpty("older payloads without the field deserialize unchanged");
    }

    [Fact]
    public void Empty_model_with_no_elements_and_relationships_is_valid()
    {
        var model = new ModelingModelDto
        {
            Id = "empty-model",
            Title = "Empty model",
            Notation = "bpmn"
        };

        model.Elements.Should().BeEmpty();
        model.Relationships.Should().BeEmpty();

        var json = JsonSerializer.Serialize(model, Options);
        var restored = JsonSerializer.Deserialize<ModelingModelDto>(json, Options)!;

        restored.Elements.Should().BeEmpty();
        restored.Relationships.Should().BeEmpty();
        restored.Metadata.Should().NotBeNull();
    }
}
