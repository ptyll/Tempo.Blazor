using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 2 / decision <c>MODELING-UNIFIED-OPERATION-ENGINE</c>: the ONE operation engine that both
/// the editor and <c>modeling_apply_operations</c> call. The 2.10.0 operation semantics (and their
/// exact messages) are byte-compatible; the additive surface is <c>set_properties</c> plus the
/// <c>Authoring</c>-only view operations, and every failure carries a machine-readable code next to
/// the legacy message.
/// </summary>
public sealed class ModelingOperationEngineTests
{
    // ── add_element ─────────────────────────────────────────────────────────────

    [Fact]
    public void Add_element_generates_an_id_when_none_is_supplied()
    {
        var model = CreateModel();

        var result = Apply(model, Op("add_element", ("name", "Check stock"), ("semanticType", "Task"), ("parentId", "lane-1")));

        result.Success.Should().BeTrue();
        result.Applied.Should().Be(1);
        result.CreatedIds.Should().HaveCount(1);
        var added = model.Elements.Single(element => element.Name == "Check stock");
        added.Id.Should().Be(result.CreatedIds[0]);
        added.ParentId.Should().Be("lane-1");
        added.Notation.Should().Be("bpmn", "an empty notation falls back to the model notation");
    }

    [Fact]
    public void Add_element_requires_name_and_semantic_type()
    {
        var model = CreateModel();

        var missingName = Apply(model, Op("add_element", ("semanticType", "Task")));
        missingName.Success.Should().BeFalse();
        missingName.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("missing_field");
        missingName.Errors[0].Message.Should().Be("operations[0] add_element: 'name' is required.");
        missingName.Errors[0].Op.Should().Be("add_element");
        missingName.Errors[0].Index.Should().Be(0);

        var missingType = Apply(model, Op("add_element", ("name", "Task")));
        missingType.Errors[0].Code.Should().Be("missing_field");
        missingType.Errors[0].Message.Should().Be("operations[0] add_element: 'semanticType' is required.");
    }

    [Fact]
    public void Add_element_rejects_a_duplicate_id()
    {
        var model = CreateModel();

        var result = Apply(model, Op("add_element", ("id", "task-1"), ("name", "Duplicate"), ("semanticType", "Task")));

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("duplicate_id");
        result.Errors[0].Message.Should().Be("operations[0] add_element: an element with id 'task-1' already exists.");
        result.Errors[0].ElementId.Should().Be("task-1");
    }

    [Fact]
    public void Add_element_rejects_an_unknown_parent()
    {
        var model = CreateModel();

        var result = Apply(model, Op("add_element", ("name", "Task"), ("semanticType", "Task"), ("parentId", "missing")));

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("parent_not_found");
        result.Errors[0].Message.Should().Be("operations[0] add_element: parent element 'missing' not found.");
        result.Errors[0].ElementId.Should().Be("missing");
    }

    [Fact]
    public void Add_element_accepts_a_parent_added_earlier_in_the_same_batch()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("add_element", ("id", "lane-3"), ("name", "Warehouse"), ("semanticType", "Lane"), ("parentId", "pool-1")),
            Op("add_element", ("id", "task-9"), ("name", "Pack"), ("semanticType", "Task"), ("parentId", "lane-3")));

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors.Select(error => error.Message)));
        result.Applied.Should().Be(2);
        model.Elements.Single(element => element.Id == "task-9").ParentId.Should().Be("lane-3");
    }

    // ── update_element ──────────────────────────────────────────────────────────

    [Fact]
    public void Update_element_moves_and_clears_the_parent()
    {
        var model = CreateModel();

        Apply(model, Op("update_element", ("id", "task-1"), ("parentId", "lane-2"))).Success.Should().BeTrue();
        model.Elements.Single(element => element.Id == "task-1").ParentId.Should().Be("lane-2");

        Apply(model, Op("update_element", ("id", "task-1"), ("parentId", ""))).Success.Should().BeTrue();
        model.Elements.Single(element => element.Id == "task-1").ParentId.Should().BeEmpty("an empty parentId makes the element a root");
    }

    [Fact]
    public void Update_element_moves_the_container_after_the_move_when_the_container_was_added_in_the_batch()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("add_element", ("id", "lane-3"), ("name", "Billing"), ("semanticType", "Lane"), ("parentId", "pool-1")),
            Op("update_element", ("id", "task-2"), ("parentId", "lane-3")));

        result.Success.Should().BeTrue();
        model.Elements.Single(element => element.Id == "task-2").ParentId.Should().Be("lane-3");
    }

    [Fact]
    public void Update_element_rejects_a_parent_cycle()
    {
        var model = CreateModel();

        var result = Apply(model, Op("update_element", ("id", "pool-1"), ("parentId", "task-1")));

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("parent_cycle");
        result.Errors[0].Message.Should().Be(
            "operations[0] update_element: setting 'task-1' as the parent of 'pool-1' would create a parent cycle.");
    }

    [Fact]
    public void Update_element_cannot_be_its_own_parent()
    {
        var model = CreateModel();

        var result = Apply(model, Op("update_element", ("id", "task-1"), ("parentId", "task-1")));

        result.Errors[0].Code.Should().Be("parent_cycle");
        result.Errors[0].Message.Should().Be("operations[0] update_element: element 'task-1' cannot be its own parent (cycle).");
    }

    [Fact]
    public void Update_element_reports_not_found()
    {
        var result = Apply(CreateModel(), Op("update_element", ("id", "missing"), ("name", "x")));

        result.Errors[0].Code.Should().Be("not_found");
        result.Errors[0].Message.Should().Be("operations[0] update_element: element 'missing' not found.");
        result.Errors[0].ElementId.Should().Be("missing");
    }

    // ── delete_element ──────────────────────────────────────────────────────────

    [Fact]
    public void Delete_element_requires_its_relationships_to_be_removed_first()
    {
        var model = CreateModel();

        var result = Apply(model, Op("delete_element", ("id", "task-1")));

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("still_referenced");
        result.Errors[0].Message.Should().Contain("still referenced by relationship(s) [flow-1]");
        model.Elements.Should().Contain(element => element.Id == "task-1", "a rejected batch must not delete anything");
    }

    [Fact]
    public void Delete_element_requires_its_children_to_be_removed_first()
    {
        var model = CreateModel();

        var result = Apply(model, Op("delete_element", ("id", "lane-1")));

        result.Errors[0].Code.Should().Be("still_has_children");
        result.Errors[0].Message.Should().Contain("still has child element(s)");
    }

    [Fact]
    public void Delete_element_succeeds_with_the_expanded_cascade_in_descendants_first_order()
    {
        var model = CreateModel();

        // The editor expands the cascade (§3.2 rule 4): incident relationships first, then the
        // children (descendants first), then the container. The engine never cascades on its own.
        var result = Apply(
            model,
            Op("delete_relationship", ("id", "flow-1")),
            Op("delete_element", ("id", "task-1")),
            Op("delete_element", ("id", "task-2")),
            Op("delete_element", ("id", "event-1")),
            Op("delete_element", ("id", "lane-1")));

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors.Select(error => error.Message)));
        result.Applied.Should().Be(5);
        model.Elements.Should().NotContain(element => element.Id == "lane-1" || element.Id == "task-1");
        model.Relationships.Should().BeEmpty();
    }

    [Fact]
    public void Delete_element_reports_not_found()
    {
        var result = Apply(CreateModel(), Op("delete_element", ("id", "missing")));

        result.Errors[0].Code.Should().Be("not_found");
        result.Errors[0].Message.Should().Be("operations[0] delete_element: element 'missing' not found.");
    }

    // ── relationships ───────────────────────────────────────────────────────────

    [Fact]
    public void Add_relationship_validates_the_notation_rule_and_keeps_the_legacy_message()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("add_relationship", ("relationshipType", "SequenceFlow"), ("sourceElementId", "pool-1"), ("targetElementId", "task-2")));

        result.Success.Should().BeFalse();
        var error = result.Errors[0];
        error.Code.Should().Be("invalid_relationship");
        error.Message.Should().Be(
            "operations[0] add_relationship: 'SequenceFlow' from 'Pool' to 'Task' is not valid for notation 'bpmn'. " +
            "BPMN SequenceFlow can connect only BPMN flow nodes. Connect tasks, events, gateways, subprocesses, or call activities with SequenceFlow.");
        error.SuggestedFix.Should().Be("Connect tasks, events, gateways, subprocesses, or call activities with SequenceFlow.");
        error.RelationshipId.Should().BeNull("the failed add never minted an id");
        model.Relationships.Should().HaveCount(1);
    }

    [Fact]
    public void Add_relationship_requires_existing_endpoints()
    {
        var model = CreateModel();

        var wrongSource = Apply(
            model,
            Op("add_relationship", ("relationshipType", "SequenceFlow"), ("sourceElementId", "missing"), ("targetElementId", "task-2")));
        wrongSource.Errors[0].Code.Should().Be("not_found");
        wrongSource.Errors[0].Message.Should().Be("operations[0] add_relationship: source element 'missing' not found.");

        var wrongTarget = Apply(
            model,
            Op("add_relationship", ("relationshipType", "SequenceFlow"), ("sourceElementId", "task-1"), ("targetElementId", "missing")));
        wrongTarget.Errors[0].Code.Should().Be("not_found");
        wrongTarget.Errors[0].Message.Should().Be("operations[0] add_relationship: target element 'missing' not found.");
    }

    [Fact]
    public void Add_relationship_generates_an_id_and_reports_it_as_created()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("add_relationship", ("relationshipType", "SequenceFlow"), ("sourceElementId", "task-1"), ("targetElementId", "task-2"), ("name", "next")));

        result.Success.Should().BeTrue();
        result.CreatedIds.Should().HaveCount(1);
        model.Relationships.Single(relationship => relationship.Id == result.CreatedIds[0]).Name.Should().Be("next");
    }

    [Fact]
    public void Add_relationship_rejects_a_duplicate_id()
    {
        var result = Apply(
            CreateModel(),
            Op("add_relationship", ("id", "flow-1"), ("relationshipType", "SequenceFlow"), ("sourceElementId", "task-1"), ("targetElementId", "task-2")));

        result.Errors[0].Code.Should().Be("duplicate_id");
        result.Errors[0].Message.Should().Be("operations[0] add_relationship: a relationship with id 'flow-1' already exists.");
        result.Errors[0].RelationshipId.Should().Be("flow-1");
    }

    [Fact]
    public void Update_relationship_retargets_and_revalidates()
    {
        var model = CreateModel();

        var invalid = Apply(model, Op("update_relationship", ("id", "flow-1"), ("targetElementId", "pool-1")));
        invalid.Success.Should().BeFalse();
        invalid.Errors[0].Code.Should().Be("invalid_relationship");
        model.Relationships[0].TargetElementId.Should().Be("task-1", "a rejected retarget must not mutate the model");

        var valid = Apply(model, Op("update_relationship", ("id", "flow-1"), ("targetElementId", "task-2")));
        valid.Success.Should().BeTrue();
        model.Relationships[0].TargetElementId.Should().Be("task-2");
    }

    [Fact]
    public void Delete_relationship_removes_it_and_reports_not_found()
    {
        var model = CreateModel();

        Apply(model, Op("delete_relationship", ("id", "flow-1"))).Success.Should().BeTrue();
        model.Relationships.Should().BeEmpty();

        var missing = Apply(model, Op("delete_relationship", ("id", "flow-1")));
        missing.Errors[0].Code.Should().Be("not_found");
        missing.Errors[0].Message.Should().Be("operations[0] delete_relationship: relationship 'flow-1' not found.");
    }

    // ── set_properties ──────────────────────────────────────────────────────────

    [Fact]
    public void Set_properties_updates_an_element_and_a_relationship_by_id()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("set_properties", ("id", "task-1"), ("properties", """{ "priority": 3, "lane": "Sales" }""")),
            Op("set_properties", ("id", "flow-1"), ("properties", """{ "condition": "approved" }""")));

        result.Success.Should().BeTrue();
        result.Applied.Should().Be(2);
        var element = model.Elements.Single(item => item.Id == "task-1");
        element.Properties["priority"].GetInt32().Should().Be(3);
        element.Properties["lane"].GetString().Should().Be("Sales");
        model.Relationships[0].Properties["condition"].GetString().Should().Be("approved");
    }

    [Fact]
    public void Set_properties_removes_keys_lists_in_removed_properties()
    {
        var model = CreateModel();
        model.Elements[2].Properties["obsolete"] = JsonSerializer.SerializeToElement("old");

        Apply(model, Op("set_properties", ("id", "task-1"), ("properties", "{}"), ("removedProperties", """[ "obsolete" ]""")));

        model.Elements.Single(item => item.Id == "task-1").Properties.Should().NotContainKey("obsolete");
    }

    [Fact]
    public void Set_properties_requires_an_id_and_a_properties_object()
    {
        var model = CreateModel();

        var missingId = Apply(model, Op("set_properties", ("properties", "{}")));
        missingId.Errors[0].Code.Should().Be("missing_field");
        missingId.Errors[0].Message.Should().Be("operations[0] set_properties: 'id' is required.");

        var missingProperties = Apply(model, Op("set_properties", ("id", "task-1")));
        missingProperties.Errors[0].Code.Should().Be("missing_field");
        missingProperties.Errors[0].Message.Should().Be("operations[0] set_properties: 'properties' is required.");

        var wrongKind = ModelingOperationEngine.ApplyJson(
            CreateModel(),
            """[ { "op": "set_properties", "id": "task-1", "properties": "nope" } ]""",
            new ModelingOperationContext { RelationshipRules = new FlowRules() });
        wrongKind.Errors[0].Code.Should().Be("property_type");
        wrongKind.Errors[0].Message.Should().Be("operations[0] set_properties: 'properties' must be a JSON object.");
    }

    [Fact]
    public void Set_properties_reports_an_unknown_target()
    {
        var result = Apply(CreateModel(), Op("set_properties", ("id", "missing"), ("properties", "{}")));

        result.Errors[0].Code.Should().Be("not_found");
        result.Errors[0].Message.Should().Be("operations[0] set_properties: element or relationship 'missing' not found.");
        result.Errors[0].ElementId.Should().Be("missing");
    }

    [Fact]
    public void Set_properties_is_available_in_the_strict_policy_too()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            ModelingOperationPolicy.Strict,
            Op("set_properties", ("id", "task-1"), ("properties", """{ "priority": 1 }""")));

        result.Success.Should().BeTrue();
    }

    // ── view operations (Authoring) ─────────────────────────────────────────────

    [Fact]
    public void Set_view_node_creates_a_node_in_the_default_view()
    {
        var model = CreateModel();

        var result = AuthoringApply(model, Op("set_view_node", ("elementId", "task-2"), ("x", 320), ("y", 140), ("width", 160), ("height", 90)));

        result.Success.Should().BeTrue();
        var node = model.Views[0].Nodes.Single(item => item.ElementId == "task-2");
        node.X.Should().Be(320);
        node.Y.Should().Be(140);
        node.Width.Should().Be(160);
        node.Height.Should().Be(90);
    }

    [Fact]
    public void Set_view_node_updates_existing_geometry_and_leaves_omitted_coordinates_alone()
    {
        var model = CreateModel();

        AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("y", 300), ("parentNodeId", "pool-1")));

        var node = model.Views[0].Nodes.Single(item => item.ElementId == "task-1");
        node.X.Should().Be(40);
        node.Y.Should().Be(300);
        node.Width.Should().Be(180);
        node.ParentNodeId.Should().Be("pool-1");
    }

    [Fact]
    public void Set_view_node_clears_the_parent_with_an_empty_string_and_keeps_it_with_null()
    {
        var model = CreateModel();
        AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("parentNodeId", "pool-1")));

        AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("parentNodeId", null)));
        model.Views[0].Nodes.Single(item => item.ElementId == "task-1").ParentNodeId.Should().Be("pool-1", "null means unchanged");

        AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("parentNodeId", "")));
        model.Views[0].Nodes.Single(item => item.ElementId == "task-1").ParentNodeId.Should().BeEmpty("an empty parentNodeId clears the parent view node");
    }

    [Fact]
    public void Set_view_node_requires_geometry_for_a_node_that_does_not_exist_yet()
    {
        var model = CreateModel();

        var missingX = AuthoringApply(model, Op("set_view_node", ("elementId", "task-2"), ("y", 140)));
        missingX.Success.Should().BeFalse();
        missingX.Errors[0].Code.Should().Be("missing_field");
        missingX.Errors[0].Message.Should().Be("operations[0] set_view_node: 'x' is required for a new view node.");

        var missingY = AuthoringApply(model, Op("set_view_node", ("elementId", "task-2"), ("x", 140)));
        missingY.Errors[0].Code.Should().Be("missing_field");
        missingY.Errors[0].Message.Should().Be("operations[0] set_view_node: 'y' is required for a new view node.");
    }

    [Fact]
    public void Set_view_node_rejects_an_unknown_element_or_view()
    {
        var model = CreateModel();

        var unknownElement = AuthoringApply(model, Op("set_view_node", ("elementId", "missing"), ("x", 1), ("y", 2)));
        unknownElement.Errors[0].Code.Should().Be("not_found");
        unknownElement.Errors[0].Message.Should().Be("operations[0] set_view_node: element 'missing' not found.");

        var unknownView = AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("viewId", "missing-view"), ("x", 1), ("y", 2)));
        unknownView.Errors[0].Code.Should().Be("not_found");
        unknownView.Errors[0].Message.Should().Be("operations[0] set_view_node: view 'missing-view' not found.");
    }

    [Fact]
    public void Set_view_node_rejects_a_parent_node_that_is_not_an_element()
    {
        var model = CreateModel();

        var result = AuthoringApply(
            model,
            Op("set_view_node", ("elementId", "task-1"), ("x", 500), ("y", 500), ("parentNodeId", "missing")));

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("parent_not_found");
        result.Errors[0].Message.Should().Be("operations[0] set_view_node: parent element 'missing' not found.");
        result.Errors[0].ElementId.Should().Be("missing");
        var node = model.Views[0].Nodes.Single(item => item.ElementId == "task-1");
        node.X.Should().Be(40, "the parent is validated BEFORE any geometry is written");
        node.ParentNodeId.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Set_view_node_rejects_a_node_that_would_be_its_own_parent_node()
    {
        var model = CreateModel();

        var result = AuthoringApply(
            model,
            Op("set_view_node", ("elementId", "task-1"), ("y", 500), ("parentNodeId", "task-1")));

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("parent_cycle");
        result.Errors[0].Message.Should().Be("operations[0] set_view_node: element 'task-1' cannot be its own parent (cycle).");
        result.Errors[0].ElementId.Should().Be("task-1");
        var node = model.Views[0].Nodes.Single(item => item.ElementId == "task-1");
        node.Y.Should().Be(120, "the cycle is validated BEFORE any geometry is written");
        node.ParentNodeId.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Set_view_node_does_not_create_a_node_when_its_parent_is_invalid()
    {
        var model = CreateModel();
        model.Views[0].Nodes.Clear();

        var result = AuthoringApply(
            model,
            Op("set_view_node", ("elementId", "task-2"), ("x", 10), ("y", 20), ("parentNodeId", "missing")));

        result.Errors[0].Code.Should().Be("parent_not_found");
        model.Views[0].Nodes.Should().BeEmpty("a rejected parent check must not add the view node");
    }

    [Fact]
    public void Set_view_node_creates_the_default_view_when_the_model_has_none()
    {
        var model = CreateModel();
        model.Views.Clear();

        var result = AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("x", 10), ("y", 20)));

        result.Success.Should().BeTrue();
        model.Views.Should().ContainSingle();
        model.Views[0].Id.Should().Be("default");
        model.Views[0].Notation.Should().Be("bpmn");
        model.Views[0].Nodes.Single().ElementId.Should().Be("task-1");
    }

    [Fact]
    public void Set_view_node_targets_the_first_view_when_view_id_is_empty()
    {
        var model = CreateModel();
        model.Views.Insert(0, new ModelingViewDto { Id = "second-view", Name = "Second", Notation = "bpmn" });

        AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("x", 1), ("y", 2)));

        model.Views[0].Nodes.Should().Contain(node => node.ElementId == "task-1");
        model.Views.Single(view => view.Id == "main-view").Nodes.Should().NotBeEmpty();
    }

    [Fact]
    public void Remove_view_node_removes_it()
    {
        var model = CreateModel();

        var result = AuthoringApply(model, Op("remove_view_node", ("elementId", "task-1")));

        result.Success.Should().BeTrue();
        model.Views[0].Nodes.Should().BeEmpty();

        var again = AuthoringApply(model, Op("remove_view_node", ("elementId", "task-1")));
        again.Errors[0].Code.Should().Be("not_found");
        again.Errors[0].Message.Should().Be("operations[0] remove_view_node: view node for element 'task-1' not found.");
    }

    [Fact]
    public void Set_view_connection_upserts_waypoints_and_clears_them_with_an_empty_array()
    {
        var model = CreateModel();

        AuthoringApply(
            model,
            Op("set_view_connection", ("relationshipId", "flow-1"), ("waypoints", """[ { "x": 100, "y": 120 }, { "x": 200, "y": 160 } ]""")));

        var connection = model.Views[0].Connections.Single(item => item.RelationshipId == "flow-1");
        connection.Waypoints.Should().HaveCount(2);
        connection.Waypoints[1].X.Should().Be(200);

        AuthoringApply(model, Op("set_view_connection", ("relationshipId", "flow-1"), ("waypoints", "[]")));
        model.Views[0].Connections.Single(item => item.RelationshipId == "flow-1").Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void Set_view_connection_adds_a_connection_for_a_relationship_without_one()
    {
        var model = CreateModel();
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "flow-2",
            RelationshipType = "SequenceFlow",
            SourceElementId = "task-1",
            TargetElementId = "task-2"
        });

        var result = AuthoringApply(model, Op("set_view_connection", ("relationshipId", "flow-2")));

        result.Success.Should().BeTrue();
        var connection = model.Views[0].Connections.Single(item => item.RelationshipId == "flow-2");
        connection.SourceNodeId.Should().Be("task-1", "a new connection carries the relationship endpoints");
        connection.TargetNodeId.Should().Be("task-2");
        connection.Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void Set_view_connection_rejects_an_unknown_relationship()
    {
        var result = AuthoringApply(CreateModel(), Op("set_view_connection", ("relationshipId", "missing")));

        result.Errors[0].Code.Should().Be("not_found");
        result.Errors[0].Message.Should().Be("operations[0] set_view_connection: relationship 'missing' not found.");
    }

    [Fact]
    public void Remove_view_connection_removes_it()
    {
        var model = CreateModel();

        AuthoringApply(model, Op("remove_view_connection", ("relationshipId", "flow-1"))).Success.Should().BeTrue();
        model.Views[0].Connections.Should().BeEmpty();

        var again = AuthoringApply(model, Op("remove_view_connection", ("relationshipId", "flow-1")));
        again.Errors[0].Code.Should().Be("not_found");
        again.Errors[0].Message.Should().Be("operations[0] remove_view_connection: view connection for relationship 'flow-1' not found.");
    }

    [Theory]
    [InlineData("set_view_node", """{ "op": "set_view_node", "elementId": "task-1", "x": 1, "y": 2 }""")]
    [InlineData("remove_view_node", """{ "op": "remove_view_node", "elementId": "task-1" }""")]
    [InlineData("set_view_connection", """{ "op": "set_view_connection", "relationshipId": "flow-1" }""")]
    [InlineData("remove_view_connection", """{ "op": "remove_view_connection", "relationshipId": "flow-1" }""")]
    public void View_operations_are_rejected_in_the_strict_policy(string op, string operationJson)
    {
        var result = ApplyJson(CreateModel(), $"[{operationJson}]");

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("unsupported_operation");
        result.Errors[0].Op.Should().Be(op);
        result.Errors[0].Index.Should().Be(0);
        result.Errors[0].Message.Should().Be(
            $"operations[0] {op}: view operations are not supported by this caller (policy 'Strict').");
    }

    [Fact]
    public void Set_view_node_rejects_an_element_the_view_viewpoint_does_not_allow()
    {
        var model = CreateModel();
        model.Views[0].ViewpointKey = "process-view";
        model.Views[0].Nodes.Clear();
        var rules = new ViewpointRules("process-view:ServiceTask");

        var result = AuthoringApplyWithViewpoints(model, rules, Op("set_view_node", ("elementId", "task-1"), ("x", 1), ("y", 2)));

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("viewpoint_disallowed");
        result.Errors[0].ElementId.Should().Be("task-1");
        result.Errors[0].Message.Should().Contain("not allowed in viewpoint 'process-view'");
        model.Views[0].Nodes.Should().NotContain(node => node.ElementId == "task-1");

        var allowed = AuthoringApplyWithViewpoints(model, new ViewpointRules("process-view:Task"), Op("set_view_node", ("elementId", "task-1"), ("x", 1), ("y", 2)));
        allowed.Success.Should().BeTrue("the viewpoint rules allowed the element type");
        model.Views[0].Nodes.Should().Contain(node => node.ElementId == "task-1");
    }

    [Fact]
    public void Set_view_node_skips_the_viewpoint_check_without_a_rules_provider_or_a_viewpoint()
    {
        var model = CreateModel();
        model.Views[0].ViewpointKey = "process-view";

        // No viewpoint rules in the context and a view WITHOUT a viewpoint key are both no-ops.
        AuthoringApply(model, Op("set_view_node", ("elementId", "task-1"), ("x", 1), ("y", 2))).Success.Should().BeTrue();
        model.Views[0].ViewpointKey = string.Empty;
        AuthoringApplyWithViewpoints(model, new ViewpointRules(), Op("set_view_node", ("elementId", "task-1"), ("y", 5))).Success.Should().BeTrue();
    }

    // ── property payloads on the collection operations ──────────────────────────

    [Fact]
    public void Add_element_and_add_relationship_carry_their_initial_properties()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            new ModelingOperation
            {
                Op = "add_element",
                Id = "task-9",
                Name = "Pack",
                SemanticType = "Task",
                ParentId = "lane-1",
                Properties = new Dictionary<string, JsonElement> { ["priority"] = JsonSerializer.SerializeToElement(2) }
            },
            new ModelingOperation
            {
                Op = "add_relationship",
                Id = "flow-9",
                RelationshipType = "SequenceFlow",
                SourceElementId = "task-9",
                TargetElementId = "task-2",
                Properties = new Dictionary<string, JsonElement> { ["condition"] = JsonSerializer.SerializeToElement("picked") }
            });

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors.Select(error => error.Message)));
        model.Elements.Single(element => element.Id == "task-9").Properties["priority"].GetInt32().Should().Be(2);
        model.Relationships.Single(relationship => relationship.Id == "flow-9").Properties["condition"].GetString().Should().Be("picked");
    }

    [Fact]
    public void Update_relationship_applies_property_edits()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            new ModelingOperation
            {
                Op = "update_relationship",
                Id = "flow-1",
                Name = "start flow",
                Properties = new Dictionary<string, JsonElement> { ["condition"] = JsonSerializer.SerializeToElement("received") }
            });

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors.Select(error => error.Message)));
        var relationship = model.Relationships.Single(item => item.Id == "flow-1");
        relationship.Name.Should().Be("start flow");
        relationship.Properties["condition"].GetString().Should().Be("received");
    }

    [Fact]
    public void Update_element_applies_property_edits_and_removals()
    {
        var model = CreateModel();
        model.Elements.Single(element => element.Id == "task-1").Properties["obsolete"] = JsonSerializer.SerializeToElement("old");

        var result = Apply(
            model,
            new ModelingOperation
            {
                Op = "update_element",
                Id = "task-1",
                Name = "Approve the order",
                Properties = new Dictionary<string, JsonElement> { ["priority"] = JsonSerializer.SerializeToElement(1) },
                RemovedProperties = ["obsolete"]
            });

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors.Select(error => error.Message)));
        var element = model.Elements.Single(item => item.Id == "task-1");
        element.Name.Should().Be("Approve the order");
        element.Properties["priority"].GetInt32().Should().Be(1);
        element.Properties.Should().NotContainKey("obsolete");
    }

    [Theory]
    [InlineData("add_element", """{ "op": "add_element", "id": "task-9", "name": "Pack", "semanticType": "Task", "properties": "nope" }""")]
    [InlineData("update_element", """{ "op": "update_element", "id": "task-1", "name": "Renamed", "removedProperties": "nope" }""")]
    [InlineData("add_relationship", """{ "op": "add_relationship", "id": "flow-9", "relationshipType": "SequenceFlow", "sourceElementId": "task-1", "targetElementId": "task-2", "properties": 42 }""")]
    [InlineData("update_relationship", """{ "op": "update_relationship", "id": "flow-1", "name": "next", "properties": "nope" }""")]
    public void A_malformed_property_payload_is_rejected_before_any_mutation(string op, string operationJson)
    {
        // All four collection operations share ONE property contract, and a malformed payload fails
        // the operation with property_type BEFORE the name/parent/geometry fields are written.
        var model = CreateModel();
        var before = JsonSerializer.Serialize(model);

        var result = ModelingOperationEngine.ApplyJson(
            model,
            $"[{operationJson}]",
            new ModelingOperationContext { RelationshipRules = new FlowRules(), Policy = ModelingOperationPolicy.Authoring });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be("property_type");
        result.Errors[0].Op.Should().Be(op);
        JsonSerializer.Serialize(model).Should().Be(before, "a malformed property payload must not mutate the model");
    }

    [Fact]
    public void Malformed_waypoints_are_reported_without_mutating_the_connection()
    {
        var model = CreateModel();

        var result = ModelingOperationEngine.ApplyJson(
            model,
            """[ { "op": "set_view_connection", "relationshipId": "flow-1", "waypoints": [ { "x": 1 } ] } ]""",
            new ModelingOperationContext { RelationshipRules = new FlowRules(), Policy = ModelingOperationPolicy.Authoring });

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("property_type");
        result.Errors[0].Message.Should().Be("operations[0] set_view_connection: waypoints[0] must be an object with numeric 'x' and 'y'.");
        model.Views[0].Connections[0].Waypoints.Should().BeEmpty();
    }

    // ── policy: post-move relationship re-validation ────────────────────────────

    [Fact]
    public void Authoring_revalidates_relationships_incident_to_a_reparented_element()
    {
        var model = CreateModel();
        // A relationship the batch does not touch, from the task that will move to another pool.
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "flow-2",
            RelationshipType = "SequenceFlow",
            SourceElementId = "task-2",
            TargetElementId = "event-1"
        });

        var result = AuthoringApply(model, Op("update_element", ("id", "task-2"), ("parentId", "pool-2")));

        result.Success.Should().BeFalse("moving the task out of the pool invalidates the untouched SequenceFlow");
        result.Errors.Should().Contain(error => error.Code == "invalid_relationship"
            && error.RelationshipId == "flow-2"
            && error.Message.Contains("not valid for notation 'bpmn'", StringComparison.Ordinal));
    }

    [Fact]
    public void Strict_policy_does_not_revalidate_after_a_parent_move()
    {
        var model = CreateModel();
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "flow-2",
            RelationshipType = "SequenceFlow",
            SourceElementId = "task-2",
            TargetElementId = "event-1"
        });

        var result = Apply(model, ModelingOperationPolicy.Strict, Op("update_element", ("id", "task-2"), ("parentId", "pool-2")));

        result.Success.Should().BeTrue("2.10.0 MCP semantics keep the per-operation validation only (phase 7 adds the post-check)");
    }

    // ── JSON front-end ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyJson_reports_the_legacy_parse_errors()
    {
        var model = CreateModel();

        var invalidJson = ApplyJson(model, "{ nope");
        invalidJson.Success.Should().BeFalse();
        invalidJson.Errors[0].Code.Should().Be("parse");
        invalidJson.Errors[0].Message.Should().StartWith("operations: invalid JSON (");

        var notAnArray = ApplyJson(model, """{ "op": "add_element" }""");
        notAnArray.Errors[0].Code.Should().Be("parse");
        notAnArray.Errors[0].Message.Should().Be("operations: expected a JSON array of operations.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyJson_reports_an_empty_or_whitespace_payload_as_invalid_json(string operationsJson)
    {
        // No whitespace shortcut: an empty payload is simply malformed JSON, so it keeps the legacy
        // "invalid JSON (...)" wording instead of the misleading "expected a JSON array" shape error.
        var model = CreateModel();

        var result = ApplyJson(model, operationsJson);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be("parse");
        result.Errors[0].Index.Should().Be(-1);
        result.Errors[0].Op.Should().BeEmpty();
        result.Errors[0].Message.Should().StartWith("operations: invalid JSON (");
    }

    [Fact]
    public void ApplyJson_reports_non_object_items_missing_op_and_unknown_ops()
    {
        var model = CreateModel();

        var nonObject = ApplyJson(model, """[ 42 ]""");
        nonObject.Errors[0].Code.Should().Be("parse");
        nonObject.Errors[0].Message.Should().Be("operations[0]: expected a JSON object.");

        var missingOp = ApplyJson(model, """[ { "name": "Task" } ]""");
        missingOp.Errors[0].Code.Should().Be("parse");
        missingOp.Errors[0].Message.Should().Be("operations[0]: missing 'op'.");

        var unknown = ApplyJson(model, """[ { "op": "explode_element" } ]""");
        unknown.Errors[0].Code.Should().Be("unknown_op");
        unknown.Errors[0].Op.Should().Be("explode_element");
        unknown.Errors[0].Message.Should().Be("operations[0]: unknown op 'explode_element'.");
    }

    [Fact]
    public void ApplyJson_keeps_the_per_operation_error_prefix_and_counts_applied_operations()
    {
        var model = CreateModel();

        var result = ApplyJson(model, """
            [
              { "op": "add_element", "id": "task-9", "name": "Pack", "semanticType": "Task", "parentId": "lane-1" },
              { "op": "update_element", "id": "missing", "name": "x" }
            ]
            """);

        result.Success.Should().BeFalse();
        result.Applied.Should().Be(1, "the valid operation before the failing one is counted");
        result.Errors.Single().Index.Should().Be(1);
        result.Errors.Single().Message.Should().Be("operations[1] update_element: element 'missing' not found.");
    }

    [Fact]
    public void Apply_and_ApplyJson_are_two_front_ends_of_one_semantics()
    {
        var typedModel = CreateModel();
        var jsonModel = CreateModel();

        var typed = Apply(
            typedModel,
            Op("add_element", ("id", "task-9"), ("name", "Pack"), ("semanticType", "Task"), ("parentId", "lane-1")),
            Op("update_element", ("id", "task-9"), ("parentId", "lane-2")),
            Op("add_relationship", ("id", "flow-9"), ("relationshipType", "SequenceFlow"), ("sourceElementId", "task-9"), ("targetElementId", "task-2")));

        var json = ModelingOperationEngine.ApplyJson(jsonModel, """
            [
              { "op": "add_element", "id": "task-9", "name": "Pack", "semanticType": "Task", "parentId": "lane-1" },
              { "op": "update_element", "id": "task-9", "parentId": "lane-2" },
              { "op": "add_relationship", "id": "flow-9", "relationshipType": "SequenceFlow", "sourceElementId": "task-9", "targetElementId": "task-2" }
            ]
            """, new ModelingOperationContext { RelationshipRules = new FlowRules() });

        typed.Success.Should().BeTrue();
        json.Success.Should().BeTrue();
        typed.Applied.Should().Be(json.Applied);
        typed.CreatedIds.Should().Equal(json.CreatedIds);
        typedModel.Elements.Select(element => element.Id).Should().Equal(jsonModel.Elements.Select(element => element.Id));
        typedModel.Relationships.Select(relationship => relationship.Id).Should().Equal(jsonModel.Relationships.Select(relationship => relationship.Id));
        JsonSerializer.Serialize(typedModel).Should().Be(JsonSerializer.Serialize(jsonModel));
    }

    [Fact]
    public void ApplyJson_view_operations_are_honoured_under_the_authoring_policy()
    {
        var model = CreateModel();

        var result = ModelingOperationEngine.ApplyJson(
            model,
            """[ { "op": "set_view_node", "elementId": "task-2", "x": 10, "y": 20 } ]""",
            new ModelingOperationContext { Policy = ModelingOperationPolicy.Authoring });

        result.Success.Should().BeTrue();
        model.Views[0].Nodes.Should().Contain(node => node.ElementId == "task-2");
    }

    // ── batch atomicity and cloning ─────────────────────────────────────────────

    [Fact]
    public void A_failing_batch_leaves_the_caller_s_model_untouched_when_the_caller_passes_a_clone()
    {
        var original = CreateModel();

        var working = ModelingOperationEngine.Clone(original);
        var result = Apply(
            working,
            Op("add_element", ("id", "task-9"), ("name", "Pack"), ("semanticType", "Task")),
            Op("delete_element", ("id", "task-1")));

        result.Success.Should().BeFalse();
        working.Elements.Should().Contain(element => element.Id == "task-9", "the working copy is partially applied...");
        original.Elements.Should().NotContain(element => element.Id == "task-9", "...so a caller must swap only on success");
        original.Elements.Should().Contain(element => element.Id == "task-1");
    }

    [Fact]
    public void Clone_deep_copies_views_properties_and_governance()
    {
        var original = CreateModel();
        var originalTask = original.Elements.Single(element => element.Id == "task-2");
        originalTask.Properties["priority"] = JsonSerializer.SerializeToElement(3);

        var clone = ModelingOperationEngine.Clone(original);

        clone.Should().NotBeSameAs(original);
        clone.Elements.Should().HaveCount(original.Elements.Count);
        var clonedTask = clone.Elements.Single(element => element.Id == "task-2");
        clonedTask.Should().NotBeSameAs(originalTask);
        clone.Views[0].Nodes[0].Should().NotBeSameAs(original.Views[0].Nodes[0]);
        clonedTask.Properties["priority"].GetInt32().Should().Be(3);

        clonedTask.Name = "Changed";
        clonedTask.Properties["priority"] = JsonSerializer.SerializeToElement(9);
        clone.Views[0].Nodes[0].X = 999;

        originalTask.Name.Should().Be("Ship order");
        originalTask.Properties["priority"].GetInt32().Should().Be(3);
        original.Views[0].Nodes[0].X.Should().Be(40);
    }

    [Fact]
    public void Clone_rejects_a_null_model()
    {
        var act = () => ModelingOperationEngine.Clone(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions TestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static ModelingOperation Op(string op, params (string Key, object? Value)[] fields)
    {
        var operation = new ModelingOperation { Op = op };
        foreach (var (key, value) in fields)
        {
            switch (key)
            {
                case "id": operation.Id = value as string; break;
                case "name": operation.Name = value as string; break;
                case "description": operation.Description = value as string; break;
                case "notation": operation.Notation = value as string; break;
                case "semanticType": operation.SemanticType = value as string; break;
                case "parentId": operation.ParentId = value as string; break;
                case "relationshipType": operation.RelationshipType = value as string; break;
                case "sourceElementId": operation.SourceElementId = value as string; break;
                case "targetElementId": operation.TargetElementId = value as string; break;
                case "elementId": operation.ElementId = value as string; break;
                case "relationshipId": operation.RelationshipId = value as string; break;
                case "viewId": operation.ViewId = value as string; break;
                case "parentNodeId": operation.ParentNodeId = value as string; break;
                case "x": operation.X = value is null ? null : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "y": operation.Y = value is null ? null : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "width": operation.Width = value is null ? null : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "height": operation.Height = value is null ? null : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "properties": operation.Properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(value as string ?? "{}", TestJsonOptions); break;
                case "removedProperties": operation.RemovedProperties = JsonSerializer.Deserialize<List<string>>(value as string ?? "[]", TestJsonOptions); break;
                case "waypoints": operation.Waypoints = JsonSerializer.Deserialize<List<ModelingViewWaypointDto>>(value as string ?? "[]", TestJsonOptions); break;
                default: throw new ArgumentOutOfRangeException(nameof(fields), key, "Unmapped operation field.");
            }
        }

        return operation;
    }

    private static ModelingOperationResult Apply(ModelingModelDto model, params ModelingOperation[] operations)
        => Apply(model, ModelingOperationPolicy.Strict, operations);

    private static ModelingOperationResult Apply(ModelingModelDto model, ModelingOperationPolicy policy, params ModelingOperation[] operations)
        => ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch { Operations = [.. operations] },
            new ModelingOperationContext { RelationshipRules = new FlowRules(), Policy = policy });

    private static ModelingOperationResult AuthoringApply(ModelingModelDto model, params ModelingOperation[] operations)
        => Apply(model, ModelingOperationPolicy.Authoring, operations);

    private static ModelingOperationResult AuthoringApplyWithViewpoints(
        ModelingModelDto model,
        IModelingViewpointRulesProvider viewpointRules,
        params ModelingOperation[] operations)
        => ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch { Operations = [.. operations] },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ViewpointRules = viewpointRules,
                Policy = ModelingOperationPolicy.Authoring
            });

    private static ModelingOperationResult ApplyJson(ModelingModelDto model, string json)
        => ModelingOperationEngine.ApplyJson(ModelingOperationEngine.Clone(model), json, new ModelingOperationContext { RelationshipRules = new FlowRules() });

    [Fact]
    public void ApplyJson_does_not_mutate_the_passed_model_when_it_throws_nowhere()
    {
        // Guards the "callers pass a clone" contract: the JSON front-end itself clones nothing, so
        // the batch mutates the instance it was given (2.10.0 semantics kept).
        var model = CreateModel();

        ModelingOperationEngine.ApplyJson(
            model,
            """[ { "op": "add_element", "id": "task-9", "name": "Pack", "semanticType": "Task" } ]""",
            new ModelingOperationContext { RelationshipRules = new FlowRules() });

        model.Elements.Should().Contain(element => element.Id == "task-9");
    }

    /// <summary>
    /// A deliberately small BPMN-ish rule set: SequenceFlow only between flow nodes that live in
    /// the same pool (the real provider lives in <c>BpmnRelationshipRulesProvider</c>; the engine
    /// must not depend on it).
    /// </summary>
    private sealed class FlowRules : IModelingRelationshipRulesProvider
    {
        public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
            => Validate(sourceType, targetType, relationshipType).IsValid;

        public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
        {
            var basic = Validate(context.SourceElement.SemanticType, context.TargetElement.SemanticType, context.Relationship.RelationshipType);
            if (!basic.IsValid)
            {
                return basic;
            }

            // SequenceFlow never crosses a pool boundary (the BPMN rule the editor's parent move
            // must not be able to break behind the engine's back).
            var sourcePool = FindPool(context.SourceElement, context.ElementsById);
            var targetPool = FindPool(context.TargetElement, context.ElementsById);
            if (!string.IsNullOrEmpty(sourcePool) && !string.IsNullOrEmpty(targetPool)
                && !string.Equals(sourcePool, targetPool, StringComparison.Ordinal))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    $"BPMN SequenceFlow cannot cross a pool boundary ('{sourcePool}' → '{targetPool}').",
                    "Keep both flow nodes inside one pool, or model the cross-pool interaction as a MessageFlow.");
            }

            return ModelingRelationshipRuleResult.Valid;
        }

        private static string FindPool(ModelingElementDto element, IReadOnlyDictionary<string, ModelingElementDto> elementsById)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = element;
            while (current is not null && seen.Add(current.Id))
            {
                if (string.Equals(current.SemanticType, "Pool", StringComparison.Ordinal))
                {
                    return current.Id;
                }

                if (string.IsNullOrEmpty(current.ParentId) || !elementsById.TryGetValue(current.ParentId, out var parent))
                {
                    return string.Empty;
                }

                current = parent;
            }

            return string.Empty;
        }

        private static ModelingRelationshipRuleResult Validate(string sourceType, string targetType, string relationshipType)
        {
            if (!string.Equals(relationshipType, "SequenceFlow", StringComparison.Ordinal))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    "Only SequenceFlow is modelled by this test rule set.",
                    "Use SequenceFlow.");
            }

            if (!IsFlowNode(sourceType) || !IsFlowNode(targetType))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    "BPMN SequenceFlow can connect only BPMN flow nodes.",
                    "Connect tasks, events, gateways, subprocesses, or call activities with SequenceFlow.");
            }

            return ModelingRelationshipRuleResult.Valid;
        }

        private static bool IsFlowNode(string semanticType)
            => semanticType is "Task" or "StartEvent" or "EndEvent" or "Gateway" or "SubProcess" or "CallActivity";
    }

    /// <summary>Viewpoint rules fake: allowed entries are declared as "viewpointKey:semanticType".</summary>
    private sealed class ViewpointRules(params string[] allowed) : IModelingViewpointRulesProvider
    {
        private readonly HashSet<string> _allowed = new(allowed, StringComparer.Ordinal);

        public bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType)
            => _allowed.Contains($"{viewpointKey}:{semanticType}");
    }

    private static ModelingModelDto CreateModel()
    {
        var model = new ModelingModelDto
        {
            Id = "order-process",
            Title = "Order process",
            Notation = "bpmn",
            Elements =
            [
                new() { Id = "pool-1", Notation = "bpmn", SemanticType = "Pool", Name = "Order handling" },
                new() { Id = "pool-2", Notation = "bpmn", SemanticType = "Pool", Name = "Warehouse" },
                new() { Id = "lane-1", Notation = "bpmn", SemanticType = "Lane", Name = "Sales", ParentId = "pool-1" },
                new() { Id = "lane-2", Notation = "bpmn", SemanticType = "Lane", Name = "Warehouse", ParentId = "pool-1" },
                new() { Id = "task-1", Notation = "bpmn", SemanticType = "Task", Name = "Approve order", ParentId = "lane-1" },
                new() { Id = "task-2", Notation = "bpmn", SemanticType = "Task", Name = "Ship order", ParentId = "lane-1" },
                new() { Id = "event-1", Notation = "bpmn", SemanticType = "StartEvent", Name = "Order received", ParentId = "lane-1" }
            ],
            Relationships =
            [
                new()
                {
                    Id = "flow-1",
                    RelationshipType = "SequenceFlow",
                    SourceElementId = "event-1",
                    TargetElementId = "task-1",
                    Name = "start"
                }
            ]
        };

        model.Views.Add(new ModelingViewDto
        {
            Id = "main-view",
            Name = "Main view",
            Notation = "bpmn",
            Nodes =
            [
                new() { ElementId = "task-1", X = 40, Y = 120, Width = 180, Height = 90 }
            ],
            Connections =
            [
                new() { RelationshipId = "flow-1", SourceNodeId = "event-1", TargetNodeId = "task-1" }
            ]
        });

        return model;
    }
}
