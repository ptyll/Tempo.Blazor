using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class DiagramCommandStackTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DiagramDocument EmptyDoc() => new()
    {
        Title = "Test", Width = 3000, Height = 2000, Nodes = [], Edges = []
    };

    private static DiagramNode MakeNode(string stencilId = "uml.class", double x = 0, double y = 0)
        => new() { Id = Guid.NewGuid().ToString("N")[..8], StencilId = stencilId, X = x, Y = y, W = 160, H = 120 };

    private static DiagramEdge MakeEdge(string sourceId, string targetId)
        => new() { Id = Guid.NewGuid().ToString("N")[..8], SourceNodeId = sourceId, TargetNodeId = targetId };

    // ── DiagramCommandStack basics ────────────────────────────────────────────

    [Fact]
    public void Push_ExecutesCommandAndAddsToUndoStack()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var node = MakeNode();

        stack.Push(new AddNodeCommand(doc, node));

        doc.Nodes.Should().ContainSingle(n => n.Id == node.Id);
        stack.CanUndo.Should().BeTrue();
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_ReversesCommand()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var node = MakeNode();

        stack.Push(new AddNodeCommand(doc, node));
        stack.Undo();

        doc.Nodes.Should().BeEmpty();
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void Redo_ReappliesCommand()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var node = MakeNode();

        stack.Push(new AddNodeCommand(doc, node));
        stack.Undo();
        stack.Redo();

        doc.Nodes.Should().ContainSingle(n => n.Id == node.Id);
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Push_ClearsRedoStack()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var node1 = MakeNode();
        var node2 = MakeNode();

        stack.Push(new AddNodeCommand(doc, node1));
        stack.Undo();
        stack.Push(new AddNodeCommand(doc, node2));

        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Stack_RespectsMaxDepth()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack(maxDepth: 3);

        for (var i = 0; i < 5; i++)
            stack.Push(new AddNodeCommand(doc, MakeNode()));

        stack.Undo(); stack.Undo(); stack.Undo();
        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void OnStackChanged_FiredOnPushUndoRedo()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var node = MakeNode();
        var count = 0;
        stack.OnStackChanged += () => count++;

        stack.Push(new AddNodeCommand(doc, node));
        stack.Undo();
        stack.Redo();

        count.Should().Be(3);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        stack.Push(new AddNodeCommand(doc, MakeNode()));
        stack.Clear();

        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void NextUndoName_ReturnsLastCommandName()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var node = MakeNode("uml.class");
        stack.Push(new AddNodeCommand(doc, node));

        stack.NextUndoName.Should().Be("Add uml.class");
    }

    // ── AddNodeCommand ────────────────────────────────────────────────────────

    [Fact]
    public void AddNodeCommand_ExecuteAddsNode()
    {
        var doc = EmptyDoc();
        var node = MakeNode();
        var cmd = new AddNodeCommand(doc, node);
        cmd.Execute();
        doc.Nodes.Should().Contain(node);
    }

    [Fact]
    public void AddNodeCommand_UndoRemovesNode()
    {
        var doc = EmptyDoc();
        var node = MakeNode();
        var cmd = new AddNodeCommand(doc, node);
        cmd.Execute();
        cmd.Undo();
        doc.Nodes.Should().BeEmpty();
    }

    // ── AddEdgeCommand ────────────────────────────────────────────────────────

    [Fact]
    public void AddEdgeCommand_ExecuteAddsEdge()
    {
        var doc = EmptyDoc();
        var edge = MakeEdge("a", "b");
        var cmd = new AddEdgeCommand(doc, edge);
        cmd.Execute();
        doc.Edges.Should().Contain(edge);
    }

    [Fact]
    public void AddEdgeCommand_UndoRemovesEdge()
    {
        var doc = EmptyDoc();
        var edge = MakeEdge("a", "b");
        var cmd = new AddEdgeCommand(doc, edge);
        cmd.Execute();
        cmd.Undo();
        doc.Edges.Should().BeEmpty();
    }

    // ── RemoveNodesCommand ────────────────────────────────────────────────────

    [Fact]
    public void RemoveNodesCommand_ExecuteRemovesNodesAndConnectedEdges()
    {
        var doc = EmptyDoc();
        var n1 = MakeNode(); var n2 = MakeNode();
        doc.Nodes.AddRange([n1, n2]);
        var edge = MakeEdge(n1.Id, n2.Id);
        doc.Edges.Add(edge);

        var cmd = new RemoveNodesCommand(doc, [n1.Id]);
        cmd.Execute();

        doc.Nodes.Should().ContainSingle(n => n.Id == n2.Id);
        doc.Edges.Should().BeEmpty();
    }

    [Fact]
    public void RemoveNodesCommand_UndoRestoresNodesAndEdges()
    {
        var doc = EmptyDoc();
        var n1 = MakeNode(); var n2 = MakeNode();
        doc.Nodes.AddRange([n1, n2]);
        var edge = MakeEdge(n1.Id, n2.Id);
        doc.Edges.Add(edge);

        var cmd = new RemoveNodesCommand(doc, [n1.Id]);
        cmd.Execute();
        cmd.Undo();

        doc.Nodes.Should().HaveCount(2);
        doc.Edges.Should().ContainSingle(e => e.Id == edge.Id);
    }

    // ── RemoveEdgesCommand ────────────────────────────────────────────────────

    [Fact]
    public void RemoveEdgesCommand_ExecuteRemovesEdges()
    {
        var doc = EmptyDoc();
        var edge = MakeEdge("a", "b");
        doc.Edges.Add(edge);

        var cmd = new RemoveEdgesCommand(doc, [edge.Id]);
        cmd.Execute();

        doc.Edges.Should().BeEmpty();
    }

    [Fact]
    public void RemoveEdgesCommand_UndoRestoresEdges()
    {
        var doc = EmptyDoc();
        var edge = MakeEdge("a", "b");
        doc.Edges.Add(edge);

        var cmd = new RemoveEdgesCommand(doc, [edge.Id]);
        cmd.Execute();
        cmd.Undo();

        doc.Edges.Should().ContainSingle(e => e.Id == edge.Id);
    }

    // ── MoveNodesCommand ──────────────────────────────────────────────────────

    [Fact]
    public void MoveNodesCommand_ExecuteMovesNode()
    {
        var doc = EmptyDoc();
        var node = MakeNode(x: 10, y: 20);
        doc.Nodes.Add(node);

        var before = new Dictionary<string, NodeMoveState> { [node.Id] = new(10, 20, null, -1, -1) };
        var after = new Dictionary<string, NodeMoveState> { [node.Id] = new(50, 60, null, -1, -1) };
        var cmd = new MoveNodesCommand(doc, before, after);
        cmd.Execute();

        node.X.Should().Be(50); node.Y.Should().Be(60);
    }

    [Fact]
    public void MoveNodesCommand_UndoRestoresPosition()
    {
        var doc = EmptyDoc();
        var node = MakeNode(x: 10, y: 20);
        doc.Nodes.Add(node);

        var before = new Dictionary<string, NodeMoveState> { [node.Id] = new(10, 20, null, -1, -1) };
        var after = new Dictionary<string, NodeMoveState> { [node.Id] = new(50, 60, null, -1, -1) };
        var cmd = new MoveNodesCommand(doc, before, after);
        cmd.Execute();
        cmd.Undo();

        node.X.Should().Be(10); node.Y.Should().Be(20);
    }

    [Fact]
    public void MoveNodesCommand_CoalescesMergesAfterPositions()
    {
        var doc = EmptyDoc();
        var node = MakeNode(x: 0, y: 0);
        doc.Nodes.Add(node);
        var stack = new DiagramCommandStack();
        // Explicit logical stamps: the coalescing window must not depend on how long the
        // machine stalled between the two pushes (full-suite parallel load, FZE-1).
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMilliseconds(50);

        var before1 = new Dictionary<string, NodeMoveState> { [node.Id] = new(0, 0, null, -1, -1) };
        var after1 = new Dictionary<string, NodeMoveState> { [node.Id] = new(10, 10, null, -1, -1) };
        stack.Push(new MoveNodesCommand(doc, before1, after1, t1));

        var before2 = new Dictionary<string, NodeMoveState> { [node.Id] = new(10, 10, null, -1, -1) };
        var after2 = new Dictionary<string, NodeMoveState> { [node.Id] = new(20, 20, null, -1, -1) };
        stack.Push(new MoveNodesCommand(doc, before2, after2, t2));

        stack.Undo();
        stack.CanUndo.Should().BeFalse("coalesced into single undo step");
        node.X.Should().Be(0); node.Y.Should().Be(0);
    }

    // ── ResizeNodeCommand ─────────────────────────────────────────────────────

    [Fact]
    public void ResizeNodeCommand_ExecuteAndUndo()
    {
        var doc = EmptyDoc();
        var node = MakeNode(x: 10, y: 20);
        node.W = 100; node.H = 50;
        doc.Nodes.Add(node);

        var cmd = new ResizeNodeCommand(doc, node.Id, 10, 20, 100, 50, 30, 40, 200, 80);
        cmd.Execute();
        node.X.Should().Be(30); node.Y.Should().Be(40); node.W.Should().Be(200); node.H.Should().Be(80);

        cmd.Undo();
        node.X.Should().Be(10); node.Y.Should().Be(20); node.W.Should().Be(100); node.H.Should().Be(50);
    }

    // ── UpdateNodeDataCommand ─────────────────────────────────────────────────

    [Fact]
    public void UpdateNodeDataCommand_ExecuteAndUndo()
    {
        var doc = EmptyDoc();
        var node = MakeNode();
        node.Data["name"] = "Old";
        doc.Nodes.Add(node);

        var oldData = new Dictionary<string, object>(node.Data);
        var newData = new Dictionary<string, object>(node.Data) { ["name"] = "New" };
        var cmd = new UpdateNodeDataCommand(doc, node.Id, oldData, newData);
        cmd.Execute();
        node.Data["name"].Should().Be("New");

        cmd.Undo();
        node.Data["name"].Should().Be("Old");
    }

    [Fact]
    public void UpdateNodeDataCommand_UndoRestoresAbsentKey()
    {
        var doc = EmptyDoc();
        var node = MakeNode();
        doc.Nodes.Add(node);

        var oldData = new Dictionary<string, object>();
        var newData = new Dictionary<string, object> { ["label"] = "value" };
        var cmd = new UpdateNodeDataCommand(doc, node.Id, oldData, newData);
        cmd.Execute();
        node.Data.Should().ContainKey("label");

        cmd.Undo();
        node.Data.Should().BeEmpty();
    }

    // ── UpdateZIndexCommand ───────────────────────────────────────────────────

    [Fact]
    public void UpdateZIndexCommand_ExecuteChangesZIndex()
    {
        var doc = EmptyDoc();
        var node = MakeNode();
        node.ZIndex = 0;
        doc.Nodes.Add(node);

        var before = new Dictionary<string, int> { [node.Id] = 0 };
        var after = new Dictionary<string, int> { [node.Id] = 5 };
        var cmd = new UpdateZIndexCommand(doc, before, after);
        cmd.Execute();

        node.ZIndex.Should().Be(5);
    }

    [Fact]
    public void UpdateZIndexCommand_UndoRestoresZIndex()
    {
        var doc = EmptyDoc();
        var node = MakeNode();
        node.ZIndex = 0;
        doc.Nodes.Add(node);

        var before = new Dictionary<string, int> { [node.Id] = 0 };
        var after = new Dictionary<string, int> { [node.Id] = 5 };
        var cmd = new UpdateZIndexCommand(doc, before, after);
        cmd.Execute();
        cmd.Undo();

        node.ZIndex.Should().Be(0);
    }

    [Fact]
    public void UpdateZIndexCommand_SupportsMultipleNodes()
    {
        var doc = EmptyDoc();
        var n1 = MakeNode(); n1.ZIndex = 1;
        var n2 = MakeNode(); n2.ZIndex = 2;
        doc.Nodes.AddRange([n1, n2]);

        var before = new Dictionary<string, int> { [n1.Id] = 1, [n2.Id] = 2 };
        var after = new Dictionary<string, int> { [n1.Id] = 10, [n2.Id] = 20 };
        var cmd = new UpdateZIndexCommand(doc, before, after);
        cmd.Execute();

        n1.ZIndex.Should().Be(10);
        n2.ZIndex.Should().Be(20);

        cmd.Undo();

        n1.ZIndex.Should().Be(1);
        n2.ZIndex.Should().Be(2);
    }

    // ── Transactions ──────────────────────────────────────────────────────────

    [Fact]
    public void TransactionScope_GroupsMultipleCommandsIntoSingleUndo()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var n1 = MakeNode();
        var n2 = MakeNode();

        using (stack.TransactionScope("Add two nodes"))
        {
            stack.Push(new AddNodeCommand(doc, n1));
            stack.Push(new AddNodeCommand(doc, n2));
        }

        doc.Nodes.Should().HaveCount(2);
        stack.CanUndo.Should().BeTrue();
        stack.NextUndoName.Should().Be("Add two nodes");

        stack.Undo();
        doc.Nodes.Should().BeEmpty();
        stack.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void TransactionScope_RedoReappliesAllCommands()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var n1 = MakeNode();

        using (stack.TransactionScope("Add node"))
        {
            stack.Push(new AddNodeCommand(doc, n1));
        }

        stack.Undo();
        doc.Nodes.Should().BeEmpty();

        stack.Redo();
        doc.Nodes.Should().ContainSingle(n => n.Id == n1.Id);
    }

    [Fact]
    public void Transaction_EmptyTransaction_IsNotPushed()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();

        using (stack.TransactionScope("Empty"))
        {
            // no commands
        }

        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Transaction_NestedTransactionThrows()
    {
        var stack = new DiagramCommandStack();
        stack.BeginTransaction("Outer");

        Action act = () => stack.BeginTransaction("Inner");
        act.Should().Throw<InvalidOperationException>().WithMessage("*already in progress*");

        stack.RollbackTransaction();
    }

    [Fact]
    public void Transaction_Rollback_RevertsCommands()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var n1 = MakeNode();

        stack.BeginTransaction("Add node");
        stack.Push(new AddNodeCommand(doc, n1));
        stack.RollbackTransaction();

        doc.Nodes.Should().BeEmpty();
        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Transaction_MoveNodes_CoalescesInsideTransaction()
    {
        var doc = EmptyDoc();
        var stack = new DiagramCommandStack();
        var node = MakeNode(x: 0, y: 0);
        doc.Nodes.Add(node);
        // Explicit logical stamps keep the coalesce window off the wall clock (FZE-1).
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMilliseconds(50);

        using (stack.TransactionScope("Move"))
        {
            var before1 = new Dictionary<string, NodeMoveState> { [node.Id] = new(0, 0, null, -1, -1) };
            var after1 = new Dictionary<string, NodeMoveState> { [node.Id] = new(10, 10, null, -1, -1) };
            stack.Push(new MoveNodesCommand(doc, before1, after1, t1));

            var before2 = new Dictionary<string, NodeMoveState> { [node.Id] = new(10, 10, null, -1, -1) };
            var after2 = new Dictionary<string, NodeMoveState> { [node.Id] = new(20, 20, null, -1, -1) };
            stack.Push(new MoveNodesCommand(doc, before2, after2, t2));
        }

        stack.CanUndo.Should().BeTrue();
        stack.Undo();
        node.X.Should().Be(0);
        node.Y.Should().Be(0);
    }
}
