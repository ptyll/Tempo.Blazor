using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 2 / decision <c>MODELING-AUTHORING-CHANGESET</c>: <see cref="ModelingChangeSetTranslator"/>
/// turns the v2 change set into ONE ordered engine batch. The order is part of the contract
/// (adds → content updates → relationship removals → relationship adds → relationship updates →
/// element removals → layout), the additive property payload rides directly on the add/update
/// operations, and a change set that names one id in more than one of the
/// added/updated/removed sections is self-contradictory: it is reported and NOTHING is applied.
/// </summary>
public sealed class ModelingChangeSetTranslatorTests
{
    [Fact]
    public void An_empty_change_set_translates_into_an_empty_successful_batch()
    {
        var translation = ModelingChangeSetTranslator.ToOperationBatch(new ModelingModelChangeSet());

        translation.Success.Should().BeTrue();
        translation.Errors.Should().BeEmpty();
        translation.Batch.Operations.Should().BeEmpty();
    }

    [Fact]
    public void An_element_added_and_removed_in_the_same_change_set_is_rejected()
    {
        var changes = new ModelingModelChangeSet
        {
            AddedElements = [new ModelingElementAdd { Id = "task-9", SemanticType = "Task", Name = "Pack" }],
            RemovedElementIds = ["task-9"]
        };

        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);

        translation.Success.Should().BeFalse();
        translation.Batch.Operations.Should().BeEmpty("the contradictory change set applies nothing");
        var error = translation.Errors.Should().ContainSingle().Subject;
        error.Index.Should().Be(-1);
        error.Code.Should().Be("duplicate_id");
        error.Message.Should().Be("changes: id 'task-9' appears in more than one of added/updated/removed.");
    }

    [Fact]
    public void A_relationship_updated_and_removed_in_the_same_change_set_is_rejected()
    {
        var changes = new ModelingModelChangeSet
        {
            Relationships = [new ModelingRelationshipChange { RelationshipId = "flow-1", Name = "next" }],
            RemovedRelationshipIds = ["flow-1"]
        };

        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);

        translation.Success.Should().BeFalse();
        translation.Batch.Operations.Should().BeEmpty();
        translation.Errors.Should().ContainSingle().Which.Message
            .Should().Be("changes: id 'flow-1' appears in more than one of added/updated/removed.");
    }

    [Fact]
    public void An_id_named_in_all_three_sections_is_reported_once()
    {
        var changes = new ModelingModelChangeSet
        {
            AddedElements = [new ModelingElementAdd { Id = "task-9", SemanticType = "Task", Name = "Pack" }],
            Elements = [new ModelingElementChange { ElementId = "task-9", Name = "Packed" }],
            RemovedElementIds = ["task-9"]
        };

        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);

        translation.Errors.Should().ContainSingle("one error per offending id, not one per section pair");
    }

    [Fact]
    public void A_rejected_change_set_leaves_a_model_untouched_when_its_batch_is_applied_anyway()
    {
        var model = new ModelingModelDto { Id = "model", Notation = "bpmn" };
        model.Elements.Add(new ModelingElementDto { Id = "task-1", SemanticType = "Task", Name = "Approve" });
        var before = JsonSerializer.Serialize(model);

        var translation = ModelingChangeSetTranslator.ToOperationBatch(new ModelingModelChangeSet
        {
            Elements = [new ModelingElementChange { ElementId = "task-1", Name = "Renamed" }],
            RemovedElementIds = ["task-1"]
        });

        var outcome = ModelingOperationEngine.Apply(model, translation.Batch);

        translation.Success.Should().BeFalse();
        outcome.Applied.Should().Be(0);
        JsonSerializer.Serialize(model).Should().Be(before);
    }

    [Fact]
    public void Translates_the_change_set_in_the_documented_operation_order()
    {
        var changes = new ModelingModelChangeSet
        {
            AddedElements =
            [
                new ModelingElementAdd
                {
                    Id = "lane-3",
                    SemanticType = "Lane",
                    Name = "Warehouse",
                    ParentId = "pool-1",
                    Properties = new Dictionary<string, JsonElement> { ["owner"] = JsonSerializer.SerializeToElement("Ops") }
                },
                new ModelingElementAdd { Id = "task-9", SemanticType = "Task", Name = "Pack", ParentId = "lane-3" }
            ],
            Elements =
            [
                new ModelingElementChange
                {
                    ElementId = "task-1",
                    Name = "Approve the order",
                    ParentId = "lane-3",
                    SetProperties = new Dictionary<string, JsonElement> { ["priority"] = JsonSerializer.SerializeToElement(1) }
                }
            ],
            RemovedRelationshipIds = ["flow-1"],
            AddedRelationships =
            [
                new ModelingRelationshipAdd
                {
                    Id = "flow-9",
                    RelationshipType = "SequenceFlow",
                    SourceElementId = "task-9",
                    TargetElementId = "task-2",
                    Properties = new Dictionary<string, JsonElement> { ["condition"] = JsonSerializer.SerializeToElement("picked") }
                }
            ],
            Relationships = [new ModelingRelationshipChange { RelationshipId = "flow-2", Name = "re-targeted" }],
            RemovedElementIds = ["event-1"],
            ViewChanges =
            [
                new ModelingViewChange
                {
                    ViewId = "main-view",
                    Nodes = [new ModelingViewNodeChange { ElementId = "task-9", X = 10, Y = 20 }],
                    RemovedNodeElementIds = ["task-2"],
                    Connections = [new ModelingViewConnectionChange { RelationshipId = "flow-9" }],
                    RemovedConnectionRelationshipIds = ["flow-3"]
                }
            ]
        };

        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);

        translation.Success.Should().BeTrue(string.Join(" | ", translation.Errors.Select(error => error.Message)));
        translation.Batch.Operations.Select(operation => operation.Op).Should().Equal(
            "add_element",
            "add_element",
            "update_element",
            "delete_relationship",
            "add_relationship",
            "update_relationship",
            "delete_element",
            "set_view_node",
            "remove_view_node",
            "set_view_connection",
            "remove_view_connection");
    }

    [Fact]
    public void The_property_payload_rides_on_the_add_and_update_operations()
    {
        // No follow-up set_properties op: add_element/update_element/add_relationship/
        // update_relationship all accept the same optional properties/removedProperties payload.
        var changes = new ModelingModelChangeSet
        {
            AddedElements =
            [
                new ModelingElementAdd
                {
                    Id = "task-9",
                    SemanticType = "Task",
                    Name = "Pack",
                    Properties = new Dictionary<string, JsonElement> { ["priority"] = JsonSerializer.SerializeToElement(2) }
                }
            ],
            Elements =
            [
                new ModelingElementChange
                {
                    ElementId = "task-1",
                    RemovedProperties = ["obsolete"]
                }
            ],
            AddedRelationships =
            [
                new ModelingRelationshipAdd
                {
                    Id = "flow-9",
                    RelationshipType = "SequenceFlow",
                    SourceElementId = "task-9",
                    TargetElementId = "task-2",
                    Properties = new Dictionary<string, JsonElement> { ["condition"] = JsonSerializer.SerializeToElement("picked") }
                }
            ],
            Relationships =
            [
                new ModelingRelationshipChange
                {
                    RelationshipId = "flow-2",
                    SetProperties = new Dictionary<string, JsonElement> { ["condition"] = JsonSerializer.SerializeToElement("done") }
                }
            ]
        };

        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);

        translation.Batch.Operations.Select(operation => operation.Op).Should().NotContain("set_properties");
        var addedElement = translation.Batch.Operations[0];
        addedElement.Op.Should().Be("add_element");
        addedElement.Properties!["priority"].GetInt32().Should().Be(2);

        var updatedElement = translation.Batch.Operations[1];
        updatedElement.Op.Should().Be("update_element");
        updatedElement.RemovedProperties.Should().Equal("obsolete");

        var addedRelationship = translation.Batch.Operations[2];
        addedRelationship.Op.Should().Be("add_relationship");
        addedRelationship.Properties!["condition"].GetString().Should().Be("picked");

        var updatedRelationship = translation.Batch.Operations[3];
        updatedRelationship.Op.Should().Be("update_relationship");
        updatedRelationship.Properties!["condition"].GetString().Should().Be("done");
    }

    [Fact]
    public void A_change_entry_with_no_fields_emits_no_operation()
    {
        var translation = ModelingChangeSetTranslator.ToOperationBatch(new ModelingModelChangeSet
        {
            Elements = [new ModelingElementChange { ElementId = "task-1" }],
            Relationships = [new ModelingRelationshipChange { RelationshipId = "flow-1" }]
        });

        translation.Success.Should().BeTrue();
        translation.Batch.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_a_null_change_set()
    {
        var act = () => ModelingChangeSetTranslator.ToOperationBatch(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
