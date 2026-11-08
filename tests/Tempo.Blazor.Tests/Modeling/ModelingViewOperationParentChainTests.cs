using FluentAssertions;
using Tempo.Blazor.Modeling;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 7 review: the view node parent chain of <c>set_view_node</c>. Rejecting only a SELF-parent
/// let one batch build <c>A parent B</c> + <c>B parent A</c> — a cycle the generator cannot nest — and
/// a cleared parent used to be stored as <c>""</c>, a second spelling of "no parent" next to
/// <c>null</c>. Kept in its own file so the (already at-the-ceiling) engine suite stays under the
/// 1200-line ceiling.
/// </summary>
public sealed class ModelingViewOperationParentChainTests
{
    [Fact]
    public void A_parent_chain_cycle_is_rejected_before_anything_is_written()
    {
        var model = CreateModel();

        var first = Apply(model, Op("set_view_node", ("elementId", "a"), ("parentNodeId", "b")));
        first.Success.Should().BeTrue();
        model.Views[0].Nodes.Single(node => node.ElementId == "a").ParentNodeId.Should().Be("b");

        var second = Apply(model, Op("set_view_node", ("elementId", "b"), ("parentNodeId", "a")));

        second.Success.Should().BeFalse();
        second.Errors.Should().ContainSingle();
        second.Errors[0].Code.Should().Be("parent_cycle");
        second.Errors[0].Message
            .Should().Be("operations[0] set_view_node: parenting element 'b' under 'a' would close a cycle.");
        second.Errors[0].ElementId.Should().Be("b");
        model.Views[0].Nodes.Single(node => node.ElementId == "b").ParentNodeId
            .Should().BeNull("the rejected parent change wrote nothing");
        model.Views[0].Nodes.Single(node => node.ElementId == "a").ParentNodeId
            .Should().Be("b", "the earlier, valid placement is untouched");
    }

    [Fact]
    public void A_cleared_parent_is_stored_as_null_not_as_an_empty_string()
    {
        var model = CreateModel();
        Apply(model, Op("set_view_node", ("elementId", "a"), ("parentNodeId", "b")));

        var cleared = Apply(model, Op("set_view_node", ("elementId", "a"), ("parentNodeId", "")));

        cleared.Success.Should().BeTrue();
        model.Views[0].Nodes.Single(node => node.ElementId == "a").ParentNodeId
            .Should().BeNull("the DTO keeps ONE spelling of \"no parent\"; the generator treats both alike");
    }

    [Fact]
    public void An_already_cyclic_view_chain_terminates_instead_of_hanging()
    {
        // A view persisted by another writer could already carry a cycle. Placing a THIRD node under
        // that chain does not close a cycle AROUND THE PLACED NODE (the pre-existing cycle is another
        // writer's damage), so the operation succeeds — but the ancestry walk must terminate on the
        // visited set instead of looping forever.
        var model = CreateModel();
        var view = model.Views[0];
        view.Nodes.Single(node => node.ElementId == "a").ParentNodeId = "b";
        view.Nodes.Single(node => node.ElementId == "b").ParentNodeId = "a";

        var result = Apply(model, Op("set_view_node", ("elementId", "c"), ("x", 10), ("y", 20), ("parentNodeId", "a")));

        result.Success.Should().BeTrue();
        view.Nodes.Single(node => node.ElementId == "c").ParentNodeId.Should().Be("a");
    }

    private static ModelingModelDto CreateModel()
    {
        var model = new ModelingModelDto { Id = "chain", Notation = "bpmn" };
        foreach (var id in new[] { "a", "b", "c" })
        {
            model.Elements.Add(new ModelingElementDto { Id = id, Name = id, SemanticType = "Task", Notation = "bpmn" });
        }

        model.Views.Add(new ModelingViewDto
        {
            Id = "main",
            Notation = "bpmn",
            Nodes =
            [
                new ModelingViewNodeDto { ElementId = "a", X = 0, Y = 0, Width = 100, Height = 60 },
                new ModelingViewNodeDto { ElementId = "b", X = 0, Y = 100, Width = 100, Height = 60 }
            ]
        });
        return model;
    }

    private static ModelingOperation Op(string op, params (string Key, object? Value)[] members)
    {
        var operation = new ModelingOperation { Op = op };
        foreach (var (key, value) in members)
        {
            switch (key)
            {
                case "elementId":
                    operation.ElementId = value as string;
                    break;
                case "parentNodeId":
                    operation.ParentNodeId = value as string;
                    break;
                case "x":
                    operation.X = value is null ? null : Convert.ToDouble(value);
                    break;
                case "y":
                    operation.Y = value is null ? null : Convert.ToDouble(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(key), key, "unsupported test member");
            }
        }

        return operation;
    }

    private static ModelingOperationResult Apply(ModelingModelDto model, ModelingOperation operation)
        => ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch { Operations = [operation] },
            new ModelingOperationContext { Policy = ModelingOperationPolicy.Authoring });
}
