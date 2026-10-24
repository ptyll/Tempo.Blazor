using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.EmailTemplates.Services;

/// <summary>
/// Maintains undo/redo history of document snapshots. Each pushed state is an immutable, independent
/// snapshot, the depth is bounded, and rapid edits sharing a coalesce key within a short window merge into
/// a single undo step (so typing is one undo, not one per keystroke).
/// <para>
/// Phase 7 (commit cost): a snapshot is the document's serialized JSON, not a materialized object graph.
/// Taking one is a single serialization (a deep copy was serialize + deserialize), a string cannot be
/// mutated through an alias, and the deserialization is paid only by an actual undo/redo.
/// </para>
/// <para>
/// Every state also carries a <see cref="Version"/>: a monotonically increasing id the editor compares with
/// the version the host last confirmed, which replaces re-serializing the whole document on every edit to
/// ask "is it dirty?". A version id is never reused, not even by a coalesced push or by
/// <see cref="Initialize"/>, so a version that was saved can never equal a later, different state.
/// </para>
/// </summary>
public sealed class EditorHistoryService
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time;
    private readonly int _maxDepth;
    private readonly LinkedList<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();

    private Snapshot _current = new(string.Empty, 0, null);
    private long _nextVersion;
    private string? _lastCoalesceKey;
    private DateTimeOffset _lastPushedAt;

    // Json is the CONTENT signature: the document serialized with its timestamp cleared (a timestamp is not
    // content). The timestamp is kept beside it, so an undo/redo still restores the document exactly.
    private readonly record struct Snapshot(string Json, long Version, DateTime? UpdatedAt);

    /// <summary>Initializes the service with an optional clock and history depth limit.</summary>
    public EditorHistoryService(TimeProvider? timeProvider = null, int maxDepth = 50)
    {
        _time = timeProvider ?? TimeProvider.System;
        _maxDepth = Math.Max(1, maxDepth);
    }

    /// <summary>Gets whether an undo is available.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets whether a redo is available.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// The id of the current state. It changes on every <see cref="Push"/> (a coalesced one included),
    /// <see cref="Initialize"/>, and on an <see cref="Undo"/>/<see cref="Redo"/> that moves, where it becomes
    /// the id of the state landed on - so undoing back onto a saved state reads as that saved version again.
    /// </summary>
    public long Version => _current.Version;

    /// <summary>
    /// The content signature of the current state: the canonical JSON with the timestamp left out. Two states
    /// with equal signatures are the same document as far as saving is concerned, so the editor compares this
    /// string (a memory comparison, no serialization) to decide that typing the saved text back is clean again.
    /// </summary>
    public string Content => _current.Json;

    /// <summary>Resets the history to a single baseline state.</summary>
    public void Initialize(EmailTemplateDocument document)
    {
        _undo.Clear();
        _redo.Clear();
        _current = Capture(document);
        _lastCoalesceKey = null;
    }

    /// <summary>
    /// Records a new document state. When <paramref name="coalesceKey"/> matches the previous push and
    /// falls within the coalesce window, it replaces the current state instead of adding a new step.
    /// </summary>
    public void Push(EmailTemplateDocument document, string? coalesceKey = null)
    {
        var now = _time.GetUtcNow();
        var coalesce = coalesceKey is not null
            && coalesceKey == _lastCoalesceKey
            && (now - _lastPushedAt) <= CoalesceWindow;

        if (!coalesce)
        {
            _undo.AddLast(_current);
            if (_undo.Count > _maxDepth) _undo.RemoveFirst();
            _redo.Clear();
        }

        _current = Capture(document);
        _lastCoalesceKey = coalesceKey;
        _lastPushedAt = now;
    }

    /// <summary>Moves one step back and returns an independent copy of that state, or <see langword="null"/>.</summary>
    public EmailTemplateDocument? Undo()
    {
        if (_undo.Count == 0) return null;
        _redo.Push(_current);
        _current = _undo.Last!.Value;
        _undo.RemoveLast();
        _lastCoalesceKey = null;
        return Restore(_current);
    }

    /// <summary>Moves one step forward and returns an independent copy of that state, or <see langword="null"/>.</summary>
    public EmailTemplateDocument? Redo()
    {
        if (_redo.Count == 0) return null;
        _undo.AddLast(_current);
        _current = _redo.Pop();
        _lastCoalesceKey = null;
        return Restore(_current);
    }

    private static EmailTemplateDocument Restore(Snapshot snapshot)
    {
        var document = EmailTemplateSerializer.Deserialize(snapshot.Json);
        document.UpdatedAt = snapshot.UpdatedAt;
        return document;
    }

    /// <summary>
    /// One serialization of the content (<see cref="EmailTemplateSerializer.SerializeContent"/> leaves the
    /// timestamp out without ever writing to the document, so a reader on another thread never sees it flicker).
    /// </summary>
    private Snapshot Capture(EmailTemplateDocument document)
        => new(EmailTemplateSerializer.SerializeContent(document), ++_nextVersion, document.UpdatedAt);
}
