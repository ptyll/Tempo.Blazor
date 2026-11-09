using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Commands;

/// <summary>Tracks the full movement state of a node including swimlane parent.</summary>
public sealed record NodeMoveState(double X, double Y, string? ParentNodeId, int SwimlaneRow, int SwimlaneColumn);

/// <summary>
/// Moves one or more nodes to new positions and updates their swimlane parent state.
///
/// <para>
/// Supports coalescing: consecutive move commands targeting the same node ids within
/// a 100 ms window are merged. The "before" snapshot is kept from the first move;
/// only the "after" positions are updated.
/// </para>
/// </summary>
public sealed class MoveNodesCommand : IDiagramCommand
{
    private readonly DiagramDocument _doc;
    private readonly Dictionary<string, NodeMoveState> _before;
    private Dictionary<string, NodeMoveState> _after;
    private DateTime _pushedAt;

    /// <summary>Production constructor: the command stamps the push time with <see cref="DateTime.UtcNow"/>.</summary>
    public MoveNodesCommand(
        DiagramDocument doc,
        Dictionary<string, NodeMoveState> before,
        Dictionary<string, NodeMoveState> after)
        : this(doc, before, after, DateTime.UtcNow)
    {
    }

    /// <param name="pushedAt">
    /// Logical push timestamp. Tests pass an explicit value so the coalescing window is deterministic
    /// regardless of how long the machine stalled between two pushes (FZE-1). Internal so the public
    /// constructor signature stays binary-compatible; production code uses the public overload.
    /// </param>
    internal MoveNodesCommand(
        DiagramDocument doc,
        Dictionary<string, NodeMoveState> before,
        Dictionary<string, NodeMoveState> after,
        DateTime pushedAt)
    {
        _doc = doc;
        _before = before;
        _after = after;
        _pushedAt = pushedAt;
    }

    public string Name => _after.Count == 1
        ? "Move node"
        : $"Move {_after.Count} nodes";

    private IReadOnlySet<string> Ids => _after.Keys.ToHashSet();

    public void Execute()
    {
        foreach (var node in _doc.Nodes)
        {
            if (_after.TryGetValue(node.Id, out var state))
            {
                node.X = state.X;
                node.Y = state.Y;
                node.ParentNodeId = state.ParentNodeId;
                node.SwimlaneRow = state.SwimlaneRow;
                node.SwimlaneColumn = state.SwimlaneColumn;
            }
        }
    }

    public void Undo()
    {
        foreach (var node in _doc.Nodes)
        {
            if (_before.TryGetValue(node.Id, out var state))
            {
                node.X = state.X;
                node.Y = state.Y;
                node.ParentNodeId = state.ParentNodeId;
                node.SwimlaneRow = state.SwimlaneRow;
                node.SwimlaneColumn = state.SwimlaneColumn;
            }
        }
    }

    /// <summary>
    /// Attempts to merge <paramref name="next"/> into this command.
    /// Returns <c>true</c> if merged.
    /// </summary>
    internal bool TryCoalesce(MoveNodesCommand next)
    {
        if ((next._pushedAt - _pushedAt).TotalMilliseconds > 100) return false;
        if (!next.Ids.SetEquals(Ids)) return false;

        foreach (var node in _doc.Nodes)
        {
            if (next._after.TryGetValue(node.Id, out var state))
            {
                node.X = state.X;
                node.Y = state.Y;
                node.ParentNodeId = state.ParentNodeId;
                node.SwimlaneRow = state.SwimlaneRow;
                node.SwimlaneColumn = state.SwimlaneColumn;
            }
        }

        _after = next._after;
        _pushedAt = next._pushedAt;
        return true;
    }
}
