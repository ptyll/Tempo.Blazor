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
/// Pass-through of the draft-guard surface on <see cref="TmNotionPageCommentSection"/>: the
/// section forwards the embedded panel's OnDraftChanged and delegates HasUnsavedDraft /
/// DiscardDraftAsync, so a host that renders the section (not the bare panel) gets the same
/// draft-guard visibility.
/// </summary>
public sealed class TmNotionPageCommentSectionDraftGuardTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("cf100000-0000-0000-0000-000000000002");

    public TmNotionPageCommentSectionDraftGuardTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddScoped<CommentNotificationOrchestrator>();
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Loading"] = "Loading",
            ["Tm_Delete"] = "Delete",
            ["Tm_Cancel"] = "Cancel",
            ["Notion_PageComments_Title"] = "Page comments",
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

    [Fact]
    public void Section_ForwardsPanelDraftStates_AndDelegatesSurface()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new SectionFakeCommentProvider();
        var cut = Render<SectionDraftGuardHost>(parameters => parameters
            .Add(c => c.Context, new NotionEditorContext
            {
                DataProvider = default!,
                BlockService = default!,
                CommentProvider = provider
            })
            .Add(c => c.PageId, PageId.ToString("D"))
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmNotionCommentDraftState>(
                this, s => states.Add(s))));

        // Expand the section through the controlled-expansion contract.
        cut.Find(".tm-npcp__toggle").ClickAsync(new MouseEventArgs()).Wait();
        cut.WaitForAssertion(() => cut.FindAll(".tm-npcp__new-comment").Should().NotBeEmpty());

        var section = cut.FindComponent<TmNotionPageCommentSection>().Instance;
        section.HasUnsavedDraft.Should().BeFalse();

        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("Section draft");

        states.Should().Contain(new TmNotionCommentDraftState(
            HasNewCommentDraft: true, HasThreadReplyDraft: false, HasInlineReplyDraft: false,
            EditingEntryId: null, HasUnsavedContent: true));
        section.HasUnsavedDraft.Should().BeTrue("the section delegates the live flag to the panel");

        cut.InvokeAsync(() => section.DiscardDraftAsync()).Wait();

        section.HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmNotionCommentDraftState(false, false, false, null, false));
    }

    /// <summary>Host that mirrors the section's controlled expansion and forwards the draft states.</summary>
    public sealed class SectionDraftGuardHost : ComponentBase
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
                childBuilder.OpenComponent<TmNotionPageCommentSection>(0);
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

    private sealed class SectionFakeCommentProvider :
        ITmCommentProvider,
        ITmCommentReactionProvider,
        ITmCommentReadTrackingProvider,
        ITmCommentSubscriptionProvider
    {
        private readonly Dictionary<string, TmCommentThread> _comments = [];

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

        public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
            TmEntityRef entityRef, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmCommentThread>>(_comments.Values.ToList());

        public Task<TmCommentThread> CreateThreadAsync(
            TmCommentThread thread, CancellationToken cancellationToken = default)
        {
            thread.Id = Guid.NewGuid().ToString("N");
            thread.EntityRef = TmEntityRef.Create("notion-page", PageId.ToString("D"));
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
            string threadId, TmCommentEntry entry, CancellationToken cancellationToken = default)
        {
            var comment = _comments[threadId];
            entry.Id = Guid.NewGuid().ToString("N");
            entry.ThreadId = threadId;
            entry.Author = User();
            entry.BodyFormat = TmCommentBodyFormat.Html;
            entry.CanEdit = true;
            entry.CanDelete = true;
            comment.Entries.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<TmCommentEntry> UpdateEntryAsync(
            string threadId, string entryId, TmCommentEntry entry, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
        {
            _comments.Remove(threadId);
            return Task.CompletedTask;
        }

        public Task DeleteEntryAsync(
            string threadId, string entryId, CancellationToken cancellationToken = default)
        {
            _comments[threadId].Entries.RemoveAll(entry => entry.Id == entryId);
            return Task.CompletedTask;
        }

        public Task<TmCommentThread> ResolveAsync(
            string threadId, TmUserRef? resolvedBy = null, CancellationToken cancellationToken = default)
        {
            var comment = _comments[threadId];
            comment.Status = TmCommentThreadStatus.Resolved;
            comment.ResolvedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(comment);
        }

        public Task<TmCommentThread> ReopenAsync(
            string threadId, TmUserRef? reopenedBy = null, CancellationToken cancellationToken = default)
        {
            var comment = _comments[threadId];
            comment.Status = TmCommentThreadStatus.Open;
            comment.ResolvedAt = null;
            return Task.FromResult(comment);
        }

        public Task<IReadOnlyList<TmCommentReaction>> GetReactionsAsync(
            string entryId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmCommentReaction>>(
                _comments.Values.SelectMany(c => c.Entries).Single(e => e.Id == entryId).Reactions);

        public Task AddReactionAsync(
            string entryId, string value, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveReactionAsync(
            string entryId, string value, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkThreadAsReadAsync(
            string threadId, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkThreadAsUnreadAsync(
            string threadId, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAllForEntityAsReadAsync(
            TmEntityRef entityRef, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SubscribeAsync(
            string threadId, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UnsubscribeAsync(
            string threadId, string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private static TmUserRef User()
            => new() { Id = "demo", DisplayName = "Demo User" };
    }
}
