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
/// Draft-guard surface of <see cref="TmDocumentCommentRail"/> for its own new-comment composer.
/// The composer holds its text in a private field, so the rail host cannot observe it — these
/// tests pin the additive surface (OnDraftChanged / HasUnsavedDraft / DiscardDraftAsync).
/// </summary>
public class TmDocumentCommentRailDraftGuardTests : LocalizationTestBase
{
    [Fact]
    public void Rail_ComposerMounted_FiresOpenEditorState()
    {
        var states = new List<TmCommentDraftState>();
        Render<TmDocumentCommentRail>(p => p
            .Add(c => c.ComposerOpen, true)
            .Add(c => c.DraftAnchor, new DocumentCommentAnchor { BlockId = "block-1" })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        states.Should().ContainSingle().Which.Should().Be(
            new TmCommentDraftState(IsEditorOpen: true, EditingCommentId: null, HasUnsavedContent: false),
            "the mounted new-comment composer is an open editor with no content yet");
        // The rail always creates a NEW comment — the editing id stays null.
        states[0].EditingCommentId.Should().BeNull();
    }

    [Fact]
    public void Rail_Typing_FiresUnsavedContent_AndDiscardClearsIt()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentRail>(p => p
            .Add(c => c.ComposerOpen, true)
            .Add(c => c.DraftAnchor, new DocumentCommentAnchor { BlockId = "block-1" })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-input']").Input("New comment draft");
        states[^1].Should().Be(new TmCommentDraftState(true, null, true));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync()).Wait();

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        (cut.Find("[data-testid='document-comment-input']").GetAttribute("value") ?? string.Empty)
            .Should().BeEmpty();
        states.Last().Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public void Rail_ComposerClosed_ReportsClosedState()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentRail>(p => p
            .Add(c => c.ComposerOpen, true)
            .Add(c => c.DraftAnchor, new DocumentCommentAnchor { BlockId = "block-1" })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-input']").Input("draft");

        // Host closes the composer — the parameter-driven unmount must surface.
        cut.Render(p => p.Add(c => c.ComposerOpen, false));

        states.Last().Should().Be(new TmCommentDraftState(false, null, false));
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    // ── Thread aggregation ───────────────────────────────────────────

    [Fact]
    public void Rail_ThreadReplyTyping_SurfacesOnTheRail()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentRail>(p => p
            .Add(c => c.Comments, new[] { OpenComment() })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Thread reply draft");

        cut.Instance.HasUnsavedDraft.Should().BeTrue("the rail aggregates the drafts typed inside its threads");
        states.Last().Should().Be(new TmCommentDraftState(true, null, true));
    }

    [Fact]
    public async Task Rail_DiscardDraftAsync_ClearsThreadDrafts_Too()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentRail>(p => p
            .Add(c => c.Comments, new[] { OpenComment() })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Thread reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        cut.Instance.HasUnsavedDraft.Should().BeFalse("the rail's discard reaches every rendered thread");
        (cut.Find("[data-testid='document-comment-reply-input']").GetAttribute("value") ?? string.Empty)
            .Should().BeEmpty();
        states.Last().Should().Be(new TmCommentDraftState(true, null, false),
            "the thread's reply composer stays mounted, but its draft is gone");
    }

    [Fact]
    public void Rail_RemovingTheThread_PrunesItsDraftState()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentRail>(p => p
            .Add(c => c.Comments, new[] { OpenComment() })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Thread reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // The host removes the thread (deleted elsewhere) — its draft state must be pruned.
        cut.Render(p => p.Add(c => c.Comments, Array.Empty<DocumentComment>()));

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmCommentDraftState(false, null, false));
    }

    [Fact]
    public void Rail_RemovingTheDraftThread_PrunesItsState_AndLeavesTheSurvivingComposerEmpty()
    {
        var cut = Render<TmDocumentCommentRail>(p => p
            .Add(c => c.Comments, new[] { OpenComment(), OpenComment("comment-2") })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, _ => { })));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Thread A draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // The host removes thread A (deleted elsewhere). Its draft must be pruned AND must not
        // leak into the surviving thread's composer through position-matched component reuse.
        cut.Render(p => p.Add(c => c.Comments, new[] { OpenComment("comment-2") }));

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        (cut.Find("[data-testid='document-comment-reply-input']").GetAttribute("value") ?? string.Empty)
            .Should().BeEmpty("the surviving thread's reply composer must start empty");
    }

    [Fact]
    public void Rail_LoadingDuringThreadDraft_ClearsTheAggregatedThreadState()
    {
        var cut = Render<TmDocumentCommentRail>(p => p
            .Add(c => c.Comments, new[] { OpenComment() })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, _ => { })));

        cut.Find("[data-testid='document-comment-reply-input']").Input("Thread reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // The host reloads the comments — every thread unmounts while loading; the stale draft
        // state must not survive into the reloaded rail.
        cut.Render(p => p.Add(c => c.IsLoading, true));

        cut.FindAll("[data-testid='document-comment-reply-input']").Should().BeEmpty("loading unmounts the thread list");
        cut.Instance.HasUnsavedDraft.Should().BeFalse("the stale thread draft must not survive the reload");
    }

    private static DocumentComment OpenComment(string id = "comment-1") => new()
    {
        Id = id,
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
}
