using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 2 / decision <c>MODELING-AUTHORING-CHANGESET</c>: the change set v2 wire contract.
/// <para>
/// The load-bearing rule is "a <c>null</c> member means unchanged" — it now covers structure
/// (added/removed items, <c>ParentId</c>, re-targeting) and layout (<c>ModelingViewChange</c>),
/// while <c>""</c> / <c>[]</c> stay REAL values (clear the parent, clear the waypoints). These
/// tests pin the shape a host writer receives through <c>OnModelChanged</c> and
/// <c>IModelingModelWriter</c>, so a phase-3 command that emits the wrong sentinel is caught here.
/// </para>
/// </summary>
public sealed class ModelingChangeSetV2SerializationTests
{
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    private static string Serialize(ModelingModelChangeSet changeSet)
        => JsonSerializer.Serialize(changeSet, WireOptions);

    [Fact]
    public void Empty_change_set_defaults_to_empty_lists_and_no_flags()
    {
        var changeSet = new ModelingModelChangeSet();

        changeSet.AddedElements.Should().BeEmpty();
        changeSet.AddedRelationships.Should().BeEmpty();
        changeSet.RemovedElementIds.Should().BeEmpty();
        changeSet.RemovedRelationshipIds.Should().BeEmpty();
        changeSet.ViewChanges.Should().BeEmpty();
        changeSet.HasStructuralChanges.Should().BeFalse();
        changeSet.HasViewChanges.Should().BeFalse();
    }

    [Fact]
    public void Null_lists_assigned_to_the_change_set_are_normalized_to_empty()
    {
        var changeSet = new ModelingModelChangeSet
        {
            AddedElements = null!,
            AddedRelationships = null!,
            RemovedElementIds = null!,
            RemovedRelationshipIds = null!,
            ViewChanges = null!
        };

        changeSet.AddedElements.Should().NotBeNull().And.BeEmpty();
        changeSet.AddedRelationships.Should().NotBeNull().And.BeEmpty();
        changeSet.RemovedElementIds.Should().NotBeNull().And.BeEmpty();
        changeSet.RemovedRelationshipIds.Should().NotBeNull().And.BeEmpty();
        changeSet.ViewChanges.Should().NotBeNull().And.BeEmpty();
        changeSet.HasStructuralChanges.Should().BeFalse();
    }

    [Fact]
    public void Full_structural_and_layout_change_set_round_trips_through_camel_case_json()
    {
        var changeSet = CreateFullChangeSet();

        var json = Serialize(changeSet);
        using var document = Parse(json);
        var root = document.RootElement;

        root.GetProperty("modelId").GetString().Should().Be("order-process");
        root.GetProperty("providerKey").GetString().Should().Be("prompthelper-processes");
        root.GetProperty("baseVersion").GetString().Should().Be("2026.11.3");

        var addedElement = root.GetProperty("addedElements")[0];
        addedElement.GetProperty("id").GetString().Should().Be("el-1");
        addedElement.GetProperty("semanticType").GetString().Should().Be("Pool");
        addedElement.GetProperty("name").GetString().Should().Be("Order handling");
        addedElement.GetProperty("parentId").GetString().Should().BeEmpty();
        addedElement.GetProperty("properties").GetProperty("mode").GetString().Should().Be("auto");

        var addedRelationship = root.GetProperty("addedRelationships")[0];
        addedRelationship.GetProperty("relationshipType").GetString().Should().Be("SequenceFlow");
        addedRelationship.GetProperty("sourceElementId").GetString().Should().Be("el-3");
        addedRelationship.GetProperty("targetElementId").GetString().Should().Be("el-4");

        root.GetProperty("elements")[0].GetProperty("parentId").GetString().Should().Be("el-2");
        root.GetProperty("relationships")[0].GetProperty("sourceElementId").GetString().Should().Be("el-5");
        root.GetProperty("removedElementIds")[0].GetString().Should().Be("el-old");
        root.GetProperty("removedRelationshipIds")[0].GetString().Should().Be("rel-old");

        var viewChange = root.GetProperty("viewChanges")[0];
        viewChange.GetProperty("viewId").GetString().Should().Be("view-default");
        viewChange.GetProperty("nodes")[0].GetProperty("x").GetDouble().Should().Be(80);
        viewChange.GetProperty("nodes")[1].GetProperty("parentNodeId").GetString().Should().Be("el-1");
        viewChange.GetProperty("removedNodeElementIds")[0].GetString().Should().Be("el-gone");
        viewChange.GetProperty("connections")[0].GetProperty("waypoints")[0].GetProperty("x").GetDouble().Should().Be(420);
        viewChange.GetProperty("removedConnectionRelationshipIds")[0].GetString().Should().Be("rel-gone");

        var restored = JsonSerializer.Deserialize<ModelingModelChangeSet>(json, WireOptions)!;
        restored.AddedElements.Should().HaveCount(2);
        restored.AddedElements[0].Properties["mode"].GetString().Should().Be("auto");
        restored.AddedRelationships.Should().HaveCount(1);
        restored.RemovedElementIds.Should().Equal("el-old");
        restored.RemovedRelationshipIds.Should().Equal("rel-old");
        restored.Elements[0].ParentId.Should().Be("el-2");
        restored.Relationships[0].SourceElementId.Should().Be("el-5");
        restored.Relationships[0].TargetElementId.Should().Be("el-6");
        restored.ViewChanges.Should().HaveCount(1);
        restored.ViewChanges[0].Nodes[1].ParentNodeId.Should().Be("el-1");
        restored.ViewChanges[0].Connections[0].Waypoints.Should().HaveCount(1);
        restored.ViewChanges[0].Connections[0].Waypoints![0].X.Should().Be(420);
        restored.HasStructuralChanges.Should().BeTrue();
        restored.HasViewChanges.Should().BeTrue();
    }

    [Fact]
    public void Unchanged_nullable_members_stay_absent_from_the_wire()
    {
        var changeSet = new ModelingModelChangeSet
        {
            ModelId = "model-1",
            Elements = [new ModelingElementChange { ElementId = "element-1", Name = "Renamed" }],
            Relationships = [new ModelingRelationshipChange { RelationshipId = "rel-1", Name = "Renamed" }]
        };

        using var document = Parse(Serialize(changeSet));
        var root = document.RootElement;

        var element = root.GetProperty("elements")[0];
        element.TryGetProperty("parentId", out _).Should().BeFalse(
            "null means unchanged — a writer must not see a re-parent intent");
        element.TryGetProperty("description", out _).Should().BeFalse();

        var relationship = root.GetProperty("relationships")[0];
        relationship.TryGetProperty("sourceElementId", out _).Should().BeFalse();
        relationship.TryGetProperty("targetElementId", out _).Should().BeFalse();

        root.GetProperty("addedElements").GetArrayLength().Should().Be(0);
        root.TryGetProperty("removedElementIds", out var removed).Should().BeTrue();
        removed.GetArrayLength().Should().Be(0);

        // A pure content change is NOT structural.
        changeSet.HasStructuralChanges.Should().BeFalse();
        changeSet.HasViewChanges.Should().BeFalse();
    }

    [Fact]
    public void Empty_string_is_a_real_value_and_never_collapses_into_null()
    {
        var changeSet = new ModelingModelChangeSet
        {
            Elements = [new ModelingElementChange { ElementId = "element-1", ParentId = string.Empty }],
            ViewChanges =
            [
                new ModelingViewChange
                {
                    ViewId = "view-1",
                    Nodes = [new ModelingViewNodeChange { ElementId = "element-1", ParentNodeId = string.Empty }],
                    Connections = [new ModelingViewConnectionChange { RelationshipId = "rel-1", Waypoints = [] }]
                }
            ]
        };

        var json = Serialize(changeSet);
        using var document = Parse(json);
        var root = document.RootElement;

        root.GetProperty("elements")[0].GetProperty("parentId").GetString().Should().BeEmpty();
        root.GetProperty("viewChanges")[0].GetProperty("nodes")[0]
            .GetProperty("parentNodeId").GetString().Should().BeEmpty();
        // An EMPTY list clears the waypoints — distinct from null (unchanged, absent on the wire).
        root.GetProperty("viewChanges")[0].GetProperty("connections")[0]
            .GetProperty("waypoints").GetArrayLength().Should().Be(0);

        var restored = JsonSerializer.Deserialize<ModelingModelChangeSet>(json, WireOptions)!;
        restored.Elements[0].ParentId.Should().BeEmpty();
        restored.ViewChanges[0].Nodes[0].ParentNodeId.Should().BeEmpty();
        restored.ViewChanges[0].Connections[0].Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void Null_waypoints_mean_unchanged_and_are_absent_from_the_wire()
    {
        var changeSet = new ModelingModelChangeSet
        {
            ViewChanges =
            [
                new ModelingViewChange
                {
                    ViewId = "view-1",
                    Connections = [new ModelingViewConnectionChange { RelationshipId = "rel-1" }]
                }
            ]
        };

        using var document = Parse(Serialize(changeSet));
        var connection = document.RootElement.GetProperty("viewChanges")[0].GetProperty("connections")[0];
        connection.TryGetProperty("waypoints", out _).Should().BeFalse();
        changeSet.ViewChanges[0].Connections[0].Waypoints.Should().BeNull();
    }

    [Fact]
    public void Computed_change_flags_are_never_serialized()
    {
        // System.Text.Json serializes get-only properties: without [JsonIgnore] a host writer would
        // receive two read-only booleans that its own serializer would then try to bind.
        var json = JsonSerializer.Serialize(CreateFullChangeSet());

        json.Should().NotContain("hasStructuralChanges");
        json.Should().NotContain("HasStructuralChanges");
        json.Should().NotContain("hasViewChanges");
        json.Should().NotContain("HasViewChanges");
    }

    [Fact]
    public void HasStructuralChanges_covers_adds_removals_parent_moves_and_retargeting()
    {
        new ModelingModelChangeSet().HasStructuralChanges.Should().BeFalse();

        new ModelingModelChangeSet
        {
            AddedElements = [new ModelingElementAdd { Id = "el-1", Name = "Task", SemanticType = "Task" }]
        }.HasStructuralChanges.Should().BeTrue();

        new ModelingModelChangeSet
        {
            AddedRelationships = [new ModelingRelationshipAdd { Id = "rel-1", RelationshipType = "SequenceFlow", SourceElementId = "a", TargetElementId = "b" }]
        }.HasStructuralChanges.Should().BeTrue();

        new ModelingModelChangeSet { RemovedElementIds = ["el-1"] }.HasStructuralChanges.Should().BeTrue();
        new ModelingModelChangeSet { RemovedRelationshipIds = ["rel-1"] }.HasStructuralChanges.Should().BeTrue();

        new ModelingModelChangeSet
        {
            Elements = [new ModelingElementChange { ElementId = "el-1", ParentId = "lane-1" }]
        }.HasStructuralChanges.Should().BeTrue("a parent move is a structural change");

        new ModelingModelChangeSet
        {
            Relationships = [new ModelingRelationshipChange { RelationshipId = "rel-1", TargetElementId = "el-2" }]
        }.HasStructuralChanges.Should().BeTrue("re-targeting is a structural change");
    }

    [Fact]
    public void HasViewChanges_only_tracks_the_view_list()
    {
        new ModelingModelChangeSet
        {
            ViewChanges = [new ModelingViewChange { ViewId = "view-1", RemovedNodeElementIds = ["el-1"] }]
        }.HasViewChanges.Should().BeTrue();

        new ModelingModelChangeSet
        {
            RemovedElementIds = ["el-1"]
        }.HasViewChanges.Should().BeFalse();
    }

    [Fact]
    public void Legacy_change_set_payload_without_v2_members_deserializes_into_empty_lists()
    {
        // A host that serialized a 2.10.0 change set (four members only) must keep working after
        // the additive change: the engine/writer sees an empty structure and an empty layout.
        const string json = """
            {
              "modelId": "model-1",
              "providerKey": "provider-1",
              "baseVersion": "2026.06",
              "elements": [ { "elementId": "element-1", "name": "Renamed" } ],
              "relationships": []
            }
            """;

        var changeSet = JsonSerializer.Deserialize<ModelingModelChangeSet>(json, WireOptions)!;

        changeSet.Elements.Should().HaveCount(1);
        changeSet.Elements[0].ParentId.Should().BeNull();
        changeSet.Relationships.Should().BeEmpty();
        changeSet.AddedElements.Should().NotBeNull().And.BeEmpty();
        changeSet.AddedRelationships.Should().NotBeNull().And.BeEmpty();
        changeSet.RemovedElementIds.Should().NotBeNull().And.BeEmpty();
        changeSet.RemovedRelationshipIds.Should().NotBeNull().And.BeEmpty();
        changeSet.ViewChanges.Should().NotBeNull().And.BeEmpty();
        changeSet.HasStructuralChanges.Should().BeFalse();
    }

    [Fact]
    public void Null_v2_lists_in_json_deserialize_into_empty_lists()
    {
        const string json = """
            {
              "modelId": "model-1",
              "addedElements": null,
              "addedRelationships": null,
              "removedElementIds": null,
              "removedRelationshipIds": null,
              "viewChanges": null
            }
            """;

        var changeSet = JsonSerializer.Deserialize<ModelingModelChangeSet>(json, WireOptions)!;

        changeSet.AddedElements.Should().NotBeNull().And.BeEmpty();
        changeSet.AddedRelationships.Should().NotBeNull().And.BeEmpty();
        changeSet.RemovedElementIds.Should().NotBeNull().And.BeEmpty();
        changeSet.RemovedRelationshipIds.Should().NotBeNull().And.BeEmpty();
        changeSet.ViewChanges.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Added_element_defaults_are_safe_to_read()
    {
        var added = new ModelingElementAdd();

        added.Id.Should().BeEmpty();
        added.SemanticType.Should().BeEmpty();
        added.Name.Should().BeEmpty();
        added.Notation.Should().BeEmpty();
        added.Description.Should().BeEmpty();
        added.ParentId.Should().BeEmpty("empty = a root element (pool/lane/subprocess containment)");
        added.Properties.Should().NotBeNull().And.BeEmpty();

        added.Name = null!;
        added.Properties = null!;
        added.Name.Should().BeEmpty();
        added.Properties.Should().NotBeNull().And.BeEmpty();

        var addedRelationship = new ModelingRelationshipAdd();
        addedRelationship.RelationshipType.Should().BeEmpty();
        addedRelationship.SourceElementId.Should().BeEmpty();
        addedRelationship.TargetElementId.Should().BeEmpty();
        addedRelationship.Properties.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void View_node_change_only_emits_the_geometry_it_sets()
    {
        var changeSet = new ModelingModelChangeSet
        {
            ViewChanges =
            [
                new ModelingViewChange
                {
                    Nodes = [new ModelingViewNodeChange { ElementId = "element-1", Y = 140 }]
                }
            ]
        };

        using var document = Parse(Serialize(changeSet));
        var node = document.RootElement.GetProperty("viewChanges")[0].GetProperty("nodes")[0];
        node.GetProperty("elementId").GetString().Should().Be("element-1");
        node.GetProperty("y").GetDouble().Should().Be(140);
        node.TryGetProperty("x", out _).Should().BeFalse("an unchanged coordinate is omitted, not zero");
        node.TryGetProperty("width", out _).Should().BeFalse();
        node.TryGetProperty("height", out _).Should().BeFalse();
        node.TryGetProperty("parentNodeId", out _).Should().BeFalse();

        var restored = JsonSerializer.Deserialize<ModelingModelChangeSet>(
            JsonSerializer.Serialize(changeSet, WireOptions), WireOptions)!;
        restored.ViewChanges[0].Nodes[0].X.Should().BeNull();
        restored.ViewChanges[0].Nodes[0].Y.Should().Be(140);
        restored.ViewChanges[0].ViewId.Should().BeEmpty("empty = the model's default view");
    }

    private static ModelingModelChangeSet CreateFullChangeSet() => new()
    {
        ModelId = "order-process",
        ProviderKey = "prompthelper-processes",
        BaseVersion = "2026.11.3",
        AddedElements =
        [
            new ModelingElementAdd
            {
                Id = "el-1",
                SemanticType = "Pool",
                Name = "Order handling",
                ParentId = string.Empty,
                Properties = new Dictionary<string, JsonElement>
                {
                    ["mode"] = JsonSerializer.SerializeToElement("auto")
                }
            },
            new ModelingElementAdd { Id = "el-2", SemanticType = "Lane", Name = "Sales", ParentId = "el-1" }
        ],
        AddedRelationships =
        [
            new ModelingRelationshipAdd
            {
                Id = "rel-1",
                RelationshipType = "SequenceFlow",
                SourceElementId = "el-3",
                TargetElementId = "el-4",
                Name = "approved"
            }
        ],
        Elements =
        [
            new ModelingElementChange { ElementId = "el-3", ParentId = "el-2" }
        ],
        Relationships =
        [
            new ModelingRelationshipChange { RelationshipId = "rel-2", SourceElementId = "el-5", TargetElementId = "el-6" }
        ],
        RemovedElementIds = ["el-old"],
        RemovedRelationshipIds = ["rel-old"],
        ViewChanges =
        [
            new ModelingViewChange
            {
                ViewId = "view-default",
                Nodes =
                [
                    new ModelingViewNodeChange { ElementId = "el-1", X = 80, Y = 80, Width = 900, Height = 260 },
                    new ModelingViewNodeChange { ElementId = "el-2", ParentNodeId = "el-1", X = 110, Y = 110 }
                ],
                RemovedNodeElementIds = ["el-gone"],
                Connections =
                [
                    new ModelingViewConnectionChange
                    {
                        RelationshipId = "rel-1",
                        Waypoints = [new ModelingViewWaypointDto { X = 420, Y = 200 }]
                    }
                ],
                RemovedConnectionRelationshipIds = ["rel-gone"]
            }
        ]
    };
}
