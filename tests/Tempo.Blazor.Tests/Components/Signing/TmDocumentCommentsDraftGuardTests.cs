using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Draft-guard surfaces of the Signing document comments stack: the thread panel's reply/edit
/// composers and the page viewer's new-thread draft panel (aggregated with the embedded panel).
/// None of those composer texts is observable through a parameter — these tests pin the additive
/// OnDraftChanged / HasUnsavedDraft / DiscardDraftAsync surfaces.
/// </summary>
public class TmDocumentCommentsDraftGuardTests : LocalizationTestBase
{
    // ── Thread panel ─────────────────────────────────────────────────────

    [Fact]
    public void ThreadPanel_SelectingThread_MountsReplyComposer_AndReportsOpenEditor()
    {
        var states = new List<TmCommentDraftState>();
        Render<TmDocumentCommentThreadPanel>(p => p
            .Add(c => c.Threads, SampleThreads())
            .Add(c => c.SelectedThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        states.Should().ContainSingle().Which.Should().Be(
            new TmCommentDraftState(IsEditorOpen: true, EditingCommentId: null, HasUnsavedContent: false));
    }

    [Fact]
    public void ThreadPanel_ReplyTyping_FiresUnsavedContent_AndDiscardClearsIt()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentThreadPanel>(p => p
            .Add(c => c.Threads, SampleThreads())
            .Add(c => c.SelectedThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find(".tm-comment-composer__input").Input("Reply draft");
        states[^1].Should().Be(new TmCommentDraftState(true, null, true));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync()).Wait();

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        (cut.Find(".tm-comment-composer__input").GetAttribute("value") ?? string.Empty).Should().BeEmpty();
        states.Last().Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public void ThreadPanel_EditOpenedWithoutChanges_IsNotUnsaved_TypingReportsOne()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentThreadPanel>(p => p
            .Add(c => c.Threads, SampleThreads())
            .Add(c => c.SelectedThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u2")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        FindTextAction(cut, "Edit").Click();

        states[^1].Should().Be(new TmCommentDraftState(true, "comment-1", false),
            "opening an edit without changing anything is not an unsaved draft");

        cut.Find(".tm-comment-composer__input").Input("Edited body");
        states[^1].Should().Be(new TmCommentDraftState(true, "comment-1", true));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public void ThreadPanel_SelectingAnotherThread_ClearsTheReplyDraft()
    {
        var cut = Render<TmDocumentCommentThreadPanel>(p => p
            .Add(c => c.Threads, SampleThreads())
            .Add(c => c.SelectedThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u1"));

        cut.Find(".tm-comment-composer__input").Input("Reply for thread 1");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        cut.Render(p => p.Add(c => c.SelectedThreadId, "thread-2"));

        (cut.Find(".tm-comment-composer__input").GetAttribute("value") ?? string.Empty)
            .Should().BeEmpty("a fresh thread must not inherit the previous thread's reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public void ThreadPanel_ClearingSelection_RestartsWithAnEmptyReplyComposer()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentThreadPanel>(p => p
            .Add(c => c.Threads, SampleThreads())
            .Add(c => c.SelectedThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find(".tm-comment-composer__input").Input("Reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // The panel's own clear button (X / back) — the reply composer unmounts with it.
        cut.Find(".tm-document-comments-panel__icon-button").Click();

        // The host accepts the cleared selection.
        cut.Render(p => p.Add(c => c.SelectedThreadId, null));

        states.Last().Should().Be(new TmCommentDraftState(false, null, false),
            "clearing the selection closes every editor and drops the reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        cut.Render(p => p.Add(c => c.SelectedThreadId, "thread-1"));

        (cut.Find(".tm-comment-composer__input").GetAttribute("value") ?? string.Empty)
            .Should().BeEmpty("reselecting the thread starts with an empty reply composer");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public void ThreadPanel_StartingAnEdit_DropsTheReplyDraft()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentThreadPanel>(p => p
            .Add(c => c.Threads, SampleThreads())
            .Add(c => c.SelectedThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u2")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find(".tm-comment-composer__input").Input("Reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        FindTextAction(cut, "Edit").Click();

        states.Last().Should().Be(new TmCommentDraftState(true, "comment-1", false),
            "the reply composer unmounted with the edit — its draft must not be counted");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        // Cancelling the edit remounts the reply composer — empty, not with the old draft.
        cut.Find(".tm-comment-composer__button--secondary").Click();

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        (cut.Find(".tm-comment-composer__input").GetAttribute("value") ?? string.Empty).Should().BeEmpty();
    }

    // ── Page viewer (own draft panel + embedded panel aggregation) ──────

    [Fact]
    public void Viewer_DraftPanelTyping_FiresUnsavedContent_AndSubmitClearsIt()
    {
        var states = new List<TmCommentDraftState>();
        DocumentCommentThreadCreateRequest? request = null;
        var cut = Render<TmDocumentPageViewer>(p => p
            .Add(c => c.Page, CreatePage(width: 800, height: 1000))
            .Add(c => c.CommentsEnabled, true)
            .Add(c => c.CommentMode, DocumentCommentMode.Comment)
            .Add(c => c.OnCommentThreadCreateRequested, r => request = r)
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        StartPointDraft(cut);
        cut.Find(".tm-comment-composer__input").Input("Please check this clause");

        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        cut.Find(".tm-comment-composer__button--primary").Click();
        request.Should().NotBeNull();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        states.Should().Equal(
            new TmCommentDraftState(IsEditorOpen: true, EditingCommentId: null, HasUnsavedContent: false),
            new TmCommentDraftState(true, null, true),
            new TmCommentDraftState(false, null, false));
    }

    [Fact]
    public void Viewer_AggregatesEmbeddedThreadPanelDrafts()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentPageViewer>(p => p
            .Add(c => c.Page, CreatePage())
            .Add(c => c.CommentsEnabled, true)
            .Add(c => c.CommentThreads, SampleThreads())
            .Add(c => c.SelectedCommentThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u2")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        // The embedded panel mounts its reply composer with the selection — the very first
        // notification must already report the open editor, before anything is typed.
        states[0].Should().Be(new TmCommentDraftState(true, null, false));

        cut.Find(".tm-comment-composer__input").Input("Reply draft");

        states.Should().Contain(s => s.HasUnsavedContent,
            "a draft typed into the embedded panel's reply composer must surface on the viewer");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // Opening an edit inside the embedded panel surfaces its comment id on the viewer.
        FindTextAction(cut, "Edit").Click();
        states.Last().EditingCommentId.Should().Be("comment-1");

        cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync()).Wait();
        cut.Instance.HasUnsavedDraft.Should().BeFalse("the viewer's discard reaches the embedded panel");
        states.Last().Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public void Viewer_HostPanelTemplate_TakesOver_AndClosesTheEmbeddedPanelState()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentPageViewer>(p => p
            .Add(c => c.Page, CreatePage())
            .Add(c => c.CommentsEnabled, true)
            .Add(c => c.CommentThreads, SampleThreads())
            .Add(c => c.SelectedCommentThreadId, "thread-1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        states[0].Should().Be(new TmCommentDraftState(true, null, false));

        // The host swaps in its own panel template — the embedded panel unmounts and the viewer
        // must stop reporting its (stale) editor-open state.
        cut.Render(p => p.Add(
            c => c.CommentThreadPanelTemplate,
            threads => builder => builder.AddContent(0, "host-owned panel")));

        states.Last().Should().Be(new TmCommentDraftState(false, null, false),
            "the unmounted panel's state must not leak into the viewer's draft surface");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        StartPointDraft(cut);

        cut.Instance.HasUnsavedDraft.Should().BeFalse(
            "with a host-owned panel the viewer owns no comment draft surface, draft anchor included");
    }

    [Fact]
    public void Viewer_TemplateTakeOverDuringDraft_DoesNotResurrectTheDraftWhenTheTemplateIsCleared()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentPageViewer>(p => p
            .Add(c => c.Page, CreatePage())
            .Add(c => c.CommentsEnabled, true)
            .Add(c => c.CommentThreads, SampleThreads())
            .Add(c => c.SelectedCommentThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find(".tm-comment-composer__input").Input("Reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // The host swaps in its own panel template mid-draft — the embedded panel unmounts and
        // the viewer must DROP its stale panel state, not just gate it.
        cut.Render(p => p.Add(
            c => c.CommentThreadPanelTemplate,
            threads => builder => builder.AddContent(0, "host-owned panel")));
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        // The host clears the template AND the selection in one go: a selection-less panel fires
        // nothing on mount, so nothing can mask a stale panel state at the clear.
        cut.Render(p => p
            .Add(c => c.CommentThreadPanelTemplate, (RenderFragment<IReadOnlyList<DocumentCommentThread>>?)null)
            .Add(c => c.SelectedCommentThreadId, (string?)null));

        cut.Instance.HasUnsavedDraft.Should().BeFalse("the pre-takeover draft must be dropped at the takeover, not resurrected on clear");

        // Reselecting the thread mounts a fresh, empty reply composer.
        cut.Render(p => p.Add(c => c.SelectedCommentThreadId, "thread-1"));

        cut.FindAll(".tm-comment-composer__input").Should().ContainSingle(
            "the embedded panel remounts once the template is cleared");
        (cut.Find(".tm-comment-composer__input").GetAttribute("value") ?? string.Empty)
            .Should().BeEmpty("the remounted panel starts with an empty reply composer");
        cut.Instance.HasUnsavedDraft.Should().BeFalse("the pre-takeover draft must not come back with the remounted panel");
        states.Last().HasUnsavedContent.Should().BeFalse();
    }

    [Fact]
    public void Viewer_LoadingDuringDraft_DoesNotResurrectTheDraftAfterLoadingCompletes()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentPageViewer>(p => p
            .Add(c => c.Page, CreatePage())
            .Add(c => c.CommentsEnabled, true)
            .Add(c => c.CommentThreads, SampleThreads())
            .Add(c => c.SelectedCommentThreadId, "thread-1")
            .Add(c => c.CurrentUserId, "u1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find(".tm-comment-composer__input").Input("Reply draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // The host flips IsLoading mid-draft — the comments shell (and the embedded panel with
        // it) unmount; the stale panel state must be dropped, not resurrected on reload.
        cut.Render(p => p.Add(c => c.IsLoading, true));

        cut.Instance.HasUnsavedDraft.Should().BeFalse("the panel is unmounted while the page loads");

        cut.Render(p => p.Add(c => c.IsLoading, false));

        cut.FindAll(".tm-comment-composer__input").Should().ContainSingle("the embedded panel remounts after loading");
        cut.Instance.HasUnsavedDraft.Should().BeFalse("loading completion remounts a FRESH panel — the stale draft must not come back");
        states.Last().HasUnsavedContent.Should().BeFalse();
    }

    [Fact]
    public void Viewer_DiscardWithOpenThreadAndNoDraft_KeepsTheComposerOpenState()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentPageViewer>(p => p
            .Add(c => c.Page, CreatePage())
            .Add(c => c.CommentsEnabled, true)
            .Add(c => c.CommentThreads, SampleThreads())
            .Add(c => c.SelectedCommentThreadId, "thread-1")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        states[0].Should().Be(new TmCommentDraftState(true, null, false),
            "the selected thread mounts the reply composer — an open editor with no content yet");

        // Nothing was typed: the discard clears nothing, and it must NOT report a closed editor
        // while the reply composer stays mounted.
        cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync()).Wait();

        states.Last().IsEditorOpen.Should().BeTrue("the reply composer stays mounted after the discard");
        states.Last().HasUnsavedContent.Should().BeFalse();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    private static void StartPointDraft(IRenderedComponent<TmDocumentPageViewer> cut)
    {
        cut.Find(".tm-document-page-viewer__page")
            .MouseDown(new MouseEventArgs { OffsetX = 200, OffsetY = 500, Buttons = 1 });
        cut.Find(".tm-document-page-viewer__page")
            .MouseUp(new MouseEventArgs { OffsetX = 200, OffsetY = 500, Buttons = 0 });
    }

    private static IElement FindTextAction(IRenderedComponent<TmDocumentCommentThreadPanel> cut, string text)
        => cut.FindAll(".tm-document-comments-panel__text-action")
              .First(e => e.TextContent.Contains(text));

    private static IElement FindTextAction(IRenderedComponent<TmDocumentPageViewer> cut, string text)
        => cut.FindAll(".tm-document-comments-panel__text-action")
              .First(e => e.TextContent.Contains(text));

    private static SigningDocumentPage CreatePage(double width = 800, double height = 1000) => new()
    {
        AttachmentUuid = "attachment-1",
        PageIndex = 0,
        ImageUrl = "/samples/page-1.png",
        Width = width,
        Height = height,
        Label = "Page 1"
    };

    private static IReadOnlyList<DocumentCommentThread> SampleThreads() =>
    [
        new DocumentCommentThread
        {
            Id = "thread-1",
            Anchor = DocumentCommentAnchor.Point(1, 0.25, 0.4),
            Status = DocumentCommentThreadStatus.Open,
            Comments =
            [
                new DocumentComment
                {
                    Id = "comment-1",
                    AuthorId = "u2",
                    AuthorName = "Bob Stone",
                    Body = "Needs review",
                    CreatedAt = new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.Zero),
                    CanEdit = true,
                    CanDelete = true
                }
            ]
        },
        new DocumentCommentThread
        {
            Id = "thread-2",
            Anchor = DocumentCommentAnchor.Point(2, 0.5, 0.6),
            Status = DocumentCommentThreadStatus.Open,
            Comments =
            [
                new DocumentComment
                {
                    Id = "comment-2",
                    AuthorId = "u1",
                    AuthorName = "Alice Ray",
                    Body = "Second thread",
                    CreatedAt = new DateTimeOffset(2026, 5, 9, 11, 0, 0, TimeSpan.Zero),
                    CanEdit = false,
                    CanDelete = false
                }
            ]
        }
    ];
}
