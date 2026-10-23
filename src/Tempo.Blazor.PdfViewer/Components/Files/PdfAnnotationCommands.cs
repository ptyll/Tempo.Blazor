using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Files;

// FZE-15: the undoable annotator commands. Every command works purely over the
// IPdfAnnotationProvider contract (Abstractions untouched): create/reply/edit/delete are
// inverted by provider calls, resolve/reopen are mutual inverses, and a deleted thread is
// re-created from a pre-delete snapshot. Thread ids are logical: re-created threads are
// remapped through the owning history so later steps keep targeting the right thread.

/// <summary>Creates a thread (comment, highlight, stamp or ink). Undo deletes the created
/// comment (which removes the thread); redo re-creates it with a remapped id.</summary>
internal sealed class PdfAnnotationCreateCommand : IPdfAnnotationCommand
{
    private readonly PdfAnnotationHistoryContext _context;
    private readonly DocumentCommentThreadCreateRequest _request;
    private readonly DocumentCommentUser _author;
    private string? _threadId;
    private string? _commentId;

    /// <summary>Creates the command; the action follows the request's annotation kind.</summary>
    public PdfAnnotationCreateCommand(
        PdfAnnotationHistoryContext context,
        DocumentCommentThreadCreateRequest request,
        DocumentCommentUser author)
    {
        _context = context;
        _request = request;
        _author = author;
        Action = request.Kind switch
        {
            DocumentAnnotationKind.Stamp => PdfAnnotatorHistoryAction.Stamp,
            DocumentAnnotationKind.Drawing => PdfAnnotatorHistoryAction.Ink,
            DocumentAnnotationKind.Highlight => PdfAnnotatorHistoryAction.Highlight,
            _ => PdfAnnotatorHistoryAction.Create
        };
    }

    /// <inheritdoc />
    public PdfAnnotatorHistoryAction Action { get; }

    /// <inheritdoc />
    public string? AffectedThreadId => _threadId;

    /// <inheritdoc />
    public async Task ExecuteAsync()
    {
        var created = await _context.Provider.CreateThreadAsync(
            _context.DocumentId, PdfAnnotationSnapshot.CloneRequest(_request), _author);

        if (!string.IsNullOrEmpty(_threadId) && !string.Equals(_threadId, created.Id, StringComparison.Ordinal))
        {
            _context.RemapThreadId(_threadId, created.Id);
        }

        _threadId = created.Id;
        _commentId = created.Comments.Count > 0 ? created.Comments[0].Id : null;
    }

    /// <inheritdoc />
    public async Task UndoAsync()
    {
        // Fetch first: a thread deleted by another user surfaces as the history's conflict
        // signal instead of a silent no-op delete.
        var threads = await _context.Provider.GetThreadsAsync(_context.DocumentId);
        var threadId = _context.ResolveThreadId(_threadId!);
        var thread = PdfAnnotationSnapshot.FindThread(threads, threadId)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' was not found.");

        var commentId = _commentId is not null && thread.Comments.Any(c => string.Equals(c.Id, _commentId, StringComparison.Ordinal))
            ? _commentId
            : throw new KeyNotFoundException($"Comment '{_commentId}' was not found in thread '{threadId}'.");

        // Only OUR comment goes — replies added afterwards (by anyone) keep the thread alive.
        await _context.Provider.DeleteAsync(_context.DocumentId, new DocumentCommentDeleteRequest
        {
            ThreadId = threadId,
            CommentId = commentId
        });
    }
}

/// <summary>Appends a reply. Undo deletes the reply comment; redo re-adds it.</summary>
internal sealed class PdfAnnotationReplyCommand : IPdfAnnotationCommand
{
    private readonly PdfAnnotationHistoryContext _context;
    private readonly DocumentCommentReplyRequest _request;
    private readonly DocumentCommentUser _author;
    private string? _commentId;

    /// <summary>Creates the command.</summary>
    public PdfAnnotationReplyCommand(
        PdfAnnotationHistoryContext context,
        DocumentCommentReplyRequest request,
        DocumentCommentUser author)
    {
        _context = context;
        _request = request;
        _author = author;
    }

    /// <inheritdoc />
    public PdfAnnotatorHistoryAction Action => PdfAnnotatorHistoryAction.Reply;

    /// <inheritdoc />
    public string? AffectedThreadId => _context.ResolveThreadId(_request.ThreadId);

    /// <inheritdoc />
    public async Task ExecuteAsync()
    {
        var thread = await _context.Provider.ReplyAsync(
            _context.DocumentId, PdfAnnotationSnapshot.CloneRequest(_request), _author);
        _commentId = thread.Comments.Count > 0 ? thread.Comments[^1].Id : null;
    }

    /// <inheritdoc />
    public async Task UndoAsync()
    {
        var (threadId, commentId) = await ResolveTargetAsync();
        await _context.Provider.DeleteAsync(_context.DocumentId, new DocumentCommentDeleteRequest
        {
            ThreadId = threadId,
            CommentId = commentId
        });
    }

    private async Task<(string ThreadId, string CommentId)> ResolveTargetAsync()
    {
        var threads = await _context.Provider.GetThreadsAsync(_context.DocumentId);
        var threadId = _context.ResolveThreadId(_request.ThreadId);
        var thread = PdfAnnotationSnapshot.FindThread(threads, threadId)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' was not found.");

        return _commentId is not null && thread.Comments.Any(c => string.Equals(c.Id, _commentId, StringComparison.Ordinal))
            ? (threadId, _commentId)
            : throw new KeyNotFoundException($"Comment '{_commentId}' was not found in thread '{threadId}'.");
    }
}

/// <summary>Edits a comment body. Undo restores the previous body; redo re-applies the new one.</summary>
internal sealed class PdfAnnotationEditCommand : IPdfAnnotationCommand
{
    private readonly PdfAnnotationHistoryContext _context;
    private readonly DocumentCommentEditRequest _request;
    private readonly string _previousBody;

    /// <summary>Creates the command; <paramref name="previousBody"/> is the body before the edit.</summary>
    public PdfAnnotationEditCommand(
        PdfAnnotationHistoryContext context,
        DocumentCommentEditRequest request,
        string previousBody)
    {
        _context = context;
        _request = request;
        _previousBody = previousBody;
    }

    /// <inheritdoc />
    public PdfAnnotatorHistoryAction Action => PdfAnnotatorHistoryAction.Edit;

    /// <inheritdoc />
    public string? AffectedThreadId => _context.ResolveThreadId(_request.ThreadId);

    /// <inheritdoc />
    public Task ExecuteAsync() => EditAsync(_request.Body);

    /// <inheritdoc />
    public Task UndoAsync() => EditAsync(_previousBody);

    private Task EditAsync(string body)
        => _context.Provider.EditAsync(_context.DocumentId, PdfAnnotationSnapshot.CloneRequest(_request, body));
}

/// <summary>Resolves or reopens a thread; the two states are mutual inverses.</summary>
internal sealed class PdfAnnotationStatusCommand : IPdfAnnotationCommand
{
    private readonly PdfAnnotationHistoryContext _context;
    private readonly string _threadId;
    private readonly DocumentCommentUser? _user;
    private readonly bool _toResolved;

    /// <summary>Creates the command; <paramref name="toResolved"/> selects resolve (true) or reopen.</summary>
    public PdfAnnotationStatusCommand(PdfAnnotationHistoryContext context, string threadId, bool toResolved, DocumentCommentUser? user)
    {
        _context = context;
        _threadId = threadId;
        _toResolved = toResolved;
        _user = user;
    }


    /// <inheritdoc />
    public PdfAnnotatorHistoryAction Action => _toResolved
        ? PdfAnnotatorHistoryAction.Resolve
        : PdfAnnotatorHistoryAction.Reopen;

    /// <inheritdoc />
    public string? AffectedThreadId => _context.ResolveThreadId(_threadId);

    /// <inheritdoc />
    public Task ExecuteAsync() => ApplyAsync(_toResolved);

    /// <inheritdoc />
    public Task UndoAsync() => ApplyAsync(!_toResolved);

    private Task ApplyAsync(bool toResolved)
    {
        var threadId = _context.ResolveThreadId(_threadId);
        return toResolved
            ? _context.Provider.ResolveAsync(_context.DocumentId, threadId, _user)
            : _context.Provider.ReopenAsync(_context.DocumentId, threadId, _user);
    }
}

/// <summary>Deletes a comment (removing the thread when it was the last one). Undo re-adds the
/// comment — via a reply when the thread survived, otherwise by re-creating the whole thread
/// from the pre-delete snapshot (same anchor, kind, color, ink and stamp, replies replayed in
/// order); redo deletes the re-added comment again.</summary>
internal sealed class PdfAnnotationDeleteCommand : IPdfAnnotationCommand
{
    private readonly PdfAnnotationHistoryContext _context;
    private readonly DocumentCommentThread _snapshot;
    private readonly string _threadId;
    private readonly int _commentIndex;
    private string? _currentCommentId;

    /// <summary>Creates the command. The snapshot must be taken BEFORE the delete; it is
    /// cloned so later mutations of the caller's copy cannot corrupt the undo.</summary>
    public PdfAnnotationDeleteCommand(
        PdfAnnotationHistoryContext context,
        DocumentCommentThread snapshot,
        DocumentCommentDeleteRequest request)
    {
        _context = context;
        _threadId = request.ThreadId;
        _snapshot = PdfAnnotationSnapshot.CloneThread(snapshot);
        _commentIndex = _snapshot.Comments.FindIndex(c => string.Equals(c.Id, request.CommentId, StringComparison.Ordinal));
        if (_commentIndex < 0)
        {
            _commentIndex = Math.Max(0, _snapshot.Comments.Count - 1);
        }

        _currentCommentId = request.CommentId;
    }

    /// <inheritdoc />
    public PdfAnnotatorHistoryAction Action => PdfAnnotatorHistoryAction.Delete;

    /// <inheritdoc />
    public string? AffectedThreadId => _context.ResolveThreadId(_threadId);

    /// <inheritdoc />
    public Task ExecuteAsync()
    {
        var threadId = _context.ResolveThreadId(_threadId);
        return _context.Provider.DeleteAsync(_context.DocumentId, new DocumentCommentDeleteRequest
        {
            ThreadId = threadId,
            CommentId = _currentCommentId ?? string.Empty
        });
    }

    /// <inheritdoc />
    public async Task UndoAsync()
    {
        var threads = await _context.Provider.GetThreadsAsync(_context.DocumentId);
        var threadId = _context.ResolveThreadId(_threadId);
        var thread = PdfAnnotationSnapshot.FindThread(threads, threadId);

        if (thread is not null)
        {
            var original = _snapshot.Comments[_commentIndex];

            // An earlier undo of the SAME thread may have already replayed this comment back
            // (the snapshot re-create restores the whole thread): if the thread now holds at
            // least as many comments as before the delete and one of them IS the deleted one,
            // just track its fresh id — re-adding would duplicate it.
            var restored = thread.Comments.FirstOrDefault(c =>
                string.Equals(c.AuthorId, original.AuthorId, StringComparison.Ordinal)
                && string.Equals(c.Body, original.Body, StringComparison.Ordinal));
            if (thread.Comments.Count >= _snapshot.Comments.Count && restored is not null)
            {
                _currentCommentId = restored.Id;
                return;
            }

            // The delete left the thread alive: the comment returns as a (new) reply.
            var replyThread = await _context.Provider.ReplyAsync(
                _context.DocumentId, ReplyRequest(threadId, original), AuthorOf(original));
            _currentCommentId = replyThread.Comments.Count > 0 ? replyThread.Comments[^1].Id : null;
            return;
        }

        // The delete removed the whole thread: re-create it from the snapshot and replay
        // every comment in order (the deleted one returns to its original position). The
        // remap records ONLY after the full replay succeeded — a half-restored thread must
        // not take over the logical id (a later step would target the incomplete copy).
        var created = await _context.Provider.CreateThreadAsync(
            _context.DocumentId, RecreateRequest(_snapshot), AuthorOf(_snapshot.Comments[0]));

        try
        {
            var newIds = new List<string>();
            if (created.Comments.Count > 0)
            {
                newIds.Add(created.Comments[0].Id);
            }

            foreach (var comment in _snapshot.Comments.Skip(1))
            {
                var replyThread = await _context.Provider.ReplyAsync(
                    _context.DocumentId, ReplyRequest(created.Id, comment), AuthorOf(comment));
                if (replyThread.Comments.Count > 0)
                {
                    newIds.Add(replyThread.Comments[^1].Id);
                }
            }

            if (_snapshot.Status == DocumentCommentThreadStatus.Resolved)
            {
                await _context.Provider.ResolveAsync(_context.DocumentId, created.Id, null);
            }

            _currentCommentId = _commentIndex < newIds.Count
                ? newIds[_commentIndex]
                : newIds.Count > 0 ? newIds[^1] : null;
        }
        catch
        {
            // The replay failed midway: remove what the partial restore created (the create
            // comment plus every reply that landed) and surface the failure — the stack stays
            // unchanged and the logical id keeps pointing where it did before.
            try
            {
                var fresh = await _context.Provider.GetThreadsAsync(_context.DocumentId);
                var partial = PdfAnnotationSnapshot.FindThread(fresh, created.Id);
                if (partial is not null)
                {
                    foreach (var comment in partial.Comments.ToList())
                    {
                        await _context.Provider.DeleteAsync(_context.DocumentId, new DocumentCommentDeleteRequest
                        {
                            ThreadId = created.Id,
                            CommentId = comment.Id
                        });
                    }
                }
            }
            catch
            {
                // The cleanup itself failed — the rethrow below still surfaces the original cause.
            }

            throw;
        }

        _context.RemapThreadId(threadId, created.Id);
    }

    private DocumentCommentReplyRequest ReplyRequest(string threadId, DocumentComment comment)
        => new()
        {
            ThreadId = threadId,
            Body = comment.Body,
            Mentions = comment.Mentions.Count == 0 ? [] : [.. comment.Mentions]
        };

    private static DocumentCommentThreadCreateRequest RecreateRequest(DocumentCommentThread snapshot)
    {
        var first = snapshot.Comments.Count > 0 ? snapshot.Comments[0] : new DocumentComment();
        return new DocumentCommentThreadCreateRequest
        {
            Anchor = PdfAnnotationSnapshot.CloneAnchor(snapshot.Anchor),
            Body = first.Body,
            Mentions = first.Mentions.Count == 0 ? [] : [.. first.Mentions],
            Kind = snapshot.Kind,
            Color = snapshot.Color,
            StampText = snapshot.StampText,
            InkStrokes = snapshot.InkStrokes.Count == 0 ? [] : [.. snapshot.InkStrokes.Select(PdfAnnotationSnapshot.CloneStroke)]
        };
    }

    private static DocumentCommentUser AuthorOf(DocumentComment comment)
        => new()
        {
            UserId = comment.AuthorId,
            DisplayName = comment.AuthorName,
            AvatarUrl = comment.AuthorAvatarUrl
        };
}

/// <summary>Shared helpers: provider-result lookups and deep clones of the annotation models
/// (the models are mutable classes, so history snapshots must copy them).</summary>
internal static class PdfAnnotationSnapshot
{
    /// <summary>Finds a thread by id, or null.</summary>
    public static DocumentCommentThread? FindThread(
        IReadOnlyList<DocumentCommentThread> threads, string threadId)
        => threads.FirstOrDefault(t => string.Equals(t.Id, threadId, StringComparison.Ordinal));

    /// <summary>Deep clone of a create request.</summary>
    public static DocumentCommentThreadCreateRequest CloneRequest(DocumentCommentThreadCreateRequest request)
        => new()
        {
            Anchor = CloneAnchor(request.Anchor),
            Body = request.Body,
            Mentions = CloneMentions(request.Mentions),
            Kind = request.Kind,
            Color = request.Color,
            StampText = request.StampText,
            InkStrokes = request.InkStrokes.Count == 0 ? [] : [.. request.InkStrokes.Select(CloneStroke)]
        };

    /// <summary>Deep clone of a reply request.</summary>
    public static DocumentCommentReplyRequest CloneRequest(DocumentCommentReplyRequest request)
        => new()
        {
            ThreadId = request.ThreadId,
            Body = request.Body,
            Mentions = CloneMentions(request.Mentions)
        };

    /// <summary>Deep clone of an edit request with an explicit body.</summary>
    public static DocumentCommentEditRequest CloneRequest(DocumentCommentEditRequest request, string body)
        => new()
        {
            ThreadId = request.ThreadId,
            CommentId = request.CommentId,
            Body = body,
            Mentions = CloneMentions(request.Mentions)
        };

    /// <summary>Deep clone of a thread (anchor, ink strokes, status and comments included).</summary>
    public static DocumentCommentThread CloneThread(DocumentCommentThread thread)
        => new()
        {
            Id = thread.Id,
            Anchor = CloneAnchor(thread.Anchor),
            Status = thread.Status,
            Comments = [.. thread.Comments.Select(CloneComment)],
            ResolvedByUserId = thread.ResolvedByUserId,
            ResolvedByName = thread.ResolvedByName,
            ResolvedAt = thread.ResolvedAt,
            Kind = thread.Kind,
            Color = thread.Color,
            StampText = thread.StampText,
            InkStrokes = thread.InkStrokes.Count == 0 ? [] : [.. thread.InkStrokes.Select(CloneStroke)]
        };

    /// <summary>Deep clone of an anchor (the rects list is copied too).</summary>
    public static DocumentCommentAnchor CloneAnchor(DocumentCommentAnchor anchor)
        => new()
        {
            PageNumber = anchor.PageNumber,
            Kind = anchor.Kind,
            X = anchor.X,
            Y = anchor.Y,
            Width = anchor.Width,
            Height = anchor.Height,
            Rects = anchor.Rects.Count == 0
                ? []
                : [.. anchor.Rects.Select(r => DocumentCommentRect.Create(r.X, r.Y, r.Width, r.Height))],
            HighlightedText = anchor.HighlightedText
        };

    private static DocumentComment CloneComment(DocumentComment comment)
        => new()
        {
            Id = comment.Id,
            AuthorId = comment.AuthorId,
            AuthorName = comment.AuthorName,
            AuthorAvatarUrl = comment.AuthorAvatarUrl,
            Body = comment.Body,
            CreatedAt = comment.CreatedAt,
            EditedAt = comment.EditedAt,
            Mentions = CloneMentions(comment.Mentions),
            Reactions = comment.Reactions.Count == 0 ? [] : [.. comment.Reactions.Select(CloneReaction)],
            CanEdit = comment.CanEdit,
            CanDelete = comment.CanDelete
        };

    private static List<DocumentCommentMention> CloneMentions(List<DocumentCommentMention> mentions)
        => mentions.Count == 0 ? [] : [.. mentions.Select(m => new DocumentCommentMention { UserId = m.UserId, DisplayName = m.DisplayName })];

    private static DocumentCommentReaction CloneReaction(DocumentCommentReaction reaction)
        => new() { Value = reaction.Value, UserIds = [.. reaction.UserIds] };

    public static DocumentInkStroke CloneStroke(DocumentInkStroke stroke)
        => new()
        {
            Points = [.. stroke.Points.Select(p => DocumentInkPoint.Create(p.X, p.Y))],
            Thickness = stroke.Thickness
        };
}
