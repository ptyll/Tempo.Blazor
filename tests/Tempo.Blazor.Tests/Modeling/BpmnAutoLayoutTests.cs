using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 3 tests for the headless deterministic <see cref="BpmnAutoLayout"/> (plan 6fca9139):
/// layered left-to-right flow, pools as stacked bands with lanes stacked inside, container sizes
/// fitted to their content, byte-level determinism and overlap freedom.
/// </summary>
public sealed class BpmnAutoLayoutTests
{
    private static ModelingElementDto Node(string id, string semanticType, string parentId = "") =>
        new() { Id = id, SemanticType = semanticType, Notation = "bpmn2", ParentId = parentId };

    private static ModelingRelationshipDto Flow(string id, string source, string target) =>
        new() { Id = id, SourceElementId = source, TargetElementId = target, RelationshipType = "SequenceFlow" };

    private static ModelingModelDto Model(
        IEnumerable<ModelingElementDto> elements,
        IEnumerable<ModelingRelationshipDto>? relationships = null) =>
        new()
        {
            Id = "auto-layout-test",
            Title = "Auto layout test",
            Notation = "bpmn2",
            Elements = elements.ToList(),
            Relationships = (relationships ?? []).ToList(),
        };

    private static string Serialize(ModelingViewDto view) =>
        JsonSerializer.Serialize(view, new JsonSerializerOptions { WriteIndented = true });

    // -------------------------------------------------------------- basics

    [Fact]
    public void Simple_chain_flows_left_to_right_in_conventional_sizes()
    {
        var model = Model(
            [Node("start", "StartEvent"), Node("work", "UserTask"), Node("done", "EndEvent")],
            [Flow("f1", "start", "work"), Flow("f2", "work", "done")]);

        var view = BpmnAutoLayout.Layout(model);

        view.Id.Should().Be("auto");
        view.Notation.Should().Be("bpmn2");
        view.Name.Should().Be("Auto layout test", "the model title names the view by default");
        view.Nodes.Should().HaveCount(3);
        view.Connections.Should().HaveCount(2);

        var start = view.Nodes.Single(node => node.ElementId == "start");
        var work = view.Nodes.Single(node => node.ElementId == "work");
        var done = view.Nodes.Single(node => node.ElementId == "done");
        (start.Width, start.Height).Should().Be((36, 36));
        (work.Width, work.Height).Should().Be((100, 80));
        work.X.Should().BeGreaterThan(start.X + start.Width, "the flow runs left to right");
        done.X.Should().BeGreaterThan(work.X + work.Width);
        (start.Y + start.Height / 2).Should().Be(work.Y + work.Height / 2,
            "single-member layers are centred on a common row — a chain is one horizontal line");
        (work.Y + work.Height / 2).Should().Be(done.Y + done.Height / 2);
        view.Connections.Single(connection => connection.RelationshipId == "f1").Waypoints.Should().BeEmpty(
            "the auto-layout leaves routing to the diagram generator");
    }

    [Fact]
    public void Options_control_the_view_identity()
    {
        var model = Model([Node("start", "StartEvent")]);
        var view = BpmnAutoLayout.Layout(model, new BpmnAutoLayoutOptions { ViewId = "custom", ViewName = "Custom name" });

        view.Id.Should().Be("custom");
        view.Name.Should().Be("Custom name");
    }

    [Fact]
    public void Layout_is_byte_for_byte_deterministic()
    {
        var model = Model(
            [Node("start", "StartEvent"), Node("work", "UserTask"), Node("done", "EndEvent")],
            [Flow("f1", "start", "work"), Flow("f2", "work", "done")]);

        Serialize(BpmnAutoLayout.Layout(model)).Should().Be(Serialize(BpmnAutoLayout.Layout(model)),
            "the same model must always produce the same view (server-side golden safety)");
    }

    [Fact]
    public void Disconnected_elements_share_the_first_layer_and_never_overlap()
    {
        var model = Model(
            [Node("a", "UserTask"), Node("b", "ServiceTask"), Node("c", "ScriptTask")]);

        var view = BpmnAutoLayout.Layout(model);
        view.Nodes.Should().OnlyContain(node => node.X == 40, "unconnected nodes stay in layer 0");
        view.Nodes.Select(node => node.Y).Should().BeInAscendingOrder();
        AssertNoOverlaps(view);
    }

    [Fact]
    public void Branches_share_a_layer_and_back_edges_do_not_hang_or_drift()
    {
        var model = Model(
            [Node("start", "StartEvent"), Node("fork", "ExclusiveGateway"),
             Node("yes", "UserTask"), Node("no", "ManualTask"), Node("done", "EndEvent")],
            [Flow("f1", "start", "fork"), Flow("f2", "fork", "yes"), Flow("f3", "fork", "no"),
             Flow("f4", "yes", "done"), Flow("f5", "no", "done")]);

        var first = Serialize(BpmnAutoLayout.Layout(model));
        var second = Serialize(BpmnAutoLayout.Layout(model));
        first.Should().Be(second, "branch layering must be deterministic");

        var view = BpmnAutoLayout.Layout(model);
        var yes = view.Nodes.Single(node => node.ElementId == "yes");
        var no = view.Nodes.Single(node => node.ElementId == "no");
        var done = view.Nodes.Single(node => node.ElementId == "done");
        yes.X.Should().Be(no.X, "branch siblings share a layer");
        done.X.Should().BeGreaterThan(yes.X);
        AssertNoOverlaps(view);
    }

    [Fact]
    public void Loop_edges_are_ignored_for_layering_but_keep_their_connection()
    {
        var model = Model(
            [Node("start", "StartEvent"), Node("a", "UserTask"), Node("b", "UserTask"), Node("done", "EndEvent")],
            [Flow("f1", "start", "a"), Flow("f2", "a", "b"), Flow("loop", "b", "a"), Flow("f3", "b", "done")]);

        var view = BpmnAutoLayout.Layout(model);
        var a = view.Nodes.Single(node => node.ElementId == "a");
        var b = view.Nodes.Single(node => node.ElementId == "b");
        b.X.Should().BeGreaterThan(a.X);
        a.X.Should().BeLessThan(200, "the back edge must not inflate the layer");
        view.Connections.Should().ContainSingle(connection => connection.RelationshipId == "loop");
        AssertNoOverlaps(view);
    }

    [Fact]
    public void Boundary_events_share_the_layer_of_the_activity_they_attach_to()
    {
        var boundary = Node("boundary", "BoundaryEvent");
        boundary.Properties["attachedToRef"] = JsonSerializer.SerializeToElement("work");
        var model = Model(
            [Node("start", "StartEvent"), Node("work", "UserTask"), boundary, Node("fix", "ManualTask"), Node("done", "EndEvent")],
            [Flow("f1", "start", "work"), Flow("f2", "work", "done"), Flow("f3", "boundary", "fix"), Flow("f4", "fix", "done")]);

        var view = BpmnAutoLayout.Layout(model);
        var work = view.Nodes.Single(node => node.ElementId == "work");
        var attached = view.Nodes.Single(node => node.ElementId == "boundary");
        attached.X.Should().Be(work.X, "a boundary event interrupts its activity, so it sits in the same column");
        AssertNoOverlaps(view);
    }

    // -------------------------------------------------------------- pools and lanes

    [Fact]
    public void Pools_stack_as_bands_and_lanes_stack_flush_inside()
    {
        var model = Model(
            [
                Node("pool-1", "Pool"), Node("lane-1a", "Lane", "pool-1"), Node("lane-1b", "Lane", "pool-1"),
                Node("start", "StartEvent", "lane-1a"), Node("work", "UserTask", "lane-1a"),
                Node("done", "EndEvent", "lane-1b"),
                Node("pool-2", "Pool"), Node("lane-2a", "Lane", "pool-2"), Node("other", "UserTask", "lane-2a"),
            ],
            [Flow("f1", "start", "work"), Flow("f2", "work", "done")]);

        var view = BpmnAutoLayout.Layout(model);
        var pool1 = view.Nodes.Single(node => node.ElementId == "pool-1");
        var pool2 = view.Nodes.Single(node => node.ElementId == "pool-2");
        var lane1a = view.Nodes.Single(node => node.ElementId == "lane-1a");
        var lane1b = view.Nodes.Single(node => node.ElementId == "lane-1b");

        pool2.Y.Should().Be(pool1.Y + pool1.Height + 40, "pools stack top to bottom with a fixed gap");
        lane1b.Y.Should().Be(lane1a.Y + lane1a.Height, "lanes band flush inside a pool");
        lane1a.ParentNodeId.Should().Be("pool-1");
        view.Nodes.Single(node => node.ElementId == "work").ParentNodeId.Should().Be("lane-1a");
        view.Nodes.Single(node => node.ElementId == "pool-2").X.Should().Be(40, "pools align on the origin column");

        AssertNoOverlaps(view);
    }

    [Fact]
    public void Nodes_stay_inside_their_lane_band()
    {
        var model = Model(
            [
                Node("pool", "Pool"), Node("lane-a", "Lane", "pool"), Node("lane-b", "Lane", "pool"),
                Node("start", "StartEvent", "lane-a"), Node("work", "UserTask", "lane-a"),
                Node("done", "EndEvent", "lane-b"),
            ],
            [Flow("f1", "start", "work"), Flow("f2", "work", "done")]);

        var view = BpmnAutoLayout.Layout(model);
        var laneA = view.Nodes.Single(node => node.ElementId == "lane-a");
        var laneB = view.Nodes.Single(node => node.ElementId == "lane-b");
        var start = view.Nodes.Single(node => node.ElementId == "start");
        var work = view.Nodes.Single(node => node.ElementId == "work");
        var done = view.Nodes.Single(node => node.ElementId == "done");

        start.Y.Should().BeGreaterThanOrEqualTo(laneA.Y);
        start.Y.Should().BeLessThan(laneA.Y + laneA.Height);
        work.Y.Should().BeLessThan(laneA.Y + laneA.Height);
        done.Y.Should().BeGreaterThanOrEqualTo(laneB.Y);
        done.Y.Should().BeLessThan(laneB.Y + laneB.Height);
        AssertNoOverlaps(view);
    }

    [Fact]
    public void Subprocesses_fit_their_children_and_nest_them()
    {
        var model = Model(
            [
                Node("start", "StartEvent"), Node("sub", "SubProcess"),
                Node("inner-start", "StartEvent", "sub"), Node("inner-work", "UserTask", "sub"),
                Node("inner-done", "EndEvent", "sub"), Node("done", "EndEvent"),
            ],
            [
                Flow("f1", "start", "sub"), Flow("f2", "sub", "done"),
                Flow("i1", "inner-start", "inner-work"), Flow("i2", "inner-work", "inner-done"),
            ]);

        var view = BpmnAutoLayout.Layout(model);
        var sub = view.Nodes.Single(node => node.ElementId == "sub");
        var inner = view.Nodes.Single(node => node.ElementId == "inner-work");
        inner.ParentNodeId.Should().Be("sub");
        inner.X.Should().BeGreaterThan(sub.X);
        inner.X.Should().BeLessThan(sub.X + sub.Width);
        inner.Y.Should().BeGreaterThan(sub.Y);
        inner.Y.Should().BeLessThan(sub.Y + sub.Height);
        AssertNoOverlaps(view);
    }

    [Fact]
    public void Artifacts_sit_in_a_row_below_the_flow()
    {
        var model = Model(
            [Node("start", "StartEvent"), Node("note", "TextAnnotation"), Node("data", "DataObject")],
            [Flow("f1", "start", "start")]);

        var view = BpmnAutoLayout.Layout(model);
        var note = view.Nodes.Single(node => node.ElementId == "note");
        var start = view.Nodes.Single(node => node.ElementId == "start");
        note.Y.Should().BeGreaterThan(start.Y + start.Height);
        AssertNoOverlaps(view);
    }

    [Fact]
    public void Empty_model_yields_an_empty_view()
    {
        var view = BpmnAutoLayout.Layout(new ModelingModelDto { Id = "empty", Notation = "bpmn2" });
        view.Nodes.Should().BeEmpty();
        view.Connections.Should().BeEmpty();
    }

    [Fact]
    public void Null_model_throws()
    {
        var act = () => BpmnAutoLayout.Layout(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------- scale (review findings 1–2)

    [Fact]
    public void Reversed_declaration_chain_of_50k_flows_layers_without_recursion()
    {
        // Review finding 1 (blocker): the layering must be an ITERATIVE longest path — a chain
        // declared back to front must not overflow the stack. Declaring 50 000 nodes in reverse
        // topological order with the recursion the phase used to have would recurse ~50 000 deep.
        const int count = 50_000;
        var elements = new List<ModelingElementDto>();
        var relationships = new List<ModelingRelationshipDto>();
        for (var index = count - 1; index >= 0; index--)
        {
            var semanticType = index == 0 ? "StartEvent" : index == count - 1 ? "EndEvent" : "UserTask";
            elements.Add(Node("n" + index, semanticType));
        }

        for (var index = count - 2; index >= 0; index--)
        {
            relationships.Add(Flow("f" + index, "n" + index, "n" + (index + 1)));
        }

        var view = BpmnAutoLayout.Layout(Model(elements, relationships));

        view.Nodes.Should().HaveCount(count);
        for (var index = 0; index + 1 < count; index += 5_000)
        {
            var current = view.Nodes.Single(node => node.ElementId == "n" + index);
            var next = view.Nodes.Single(node => node.ElementId == "n" + (index + 1));
            next.X.Should().BeGreaterThan(current.X + current.Width,
                $"the reversed-declaration chain must still layer n{index + 1} after n{index}");
        }
    }

    [Fact]
    public void Chain_of_100k_flows_lays_out_within_five_seconds()
    {
        // Review finding 2: linear complexity — relationship indices once, layer bucketing in one
        // pass. A 100 000-node chain must lay out well under the 5 s budget.
        const int count = 100_000;
        var elements = new List<ModelingElementDto>();
        var relationships = new List<ModelingRelationshipDto>();
        for (var index = 0; index < count; index++)
        {
            var semanticType = index == 0 ? "StartEvent" : index == count - 1 ? "EndEvent" : "UserTask";
            elements.Add(Node("n" + index, semanticType));
        }

        for (var index = 0; index + 1 < count; index++)
        {
            relationships.Add(Flow("f" + index, "n" + index, "n" + (index + 1)));
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var view = BpmnAutoLayout.Layout(Model(elements, relationships));
        watch.Stop();

        view.Nodes.Should().HaveCount(count);
        view.Nodes.Single(node => node.ElementId == "n" + (count - 1)).X.Should().BeGreaterThan(
            view.Nodes.Single(node => node.ElementId == "n0").X);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "layering and placement must stay O(V+E) — no per-layer rescans of the node list");
    }

    // -------------------------------------------------------------- helpers

    private static void AssertNoOverlaps(ModelingViewDto view)
    {
        var nodes = view.Nodes.ToList();
        for (var first = 0; first < nodes.Count; first++)
        {
            for (var second = first + 1; second < nodes.Count; second++)
            {
                var a = nodes[first];
                var b = nodes[second];
                var contains = a.ParentNodeId == b.ElementId || b.ParentNodeId == a.ElementId
                    || IsDescendant(view, a, b) || IsDescendant(view, b, a);
                if (contains)
                {
                    continue;
                }

                var overlap = a.X < b.X + b.Width && b.X < a.X + a.Width
                    && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                overlap.Should().BeFalse($"nodes {a.ElementId} and {b.ElementId} must not overlap");
            }
        }
    }

    private static bool IsDescendant(ModelingViewDto view, ModelingViewNodeDto node, ModelingViewNodeDto ancestor)
    {
        var byElement = view.Nodes.ToDictionary(candidate => candidate.ElementId);
        var current = node.ParentNodeId;
        while (current is not null)
        {
            if (string.Equals(current, ancestor.ElementId, StringComparison.Ordinal))
            {
                return true;
            }

            current = byElement.TryGetValue(current, out var parent) ? parent.ParentNodeId : null;
        }

        return false;
    }
}
