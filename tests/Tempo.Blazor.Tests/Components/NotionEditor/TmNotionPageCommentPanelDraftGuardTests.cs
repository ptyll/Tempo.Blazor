using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Draft-guard surface of <see cref="TmNotionPageCommentPanel"/> (OnDraftChanged / HasUnsavedDraft /
/// DiscardDraftAsync). Every composer of the panel — new comment, thread reply, inline reply and the
/// entry edit — holds text the host cannot observe through parameters; these tests pin the additive
/// surface that reports it.
/// </summary>
public sealed class TmNotionPageCommentPanelDraftGuardTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("cf100000-0000-0000-0000-000000000001");

    public TmNotionPageCommentPanelDraftGuardTests()
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

    private static NotionEditorContext ContextWith(FakeCommentProvider provider) => new()
    {
        DataProvider = default!,
        BlockService = default!,
        CommentProvider = provider
    };

    private IRenderedComponent<DraftGuardHost> RenderHost(
        FakeCommentProvider provider,
        List<TmNotionCommentDraftState>? states = null)
        => Render<DraftGuardHost>(parameters => parameters
            .Add(component => component.Context, ContextWith(provider))
            .Add(component => component.PageId, PageId.ToString("D"))
            .Add(component => component.OnDraftChanged, EventCallback.Factory.Create<TmNotionCommentDraftState>(
                this, state => states?.Add(state))));

    /// <summary>Expands the panel and waits until the new-comment composer rendered.</summary>
    private static async Task ExpandAsync(IRenderedComponent<DraftGuardHost> cut)
    {
        await cut.Find(".tm-npcp__toggle").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.FindAll(".tm-npcp__new-comment").Should().NotBeEmpty());
    }

    private static async Task SeedThreadAsync(IRenderedComponent<DraftGuardHost> cut, FakeCommentProvider provider)
    {
        await ExpandAsync(cut);
        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("Page comment");
        await cut.Find(".tm-npcp__new-comment .tm-npcp__reply-send").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
            cut.Find(".tm-npcp__entry-text").TextContent.Should().Contain("Page comment"));
        provider.PageComments.Should().ContainSingle();
    }

    private static IElement FindEntryAction(IRenderedComponent<DraftGuardHost> cut, string titlePart)
        => cut.FindAll(".tm-npcp__entry-action")
              .First(e => e.GetAttribute("title")?.Contains(titlePart) == true);

    /// <summary>The panel instance under the host — the draft-guard surface lives there.</summary>
    private static TmNotionPageCommentPanel Panel(IRenderedComponent<DraftGuardHost> cut)
        => cut.FindComponent<TmNotionPageCommentPanel>().Instance;

    // ── New comment ──────────────────────────────────────────────────

    [Fact]
    public void NewComment_Typing_FiresOnDraftChanged()
    {
        var states = new List<TmNotionCommentDraftState>();
        var cut = RenderHost(new FakeCommentProvider(PageId.ToString("D")), states);
        ExpandAsync(cut).Wait();

        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("Draft");

        states.Should().ContainSingle().Which.Should().Be(new TmNotionCommentDraftState(
            HasNewCommentDraft: true, HasThreadReplyDraft: false, HasInlineReplyDraft: false,
            EditingEntryId: null, HasUnsavedContent: true));
        Panel(cut).HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public void NewComment_DedupsNotificationsWhileDraftStateUnchanged()
    {
        var states = new List<TmNotionCommentDraftState>();
        var cut = RenderHost(new FakeCommentProvider(PageId.ToString("D")), states);
        ExpandAsync(cut).Wait();

        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("a");
        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("ab");

        states.Should().ContainSingle("value-equality dedup suppresses identical states")
            .Which.HasNewCommentDraft.Should().BeTrue();
    }

    [Fact]
    public async Task NewComment_Sending_ClearsTheDraft()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var cut = RenderHost(provider, states);
        await SeedThreadAsync(cut, provider);

        Panel(cut).HasUnsavedDraft.Should().BeFalse("the comment was submitted");
        states.Last().HasNewCommentDraft.Should().BeFalse();
    }

    // ── Thread reply ─────────────────────────────────────────────────

    [Fact]
    public async Task ThreadReply_Typing_FiresOnDraftChanged()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var cut = RenderHost(provider, states);
        await SeedThreadAsync(cut, provider);

        await cut.Find(".tm-npcp__reply-trigger").ClickAsync(new MouseEventArgs());
        cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-input").Input("Thread reply");

        states[^1].Should().Be(new TmNotionCommentDraftState(
            HasNewCommentDraft: false, HasThreadReplyDraft: true, HasInlineReplyDraft: false,
            EditingEntryId: null, HasUnsavedContent: true));

        cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-cancel").Click();
        Panel(cut).HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task ThreadReply_DraftIsOnlyReportedWhileTheReplyIsOpen()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var cut = RenderHost(provider, states);
        await SeedThreadAsync(cut, provider);

        await cut.Find(".tm-npcp__reply-trigger").ClickAsync(new MouseEventArgs());
        cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-input").Input("Thread reply");
        Panel(cut).HasUnsavedDraft.Should().BeTrue();

        cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-cancel").Click();

        Panel(cut).HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmNotionCommentDraftState(false, false, false, null, false),
            "a closed reply must never report a thread draft, even if its text lingered");
    }

    // ── Inline reply ─────────────────────────────────────────────────

    [Fact]
    public async Task InlineReply_OpenedWithAutoQuote_IsNotAnUnsavedDraft_UntilTyped()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var cut = RenderHost(provider, states);
        await SeedThreadAsync(cut, provider);

        // Enter on the entry starts the inline reply prefilled with the auto quote — that
        // auto-generated text alone is not user content, so it must not report a draft.
        cut.Find(".tm-npcp__entry").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-npcp__inline-reply").Should().ContainSingle();

        var afterOpen = states.Last();
        afterOpen.EditingEntryId.Should().BeNull();
        afterOpen.HasUnsavedContent.Should().BeFalse();

        cut.Find(".tm-npcp__inline-reply .tm-npcp__reply-input").Input("typed");
        states[^1].HasInlineReplyDraft.Should().BeTrue();
        states[^1].HasUnsavedContent.Should().BeTrue();
    }

    // ── Edit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Edit_OpenedWithoutChanges_IsNotAnUnsavedDraft_TypingReportsOne()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var cut = RenderHost(provider, states);
        await SeedThreadAsync(cut, provider);

        FindEntryAction(cut, "Edit").Click();
        cut.FindAll(".tm-npcp__edit-wrap").Should().ContainSingle();

        var entryId = provider.PageComments[0].Entries[0].Id;
        states.Last().Should().Be(new TmNotionCommentDraftState(
            HasNewCommentDraft: false, HasThreadReplyDraft: false, HasInlineReplyDraft: false,
            EditingEntryId: entryId, HasUnsavedContent: false),
            "opening an edit without changing anything is not an unsaved draft");

        cut.Find("textarea.tm-npcp__edit-input").Input("Changed");
        states[^1].EditingEntryId.Should().Be(entryId);
        states[^1].HasUnsavedContent.Should().BeTrue();

        // Escape cancels the edit and clears the draft flag.
        cut.Find("textarea.tm-npcp__edit-input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-npcp__edit-wrap").Should().BeEmpty();
        Panel(cut).HasUnsavedDraft.Should().BeFalse();
    }

    // ── DiscardDraftAsync ────────────────────────────────────────────

    [Fact]
    public async Task DiscardDraftAsync_ClearsEveryDraftSlot_AndReportsOneClearedState()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new FakeCommentProvider(PageId.ToString("D"));
        var cut = RenderHost(provider, states);
        await SeedThreadAsync(cut, provider);

        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("new draft");
        await cut.Find(".tm-npcp__reply-trigger").ClickAsync(new MouseEventArgs());
        cut.Find(".tm-npcp__thread-reply .tm-npcp__reply-input").Input("reply draft");
        Panel(cut).HasUnsavedDraft.Should().BeTrue();

        await cut.InvokeAsync(() => Panel(cut).DiscardDraftAsync());

        Panel(cut).HasUnsavedDraft.Should().BeFalse();
        (cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").GetAttribute("value") ?? string.Empty).Should().BeEmpty();
        states.Last().Should().Be(new TmNotionCommentDraftState(
            false, false, false, null, false));
    }

    [Fact]
    public async Task DiscardDraftAsync_WithNoDraft_IsANoOp_WithoutNotification()
    {
        var states = new List<TmNotionCommentDraftState>();
        var cut = RenderHost(new FakeCommentProvider(PageId.ToString("D")), states);
        ExpandAsync(cut).Wait();

        await cut.InvokeAsync(() => Panel(cut).DiscardDraftAsync());

        states.Should().BeEmpty("the dedup suppresses a notification when nothing changed");
    }

    /// <summary>
    /// Host that mirrors the panel's controlled-expansion contract (Expanded + OnExpandedChanged)
    /// and forwards the draft notifications. The panel loads its threads only in
    /// ToggleExpandedAsync, so the tests drive the toggle exactly like a real host would.
    /// </summary>
    public sealed class DraftGuardHost : ComponentBase
    {
        [Parameter] public NotionEditorContext Context { get; set; } = default!;
        [Parameter] public string PageId { get; set; } = string.Empty;
        [Parameter] public EventCallback<TmNotionCommentDraftState> OnDraftChanged { get; set; }

        private bool _expanded;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", Context);
            builder.AddAttribute(2, "IsFixed", true);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TmNotionPageCommentPanel>(0);
                childBuilder.AddAttribute(1, "PageId", PageId);
                childBuilder.AddAttribute(2, "Expanded", _expanded);
                childBuilder.AddAttribute(3, "OnExpandedChanged", EventCallback.Factory.Create<bool>(
                    this, value =>
                    {
                        _expanded = value;
                        StateHasChanged();
                    }));
                childBuilder.AddAttribute(4, "OnDraftChanged", OnDraftChanged);
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
