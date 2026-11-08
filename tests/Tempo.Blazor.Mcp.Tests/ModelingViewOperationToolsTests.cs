using System.Text.Json;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>
/// Phase 7: <c>modeling_apply_operations</c> accepts the view (layout) operations the editor's save
/// already runs, under the SAME <see cref="ModelingOperationPolicy.Authoring"/> policy — so an agent
/// can persist geometry and waypoints, and the applied diff comes back with the response.
/// </summary>
public class ModelingViewOperationToolsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static (FakeModelingBackend backend, Guid id) Seeded()
    {
        var model = new ModelingModelDto { Notation = "archimate" };
        model.Elements.Add(new ModelingElementDto { Id = "a", Name = "Customer", SemanticType = "BusinessActor", Notation = "archimate" });
        model.Elements.Add(new ModelingElementDto { Id = "r", Name = "Buyer", SemanticType = "BusinessRole", Notation = "archimate" });
        model.Relationships.Add(new ModelingRelationshipDto { Id = "rel1", RelationshipType = "Assignment", SourceElementId = "a", TargetElementId = "r" });
        var backend = new FakeModelingBackend();
        var id = backend.Add("View ops", "/", model);
        return (backend, id);
    }

    private static Task<string> Apply(FakeModelingBackend backend, Guid id, string ops)
        => ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            ops,
            FakeModelingNotation.RelationshipRules(),
            backend.ModifiedAtOf(id));

    [Fact]
    public async Task View_operations_are_applied_and_persisted()
    {
        var (backend, id) = Seeded();
        var ops = """
        [
          { "op": "set_view_node", "elementId": "a", "x": 40, "y": 60, "width": 180, "height": 90 },
          { "op": "set_view_node", "elementId": "r", "x": 320, "y": 60 },
          { "op": "set_view_connection", "relationshipId": "rel1", "waypoints": [ { "x": 240, "y": 100 } ] }
        ]
        """;

        var result = Parse(await Apply(backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            result.TryGetProperty("message", out var message) ? message.GetString() : string.Empty);
        result.GetProperty("applied").GetInt32().Should().Be(3);
        backend.SaveCount.Should().Be(1);

        var view = backend.ModelOf(id).Views.Should().ContainSingle().Subject;
        view.Nodes.Should().HaveCount(2);
        view.Nodes.Single(node => node.ElementId == "a").Width.Should().Be(180);
        view.Nodes.Single(node => node.ElementId == "r").Height.Should().Be(0, "omitted dimensions stay stencil defaults");
        view.Connections.Should().ContainSingle()
            .Which.Waypoints.Should().ContainSingle()
            .Which.X.Should().Be(240);
    }

    [Fact]
    public async Task View_node_and_connection_removals_are_applied()
    {
        var (backend, id) = Seeded();
        await Apply(backend, id, """
        [
          { "op": "set_view_node", "elementId": "a", "x": 40, "y": 60 },
          { "op": "set_view_node", "elementId": "r", "x": 320, "y": 60 },
          { "op": "set_view_connection", "relationshipId": "rel1" }
        ]
        """);

        var result = Parse(await Apply(backend, id, """
        [
          { "op": "remove_view_connection", "relationshipId": "rel1" },
          { "op": "remove_view_node", "elementId": "r" }
        ]
        """));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var view = backend.ModelOf(id).Views[0];
        view.Connections.Should().BeEmpty();
        view.Nodes.Should().ContainSingle().Which.ElementId.Should().Be("a");
    }

    [Fact]
    public async Task A_view_operation_on_an_unknown_element_fails_the_batch_without_saving()
    {
        var (backend, id) = Seeded();

        var result = Parse(await Apply(backend, id, """
        [ { "op": "set_view_node", "elementId": "missing", "x": 10, "y": 10 } ]
        """));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("validation_failed");
        result.GetProperty("validationErrors")[0].GetString().Should().Contain("'missing' not found");
        backend.SaveCount.Should().Be(0);
        backend.ModelOf(id).Views.Should().BeEmpty();
    }

    [Fact]
    public async Task The_success_payload_carries_the_applied_diff()
    {
        var (backend, id) = Seeded();

        var result = Parse(await Apply(backend, id, """
        [
          { "op": "add_element", "id": "c", "name": "Billing", "semanticType": "ApplicationComponent", "notation": "archimate" },
          { "op": "add_relationship", "id": "serve", "relationshipType": "Serving", "sourceElementId": "c", "targetElementId": "a" },
          { "op": "set_view_node", "elementId": "a", "x": 40, "y": 60 }
        ]
        """));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var diff = result.GetProperty("diff");
        diff.GetProperty("addedElementIds")[0].GetString().Should().Be("c");
        diff.GetProperty("addedRelationshipIds")[0].GetString().Should().Be("serve");
        diff.GetProperty("layoutOnlyElementIds")[0].GetString().Should().Be("a");
        diff.GetProperty("counts").GetProperty("addedElements").GetInt32().Should().Be(1);
        diff.GetProperty("counts").GetProperty("layoutOnlyElements").GetInt32().Should().Be(1);
        diff.GetProperty("counts").GetProperty("changedElements").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_parent_change_revalidates_the_incident_relationships_like_the_editor_save()
    {
        var (backend, id) = Seeded();
        backend.ModelOf(id).Elements.Add(new ModelingElementDto { Id = "lane", Name = "Lane", SemanticType = "Grouping" });
        backend.ModelOf(id).Elements.Add(new ModelingElementDto { Id = "other-lane", Name = "Other lane", SemanticType = "Grouping" });

        // SameParentRules (the parity fixture) rejects a flow whose ends sit in different parents. The
        // engine's per-operation pass sees no relationship, so only the post-batch re-validation can
        // reject it — and the MCP policy now runs that pass.
        var result = Parse(await ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            """[ { "op": "update_element", "id": "a", "parentId": "lane" }, { "op": "update_element", "id": "r", "parentId": "other-lane" } ]""",
            new DifferentParentsRejectedRules(),
            backend.ModifiedAtOf(id)));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("validationErrors")[0].GetString()
            .Should().Contain("Cross-parent flow is not allowed.");
        backend.SaveCount.Should().Be(0);
    }

    /// <summary>A rule rejecting any relationship whose ends sit in different parents.</summary>
    private sealed class DifferentParentsRejectedRules : IModelingRelationshipRulesProvider
    {
        public bool IsValidRelationship(
            string notationKey,
            string sourceType,
            string targetType,
            string relationshipType) => true;

        public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
            => string.Equals(context.SourceElement.ParentId, context.TargetElement.ParentId, StringComparison.Ordinal)
                ? ModelingRelationshipRuleResult.Valid
                : ModelingRelationshipRuleResult.Invalid(
                    "Cross-parent flow is not allowed.",
                    "Keep both ends in one parent.");
    }
}
