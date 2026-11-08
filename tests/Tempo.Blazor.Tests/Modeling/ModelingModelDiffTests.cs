using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Modeling;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 7: the headless <see cref="ModelingModelDiff"/> — the diff <c>TmModelingViewer</c> renders,
/// computed in Abstractions so an MCP tool or a host can produce it without the Blazor package.
/// </summary>
public sealed class ModelingModelDiffTests
{
    [Fact]
    public void The_phase_6_viewer_fixture_is_reproduced_by_the_headless_diff()
    {
        var overlay = ModelingModelDiff.Compute(
            ModelingViewerTestModels.CreateProcessModel(),
            ModelingViewerTestModels.CreateDiffModel());

        overlay.AddedElementIds.Should().Equal("e-notify");
        overlay.RemovedElementIds.Should().Equal("e-approve");
        overlay.ChangedElementIds.Should().Equal("e-review");
        overlay.LayoutOnlyElementIds.Should().Equal("e-end");
        overlay.AddedRelationshipIds.Should().Equal("r4", "r5");
        overlay.RemovedRelationshipIds.Should().Equal("r2", "r3");
        overlay.ChangedRelationshipIds.Should().BeEmpty("no surviving relationship changed");
        overlay.BaselineModel.Should().NotBeNull("removed ids must be nameable by the viewer");
    }

    [Fact]
    public void An_unchanged_model_produces_an_empty_overlay()
    {
        var model = ModelingViewerTestModels.CreateProcessModel();

        var overlay = ModelingModelDiff.Compute(model, ModelingViewerTestModels.CreateProcessModel());

        overlay.AddedElementIds.Should().BeEmpty();
        overlay.RemovedElementIds.Should().BeEmpty();
        overlay.ChangedElementIds.Should().BeEmpty();
        overlay.LayoutOnlyElementIds.Should().BeEmpty();
        overlay.AddedRelationshipIds.Should().BeEmpty();
        overlay.RemovedRelationshipIds.Should().BeEmpty();
        overlay.ChangedRelationshipIds.Should().BeEmpty();
    }

    [Fact]
    public void A_null_baseline_reports_everything_as_added_and_keeps_no_baseline()
    {
        var overlay = ModelingModelDiff.Compute(null, ModelingViewerTestModels.CreateProcessModel());

        overlay.AddedElementIds.Should().Equal("e-start", "e-review", "e-approve", "e-end");
        overlay.AddedRelationshipIds.Should().Equal("r1", "r2", "r3");
        overlay.RemovedElementIds.Should().BeEmpty();
        overlay.BaselineModel.Should().BeNull("there is no before model to name a removed item from");
    }

    [Fact]
    public void A_null_current_reports_everything_as_removed()
    {
        var overlay = ModelingModelDiff.Compute(ModelingViewerTestModels.CreateProcessModel(), null);

        overlay.RemovedElementIds.Should().Equal("e-start", "e-review", "e-approve", "e-end");
        overlay.RemovedRelationshipIds.Should().Equal("r1", "r2", "r3");
        overlay.AddedElementIds.Should().BeEmpty();
    }

    [Fact]
    public void Removed_ids_keep_the_baseline_order_and_added_ids_the_current_order()
    {
        var baseline = Model("a", "b", "c");
        var current = Model("c", "d", "a");

        var overlay = ModelingModelDiff.Compute(baseline, current);

        overlay.AddedElementIds.Should().Equal("d");
        overlay.RemovedElementIds.Should().Equal("b");
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("semanticType")]
    [InlineData("notation")]
    [InlineData("parentId")]
    [InlineData("tags")]
    [InlineData("properties")]
    [InlineData("governance")]
    [InlineData("source")]
    public void Every_semantic_element_field_counts_as_a_change(string field)
    {
        var baseline = Model("a");
        var current = Model("a");
        var element = current.Elements[0];
        switch (field)
        {
            case "name":
                element.Name = "Renamed";
                break;
            case "description":
                element.Description = "Described";
                break;
            case "semanticType":
                element.SemanticType = "userTask";
                break;
            case "notation":
                element.Notation = "archimate";
                break;
            case "parentId":
                element.ParentId = "lane-1";
                break;
            case "tags":
                element.Tags.Add("critical");
                break;
            case "properties":
                element.Properties["priority"] = JsonSerializer.SerializeToElement(3);
                break;
            case "governance":
                element.Governance.ReviewState = "approved";
                break;
            default:
                element.SourcePath = "/bpmn/task/1";
                break;
        }

        var overlay = ModelingModelDiff.Compute(baseline, current);

        overlay.ChangedElementIds.Should().Equal("a");
        overlay.LayoutOnlyElementIds.Should().BeEmpty("a changed element never also counts as a move");
    }

    [Fact]
    public void A_moved_element_is_layout_only_and_a_renamed_one_is_changed()
    {
        var baseline = ModelingViewerTestModels.CreateProcessModel();
        var current = ModelingViewerTestModels.CreateProcessModel();
        current.Views[0].Nodes.Find(node => node.ElementId == "e-end")!.X = 1200;
        current.Elements.Find(element => element.Id == "e-review")!.Name = "Validate and verify";

        var overlay = ModelingModelDiff.Compute(baseline, current);

        overlay.LayoutOnlyElementIds.Should().Equal("e-end");
        overlay.ChangedElementIds.Should().Equal("e-review");
    }

    [Fact]
    public void A_resized_node_and_a_changed_parent_node_are_layout_changes()
    {
        var baseline = ModelingViewerTestModels.CreateProcessModel();

        var resized = ModelingViewerTestModels.CreateProcessModel();
        resized.Views[0].Nodes.Find(node => node.ElementId == "e-start")!.Width = 320;
        ModelingModelDiff.Compute(baseline, resized).LayoutOnlyElementIds.Should().Equal("e-start");

        var reParented = ModelingViewerTestModels.CreateProcessModel();
        reParented.Views[0].Nodes.Find(node => node.ElementId == "e-review")!.ParentNodeId = "e-start";
        ModelingModelDiff.Compute(baseline, reParented).LayoutOnlyElementIds.Should().Equal("e-review");
    }

    [Fact]
    public void A_view_node_membership_change_is_layout_only()
    {
        var baseline = ModelingViewerTestModels.CreateProcessModel();

        var dropped = ModelingViewerTestModels.CreateProcessModel();
        dropped.Views[0].Nodes.RemoveAll(node => node.ElementId == "e-approve");
        ModelingModelDiff.Compute(baseline, dropped).LayoutOnlyElementIds.Should().Equal("e-approve");

        var added = ModelingViewerTestModels.CreateProcessModel();
        added.Views[0].Nodes.Add(new ModelingViewNodeDto { ElementId = "e-start", X = 10, Y = 10 });
        ModelingModelDiff.Compute(baseline, added).LayoutOnlyElementIds.Should().Equal("e-start");
    }

    [Fact]
    public void Waypoint_only_relationship_layout_is_not_a_change()
    {
        var baseline = ModelingViewerTestModels.CreateProcessModel();
        var current = ModelingViewerTestModels.CreateProcessModel();
        current.Views[0].Connections[0].Waypoints.Add(new ModelingViewWaypointDto { X = 260, Y = 120 });

        var overlay = ModelingModelDiff.Compute(baseline, current);

        overlay.ChangedRelationshipIds.Should().BeEmpty(
            "a relationship's layout is not part of the overlay");
        overlay.LayoutOnlyElementIds.Should().BeEmpty();
    }

    [Fact]
    public void A_retargeted_retyped_or_renamed_relationship_is_changed()
    {
        var baseline = ModelingViewerTestModels.CreateProcessModel();

        var retargeted = ModelingViewerTestModels.CreateProcessModel();
        retargeted.Relationships.Find(relationship => relationship.Id == "r1")!.TargetElementId = "e-approve";
        ModelingModelDiff.Compute(baseline, retargeted).ChangedRelationshipIds.Should().Equal("r1");

        var retyped = ModelingViewerTestModels.CreateProcessModel();
        retyped.Relationships.Find(relationship => relationship.Id == "r2")!.RelationshipType = "messageFlow";
        ModelingModelDiff.Compute(baseline, retyped).ChangedRelationshipIds.Should().Equal("r2");

        var renamed = ModelingViewerTestModels.CreateProcessModel();
        renamed.Relationships.Find(relationship => relationship.Id == "r3")!.Name = "Renamed flow";
        ModelingModelDiff.Compute(baseline, renamed).ChangedRelationshipIds.Should().Equal("r3");
    }

    [Fact]
    public void Properties_compare_by_value_not_insertion_order_or_kind()
    {
        var baseline = Model("a");
        baseline.Elements[0].Properties["priority"] = JsonSerializer.SerializeToElement(3);
        baseline.Elements[0].Properties["owner"] = JsonSerializer.SerializeToElement("ops");

        var reordered = Model("a");
        reordered.Elements[0].Properties["owner"] = JsonSerializer.SerializeToElement("ops");
        reordered.Elements[0].Properties["priority"] = JsonSerializer.SerializeToElement(3);
        ModelingModelDiff.Compute(baseline, reordered).ChangedElementIds.Should().BeEmpty();

        var reTyped = Model("a");
        reTyped.Elements[0].Properties["priority"] = JsonSerializer.SerializeToElement("3");
        reTyped.Elements[0].Properties["owner"] = JsonSerializer.SerializeToElement("ops");
        ModelingModelDiff.Compute(baseline, reTyped).ChangedElementIds.Should()
            .Equal(new[] { "a" }, "1 and \"1\" are different values");
    }

    [Fact]
    public void The_same_instance_produces_no_diff_and_no_baseline()
    {
        var model = ModelingViewerTestModels.CreateProcessModel();

        var overlay = ModelingModelDiff.Compute(model, model);

        overlay.BaselineModel.Should().BeNull();
        overlay.AddedElementIds.Should().BeEmpty();
        overlay.ChangedElementIds.Should().BeEmpty();
    }

    private static ModelingModelDto Model(params string[] elementIds)
    {
        var model = new ModelingModelDto { Id = "diff", Notation = "bpmn" };
        foreach (var id in elementIds)
        {
            model.Elements.Add(new ModelingElementDto
            {
                Id = id,
                Name = id,
                SemanticType = "task",
                Notation = "bpmn"
            });
        }

        return model;
    }
}
