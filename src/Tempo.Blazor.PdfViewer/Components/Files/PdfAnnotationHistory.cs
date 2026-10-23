using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Files;

// FZE-15: the annotator's per-session undo/redo. A small command stack (the
// DocumentEditorCommandStack pattern, 100 steps) whose commands run against the
// annotator's IPdfAnnotationProvider — the Abstractions contract is untouched.
// The history lives per TmPdfAnnotator instance, is cleared when the document
// changes, and its redo tail is dropped by every new user action.

/// <summary>The user-visible action a history command represents (drives the localized
/// "Undo: {action}" labels and the aria-live announcements).</summary>
internal enum PdfAnnotatorHistoryAction
{
    /// <summary>A new comment thread.</summary>
    Create,

    /// <summary>A reply added to an existing thread.</summary>
    Reply,

    /// <summary>A comment body edit.</summary>
    Edit,

    /// <summary>A thread resolved.</summary>
    Resolve,

    /// <summary>A thread reopened.</summary>
    Reopen,

    /// <summary>A comment (or the whole thread) deleted.</summary>
    Delete,

    /// <summary>A stamp annotation placed.</summary>
    Stamp,

    /// <summary>A freehand ink drawing created.</summary>
    Ink,

    /// <summary>A text highlight created.</summary>
    Highlight
}

/// <summary>Outcome of a history operation.</summary>
internal enum PdfAnnotationHistoryOutcome
{
    /// <summary>There was nothing to undo/redo.</summary>
    NothingToDo,

    /// <summary>The step was applied and the stack moved.</summary>
    Applied,

    /// <summary>The step was skipped: another user changed or deleted the thread, so the
    /// command is moot — it is dropped from the stack and a localized hint is raised.</summary>
    Skipped
}

/// <summary>Result of a history operation: the outcome plus the current id of the affected
/// thread (null when the thread no longer exists or nothing was applied).</summary>
internal readonly record struct PdfAnnotationHistoryResult(
    PdfAnnotationHistoryOutcome Outcome,
    string? ThreadId,
    PdfAnnotatorHistoryAction? Action = null)
{
    /// <summary>Whether the operation moved the stack (applied or skipped a step).</summary>
    public bool MovedStack => Outcome is not PdfAnnotationHistoryOutcome.NothingToDo;
}

/// <summary>One undoable annotator mutation. <see cref="ExecuteAsync"/> runs the action
/// (the first execution and every redo); <see cref="UndoAsync"/> runs the inverse.</summary>
internal interface IPdfAnnotationCommand
{
    /// <summary>The user-visible action the command represents.</summary>
    PdfAnnotatorHistoryAction Action { get; }

    /// <summary>Current id of the thread the command last touched (null when the thread no
    /// longer exists, e.g. after undoing a create). Resolved through the history's id map.</summary>
    string? AffectedThreadId { get; }

    /// <summary>Executes the action.</summary>
    Task ExecuteAsync();

    /// <summary>Reverts the action.</summary>
    Task UndoAsync();
}

/// <summary>Services a command needs from its owning history: the provider, the document
/// and the thread-id remapping of re-created threads.</summary>
internal sealed class PdfAnnotationHistoryContext
{
    /// <summary>Creates a context for one command execution scope.</summary>
    public PdfAnnotationHistoryContext(
        IPdfAnnotationProvider provider,
        string documentId,
        Func<string, string> resolveThreadId,
        Action<string, string> remapThreadId)
    {
        Provider = provider;
        DocumentId = documentId;
        ResolveThreadId = resolveThreadId;
        RemapThreadId = remapThreadId;
    }

    /// <summary>The annotator's persistence provider.</summary>
    public IPdfAnnotationProvider Provider { get; }

    /// <summary>The document the command belongs to (the stack is cleared on a change).</summary>
    public string DocumentId { get; }

    /// <summary>Maps a recorded thread id to its CURRENT id (re-created threads are remapped).</summary>
    public Func<string, string> ResolveThreadId { get; }

    /// <summary>Records that <paramref name="oldId"/> now lives under <paramref name="newId"/>.</summary>
    public Action<string, string> RemapThreadId { get; }
}

/// <summary>
/// Per-session undo/redo stack for <see cref="TmPdfAnnotator"/>. Commands are executed and
/// recorded by the annotator's mutation methods; <see cref="UndoAsync"/>/<see cref="RedoAsync"/>
/// walk the stack. A conflict (the thread changed or deleted by another user) skips the step,
/// drops the command and raises <see cref="ConflictDetected"/>; any other provider failure
/// propagates and leaves the stack untouched.
/// </summary>
internal sealed class PdfAnnotationHistory
{
    /// <summary>Default maximum number of recorded steps.</summary>
    public const int DefaultLimit = 100;

    private readonly int _maxDepth;
    private readonly List<IPdfAnnotationCommand> _undo = [];
    private readonly List<IPdfAnnotationCommand> _redo = [];
    private readonly Dictionary<string, string> _idMap = new(StringComparer.Ordinal);

    // FZE-15 review: Execute/Undo/Redo serialize on a one-slot gate — a double-click Undo
    // (or an execute landing mid-undo) must never interleave two stack walks, or the
    // positional pops could drop the wrong command. The gate is awaited INSIDE the busy
    // flag: _busy flips before the wait so the UI can disable the buttons right away.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _busy;

    /// <summary>Creates a history with the requested maximum depth (default 100 steps).</summary>
    public PdfAnnotationHistory(int maxDepth = DefaultLimit)
    {
        _maxDepth = Math.Max(1, maxDepth);
    }

    /// <summary>Raised whenever the stack state changes (execute, undo, redo, clear, skip).</summary>
    public event Action? Changed;

    /// <summary>Raised when an undo/redo step was skipped because another user changed or
    /// deleted the thread — the annotator shows the localized conflict hint.</summary>
    public event Action? ConflictDetected;

    /// <summary>Whether there is a step to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether there is a step to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The action of the next undo step, or null when the stack is empty.</summary>
    public PdfAnnotatorHistoryAction? NextUndoAction
        => _undo.Count == 0 ? null : _undo[^1].Action;

    /// <summary>The action of the next redo step, or null when the redo tail is empty.</summary>
    public PdfAnnotatorHistoryAction? NextRedoAction
        => _redo.Count == 0 ? null : _redo[^1].Action;

    /// <summary>Number of recorded undo steps.</summary>
    public int Count => _undo.Count;

    /// <summary>Whether an Execute/Undo/Redo is currently running or waiting for a concurrent
    /// one to finish. The undo/redo buttons and the mobile "More" entries disable while true.</summary>
    public bool IsBusy => _busy;

    /// <summary>Resolves a recorded thread id to its current id (re-created threads are
    /// remapped; unknown ids pass through).</summary>
    public string ResolveThreadId(string threadId)
    {
        var current = threadId;
        var hops = 0;
        while (_idMap.TryGetValue(current, out var mapped))
        {
            current = mapped;
            if (++hops > _idMap.Count)
            {
                break; // defensive: a cycle can only come from a broken remap call
            }
        }

        return current;
    }

    /// <summary>Executes a command, records it as an undo step and clears the redo tail.
    /// Serialized against Undo/Redo — a call landing mid-undo runs after it.</summary>
    public async Task<PdfAnnotationHistoryResult> ExecuteAsync(IPdfAnnotationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var _ = await AcquireAsync();

        // A failing provider call is recorded as nothing: the stack stays consistent.
        await command.ExecuteAsync();

        _undo.Add(command);
        if (_undo.Count > _maxDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke();
        return new(PdfAnnotationHistoryOutcome.Applied, command.AffectedThreadId, command.Action);
    }

    /// <summary>Undoes the newest command. Returns its outcome; on a conflict the command is
    /// dropped and <see cref="ConflictDetected"/> fires; other failures propagate untouched.
    /// Serialized against Execute/Redo — a concurrent call runs after this one completes.</summary>
    public async Task<PdfAnnotationHistoryResult> UndoAsync()
    {
        await using var _ = await AcquireAsync();

        if (_undo.Count == 0)
        {
            return new(PdfAnnotationHistoryOutcome.NothingToDo, null, null);
        }

        var command = _undo[^1];
        try
        {
            await command.UndoAsync();
        }
        catch (KeyNotFoundException)
        {
            // Another user changed or deleted the thread: the step is moot — drop it.
            // By-reference removal: a concurrent clear/mutation can never pop a sibling.
            _undo.Remove(command);
            ConflictDetected?.Invoke();
            Changed?.Invoke();
            return new(PdfAnnotationHistoryOutcome.Skipped, null, null);
        }

        _undo.Remove(command);
        _redo.Add(command);
        if (_redo.Count > _maxDepth)
        {
            _redo.RemoveAt(0);
        }

        Changed?.Invoke();
        return new(PdfAnnotationHistoryOutcome.Applied, command.AffectedThreadId, command.Action);
    }

    /// <summary>Redoes the most recently undone command. Conflict handling mirrors
    /// <see cref="UndoAsync"/>; the call is serialized against Execute/Undo.</summary>
    public async Task<PdfAnnotationHistoryResult> RedoAsync()
    {
        await using var _ = await AcquireAsync();

        if (_redo.Count == 0)
        {
            return new(PdfAnnotationHistoryOutcome.NothingToDo, null, null);
        }

        var command = _redo[^1];
        try
        {
            await command.ExecuteAsync();
        }
        catch (KeyNotFoundException)
        {
            _redo.Remove(command);
            ConflictDetected?.Invoke();
            Changed?.Invoke();
            return new(PdfAnnotationHistoryOutcome.Skipped, null, null);
        }

        _redo.Remove(command);
        _undo.Add(command);
        if (_undo.Count > _maxDepth)
        {
            _undo.RemoveAt(0);
        }

        Changed?.Invoke();
        return new(PdfAnnotationHistoryOutcome.Applied, command.AffectedThreadId, command.Action);
    }

    /// <summary>Acquires the history's one-slot gate for the calling operation: the busy flag
    /// flips synchronously (the UI disables the buttons), the gate serializes the walk, and
    /// both release through the returned awaitable even when the command throws.</summary>
    private async Task<GateLease> AcquireAsync()
    {
        _busy = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        return new GateLease(this);
    }

    private readonly struct GateLease : IAsyncDisposable
    {
        private readonly PdfAnnotationHistory _history;

        public GateLease(PdfAnnotationHistory history) => _history = history;

        public async ValueTask DisposeAsync()
        {
            _history._gate.Release();
            _history._busy = false;
            await ValueTask.CompletedTask;
        }
    }

    /// <summary>Clears the whole history (a new document invalidates every recorded step).</summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0 && _idMap.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        _idMap.Clear();
        Changed?.Invoke();
    }

    internal void RemapThreadId(string oldId, string newId)
    {
        if (string.IsNullOrEmpty(oldId)
            || string.IsNullOrEmpty(newId)
            || string.Equals(oldId, newId, StringComparison.Ordinal))
        {
            return;
        }

        _idMap[oldId] = newId;
    }
}
