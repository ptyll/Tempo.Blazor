using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionPageCommentSectionTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("cf100000-0000-0000-0000-000000000001");

    public TmNotionPageCommentSectionTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddScoped<CommentNotificationOrchestrator>();
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Loading"] = "Loading",
            ["Tm_Delete"] = "Delete",
            ["Tm_Cancel"] = "Cancel",
            ["TmCommentReactions_Label"] = "Reactions",
            ["TmCommentReactions_Add"] = "Add reaction",
            ["TmCommentReactions_You"] = "You",
            ["TmCommentReactions_More"] = "+{0} more",
            ["Notion_PageComments_Title"] = "Page comments",
            ["Notion_PageComments_Add"] = "Comment",
            ["Notion_PageComments_Placeholder"] = "Add a comment to this page",
            ["Notion_PageComments_Empty"] = "No comments yet",
            ["Notion_PageComments_Reply"] = "Reply",
            ["TmNotionPageComment_PanelLabel"] = "Page comments",
            ["TmNotionPageComment_Toggle"] = "Comments",
            ["TmNotionPageComment_BadgeLabel"] = "{0} unresolved comments",
            ["TmNotionPageComment_Loading"] = "Loading comments",
            ["TmNotionPageComment_NoComments"] = "No comments yet",
            ["TmNotionPageComment_Thread"] = "Comment threads",
            ["TmNotionPageComment_Resolve"] = "Resolve",
            ["TmNotionPageComment_Unresolve"] = "Re-open",
            ["TmNotionPageComment_Resolved"] = "Resolved",
            ["TmNotionPageComment_Edited"] = "edited",
            ["TmNotionPageComment_Edit"] = "Edit",
            ["TmNotionPageComment_Delete"] = "Delete",
            ["TmNotionPageComment_EditLabel"] = "Edit comment",
            ["TmNotionPageComment_Save"] = "Save",
            ["TmNotionPageComment_Cancel"] = "Cancel",
            ["TmNotionPageComment_ReplyTrigger"] = "Reply",
            ["TmNotionPageComment_ReplyPlaceholder"] = "Reply to this thread",
            ["TmNotionPageComment_ReplyLabel"] = "Write a reply",
            ["TmNotionPageComment_ReplyHint"] = "Ctrl+Enter to send",
            ["TmNotionPageComment_Reply"] = "Reply",
            ["TmNotionPageComment_ReplyToThis"] = "Reply to this",
            ["TmNotionPageComment_NewPlaceholder"] = "Add a comment to this page",
            ["TmNotionPageComment_NewLabel"] = "New comment",
            ["TmNotionPageComment_Comment"] = "Comment",
            ["TmNotionPageComment_DeleteConfirm"] = "Delete this comment?",
            ["TmNotionPageComment_LoadError"] = "Could not load comments.",
            ["TmNotionPageComment_SendError"] = "Could not send comment.",
            ["TmNotionPageComment_ActionError"] = "Action failed.",
            ["TmNotionPageComment_MarkAllAsRead"] = "Mark all as read",
            ["TmNotionPageComment_Watch"] = "Watch thread",
            ["TmNotionPageComment_Unwatch"] = "Unwatch",
            ["TmNotionPageComment_Time_JustNow"] = "just now",
            ["TmNotionPageComment_Time_MinutesAgo"] = "{0}m ago",
            ["TmNotionPageComment_Time_HoursAgo"] = "{0}h ago",
            ["TmNotionPageComment_Time_DaysAgo"] = "{0}d ago"
        });
    }

    [Fact]
    public async Task PageCommentSection_AddsRepliesReactsAndResolves()
    {
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var context = new NotionEditorContext
        {
            DataProvider = default!,
            BlockService = default!,
            CommentProvider = provider
        };
        var cut = Render<PageCommentHost>(parameters => parameters
            .Add(component => component.Context, context)
            .Add(component => component.PageId, PageId.ToString("D")));

        await cut.Find(".tm-npcp__toggle").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__status").TextContent.Should().Contain("No comments yet"));

        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("First page comment");
        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__new-comment .tm-npcp__reply-send").HasAttribute("disabled").Should().BeFalse());
        await cut.Find(".tm-npcp__new-comment .tm-npcp__reply-send").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__entry-text").TextContent.Should().Contain("First page comment"));
        provider.PageComments.Should().ContainSingle();

        await cut.Find(".tm-npcp__reply-trigger").ClickAsync(new MouseEventArgs());
        cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-input").Input("Thread reply");
        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-send").HasAttribute("disabled").Should().BeFalse());
        await cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-send").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Thread reply"));
        provider.PageComments[0].Entries.Should().HaveCount(2);

        await cut.Find(".tm-comment-reaction--add").ClickAsync(new MouseEventArgs());
        await cut.Find(".tm-comment-reaction-picker__item").ClickAsync(new MouseEventArgs());
        provider.PageComments[0].Entries[0].Reactions.Should().ContainSingle(reaction => reaction.Value == "👍");

        await cut.Find(".tm-npcp__thread-action").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__thread").ClassList.Should().Contain("tm-npcp__thread--resolved"));
        provider.PageComments[0].Status.Should().Be(TmCommentThreadStatus.Resolved);
    }

    // ── Keyboard isolation ──────────────────────────────────────────────────
    // Entry rows are non-native focusables (<div tabindex="0">) that
    // legitimately map Enter -> start inline reply. The native controls nested
    // inside an entry stop their keydowns from reaching that handler —
    // otherwise Enter on the Edit button opened the editor (native click) AND
    // started a reply (bubbled keydown) for one key press.

    [Fact]
    public async Task PageCommentSection_EnterOnEntryEditButton_OpensEdit_WithoutStartingReply()
    {
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var context = new NotionEditorContext
        {
            DataProvider = default!,
            BlockService = default!,
            CommentProvider = provider
        };
        var cut = Render<PageCommentHost>(parameters => parameters
            .Add(component => component.Context, context)
            .Add(component => component.PageId, PageId.ToString("D")));

        await SeedCommentAsync(cut);

        var edit = FindEntryAction(cut, "Edit");
        var act = () => edit.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the actions group stops keydowns before the entry's Enter->reply handler");
        cut.FindAll(".tm-npcp__inline-reply").Should().BeEmpty();

        // The native click half of the sequence still does the button's own job.
        FindEntryAction(cut, "Edit").Click();
        cut.FindAll(".tm-npcp__edit-wrap").Should().ContainSingle();
        cut.FindAll(".tm-npcp__inline-reply").Should().BeEmpty();
    }

    [Fact]
    public async Task PageCommentSection_EnterOnEntryDeleteButton_Confirms_WithoutStartingReply()
    {
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var context = new NotionEditorContext
        {
            DataProvider = default!,
            BlockService = default!,
            CommentProvider = provider
        };
        var cut = Render<PageCommentHost>(parameters => parameters
            .Add(component => component.Context, context)
            .Add(component => component.PageId, PageId.ToString("D")));

        await SeedCommentAsync(cut);

        var delete = FindEntryAction(cut, "Delete");
        var act = () => delete.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>();

        FindEntryAction(cut, "Delete").Click();
        cut.FindAll(".tm-dialog").Should().ContainSingle("the delete click shows its confirm dialog");
        cut.FindAll(".tm-npcp__inline-reply").Should().BeEmpty();
    }

    [Fact]
    public async Task PageCommentSection_EnterInEditTextarea_DoesNotStartReply_EscapeCancelsEdit()
    {
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var context = new NotionEditorContext
        {
            DataProvider = default!,
            BlockService = default!,
            CommentProvider = provider
        };
        var cut = Render<PageCommentHost>(parameters => parameters
            .Add(component => component.Context, context)
            .Add(component => component.PageId, PageId.ToString("D")));

        await SeedCommentAsync(cut);
        FindEntryAction(cut, "Edit").Click();
        cut.FindAll(".tm-npcp__edit-wrap").Should().ContainSingle();

        // Enter inside the edit input inserts a newline natively; the edit
        // wrap stops the keydown so the entry must not start a reply.
        cut.Find("textarea.tm-npcp__edit-input")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-npcp__edit-wrap").Should().ContainSingle();
        cut.FindAll(".tm-npcp__inline-reply").Should().BeEmpty();

        // Escape->cancel previously relied on bubbling to the entry handler;
        // the edit input now handles it locally.
        cut.Find("textarea.tm-npcp__edit-input")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-npcp__edit-wrap").Should().BeEmpty();
    }

    [Fact]
    public async Task PageCommentSection_EnterOnEntryItself_StillStartsInlineReply()
    {
        // Legitimate emulation kept: the entry row is a non-native focusable.
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var context = new NotionEditorContext
        {
            DataProvider = default!,
            BlockService = default!,
            CommentProvider = provider
        };
        var cut = Render<PageCommentHost>(parameters => parameters
            .Add(component => component.Context, context)
            .Add(component => component.PageId, PageId.ToString("D")));

        await SeedCommentAsync(cut);

        cut.Find(".tm-npcp__entry").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-npcp__inline-reply").Should().ContainSingle();
    }

    private static async Task SeedCommentAsync(IRenderedComponent<PageCommentHost> cut)
    {
        await cut.Find(".tm-npcp__toggle").ClickAsync(new MouseEventArgs());
        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("Page comment");
        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__new-comment .tm-npcp__reply-send").HasAttribute("disabled").Should().BeFalse());
        await cut.Find(".tm-npcp__new-comment .tm-npcp__reply-send").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__entry-text").TextContent.Should().Contain("Page comment"));
    }

    private static IElement FindEntryAction(IRenderedComponent<PageCommentHost> cut, string titlePart)
        => cut.FindAll(".tm-npcp__entry-action")
              .First(e => e.GetAttribute("title")?.Contains(titlePart) == true);

    public sealed class PageCommentHost : ComponentBase
    {
        [Parameter] public NotionEditorContext Context { get; set; } = default!;
        [Parameter] public string PageId { get; set; } = string.Empty;

        private bool _expanded;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", Context);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TmNotionPageCommentSection>(0);
                childBuilder.AddAttribute(1, "PageId", PageId);
                childBuilder.AddAttribute(2, "Expanded", _expanded);
                childBuilder.AddAttribute(3, "OnExpandedChanged", EventCallback.Factory.Create<bool>(this, value =>
                {
                    _expanded = value;
                    StateHasChanged();
                }));
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class FakeCommentProvider :
        ITmCommentProvider,
        ITmCommentReactionProvider,
        ITmCommentReadTrackingProvider,
        ITmCommentSubscriptionProvider
    {
        private readonly string _pageId;
        private readonly Dictionary<string, TmCommentThread> _comments = [];

        public FakeCommentProvider(string pageId)
        {
            _pageId = pageId;
        }

        public TmCommentProviderCapabilities Capabilities =>
            TmCommentProviderCapabilities.Read
            | TmCommentProviderCapabilities.CreateThread
            | TmCommentProviderCapabilities.Reply
            | TmCommentProviderCapabilities.Delete
            | TmCommentProviderCapabilities.Resolve
            | TmCommentProviderCapabilities.Reactions
            | TmCommentProviderCapabilities.ReadTracking
            | TmCommentProviderCapabilities.Subscriptions
            | TmCommentProviderCapabilities.RichText;

        public List<TmCommentThread> PageComments => _comments.Values.ToList();

        public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
            TmEntityRef entityRef,
            CancellationToken cancellationToken = default)
        {
            var result = entityRef.EntityType == "notion-page" && entityRef.EntityId == _pageId
                ? _comments.Values.ToList()
                : [];
            return Task.FromResult<IReadOnlyList<TmCommentThread>>(result);
        }

        public Task<TmCommentThread> CreateThreadAsync(
            TmCommentThread thread,
            CancellationToken cancellationToken = default)
        {
            thread.Id = Guid.NewGuid().ToString("N");
            thread.EntityRef = TmEntityRef.Create("notion-page", _pageId);
            foreach (var entry in thread.Entries)
            {
                entry.Id = Guid.NewGuid().ToString("N");
                entry.ThreadId = thread.Id;
                entry.Author = User();
                entry.CanEdit = true;
                entry.CanDelete = true;
            }
            thread.SubscribedUserIds.Add("demo");
            _comments[thread.Id] = thread;
            return Task.FromResult(thread);
        }

        public Task<TmCommentEntry> ReplyAsync(
            string threadId,
            TmCommentEntry entry,
            CancellationToken cancellationToken = default)
        {
            var comment = _comments[threadId];
            entry.Id = Guid.NewGuid().ToString("N");
            entry.ThreadId = threadId;
            entry.Author = User();
            entry.BodyFormat = TmCommentBodyFormat.Html;
            entry.CanEdit = true;
            entry.CanDelete = true;
            comment.Entries.Add(entry);
            comment.UpdatedAt = entry.CreatedAt;
            return Task.FromResult(entry);
        }

        public Task<TmCommentEntry> UpdateEntryAsync(
            string threadId,
            string entryId,
            TmCommentEntry entry,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
        {
            _comments.Remove(threadId);
            return Task.CompletedTask;
        }

        public Task DeleteEntryAsync(
            string threadId,
            string entryId,
            CancellationToken cancellationToken = default)
        {
            _comments[threadId].Entries.RemoveAll(entry => entry.Id == entryId);
            return Task.CompletedTask;
        }

        public Task<TmCommentThread> ResolveAsync(
            string threadId,
            TmUserRef? resolvedBy = null,
            CancellationToken cancellationToken = default)
        {
            var comment = _comments[threadId];
            comment.Status = TmCommentThreadStatus.Resolved;
            comment.ResolvedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(comment);
        }

        public Task<TmCommentThread> ReopenAsync(
            string threadId,
            TmUserRef? reopenedBy = null,
            CancellationToken cancellationToken = default)
        {
            var comment = _comments[threadId];
            comment.Status = TmCommentThreadStatus.Open;
            comment.ResolvedAt = null;
            return Task.FromResult(comment);
        }

        public Task<IReadOnlyList<TmCommentReaction>> GetReactionsAsync(
            string entryId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmCommentReaction>>(FindEntry(entryId).Reactions);

        public Task AddReactionAsync(
            string entryId,
            string value,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var entry = FindEntry(entryId);
            var reaction = entry.Reactions.SingleOrDefault(reaction => reaction.Value == value);
            if (reaction is null)
            {
                reaction = new TmCommentReaction { Value = value };
                entry.Reactions.Add(reaction);
            }
            if (!reaction.UserIds.Contains(userId))
                reaction.UserIds.Add(userId);
            return Task.CompletedTask;
        }

        public Task RemoveReactionAsync(
            string entryId,
            string value,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkThreadAsReadAsync(
            string threadId,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkThreadAsUnreadAsync(
            string threadId,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAllForEntityAsReadAsync(
            TmEntityRef entityRef,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SubscribeAsync(
            string threadId,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UnsubscribeAsync(
            string threadId,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private TmCommentEntry FindEntry(string entryId)
            => _comments.Values.SelectMany(comment => comment.Entries).Single(entry => entry.Id == entryId);

        private static TmUserRef User()
            => new() { Id = "demo", DisplayName = "Demo User" };
    }
}
