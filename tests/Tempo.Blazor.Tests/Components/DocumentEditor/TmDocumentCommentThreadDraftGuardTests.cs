using Bunit;
using Microsoft.AspNetCore.Components;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Draft-guard surface of <see cref="TmDocumentCommentThread"/>. The reply and edit composers hold
/// their text in private fields, so the thread host cannot observe it — these tests pin the
/// aggregate surface (OnDraftChanged / HasUnsavedDraft / DiscardDraftAsync).
/// </summary>
public class TmDocumentCommentThreadDraftGuardTests : LocalizationTestBase
{
    [Fact]
    public void Thread_MountingReplyComposer_FiresOpenEditorState()
    {
        var states = new List<TmCommentDraftState>();
        Render<TmDocumentCommentThread>(p => p
            .Add(c => c.Comment, OpenComment())
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        states.Should().ContainSingle().Which.Should().Be(
            new TmCommentDraftState(IsEditorOpen: true, EditingCommentId: null, HasUnsavedContent: false),
            "the mounted reply composer is an open editor with no content yet");
    }

    [Fact]
    public void Thread_ReplyTyping_FiresUnsavedContent_AndSubmitClearsIt()
    {
        var states = new List<TmCommentDraftState>();
        DocumentEditorCommentReplyRequest? reply = null;
        var cut = Render<TmDocumentCommentThread>(p => p
            .Add(c => c.Comment, OpenComment())
            .Add(c => c.OnReply, r => reply = r)
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Reply text");
        states[^1].Should().Be(new TmCommentDraftState(true, null, true));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        cut.Find("[data-testid='document-comment-reply-submit']").Click();
        reply.Should().NotBeNull();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public void Thread_EditOpenedWithoutChanges_IsNotUnsaved_TypingReportsOne()
    {
        var states = new List<TmCommentDraftState>();
        var comment = OwnOpenComment();
        var cut = Render<TmDocumentCommentThread>(p => p
            .Add(c => c.Comment, comment)
            .Add(c => c.CurrentAuthorId, "author-1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-edit']").Click();

        states[^1].Should().Be(new TmCommentDraftState(true, comment.Entries[0].Id, false),
            "opening an edit without changing anything is not an unsaved draft");

        cut.Find("[data-testid='document-comment-edit-input']").Input("Changed");
        states[^1].Should().Be(new TmCommentDraftState(true, comment.Entries[0].Id, true));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public async Task Thread_DiscardDraftAsync_ClearsEverySlot()
    {
        var states = new List<TmCommentDraftState>();
        var comment = OwnOpenComment();
        var cut = Render<TmDocumentCommentThread>(p => p
            .Add(c => c.Comment, comment)
            .Add(c => c.CurrentAuthorId, "author-1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-reply-input']").Input("reply draft");
        cut.Find("[data-testid='document-comment-edit']").Click();
        cut.Find("[data-testid='document-comment-edit-input']").Input("edit draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        cut.FindAll("[data-testid='document-comment-edit-input']").Should().BeEmpty("the entry edit is cancelled");
        (cut.Find("[data-testid='document-comment-reply-input']").GetAttribute("value") ?? string.Empty)
            .Should().BeEmpty("the reply draft is cleared");
        states.Last().Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public void Thread_ParameterDrivenComposerUnmount_ReportsClosedEmptyState()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentThread>(p => p
            .Add(c => c.Comment, OpenComment())
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Reply text");
        states[^1].Should().Be(new TmCommentDraftState(true, null, true));

        // The host flips CanComment — the reply composer unmounts WITHOUT any event from it
        // (unmounted components do not notify). The stale @ref must not leak into the state.
        cut.Render(p => p.Add(c => c.CanComment, false));

        states.Last().Should().Be(new TmCommentDraftState(false, null, false),
            "the parameter-driven unmount must surface as a closed, empty state");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public void Thread_ResolvingThread_ReportsClosedEmptyState_EvenWithTypedReply()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentThread>(p => p
            .Add(c => c.Comment, OpenComment())
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Reply text");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        cut.Render(p => p.Add(c => c.Comment, ResolvedComment()));

        states.Last().Should().Be(new TmCommentDraftState(false, null, false),
            "a resolved thread collapses and unmounts its reply composer");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public void Thread_CollapseWithAStaleEditSlot_ReportsClosedEmptyState()
    {
        var states = new List<TmCommentDraftState>();
        var comment = OwnOpenComment();
        var cut = Render<TmDocumentCommentThread>(p => p
            .Add(c => c.Comment, comment)
            .Add(c => c.CurrentAuthorId, "author-1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-edit']").Click();
        states[^1].Should().Be(new TmCommentDraftState(true, comment.Entries[0].Id, false));

        // The host resolves the thread — it collapses (CollapseResolved) and the edit composer
        // unmounts with the collapsed view, but the internal edit slot stays set. The state must
        // report the post-transition closed, empty state.
        cut.Render(p => p.Add(c => c.Comment, ResolvedComment()));

        states.Last().Should().Be(new TmCommentDraftState(false, null, false),
            "a collapsed thread shows no editor — the stale edit slot must not surface");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    private static DocumentComment OpenComment() => new()
    {
        Id = "comment-1",
        Status = DocumentCommentStatus.Open,
        Anchor = new DocumentCommentAnchor { BlockId = "block-1" },
        Entries =
        [
            new DocumentCommentEntry
            {
                Author = new DocumentEditorAuthor { Id = "author-2", DisplayName = "Other" },
                Text = "Entry text",
                CreatedAt = DateTimeOffset.Parse("2026-05-17T08:00:00Z")
            }
        ]
    };

    private static DocumentComment ResolvedComment() => new()
    {
        Id = "comment-1",
        Status = DocumentCommentStatus.Resolved,
        Anchor = new DocumentCommentAnchor { BlockId = "block-1" },
        Entries =
        [
            new DocumentCommentEntry
            {
                Author = new DocumentEditorAuthor { Id = "author-2", DisplayName = "Other" },
                Text = "Entry text",
                CreatedAt = DateTimeOffset.Parse("2026-05-17T08:00:00Z")
            }
        ]
    };

    /// <summary>An open thread whose only entry belongs to "author-1" so the edit button renders.</summary>
    private static DocumentComment OwnOpenComment() => new()
    {
        Id = "comment-1",
        Status = DocumentCommentStatus.Open,
        Anchor = new DocumentCommentAnchor { BlockId = "block-1" },
        Entries =
        [
            new DocumentCommentEntry
            {
                Author = new DocumentEditorAuthor { Id = "author-1", DisplayName = "Me" },
                Text = "My entry text",
                CreatedAt = DateTimeOffset.Parse("2026-05-17T08:00:00Z")
            }
        ]
    };
}
