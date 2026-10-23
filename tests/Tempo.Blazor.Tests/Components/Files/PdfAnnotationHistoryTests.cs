using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// Unit tests for the FZE-15 <see cref="PdfAnnotationHistory"/> command stack: create/reply/
/// edit/resolve/delete inversions, the redo tail rules, the 100-step limit, the id remapping
/// of re-created threads and the conflict/provider-failure paths.
/// </summary>
public class PdfAnnotationHistoryTests
{
    private const string DocId = "doc-1";
    private static readonly DocumentCommentUser Alice = new() { UserId = "alice", DisplayName = "Alice" };

    private static DocumentCommentThreadCreateRequest CreateRequest(
        string body = "note",
        DocumentAnnotationKind kind = DocumentAnnotationKind.Comment,
        string? color = null,
        string? stampText = null)
        => new()
        {
            Anchor = DocumentCommentAnchor.Area(1, 0.25, 0.25, 0.2, 0.05),
            Body = body,
            Kind = kind,
            Color = color,
            StampText = stampText
        };

    private static PdfAnnotationHistoryContext Context(IPdfAnnotationProvider provider, PdfAnnotationHistory history)
        => new(provider, DocId, history.ResolveThreadId, history.RemapThreadId);

    private static async Task<DocumentCommentThread> CreateAsync(IPdfAnnotationProvider provider, PdfAnnotationHistory history, DocumentCommentThreadCreateRequest? request = null)
    {
        var command = new PdfAnnotationCreateCommand(Context(provider, history), request ?? CreateRequest(), Alice);
        await history.ExecuteAsync(command);
        return (await provider.GetThreadsAsync(DocId))[0];
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Undo_Create_DeletesThread_Redo_RecreatesWithRemappedId()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        var created = await CreateAsync(provider, history);
        (await provider.GetThreadsAsync(DocId)).Should().HaveCount(1);

        var undo = await history.UndoAsync();

        undo.Outcome.Should().Be(PdfAnnotationHistoryOutcome.Applied);
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();
        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeTrue();

        var redo = await history.RedoAsync();

        redo.Outcome.Should().Be(PdfAnnotationHistoryOutcome.Applied);
        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().HaveCount(1);
        threads[0].Id.Should().NotBe(created.Id);
        redo.ThreadId.Should().Be(threads[0].Id);
        // the logical id recorded by the first create now resolves to the re-created thread
        history.ResolveThreadId(created.Id).Should().Be(threads[0].Id);
    }

    [Fact]
    public async Task Undo_Reply_RemovesReply()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        await CreateAsync(provider, history);

        var thread = (await provider.GetThreadsAsync(DocId))[0];
        await history.ExecuteAsync(new PdfAnnotationReplyCommand(
            Context(provider, history), new DocumentCommentReplyRequest { ThreadId = thread.Id, Body = "a reply" }, Alice));

        await history.UndoAsync();

        var afterUndo = (await provider.GetThreadsAsync(DocId)).Single();
        afterUndo.Comments.Should().ContainSingle(c => c.Body == "note");

        await history.RedoAsync();

        var afterRedo = (await provider.GetThreadsAsync(DocId)).Single();
        afterRedo.Comments.Should().HaveCount(2);
        afterRedo.Comments[^1].Body.Should().Be("a reply");
    }

    [Fact]
    public async Task Undo_Edit_RestoresPreviousBody()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        var thread = await CreateAsync(provider, history);
        var commentId = thread.Comments[0].Id;

        await history.ExecuteAsync(new PdfAnnotationEditCommand(
            Context(provider, history),
            new DocumentCommentEditRequest { ThreadId = thread.Id, CommentId = commentId, Body = "edited" },
            previousBody: "note"));

        await history.UndoAsync();
        (await provider.GetThreadsAsync(DocId))[0].Comments[0].Body.Should().Be("note");

        await history.RedoAsync();
        (await provider.GetThreadsAsync(DocId))[0].Comments[0].Body.Should().Be("edited");
    }

    [Fact]
    public async Task Undo_Resolve_Reopens()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        var thread = await CreateAsync(provider, history);

        await history.ExecuteAsync(new PdfAnnotationStatusCommand(
            Context(provider, history), thread.Id, toResolved: true, Alice));

        await history.UndoAsync();
        (await provider.GetThreadsAsync(DocId))[0].Status.Should().Be(DocumentCommentThreadStatus.Open);

        await history.RedoAsync();
        (await provider.GetThreadsAsync(DocId))[0].Status.Should().Be(DocumentCommentThreadStatus.Resolved);
    }

    [Fact]
    public async Task Undo_DeleteThread_RecreatesAnchorKindColorInkAndReplies()
    {
        // A backend may remove the WHOLE thread on a comment delete — the undo then
        // re-creates the thread from the snapshot and replays every comment in order.
        var inner = new InMemoryPdfAnnotationProvider();
        var provider = new WholeThreadDeleteProvider(inner);
        var history = new PdfAnnotationHistory();
        var created = await CreateAsync(provider, history, CreateRequest(
            body: "stamped", kind: DocumentAnnotationKind.Stamp, color: "#123456", stampText: "APPROVED"));
        await history.ExecuteAsync(new PdfAnnotationReplyCommand(
            Context(provider, history), new DocumentCommentReplyRequest { ThreadId = created.Id, Body = "a reply" }, Alice));
        var snapshot = PdfAnnotationSnapshot.CloneThread((await provider.GetThreadsAsync(DocId))[0]);

        await history.ExecuteAsync(new PdfAnnotationDeleteCommand(
            Context(provider, history),
            snapshot,
            new DocumentCommentDeleteRequest { ThreadId = created.Id, CommentId = created.Comments[0].Id }));
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();

        await history.UndoAsync();

        var restored = (await provider.GetThreadsAsync(DocId)).Single();
        restored.Id.Should().NotBe(created.Id);
        restored.Kind.Should().Be(DocumentAnnotationKind.Stamp);
        restored.Color.Should().Be("#123456");
        restored.StampText.Should().Be("APPROVED");
        restored.Anchor.PageNumber.Should().Be(1);
        restored.Anchor.Kind.Should().Be(DocumentCommentAnchorKind.Area);
        restored.Anchor.X.Should().BeApproximately(0.25, 0.0001);
        restored.Anchor.Width.Should().BeApproximately(0.2, 0.0001);
        restored.Comments.Should().HaveCount(2);
        restored.Comments[0].Body.Should().Be("stamped");
        restored.Comments[1].Body.Should().Be("a reply");
        history.ResolveThreadId(created.Id).Should().Be(restored.Id);

        await history.RedoAsync();
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Undo_DeleteComment_WhenThreadSurvives_ReaddsCommentAsReply()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        var created = await CreateAsync(provider, history);
        var replyRequest = new DocumentCommentReplyRequest { ThreadId = created.Id, Body = "a reply" };
        await history.ExecuteAsync(new PdfAnnotationReplyCommand(Context(provider, history), replyRequest, Alice));

        // delete the reply — the thread keeps its first comment
        await history.ExecuteAsync(new PdfAnnotationDeleteCommand(
            Context(provider, history),
            PdfAnnotationSnapshot.CloneThread((await provider.GetThreadsAsync(DocId))[0]),
            new DocumentCommentDeleteRequest { ThreadId = created.Id, CommentId = created.Comments[0].Id }));
        (await provider.GetThreadsAsync(DocId)).Single().Comments.Should().ContainSingle();

        await history.UndoAsync();

        var restored = (await provider.GetThreadsAsync(DocId)).Single();
        restored.Id.Should().Be(created.Id); // the thread was never removed: no remap
        restored.Comments.Should().HaveCount(2);
        restored.Comments.Should().Contain(c => c.Body == "note"); // re-added (appended)

        await history.RedoAsync();
        (await provider.GetThreadsAsync(DocId)).Single().Comments.Should().ContainSingle();
    }

    // ── Stack rules ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NewAction_ClearsRedo()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();

        await CreateAsync(provider, history);
        await history.UndoAsync();
        history.CanRedo.Should().BeTrue();

        await CreateAsync(provider, history);
        history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task History_Limit100_DropsOldest()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();

        DocumentCommentThread first = null!;
        for (var i = 0; i < PdfAnnotationHistory.DefaultLimit + 1; i++)
        {
            var thread = await CreateAsync(provider, history, CreateRequest($"note {i}"));
            first ??= thread;
        }

        history.Count.Should().Be(PdfAnnotationHistory.DefaultLimit);

        for (var i = 0; i < PdfAnnotationHistory.DefaultLimit; i++)
        {
            (await history.UndoAsync()).Outcome.Should().Be(PdfAnnotationHistoryOutcome.Applied);
        }

        (await history.UndoAsync()).Outcome.Should().Be(PdfAnnotationHistoryOutcome.NothingToDo);
        // the oldest create was dropped from the stack: its thread survives
        (await provider.GetThreadsAsync(DocId)).Should().ContainSingle(t => t.Id == first.Id);
    }

    [Fact]
    public async Task History_ClearedBy_Clear()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        await CreateAsync(provider, history);
        await history.UndoAsync();

        history.Clear();

        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeFalse();
        history.Count.Should().Be(0);
    }

    // ── Conflicts and provider failures ──────────────────────────────────────

    [Fact]
    public async Task Undo_TwoDeletes_RestoresFullThread_WithoutDuplicates()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        var created = await CreateAsync(provider, history, CreateRequest("note"));
        await history.ExecuteAsync(new PdfAnnotationReplyCommand(
            Context(provider, history), new DocumentCommentReplyRequest { ThreadId = created.Id, Body = "a reply" }, Alice));

        // delete the reply (the thread keeps "note"), then the root comment — gone.
        var withReply = (await provider.GetThreadsAsync(DocId)).Single();
        await history.ExecuteAsync(new PdfAnnotationDeleteCommand(
            Context(provider, history),
            PdfAnnotationSnapshot.CloneThread(withReply),
            new DocumentCommentDeleteRequest { ThreadId = created.Id, CommentId = withReply.Comments[^1].Id }));
        (await provider.GetThreadsAsync(DocId)).Single().Comments.Should().ContainSingle();

        await history.ExecuteAsync(new PdfAnnotationDeleteCommand(
            Context(provider, history),
            PdfAnnotationSnapshot.CloneThread((await provider.GetThreadsAsync(DocId))[0]),
            new DocumentCommentDeleteRequest { ThreadId = created.Id, CommentId = created.Comments[0].Id }));
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();

        // LIFO: the last delete (root comment) re-creates the WHOLE thread; the earlier
        // delete (the reply) must recognize the replay and not duplicate it.
        await history.UndoAsync();
        await history.UndoAsync();

        var restored = (await provider.GetThreadsAsync(DocId)).Single();
        restored.Comments.Should().HaveCount(2);
        restored.Comments[0].Body.Should().Be("note");
        restored.Comments[1].Body.Should().Be("a reply");

        // and the full cycle redoes cleanly
        await history.RedoAsync();
        await history.RedoAsync();
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Undo_ThreadDeletedByOtherUser_DropsCommandAndRaisesHint()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var history = new PdfAnnotationHistory();
        var thread = await CreateAsync(provider, history);

        var conflicts = 0;
        history.ConflictDetected += () => conflicts++;

        // another user removes the thread behind the history's back
        await provider.DeleteAsync(DocId, new DocumentCommentDeleteRequest { ThreadId = thread.Id, CommentId = thread.Comments[0].Id });

        var result = await history.UndoAsync();

        result.Outcome.Should().Be(PdfAnnotationHistoryOutcome.Skipped);
        conflicts.Should().Be(1);
        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task Undo_ProviderThrows_KeepsStackConsistent()
    {
        var inner = new InMemoryPdfAnnotationProvider();
        var provider = new DeleteFailingProvider(inner);
        var history = new PdfAnnotationHistory();
        await CreateAsync(provider, history);

        await Assert.ThrowsAsync<InvalidOperationException>(() => history.UndoAsync());

        history.CanUndo.Should().BeTrue();
        history.Count.Should().Be(1);
        history.CanRedo.Should().BeFalse();
        (await provider.GetThreadsAsync(DocId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Redo_WithoutUndo_ReportsNothingToDo()
    {
        var history = new PdfAnnotationHistory();
        var result = await history.RedoAsync();
        result.Outcome.Should().Be(PdfAnnotationHistoryOutcome.NothingToDo);
        result.ThreadId.Should().BeNull();
    }

    // ── Concurrency (FZE-15 review) ──────────────────────────────────────────

    [Fact]
    public async Task DoubleConcurrentUndo_Serializes_BothStepsApplied()
    {
        var inner = new InMemoryPdfAnnotationProvider();
        var provider = new GatedDeleteProvider(inner);
        var history = new PdfAnnotationHistory();
        await CreateAsync(provider, history, CreateRequest("note 1"));
        await CreateAsync(provider, history, CreateRequest("note 2"));
        (await provider.GetThreadsAsync(DocId)).Should().HaveCount(2);

        // The first undo parks INSIDE the command (the gate is held); the second one is
        // started while the first is still running — it must wait, never interleave.
        var first = history.UndoAsync();
        await provider.DeleteEntered.Task;
        history.IsBusy.Should().BeTrue("an undo is in flight");
        var second = history.UndoAsync();

        provider.Release();
        var results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(r => r.Outcome == PdfAnnotationHistoryOutcome.Applied,
            "the serialized undos each moved the stack");
        history.IsBusy.Should().BeFalse();
        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeTrue();
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty("both creates were undone");
    }

    [Fact]
    public async Task Execute_LandingMidUndo_SerializesAfterTheUndo()
    {
        var inner = new InMemoryPdfAnnotationProvider();
        var provider = new GatedDeleteProvider(inner);
        var history = new PdfAnnotationHistory();
        await CreateAsync(provider, history);

        // The undo parks inside its command; the execute is started MID-UNDO. The undo
        // completes first (its thread disappears), then the create lands.
        var undo = history.UndoAsync();
        await provider.DeleteEntered.Task;
        var execute = history.ExecuteAsync(new PdfAnnotationCreateCommand(
            Context(provider, history), CreateRequest("late note"), Alice));

        provider.Release();
        var results = await Task.WhenAll(undo, execute);

        results[0].Outcome.Should().Be(PdfAnnotationHistoryOutcome.Applied, "the undo completed first");
        results[1].Outcome.Should().Be(PdfAnnotationHistoryOutcome.Applied, "the execute ran after it, never interleaved");
        history.IsBusy.Should().BeFalse();

        // LIFO order restored by the serialization: the late create is on top of the undo
        // stack and the redo tail the undo produced was cleared by the execute.
        history.NextUndoAction.Should().Be(PdfAnnotatorHistoryAction.Create);
        history.CanRedo.Should().BeFalse("a new action clears the redo tail");
        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().ContainSingle(t => t.Comments.Single().Body == "late note");
    }

    [Fact]
    public async Task Undo_DeleteReplayFails_RemovesPartialThread_AndDoesNotRemap()
    {
        var inner = new InMemoryPdfAnnotationProvider();
        var replyFailing = new ReplyFailingProvider(inner);
        var provider = new WholeThreadDeleteProvider(replyFailing);
        var history = new PdfAnnotationHistory();
        var created = await CreateAsync(provider, history, CreateRequest("note"));
        await history.ExecuteAsync(new PdfAnnotationReplyCommand(
            Context(provider, history), new DocumentCommentReplyRequest { ThreadId = created.Id, Body = "a reply" }, Alice));
        var snapshot = PdfAnnotationSnapshot.CloneThread((await provider.GetThreadsAsync(DocId))[0]);

        await history.ExecuteAsync(new PdfAnnotationDeleteCommand(
            Context(provider, history),
            snapshot,
            new DocumentCommentDeleteRequest { ThreadId = created.Id, CommentId = created.Comments[0].Id }));
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();

        // The re-create lands, but the reply replay fails midway: the partial thread is
        // cleaned up, the logical id is NOT remapped and the stack stays unchanged.
        replyFailing.FailReplies = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.UndoAsync());

        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty("the partial restore was removed");
        history.ResolveThreadId(created.Id).Should().Be(created.Id, "the remap records only after the full replay");
        history.CanUndo.Should().BeTrue("the failed step stays on the stack");
        history.CanRedo.Should().BeFalse();
    }

    /// <summary>A provider whose <c>DeleteAsync</c> parks until the test releases it — the
    /// hook the concurrency tests use to hold an undo mid-flight.</summary>
    private sealed class GatedDeleteProvider : IPdfAnnotationProvider
    {
        private readonly InMemoryPdfAnnotationProvider _inner;

        public GatedDeleteProvider(InMemoryPdfAnnotationProvider inner) => _inner = inner;

        /// <summary>Completed when the first gated delete has been entered.</summary>
        public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource? _release;

        /// <summary>Releases the parked delete (and every later one).</summary>
        public void Release()
        {
            _release ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            _release.TrySetResult();
        }

        public async Task DeleteAsync(string documentId, DocumentCommentDeleteRequest request, CancellationToken cancellationToken = default)
        {
            DeleteEntered.TrySetResult();
            var release = _release ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            await release.Task;

            await _inner.DeleteAsync(documentId, request, cancellationToken);
        }

        public Task<IReadOnlyList<DocumentCommentThread>> GetThreadsAsync(string documentId, CancellationToken cancellationToken = default)
            => _inner.GetThreadsAsync(documentId, cancellationToken);

        public Task<DocumentCommentThread> CreateThreadAsync(string documentId, DocumentCommentThreadCreateRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.CreateThreadAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> ReplyAsync(string documentId, DocumentCommentReplyRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.ReplyAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> EditAsync(string documentId, DocumentCommentEditRequest request, CancellationToken cancellationToken = default)
            => _inner.EditAsync(documentId, request, cancellationToken);

        public Task<DocumentCommentThread> ResolveAsync(string documentId, string threadId, DocumentCommentUser? resolvedBy = null, CancellationToken cancellationToken = default)
            => _inner.ResolveAsync(documentId, threadId, resolvedBy, cancellationToken);

        public Task<DocumentCommentThread> ReopenAsync(string documentId, string threadId, DocumentCommentUser? reopenedBy = null, CancellationToken cancellationToken = default)
            => _inner.ReopenAsync(documentId, threadId, reopenedBy, cancellationToken);
    }

    /// <summary>A provider whose reply replay can be made to fail midway — the hook the
    /// partial-restore cleanup test uses.</summary>
    private sealed class ReplyFailingProvider : IPdfAnnotationProvider
    {
        private readonly InMemoryPdfAnnotationProvider _inner;

        public ReplyFailingProvider(InMemoryPdfAnnotationProvider inner) => _inner = inner;

        /// <summary>When true, the next (and every later) reply throws.</summary>
        public bool FailReplies;

        public async Task<DocumentCommentThread> ReplyAsync(string documentId, DocumentCommentReplyRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
        {
            if (FailReplies)
            {
                throw new InvalidOperationException("reply replay failed");
            }

            return await _inner.ReplyAsync(documentId, request, author, cancellationToken);
        }

        public Task<IReadOnlyList<DocumentCommentThread>> GetThreadsAsync(string documentId, CancellationToken cancellationToken = default)
            => _inner.GetThreadsAsync(documentId, cancellationToken);

        public Task<DocumentCommentThread> CreateThreadAsync(string documentId, DocumentCommentThreadCreateRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.CreateThreadAsync(documentId, request, author, cancellationToken);

        public Task DeleteAsync(string documentId, DocumentCommentDeleteRequest request, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(documentId, request, cancellationToken);

        public Task<DocumentCommentThread> EditAsync(string documentId, DocumentCommentEditRequest request, CancellationToken cancellationToken = default)
            => _inner.EditAsync(documentId, request, cancellationToken);

        public Task<DocumentCommentThread> ResolveAsync(string documentId, string threadId, DocumentCommentUser? resolvedBy = null, CancellationToken cancellationToken = default)
            => _inner.ResolveAsync(documentId, threadId, resolvedBy, cancellationToken);

        public Task<DocumentCommentThread> ReopenAsync(string documentId, string threadId, DocumentCommentUser? reopenedBy = null, CancellationToken cancellationToken = default)
            => _inner.ReopenAsync(documentId, threadId, reopenedBy, cancellationToken);
    }

    /// <summary>A provider whose comment delete removes the whole thread (a backend that
    /// treats the request as a thread delete) — exercises the re-create undo path.</summary>
    private sealed class WholeThreadDeleteProvider : IPdfAnnotationProvider
    {
        private readonly IPdfAnnotationProvider _inner;

        public WholeThreadDeleteProvider(IPdfAnnotationProvider inner) => _inner = inner;

        public async Task DeleteAsync(string documentId, DocumentCommentDeleteRequest request, CancellationToken cancellationToken = default)
        {
            var threads = await _inner.GetThreadsAsync(documentId, cancellationToken);
            var thread = threads.FirstOrDefault(t => string.Equals(t.Id, request.ThreadId, StringComparison.Ordinal));
            if (thread is null)
            {
                return;
            }

            foreach (var comment in thread.Comments.ToList())
            {
                await _inner.DeleteAsync(documentId, new DocumentCommentDeleteRequest { ThreadId = request.ThreadId, CommentId = comment.Id }, cancellationToken);
            }
        }

        public Task<IReadOnlyList<DocumentCommentThread>> GetThreadsAsync(string documentId, CancellationToken cancellationToken = default)
            => _inner.GetThreadsAsync(documentId, cancellationToken);

        public Task<DocumentCommentThread> CreateThreadAsync(string documentId, DocumentCommentThreadCreateRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.CreateThreadAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> ReplyAsync(string documentId, DocumentCommentReplyRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.ReplyAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> EditAsync(string documentId, DocumentCommentEditRequest request, CancellationToken cancellationToken = default)
            => _inner.EditAsync(documentId, request, cancellationToken);

        public Task<DocumentCommentThread> ResolveAsync(string documentId, string threadId, DocumentCommentUser? resolvedBy = null, CancellationToken cancellationToken = default)
            => _inner.ResolveAsync(documentId, threadId, resolvedBy, cancellationToken);

        public Task<DocumentCommentThread> ReopenAsync(string documentId, string threadId, DocumentCommentUser? reopenedBy = null, CancellationToken cancellationToken = default)
            => _inner.ReopenAsync(documentId, threadId, reopenedBy, cancellationToken);
    }

    private sealed class DeleteFailingProvider : IPdfAnnotationProvider
    {        private readonly InMemoryPdfAnnotationProvider _inner;

        public DeleteFailingProvider(InMemoryPdfAnnotationProvider inner) => _inner = inner;

        public Task<IReadOnlyList<DocumentCommentThread>> GetThreadsAsync(string documentId, CancellationToken cancellationToken = default)
            => _inner.GetThreadsAsync(documentId, cancellationToken);

        public Task<DocumentCommentThread> CreateThreadAsync(string documentId, DocumentCommentThreadCreateRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.CreateThreadAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> ReplyAsync(string documentId, DocumentCommentReplyRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.ReplyAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> EditAsync(string documentId, DocumentCommentEditRequest request, CancellationToken cancellationToken = default)
            => _inner.EditAsync(documentId, request, cancellationToken);

        public Task DeleteAsync(string documentId, DocumentCommentDeleteRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("delete failed");

        public Task<DocumentCommentThread> ResolveAsync(string documentId, string threadId, DocumentCommentUser? resolvedBy = null, CancellationToken cancellationToken = default)
            => _inner.ResolveAsync(documentId, threadId, resolvedBy, cancellationToken);

        public Task<DocumentCommentThread> ReopenAsync(string documentId, string threadId, DocumentCommentUser? reopenedBy = null, CancellationToken cancellationToken = default)
            => _inner.ReopenAsync(documentId, threadId, reopenedBy, cancellationToken);
    }
}
