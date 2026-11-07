using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Modeling;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The deterministic arrangement behind "Arrange" (plan <c>eadbf967</c> phase 4, decisions §9.4 and
/// the review items A6/U4): the flow nodes of one container are RANKED by their relationships (a
/// source before its target, the targets of one branch on the SAME rank, ties in declaration order),
/// a run with no relationship is packed into a square-ish grid, and a pool's lanes are FLUSH bands
/// that span the pool's content width.
/// </summary>
public sealed class ModelingLayoutArrangerTests
{
    [Fact]
    public void A_relationship_chain_is_ranked_left_to_right_regardless_of_declaration_order()
    {
        // Declared c, b, a — the relationships a > b > c decide the columns.
        var elements = new List<ModelingElementDto>
        {
            Element("task-c"),
            Element("task-b"),
            Element("task-a")
        };
        var nodes = elements.Select(element => Leaf(element.Id)).ToList();
        var relationships = new List<ModelingRelationshipDto>
        {
            Relationship("ab", "task-a", "task-b"),
            Relationship("bc", "task-b", "task-c")
        };

        var boxes = Arrange(elements, nodes, relationships, ModelingLayoutArranger.Direction.LeftToRight);

        var a = BoxOf(boxes, "task-a");
        var b = BoxOf(boxes, "task-b");
        var c = BoxOf(boxes, "task-c");
        a.X.Should().BeLessThan(b.X);
        b.X.Should().BeLessThan(c.X);
        a.Y.Should().Be(b.Y, "one rank sits on one row in LR");
        b.Y.Should().Be(c.Y);
    }

    [Fact]
    public void The_targets_of_one_branching_node_share_a_rank()
    {
        // start > gateway > (approve, reject): both branches land in the SAME column.
        var elements = new List<ModelingElementDto>
        {
            Element("start"),
            Element("gateway"),
            Element("approve"),
            Element("reject")
        };
        var nodes = elements.Select(element => Leaf(element.Id)).ToList();
        var relationships = new List<ModelingRelationshipDto>
        {
            Relationship("s-g", "start", "gateway"),
            Relationship("g-a", "gateway", "approve"),
            Relationship("g-r", "gateway", "reject")
        };

        var boxes = Arrange(elements, nodes, relationships, ModelingLayoutArranger.Direction.LeftToRight);

        var gateway = BoxOf(boxes, "gateway");
        var approve = BoxOf(boxes, "approve");
        var reject = BoxOf(boxes, "reject");
        approve.X.Should().Be(reject.X, "the branch targets share the rank after the gateway");
        approve.X.Should().BeGreaterThan(gateway.X);
        approve.Y.Should().NotBe(reject.Y, "same-rank siblings stack on the cross axis");
        approve.X.Should().BeGreaterThan(BoxOf(boxes, "start").X);
    }

    [Fact]
    public void Three_hundred_unrelated_nodes_stay_a_block()
    {
        // No relationship at all: the run wraps at ceil(sqrt(n)) columns, so 300 nodes never turn
        // into a 300-node strip (the plan's "300 elements" edge case).
        var elements = new List<ModelingElementDto>();
        var nodes = new List<DiagramNode>();
        for (var index = 0; index < 300; index++)
        {
            var id = $"task-{index:D3}";
            elements.Add(Element(id));
            nodes.Add(Leaf(id));
        }

        var boxes = Arrange(elements, nodes, [], ModelingLayoutArranger.Direction.LeftToRight);

        boxes.Should().HaveCount(300);
        var width = boxes.Max(box => box.X + box.Width) - boxes.Min(box => box.X);
        width.Should().BeLessThan(6000, "the wrap keeps the arrangement a block, not a strip");
        boxes.Max(box => box.Y + box.Height).Should().BeGreaterThan(boxes.Min(box => box.Y));
    }

    [Fact]
    public void A_cycle_in_the_relationships_still_arranges()
    {
        var elements = new List<ModelingElementDto> { Element("a"), Element("b") };
        var nodes = elements.Select(element => Leaf(element.Id)).ToList();
        var relationships = new List<ModelingRelationshipDto>
        {
            Relationship("ab", "a", "b"),
            Relationship("ba", "b", "a")
        };

        var boxes = Arrange(elements, nodes, relationships, ModelingLayoutArranger.Direction.LeftToRight);

        boxes.Should().HaveCount(2, "the back edge is ignored instead of looping");
    }

    [Fact]
    public void Top_to_bottom_stacks_the_ranks_vertically()
    {
        var elements = new List<ModelingElementDto> { Element("a"), Element("b") };
        var nodes = elements.Select(element => Leaf(element.Id)).ToList();
        var relationships = new List<ModelingRelationshipDto> { Relationship("ab", "a", "b") };

        var boxes = Arrange(elements, nodes, relationships, ModelingLayoutArranger.Direction.TopToBottom);

        BoxOf(boxes, "a").Y.Should().BeLessThan(BoxOf(boxes, "b").Y);
        BoxOf(boxes, "a").X.Should().Be(BoxOf(boxes, "b").X);
    }

    [Fact]
    public void Lanes_are_flush_inside_their_pool()
    {
        // U4: no margin, no gap — the lanes fill the pool's content box and span its width.
        var elements = new List<ModelingElementDto>
        {
            Element("pool"),
            Element("lane-1", "pool"),
            Element("lane-2", "pool")
        };
        var nodes = new List<DiagramNode>
        {
            Band("pool", 40, 60, 900, 280),
            Band("lane-1", 70, 60, 870, 140),
            Band("lane-2", 70, 200, 870, 140)
        };

        var boxes = Arrange(elements, nodes, [], ModelingLayoutArranger.Direction.LeftToRight);

        var pool = BoxOf(boxes, "pool");
        var first = BoxOf(boxes, "lane-1");
        var second = BoxOf(boxes, "lane-2");

        first.X.Should().Be(pool.X + ModelingLayoutArranger.LabelBandWidth,
            "a lane starts after the pool's label band");
        first.Width.Should().Be(pool.Width - ModelingLayoutArranger.LabelBandWidth,
            "a lane spans the pool's content width");
        second.Width.Should().Be(first.Width, "sibling lanes are equalized");
        first.Y.Should().Be(pool.Y, "the first lane is flush with the pool top");
        second.Y.Should().Be(first.Y + first.Height, "no gap between the lane bands");
        pool.Height.Should().Be(first.Height + second.Height, "the pool wraps its lanes exactly");
    }

    [Fact]
    public void A_lane_holds_its_flow_run_after_its_own_label_band()
    {
        var elements = new List<ModelingElementDto>
        {
            Element("pool"),
            Element("lane", "pool"),
            Element("task", "lane")
        };
        var nodes = new List<DiagramNode>
        {
            Band("pool", 40, 60, 700, 200),
            Band("lane", 70, 60, 670, 200),
            Leaf("task")
        };

        var boxes = Arrange(elements, nodes, [], ModelingLayoutArranger.Direction.LeftToRight);

        var lane = BoxOf(boxes, "lane");
        var task = BoxOf(boxes, "task");
        task.X.Should().BeGreaterThan(lane.X + ModelingLayoutArranger.LabelBandWidth,
            "the flow run starts after the lane's own label band and its content margin");
        task.X.Should().BeLessThan(lane.X + lane.Width);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<ModelingLayoutArranger.Box> Arrange(
        List<ModelingElementDto> elements,
        List<DiagramNode> nodes,
        List<ModelingRelationshipDto> relationships,
        ModelingLayoutArranger.Direction direction)
        => ModelingLayoutArranger.Arrange(elements, nodes, relationships, _ => false, direction);

    private static ModelingElementDto Element(string id, string? parentId = null)
        => new()
        {
            Id = id,
            SemanticType = "UserTask",
            Name = id,
            ParentId = parentId ?? string.Empty
        };

    private static ModelingRelationshipDto Relationship(string id, string source, string target)
        => new()
        {
            Id = id,
            SourceElementId = source,
            TargetElementId = target,
            RelationshipType = "SequenceFlow"
        };

    private static DiagramNode Leaf(string elementId)
        => new()
        {
            Id = elementId,
            X = 0,
            Y = 0,
            W = 160,
            H = 88,
            Data = new Dictionary<string, object>(StringComparer.Ordinal) { ["modelElementId"] = elementId }
        };

    private static DiagramNode Band(string elementId, double x, double y, double width, double height)
        => new()
        {
            Id = elementId,
            X = x,
            Y = y,
            W = width,
            H = height,
            SwimlaneData = new DiagramNodeSwimlaneData
            {
                IsHorizontal = true,
                RowCount = 1,
                ColumnCount = 1,
                HeaderSize = 30,
                CellLabels = [elementId]
            },
            Data = new Dictionary<string, object>(StringComparer.Ordinal) { ["modelElementId"] = elementId }
        };

    private static ModelingLayoutArranger.Box BoxOf(IReadOnlyList<ModelingLayoutArranger.Box> boxes, string elementId)
        => boxes.Single(box => box.ElementId == elementId);
}
