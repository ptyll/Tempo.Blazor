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
/// Draft-guard surfaces of the Notion block and text comment panels. Their composers hold text in
/// private fields the host cannot observe — these tests pin the additive OnDraftChanged /
/// HasUnsavedDraft / DiscardDraftAsync surfaces (reusing TmNotionCommentDraftState).
/// </summary>
public class TmNotionBlockCommentPanelDraftGuardTests : LocalizationTestBase
{
    public TmNotionBlockCommentPanelDraftGuardTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddScoped<CommentNotificationOrchestrator>();
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Delete"] = "Delete",
            ["Tm_Cancel"] = "Cancel",
            ["TmCommentReactions_Label"] = "Reactions",
            ["TmCommentReactions_Add"] = "Add reaction",
            ["TmCommentReactions_You"] = "You",
            ["TmCommentReactions_More"] = "+{0} more",
            ["TmNotionBlockComment_Title"] = "Comments",
            ["TmNotionBlockComment_NewThread"] = "New thread",
            ["TmNotionBlockComment_ThreadDetail"] = "Thread",
            ["TmNotionBlockComment_ThreadList"] = "Threads",
            ["TmNotionBlockComment_Thread"] = "Thread",
            ["TmNotionBlockComment_ThreadActions"] = "Actions",
            ["TmNotionBlockComment_EntryActions"] = "Entry actions",
            ["TmNotionBlockComment_Resolve"] = "Resolve",
            ["TmNotionBlockComment_Unresolve"] = "Re-open",
            ["TmNotionBlockComment_Resolved"] = "Resolved",
            ["TmNotionBlockComment_Edited"] = "edited",
            ["TmNotionBlockComment_Edit"] = "Edit",
            ["TmNotionBlockComment_Delete"] = "Delete",
            ["TmNotionBlockComment_DeleteConfirm"] = "Delete?",
            ["TmNotionBlockComment_EditLabel"] = "Edit comment",
            ["TmNotionBlockComment_Save"] = "Save",
            ["TmNotionBlockComment_ReplyToThis"] = "Reply to this",
            ["TmNotionBlockComment_ReplyPlaceholder"] = "Reply…",
            ["TmNotionBlockComment_NewPlaceholder"] = "New comment…",
            ["TmNotionBlockComment_ReplyLabel"] = "Reply",
            ["TmNotionBlockComment_ReplyHint"] = "Ctrl+Enter",
            ["TmNotionBlockComment_Reply"] = "Reply",
            ["TmNotionBlockComment_Comment"] = "Comment",
            ["TmNotionBlockComment_MarkAsRead"] = "Mark as read",
            ["TmNotionBlockComment_Watch"] = "Watch",
            ["TmNotionBlockComment_Unwatch"] = "Unwatch",
            ["TmNotionBlockComment_RepliesCount"] = "{0} replies",
            ["TmNotionBlockComment_NoComments"] = "No comments",
            ["TmNotionBlockComment_Loading"] = "Loading"
        });
    }

    private static NotionEditorContext ContextWith(BlockFakeCommentProvider provider) => new()
    {
        DataProvider = default!,
        BlockService = default!,
        CommentProvider = provider
    };

    private IRenderedComponent<BlockDraftGuardHost> RenderHost(
        BlockFakeCommentProvider provider,
        bool startInNewThreadMode,
        List<TmNotionCommentDraftState>? states = null)
        => Render<BlockDraftGuardHost>(parameters => parameters
            .Add(c => c.Context, ContextWith(provider))
            .Add(c => c.BlockId, "block-1")
            .Add(c => c.StartInNewThreadMode, startInNewThreadMode)
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmNotionCommentDraftState>(
                this, s => states?.Add(s))));

    [Fact]
    public void NewThread_Typing_FiresOnDraftChanged_AndSendClearsIt()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new BlockFakeCommentProvider();
        var cut = RenderHost(provider, startInNewThreadMode: true, states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nbcp__reply").Should().NotBeEmpty());

        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Input("New block thread");
        states.Should().Contain(new TmNotionCommentDraftState(
            HasNewCommentDraft: true, HasThreadReplyDraft: false, HasInlineReplyDraft: false,
            EditingEntryId: null, HasUnsavedContent: true));
        Panel(cut).HasUnsavedDraft.Should().BeTrue();

        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-send").Click();
        cut.WaitForAssertion(() => provider.BlockComments.Should().ContainSingle());
        Panel(cut).HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmNotionCommentDraftState(false, false, false, null, false));
    }

    [Fact]
    public void ThreadReply_Typing_FiresThreadSlot_AndDiscardClearsEverything()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new BlockFakeCommentProvider();
        var cut = RenderHost(provider, startInNewThreadMode: true, states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nbcp__reply").Should().NotBeEmpty());

        // Create the thread, then the bottom composer becomes the thread reply.
        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Input("Root");
        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-send").Click();
        cut.WaitForAssertion(() => provider.BlockComments.Should().ContainSingle());
        var threadId = provider.BlockComments[0].Id;

        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Input("Reply draft");
        states[^1].Should().Be(new TmNotionCommentDraftState(
            HasNewCommentDraft: false, HasThreadReplyDraft: true, HasInlineReplyDraft: false,
            EditingEntryId: null, HasUnsavedContent: true));

        cut.InvokeAsync(() => Panel(cut).DiscardDraftAsync()).Wait();

        Panel(cut).HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmNotionCommentDraftState(false, false, false, null, false));
        provider.BlockComments.Should().ContainSingle(t => t.Id == threadId);
    }

    [Fact]
    public void InlineReply_OpenedWithAutoQuote_IsNotUnsaved_UntilTyped()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new BlockFakeCommentProvider();
        var cut = RenderHost(provider, startInNewThreadMode: true, states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nbcp__reply").Should().NotBeEmpty());

        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Input("Root");
        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-send").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-nbcp__entry").Should().NotBeEmpty());

        cut.Find(".tm-nbcp__entry").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-nbcp__inline-reply").Should().ContainSingle();

        var afterOpen = states.Last();
        afterOpen.HasInlineReplyDraft.Should().BeFalse(
            "the auto-inserted quote is not user content");
        afterOpen.HasUnsavedContent.Should().BeFalse();

        cut.Find(".tm-nbcp__inline-reply .tm-nbcp__reply-input").Input("typed");
        states[^1].HasInlineReplyDraft.Should().BeTrue();
        states[^1].HasUnsavedContent.Should().BeTrue();
    }

    [Fact]
    public void DeleteLastThread_EnterNewThreadMode_ReportsTheTransition()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new BlockFakeCommentProvider();
        SeedSingleThreadAsync(provider).Wait();
        var cut = RenderHost(provider, startInNewThreadMode: false, states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nbcp__entry").Should().NotBeEmpty());

        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Input("Reply draft");
        Panel(cut).HasUnsavedDraft.Should().BeTrue();

        cut.FindAll(".tm-nbcp__entry-action")
            .First(e => e.GetAttribute("title")?.Contains("Delete") == true)
            .Click();
        cut.Find(".tm-dialog-btn-ok").Click();

        // The typed text survives into the new-thread composer (same input), so the delete must
        // REPORT the transition: the thread-reply slot closed, the new-thread slot took over.
        cut.WaitForAssertion(() => states.Last().Should().Be(new TmNotionCommentDraftState(
            HasNewCommentDraft: true, HasThreadReplyDraft: false, HasInlineReplyDraft: false,
            EditingEntryId: null, HasUnsavedContent: true)));
        Panel(cut).HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public void HidePanel_ClearsDraftSlots_AndReportsTheClearedState()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new BlockFakeCommentProvider();
        var cut = RenderHost(provider, startInNewThreadMode: true, states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nbcp__reply").Should().NotBeEmpty());

        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Input("Root");
        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-send").Click();
        cut.WaitForAssertion(() => provider.BlockComments.Should().ContainSingle());
        cut.Find(".tm-nbcp__reply .tm-nbcp__reply-input").Input("Reply draft");
        Panel(cut).HasUnsavedDraft.Should().BeTrue();

        cut.Render(parameters => parameters.Add(h => h.Visible, false));

        cut.WaitForAssertion(() => Panel(cut).HasUnsavedDraft.Should().BeFalse());
        states.Last().Should().Be(new TmNotionCommentDraftState(false, false, false, null, false),
            "hiding the panel reports the cleared state once, exactly like a real close");
    }

    /// <summary>Seeds one thread so the panel opens straight into its detail view.</summary>
    private static async Task SeedSingleThreadAsync(BlockFakeCommentProvider provider)
    {
        var thread = new TmCommentThread
        {
            EntityRef = TmEntityRef.Create("notion-block", "block-1"),
            Visibility = TmCommentVisibility.Internal
        };
        thread.Entries.Add(new TmCommentEntry
        {
            Id = "entry-1",
            Author = new TmUserRef { Id = "demo", DisplayName = "Demo User" },
            Body = "root entry",
            CanEdit = true,
            CanDelete = true
        });
        await provider.CreateThreadAsync(thread);
    }

    /// <summary>The panel instance under the host — the draft-guard surface lives there.</summary>
    private static TmNotionBlockCommentPanel Panel(IRenderedComponent<BlockDraftGuardHost> cut)
        => cut.FindComponent<TmNotionBlockCommentPanel>().Instance;

    public sealed class BlockDraftGuardHost : ComponentBase
    {
        [Parameter] public NotionEditorContext Context { get; set; } = default!;
        [Parameter] public string BlockId { get; set; } = string.Empty;
        [Parameter] public bool StartInNewThreadMode { get; set; }
        [Parameter] public bool Visible { get; set; } = true;
        [Parameter] public EventCallback<TmNotionCommentDraftState> OnDraftChanged { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", Context);
            builder.AddAttribute(2, "IsFixed", true);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TmNotionBlockCommentPanel>(0);
                childBuilder.AddAttribute(1, "Visible", Visible);
                childBuilder.AddAttribute(2, "BlockId", BlockId);
                childBuilder.AddAttribute(3, "StartInNewThreadMode", StartInNewThreadMode);
                childBuilder.AddAttribute(4, "OnDraftChanged", OnDraftChanged);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}

/// <summary>
/// Draft-guard surface of <see cref="TmNotionTextCommentPanel"/> — the text-anchor twin of the
/// block panel with the same record.
/// </summary>
public class TmNotionTextCommentPanelDraftGuardTests : LocalizationTestBase
{
    public TmNotionTextCommentPanelDraftGuardTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddScoped<CommentNotificationOrchestrator>();
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Delete"] = "Delete",
            ["Tm_Cancel"] = "Cancel",
            ["TmCommentReactions_Label"] = "Reactions",
            ["TmCommentReactions_Add"] = "Add reaction",
            ["TmCommentReactions_You"] = "You",
            ["TmCommentReactions_More"] = "+{0} more",
            ["TmNotionTextComment_PanelLabel"] = "Comments",
            ["TmNotionTextComment_Thread"] = "Thread",
            ["TmNotionTextComment_Resolve"] = "Resolve",
            ["TmNotionTextComment_Unresolve"] = "Re-open",
            ["TmNotionTextComment_Resolved"] = "Resolved",
            ["TmNotionTextComment_Edited"] = "edited",
            ["TmNotionTextComment_Edit"] = "Edit",
            ["TmNotionTextComment_Delete"] = "Delete",
            ["TmNotionTextComment_DeleteConfirm"] = "Delete?",
            ["TmNotionTextComment_EditLabel"] = "Edit comment",
            ["TmNotionTextComment_Save"] = "Save",
            ["TmNotionTextComment_ReplyToThis"] = "Reply to this",
            ["TmNotionTextComment_ReplyPlaceholder"] = "Reply…",
            ["TmNotionTextComment_NewPlaceholder"] = "New comment…",
            ["TmNotionTextComment_ReplyLabel"] = "Reply",
            ["TmNotionTextComment_ReplyHint"] = "Ctrl+Enter",
            ["TmNotionTextComment_Reply"] = "Reply",
            ["TmNotionTextComment_Comment"] = "Comment",
            ["TmNotionTextComment_MarkAsRead"] = "Mark as read",
            ["TmNotionTextComment_Watch"] = "Watch",
            ["TmNotionTextComment_Unwatch"] = "Unwatch",
            ["TmNotionTextComment_NoComments"] = "No comments",
            ["TmNotionTextComment_Loading"] = "Loading"
        });
    }

    private IRenderedComponent<TextDraftGuardHost> RenderHost(
        BlockFakeCommentProvider provider,
        List<TmNotionCommentDraftState>? states = null)
        => Render<TextDraftGuardHost>(parameters => parameters
            .Add(c => c.Context, new NotionEditorContext
            {
                DataProvider = default!,
                BlockService = default!,
                CommentProvider = provider
            })
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmNotionCommentDraftState>(
                this, s => states?.Add(s))));

    [Fact]
    public void NewComment_Typing_FiresOnDraftChanged_AndSendClearsIt()
    {
        var states = new List<TmNotionCommentDraftState>();
        var provider = new BlockFakeCommentProvider();
        var cut = RenderHost(provider, states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-ntcp__reply").Should().NotBeEmpty());

        cut.Find(".tm-ntcp__reply .tm-ntcp__reply-input").Input("Anchored comment");
        states.Should().Contain(new TmNotionCommentDraftState(
            HasNewCommentDraft: true, HasThreadReplyDraft: false, HasInlineReplyDraft: false,
            EditingEntryId: null, HasUnsavedContent: true),
            "with no thread loaded the bottom composer creates a new anchored comment");
        Panel(cut).HasUnsavedDraft.Should().BeTrue();

        cut.Find(".tm-ntcp__reply .tm-ntcp__reply-send").Click();
        cut.WaitForAssertion(() => provider.BlockComments.Should().ContainSingle());
        Panel(cut).HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmNotionCommentDraftState(false, false, false, null, false));
    }

    [Fact]
    public async Task DiscardDraftAsync_WithNoDraft_IsANoOp_WithoutNotification()
    {
        var states = new List<TmNotionCommentDraftState>();
        var cut = RenderHost(new BlockFakeCommentProvider(), states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-ntcp__reply").Should().NotBeEmpty());

        await cut.InvokeAsync(() => Panel(cut).DiscardDraftAsync());

        states.Should().BeEmpty("the dedup suppresses a notification when nothing changed");
    }

    [Fact]
    public void HidePanel_ClearsDraftSlots_AndReportsTheClearedState()
    {
        var states = new List<TmNotionCommentDraftState>();
        var cut = RenderHost(new BlockFakeCommentProvider(), states);
        cut.WaitForAssertion(() => cut.FindAll(".tm-ntcp__reply").Should().NotBeEmpty());

        cut.Find(".tm-ntcp__reply .tm-ntcp__reply-input").Input("Anchored draft");
        Panel(cut).HasUnsavedDraft.Should().BeTrue();

        cut.Render(parameters => parameters.Add(h => h.Visible, false));

        cut.WaitForAssertion(() => Panel(cut).HasUnsavedDraft.Should().BeFalse());
        states.Last().Should().Be(new TmNotionCommentDraftState(false, false, false, null, false),
            "hiding the panel reports the cleared state once, exactly like a real close");
    }

    /// <summary>The panel instance under the host — the draft-guard surface lives there.</summary>
    private static TmNotionTextCommentPanel Panel(IRenderedComponent<TextDraftGuardHost> cut)
        => cut.FindComponent<TmNotionTextCommentPanel>().Instance;

    public sealed class TextDraftGuardHost : ComponentBase
    {
        [Parameter] public NotionEditorContext Context { get; set; } = default!;
        [Parameter] public bool Visible { get; set; } = true;
        [Parameter] public EventCallback<TmNotionCommentDraftState> OnDraftChanged { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", Context);
            builder.AddAttribute(2, "IsFixed", true);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TmNotionTextCommentPanel>(0);
                childBuilder.AddAttribute(1, "Visible", Visible);
                childBuilder.AddAttribute(2, "BlockId", "block-1");
                childBuilder.AddAttribute(3, "CommentId", string.Empty);
                childBuilder.AddAttribute(4, "OnDraftChanged", OnDraftChanged);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}

/// <summary>
/// Minimal in-memory provider over the BASE ITmCommentProvider contract — the Notion panels talk
/// to it through the NotionCommentProviderExtensions (GetBlockCommentsAsync / AddBlockCommentAsync /
/// ReplyToCommentAsync / EditCommentAsync are extension methods).
/// </summary>
internal sealed class BlockFakeCommentProvider :
    ITmCommentProvider,
    ITmCommentReactionProvider
{
    private readonly Dictionary<string, TmCommentThread> _comments = [];

    public List<TmCommentThread> BlockComments => _comments.Values.ToList();

    public TmCommentProviderCapabilities Capabilities =>
        TmCommentProviderCapabilities.Read
        | TmCommentProviderCapabilities.CreateThread
        | TmCommentProviderCapabilities.Reply
        | TmCommentProviderCapabilities.Delete
        | TmCommentProviderCapabilities.Resolve
        | TmCommentProviderCapabilities.Reactions
        | TmCommentProviderCapabilities.RichText;

    public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
        TmEntityRef entityRef, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TmCommentThread>>(_comments.Values.ToList());

    public Task<TmCommentThread> CreateThreadAsync(
        TmCommentThread thread, CancellationToken cancellationToken = default)
    {
        thread.Id = string.IsNullOrWhiteSpace(thread.Id) ? Guid.NewGuid().ToString("N") : thread.Id;
        foreach (var entry in thread.Entries)
        {
            entry.Id = Guid.NewGuid().ToString("N");
            entry.ThreadId = thread.Id;
            entry.Author ??= User();
            entry.CanEdit = true;
            entry.CanDelete = true;
        }

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
    {
        var comment = _comments[threadId];
        var index = comment.Entries.FindIndex(e => e.Id == entryId);
        entry.Id = entryId;
        entry.ThreadId = threadId;
        if (index >= 0)
        {
            comment.Entries[index] = entry;
        }

        return Task.FromResult(entry);
    }

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
        => Task.FromResult<IReadOnlyList<TmCommentReaction>>(FindEntry(entryId).Reactions);

    public Task AddReactionAsync(
        string entryId, string value, string userId, CancellationToken cancellationToken = default)
    {
        var entry = FindEntry(entryId);
        var reaction = entry.Reactions.SingleOrDefault(r => r.Value == value);
        if (reaction is null)
        {
            reaction = new TmCommentReaction { Value = value };
            entry.Reactions.Add(reaction);
        }

        if (!reaction.UserIds.Contains(userId))
        {
            reaction.UserIds.Add(userId);
        }

        return Task.CompletedTask;
    }

    public Task RemoveReactionAsync(
        string entryId, string value, string userId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private TmCommentEntry FindEntry(string entryId)
        => _comments.Values.SelectMany(comment => comment.Entries).Single(entry => entry.Id == entryId);

    private static TmUserRef User()
        => new() { Id = "demo", DisplayName = "Demo User" };
}
