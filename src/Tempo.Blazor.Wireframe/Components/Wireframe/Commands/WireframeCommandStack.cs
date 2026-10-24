namespace Tempo.Blazor.Components.Wireframe.Commands;

/// <summary>
/// Undo/redo history for a single wireframe editor instance.
///
/// <para>
/// Not a DI service. <c>TmWireframeEditor</c> creates one instance and cascades it
/// to child components via <c>CascadingValue</c>. Each editor on the page gets its
/// own isolated stack.
/// </para>
/// </summary>
public sealed class WireframeCommandStack
{
    private readonly int _maxDepth;
    private readonly LinkedList<IWireframeCommand> _undoStack = new();
    private readonly Stack<IWireframeCommand> _redoStack = new();

    /// <summary>Raised after every Push / Undo / Redo so the toolbar can refresh.</summary>
    public event Action? OnStackChanged;

    /// <summary>
    /// A counter that grows by one on EVERY change of the history - a push (also a coalesced one, which edits the document without
    /// adding a step), an undo, a redo and a clear. The editor feeds it to the canvas' render gate as the "the document changed behind
    /// your back" token, so a command path can no longer forget to bump a hand-kept version.
    /// </summary>
    public int Revision { get; private set; }

    private void Notify()
    {
        unchecked { Revision++; }
        OnStackChanged?.Invoke();
    }

    /// <param name="maxDepth">Maximum undo steps retained (default 50).</param>
    public WireframeCommandStack(int maxDepth = 50)
    {
        _maxDepth = maxDepth;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Name of the next command that would be undone, or <c>null</c>.</summary>
    public string? NextUndoName => _undoStack.Last?.Value.Name;

    /// <summary>Name of the next command that would be redone, or <c>null</c>.</summary>
    public string? NextRedoName => _redoStack.Count > 0 ? _redoStack.Peek().Name : null;

    // ── Push ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes <paramref name="command"/> and pushes it onto the undo stack.
    /// Clears the redo stack.
    /// <para>
    /// Move coalescing: if the previous command is a <see cref="MoveElementsCommand"/>
    /// targeting the same element ids and was pushed within 100 ms, the new command
    /// is merged into it (keeps the original "before" positions, updates "after").
    /// </para>
    /// </summary>
    public void Push(IWireframeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Attempt coalescing for move commands
        if (command is MoveElementsCommand newMove
            && _undoStack.Last?.Value is MoveElementsCommand prevMove
            && prevMove.TryCoalesce(newMove))
        {
            // Merged – just notify, no stack change
            Notify();
            return;
        }

        // Attempt coalescing for waypoint updates
        if (command is UpdateConnectorWaypointsCommand newWp
            && _undoStack.Last?.Value is UpdateConnectorWaypointsCommand prevWp
            && prevWp.TryCoalesce(newWp))
        {
            Notify();
            return;
        }

        command.Execute();

        _undoStack.AddLast(command);
        if (_undoStack.Count > _maxDepth)
            _undoStack.RemoveFirst();

        _redoStack.Clear();
        Notify();
    }

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    /// <summary>Reverses the most recent command. No-op if stack is empty.</summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Last!.Value;
        _undoStack.RemoveLast();
        cmd.Undo();
        _redoStack.Push(cmd);
        Notify();
    }

    /// <summary>Re-applies the most recently undone command. No-op if stack is empty.</summary>
    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.AddLast(cmd);
        Notify();
    }

    /// <summary>Clears both stacks (e.g. after loading a new document).</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        Notify();
    }
}
