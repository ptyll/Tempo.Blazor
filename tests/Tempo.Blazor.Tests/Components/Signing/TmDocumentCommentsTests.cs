using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmDocumentCommentsTests : LocalizationTestBase
{
    [Fact]
    public void Viewer_CommentsDisabled_DoesNotRenderCommentPanel()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.ShowToolbar, true));

        cut.FindAll(".tm-document-comments-panel").Should().BeEmpty();
        cut.FindAll(".tm-document-page-viewer__comment-toggle").Should().BeEmpty();
    }

    [Fact]
    public void Viewer_CommentsEnabled_RendersToolbarPanelAndMarkers()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentThreads, SampleThreads()));

        cut.Find(".tm-document-page-viewer__comment-toggle").TextContent.Should().Contain("Comments");
        cut.Find(".tm-document-page-viewer__comment-count").TextContent.Should().Contain("1");
        cut.Find(".tm-document-comments-panel").TextContent.Should().Contain("Needs review");
        cut.Find(".tm-document-comments-layer__point").GetAttribute("style").Should().Contain("left: 25%");
        cut.FindAll(".tm-document-comments-layer__area").Should().BeEmpty("resolved threads are hidden by default");
    }

    [Fact]
    public void Viewer_Mobile_CommentsPanelInSheet_WithStickyComposer()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
                      .Add(p => p.CommentThreads, SampleThreads()));

        cut.Find(".tm-document-page-viewer__comments-sheet").Should().NotBeNull();
        cut.Find(".tm-drawer--bottom").Should().NotBeNull();
        cut.Find(".tm-document-comments-panel").Should().NotBeNull();
    }

    [Fact]
    public void Comments_Filters_RoleGroup_AriaPressed()
    {
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, SampleThreads()));

        var filters = cut.Find(".tm-document-comments-panel__filters");
        filters.GetAttribute("role").Should().Be("group");
        filters.GetAttribute("aria-labelledby").Should().NotBeNullOrWhiteSpace();
        cut.Find(".tm-document-comments-panel__filter-label").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.FindAll(".tm-document-comments-panel__filter").Should().OnlyContain(button =>
            button.GetAttribute("aria-pressed") == "true" || button.GetAttribute("aria-pressed") == "false");
        cut.FindAll("[role='tablist']").Should().BeEmpty();
    }

    [Fact]
    public void Comments_FilterLabelVisible()
    {
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, SampleThreads()));

        cut.Find(".tm-document-comments-panel__filter-label").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Comments_MentionHighlightedInBody_AndEncoded()
    {
        var threads = SampleThreads().ToList();
        threads[0].Comments[0].Body = "Hi @Alice Johnson <script>alert(1)</script>";
        threads[0].Comments[0].Mentions = [new DocumentCommentMention { UserId = "u1", DisplayName = "Alice Johnson" }];
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, threads)
                      .Add(p => p.SelectedThreadId, threads[0].Id));

        var markup = cut.Markup;
        markup.Should().Contain("tm-document-comments-panel__mention");
        markup.Should().Contain("&lt;script&gt;");
        markup.Should().NotContain("<script>alert");
    }

    [Fact]
    public void Comments_ItemActions_InMoreMenu_OnlyWithPermission()
    {
        var threads = SampleThreads().ToList();
        threads[0].Comments[0].CanEdit = false;
        threads[0].Comments[0].CanDelete = false;
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, threads)
                      .Add(p => p.SelectedThreadId, threads[0].Id)
                      .Add(p => p.CurrentUserId, "someone-else"));

        cut.FindAll(".tm-document-comments-panel__text-action").Should().BeEmpty();
        cut.FindAll(".tm-document-comments-panel__more").Should().BeEmpty("no permission means no more menu");
    }

    [Fact]
    public void Comments_RelativeTime_TodayYesterday()
    {
        var now = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
        Services.AddSingleton<TimeProvider>(new FixedTime(now));
        var threads = SampleThreads().ToList();
        threads[0].Comments[0].CreatedAt = now.AddHours(-2);
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, threads)
                      .Add(p => p.SelectedThreadId, threads[0].Id));

        cut.Find("time").TextContent.Should().NotContain("5/9/2026");
        cut.Find("time").GetAttribute("datetime").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Comments_NewCommentButton_EnablesCommentMode()
    {
        DocumentCommentMode? mode = null;
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentThreads, SampleThreads())
                      .Add(p => p.CommentModeChanged, (DocumentCommentMode value) => mode = value));

        cut.Find(".tm-document-comments-panel__new").Click();
        mode.Should().Be(DocumentCommentMode.Comment);
    }

    [Fact]
    public void Comments_OnClose_Callback()
    {
        var closed = false;
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, SampleThreads())
                      .Add(p => p.OnClose, () => closed = true));

        cut.Find(".tm-document-comments-panel__close").Click();
        closed.Should().BeTrue();
    }

    [Fact]
    public void Comments_AvatarColor_DeterministicFromAuthor_TokenPalette()
    {
        var threads = SampleThreads();
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, threads)
                      .Add(p => p.SelectedThreadId, threads[0].Id));

        var avatar = cut.Find(".tm-document-comments-panel__avatar");
        avatar.GetAttribute("data-avatar-token").Should().BeOneOf("primary", "info", "success", "warning", "danger");
        avatar.GetAttribute("style").Should().BeNull();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Viewer_CommentsEnabledWithoutMainToolbar_RendersFallbackCommentToolbar()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.ShowToolbar, false)
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentThreads, SampleThreads()));

        cut.Find(".tm-document-page-viewer__comments-toolbar").TextContent.Should().Contain("Comments");
        cut.Find(".tm-document-page-viewer__comment-toggle").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Viewer_ShowResolvedComments_RendersResolvedAreaMarker()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.ShowResolvedComments, true)
                      .Add(p => p.CommentThreads, SampleThreads()));

        cut.Find(".tm-document-comments-layer__area").GetAttribute("style").Should().Contain("width: 20%");
    }

    [Fact]
    public void ThreadPanel_PageAnchor_RendersAsWholePageThreadWithoutMarker()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentThreads, PageThread()));

        cut.Find(".tm-document-comments-panel").TextContent.Should().Contain("Whole page 1");
        cut.FindAll(".tm-document-comments-layer__point").Should().BeEmpty();
        cut.FindAll(".tm-document-comments-layer__area").Should().BeEmpty();
    }

    [Fact]
    public void Viewer_CommentMarkerTemplate_ReplacesDefaultMarkerContent()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentThreads, SampleThreads())
                      .Add(p => p.CommentMarkerTemplate, thread => builder =>
                      {
                          builder.OpenElement(0, "span");
                          builder.AddAttribute(1, "class", "custom-comment-marker");
                          builder.AddContent(2, thread.Id);
                          builder.CloseElement();
                      }));

        cut.Find(".custom-comment-marker").TextContent.Should().Be("thread-1");
    }

    [Fact]
    public void Viewer_ToggleCommentMode_InvokesChangedAndSetsPressedState()
    {
        DocumentCommentMode? mode = null;
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentModeChanged, value => mode = value));

        cut.Find(".tm-document-page-viewer__comment-toggle").Click();

        mode.Should().Be(DocumentCommentMode.Comment);
        cut.Find(".tm-document-page-viewer__comment-toggle").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void Viewer_ClickInCommentMode_CreatesDraftAtNormalizedPoint()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage(width: 800, height: 1000))
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentMode, DocumentCommentMode.Comment));

        cut.Find(".tm-document-page-viewer__page").PointerDown(new PointerEventArgs { OffsetX = 400, OffsetY = 250, Buttons = 1 });
        cut.Find(".tm-document-page-viewer__page").PointerUp(new PointerEventArgs { OffsetX = 400, OffsetY = 250, Buttons = 0 });

        cut.Find("[data-testid='document-comment-draft']").TextContent.Should().Contain("New comment");
        cut.Find(".tm-document-comments-layer__draft-point").GetAttribute("style").Should().Contain("left: 50%");
    }

    [Fact]
    public void Viewer_DragInCommentMode_CreatesAreaDraft()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage(width: 800, height: 1000))
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentMode, DocumentCommentMode.Comment));

        var page = cut.Find(".tm-document-page-viewer__page");
        page.PointerDown(new PointerEventArgs { OffsetX = 80, OffsetY = 100, Buttons = 1 });
        page.PointerMove(new PointerEventArgs { OffsetX = 240, OffsetY = 300, Buttons = 1 });
        page.PointerUp(new PointerEventArgs { OffsetX = 240, OffsetY = 300, Buttons = 0 });

        var draft = cut.Find(".tm-document-comments-layer__draft");
        draft.GetAttribute("style").Should().Contain("left: 10%");
        draft.GetAttribute("style").Should().Contain("width: 20%");
    }

    [Fact]
    public void Viewer_SubmitDraft_InvokesCreateRequest()
    {
        DocumentCommentThreadCreateRequest? request = null;
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage(width: 800, height: 1000))
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentMode, DocumentCommentMode.Comment)
                      .Add(p => p.OnCommentThreadCreateRequested, value => request = value));

        cut.Find(".tm-document-page-viewer__page").PointerDown(new PointerEventArgs { OffsetX = 200, OffsetY = 500, Buttons = 1 });
        cut.Find(".tm-document-page-viewer__page").PointerUp(new PointerEventArgs { OffsetX = 200, OffsetY = 500, Buttons = 0 });
        cut.Find(".tm-comment-composer textarea").Input("Please check this clause");
        cut.Find(".tm-comment-composer__button--primary").Click();

        request.Should().NotBeNull();
        request!.Body.Should().Be("Please check this clause");
        request.Anchor.X.Should().BeApproximately(0.25, 0.001);
        request.Anchor.Y.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void Viewer_PageClick_ClearsSelectedCommentThread()
    {
        string? selected = "thread-1";
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.SelectedCommentThreadId, selected)
                      .Add(p => p.SelectedCommentThreadIdChanged, value => selected = value)
                      .Add(p => p.CommentThreads, SampleThreads()));

        cut.Find(".tm-document-page-viewer__page").Click();

        selected.Should().BeNull();
    }

    [Fact]
    public void Viewer_DisabledComments_BlockDraftCreationAndComposerSubmit()
    {
        DocumentCommentThreadCreateRequest? request = null;
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.Disabled, true)
                      .Add(p => p.CommentMode, DocumentCommentMode.Comment)
                      .Add(p => p.OnCommentThreadCreateRequested, value => request = value));

        cut.Find(".tm-document-page-viewer__page").PointerDown(new PointerEventArgs { OffsetX = 200, OffsetY = 500, Buttons = 1 });
        cut.FindAll("[data-testid='document-comment-draft']").Should().BeEmpty();
        request.Should().BeNull();
    }

    [Fact]
    public void ThreadPanel_SelectResolveReplyAndReaction_EmitCallbacks()
    {
        string? selected = null;
        DocumentCommentThreadStatusRequest? resolved = null;
        DocumentCommentReplyRequest? reply = null;
        DocumentCommentReactionToggleRequest? reaction = null;

        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, SampleThreads())
                      .Add(p => p.SelectedThreadIdChanged, value => selected = value)
                      .Add(p => p.OnResolveRequested, value => resolved = value)
                      .Add(p => p.OnReplyRequested, value => reply = value)
                      .Add(p => p.OnReactionToggled, value => reaction = value));

        cut.Find("[data-thread-id='thread-1']").Click();
        selected.Should().Be("thread-1");

        cut.Render(parameters => parameters.Add(p => p.SelectedThreadId, "thread-1"));
        cut.Find(".tm-comment-composer textarea").Input("Reply body");
        cut.Find(".tm-comment-composer__button--primary").Click();
        cut.Find(".tm-document-comments-panel__resolve").Click();
        cut.Find(".tm-document-comments-panel__reaction-add").Click();
        cut.Find(".tm-document-comments-panel__reaction-choice").Click();

        reply!.ThreadId.Should().Be("thread-1");
        reply.Body.Should().Be("Reply body");
        resolved!.ThreadId.Should().Be("thread-1");
        reaction!.ThreadId.Should().Be("thread-1");
        reaction.CommentId.Should().Be("comment-1");
    }

    [Fact]
    public void ThreadPanel_SelectThread_EmitsNavigationRequest()
    {
        DocumentCommentThreadNavigateRequest? navigation = null;
        var cut = Render<TmDocumentCommentThreadPanel>(parameters =>
            parameters.Add(p => p.Threads, PageTwoThread())
                      .Add(p => p.OnThreadNavigateRequested, value => navigation = value));

        cut.Find("[data-thread-id='thread-page-two']").Click();

        navigation.Should().NotBeNull();
        navigation!.ThreadId.Should().Be("thread-page-two");
        navigation.PageNumber.Should().Be(2);
    }

    [Fact]
    public void Composer_MentionSelection_EmitsStableMention()
    {
        IReadOnlyList<DocumentCommentMention>? mentions = null;
        var cut = Render<TmCommentComposer>(parameters =>
            parameters.Add(p => p.MentionUsers, MentionUsers())
                      .Add(p => p.MentionsChanged, value => mentions = value));

        cut.Find(".tm-comment-composer textarea").Input("Hello @al");
        cut.Find(".tm-comment-composer__mention-option").Click();

        cut.Find(".tm-comment-composer textarea").TextContent.Should().Contain("Hello @Alice Johnson");
        mentions.Should().ContainSingle(mention => mention.UserId == "u1");
    }

    [Fact]
    public void Composer_KeyboardMentionSelection_UsesActiveCandidate()
    {
        IReadOnlyList<DocumentCommentMention>? mentions = null;
        var cut = Render<TmCommentComposer>(parameters =>
            parameters.Add(p => p.MentionUsers, MentionUsers())
                      .Add(p => p.MentionsChanged, value => mentions = value));

        cut.Find(".tm-comment-composer textarea").Input("@");
        cut.Find(".tm-comment-composer textarea").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-comment-composer textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find(".tm-comment-composer textarea").TextContent.Should().Contain("@Bob Stone");
        mentions.Should().ContainSingle(mention => mention.UserId == "u2");
    }

    [Fact]
    public void Composer_SubmitOnEnter_SubmitsWhenMentionListIsClosed()
    {
        DocumentCommentComposerSubmitEventArgs? submitted = null;
        var cut = Render<TmCommentComposer>(parameters =>
            parameters.Add(p => p.SubmitOnEnter, true)
                      .Add(p => p.OnSubmit, args => submitted = args));

        cut.Find(".tm-comment-composer textarea").Input("Keyboard reply");
        cut.Find(".tm-comment-composer textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        submitted!.Body.Should().Be("Keyboard reply");
    }

    [Fact]
    public void Composer_AutoFocus_SetsAutofocusAndKeepsTextareaClass()
    {
        var cut = Render<TmCommentComposer>(parameters =>
            parameters.Add(p => p.AutoFocus, true));

        var field = cut.Find("textarea");
        field.GetAttribute("autofocus").Should().NotBeNull();
        field.ClassList.Should().Contain("tm-textarea");
        JSInterop.Invocations.Count(invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "AutoFocus moves focus through TmTextArea.FocusAsync exactly once");
    }

    [Fact]
    public void Viewer_MobileDraft_SubmitsFromSheet()
    {
        DocumentCommentThreadCreateRequest? created = null;
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
                      .Add(p => p.CommentsEnabled, true)
                      .Add(p => p.CommentMode, DocumentCommentMode.Comment)
                      .Add(p => p.OnCommentThreadCreateRequested, request => created = request));

        cut.Find(".tm-document-page-viewer__comments-sheet").Should().NotBeNull();
        cut.Find(".tm-document-page-viewer__page").PointerDown(new PointerEventArgs { OffsetX = 100, OffsetY = 100, Buttons = 1 });
        cut.Find(".tm-document-page-viewer__page").PointerUp(new PointerEventArgs { OffsetX = 100, OffsetY = 100, Buttons = 1 });
        cut.Find(".tm-comment-composer textarea").Input("Sheet draft");
        cut.Find(".tm-comment-composer__button--primary").Click();

        created.Should().NotBeNull();
        created!.Body.Should().Be("Sheet draft");
    }

    private static SigningDocumentPage CreatePage(double width = 800, double height = 1000)
    {
        return new SigningDocumentPage
        {
            AttachmentUuid = "attachment-1",
            PageIndex = 0,
            ImageUrl = "/samples/page-1.png",
            Width = width,
            Height = height,
            Label = "Page 1"
        };
    }

    private static IReadOnlyList<DocumentCommentThread> SampleThreads()
    {
        return
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
                        CanDelete = true,
                        Mentions = [new DocumentCommentMention { UserId = "u1", DisplayName = "Alice Johnson" }],
                        Reactions = [new DocumentCommentReaction { Value = "👍", UserIds = ["u2"] }]
                    }
                ]
            },
            new DocumentCommentThread
            {
                Id = "thread-2",
                Anchor = DocumentCommentAnchor.Area(1, 0.55, 0.6, 0.2, 0.1),
                Status = DocumentCommentThreadStatus.Resolved,
                Comments =
                [
                    new DocumentComment
                    {
                        Id = "comment-2",
                        AuthorId = "u3",
                        AuthorName = "Cara",
                        Body = "Resolved note",
                        CreatedAt = new DateTimeOffset(2026, 5, 9, 11, 0, 0, TimeSpan.Zero)
                    }
                ]
            }
        ];
    }

    private static IReadOnlyList<DocumentCommentThread> PageThread()
    {
        return
        [
            new DocumentCommentThread
            {
                Id = "thread-page",
                Anchor = DocumentCommentAnchor.Page(1),
                Comments =
                [
                    new DocumentComment
                    {
                        Id = "comment-page",
                        AuthorId = "u2",
                        AuthorName = "Bob Stone",
                        Body = "Please review this whole page.",
                        CreatedAt = new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.Zero)
                    }
                ]
            }
        ];
    }

    private static IReadOnlyList<DocumentCommentThread> PageTwoThread()
    {
        return
        [
            new DocumentCommentThread
            {
                Id = "thread-page-two",
                Anchor = DocumentCommentAnchor.Point(2, 0.2, 0.2),
                Comments =
                [
                    new DocumentComment
                    {
                        Id = "comment-page-two",
                        AuthorId = "u2",
                        AuthorName = "Bob Stone",
                        Body = "Second page",
                        CreatedAt = new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.Zero)
                    }
                ]
            }
        ];
    }

    private static IReadOnlyList<DocumentCommentUser> MentionUsers()
    {
        return
        [
            new DocumentCommentUser { UserId = "u1", DisplayName = "Alice Johnson", Email = "alice@example.test" },
            new DocumentCommentUser { UserId = "u2", DisplayName = "Bob Stone", Email = "bob@example.test" }
        ];
    }
}
