using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Draft-guard surface of <see cref="TmChat"/> (OnDraftChanged / HasUnsavedDraft / DiscardDraftAsync).
/// The chat composer, the thread-reply input and the inline message edit all hold text the host
/// cannot observe through parameters — these tests pin the additive surface that reports it.
/// </summary>
public class TmChatDraftGuardTests : LocalizationTestBase
{
    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private static readonly ChatMessage[] OneIncoming =
    [
        new ChatMessage("m1", "Question?", Bob, ChatMessageType.Incoming),
    ];

    private static ChatMessage OwnMessage(string id, string text) =>
        new(id, text, Me, ChatMessageType.Outgoing);

    private IRenderedComponent<TmChat> RenderChat(
        IReadOnlyList<ChatMessage> messages,
        Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null,
        List<TmChatDraftState>? states = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me).Add(c => c.Messages, messages);
            if (states is not null)
            {
                p.Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmChatDraftState>(
                    this, state => states.Add(state)));
            }
            extra?.Invoke(p);
        });

    // ── Main composer ────────────────────────────────────────────────

    [Fact]
    public void Composer_Typing_FiresOnDraftChangedWithComposerDraft()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states);

        cut.Find(".tm-chat__input").Input("Hello");

        states.Should().ContainSingle().Which.Should().Be(
            new TmChatDraftState(HasComposerDraft: true, HasThreadReplyDraft: false, EditingMessageId: null, HasUnsavedContent: true));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public void Composer_DedupsNotificationsWhileDraftStateUnchanged()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states);

        // Every keystroke invokes the notification path, but the record value does not change
        // after the first character — the callback must fire once, not per keystroke.
        cut.Find(".tm-chat__input").Input("a");
        cut.Find(".tm-chat__input").Input("ab");
        cut.Find(".tm-chat__input").Input("abc");

        states.Should().ContainSingle("value-equality dedup suppresses identical states")
            .Which.HasComposerDraft.Should().BeTrue();

        // Clearing the draft changes the state again — exactly one more notification.
        cut.Find(".tm-chat__input").Input("");
        states.Should().HaveCount(2);
        states[1].Should().Be(new TmChatDraftState(false, false, null, false));
    }

    [Fact]
    public void Composer_WhitespaceOnly_IsNotADraft()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states);

        cut.Find(".tm-chat__input").Input("   ");

        states.Should().BeEmpty("whitespace-only composer text is not an unsaved draft");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task Composer_Sending_ClearsTheDraft_AndFiresClearedState()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states, extra: p => p.Add(c => c.OnSend,
            EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.Find(".tm-chat__input").Input("To send");
        cut.Find(".tm-chat__send-btn").Click();
        await cut.InvokeAsync(() => { });

        states.Should().HaveCount(2);
        states[1].Should().Be(new TmChatDraftState(false, false, null, false));
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    // ── Thread reply ─────────────────────────────────────────────────

    [Fact]
    public void ThreadReply_Typing_FiresOnDraftChangedWithThreadDraft()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states, extra: p => p.Add(c => c.OnSend,
            EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.Find("[data-testid='chat-reply-m1']").Click();
        cut.Find("[data-testid='chat-thread-input']").Input("Follow-up");

        states.Should().ContainSingle().Which.Should().Be(
            new TmChatDraftState(HasComposerDraft: false, HasThreadReplyDraft: true, EditingMessageId: null, HasUnsavedContent: true));

        cut.Find("[data-testid='chat-thread-close']").Click();
        cut.Instance.HasUnsavedDraft.Should().BeFalse("closing the thread clears its reply draft");
    }

    [Fact]
    public async Task ThreadReply_Sending_ClearsTheDraft()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states, extra: p => p.Add(c => c.OnSend,
            EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.Find("[data-testid='chat-reply-m1']").Click();
        cut.Find("[data-testid='chat-thread-input']").Input("Follow-up");
        cut.Find("[data-testid='chat-thread-send']").Click();
        await cut.InvokeAsync(() => { });

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmChatDraftState(false, false, null, false));
    }

    [Fact]
    public void ThreadReply_DanglingRootMessage_StopsBeingReported()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states, extra: p => p
            .Add(c => c.ActiveThreadRootId, "m1")
            .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.Find("[data-testid='chat-thread-input']").Input("Follow-up");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // The root message disappears from Messages (deleted/reloaded host store) — the thread
        // panel unmounts parameter-driven and its draft must leave the aggregate.
        cut.Render(p => p.Add(c => c.Messages, Array.Empty<ChatMessage>()));

        cut.Instance.HasUnsavedDraft.Should().BeFalse("the thread panel is gone with its root message");
        states.Last().Should().Be(new TmChatDraftState(false, false, null, false),
            "the parameter-driven unmount must be reported");
    }

    // ── Inline edit ──────────────────────────────────────────────────

    [Fact]
    public void Edit_OpenedWithoutChanges_IsNotAnUnsavedDraft_ButIsReported()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat([OwnMessage("m1", "Hello")], states: states, extra: p => p.Add(c => c.OnEditMessage,
            EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { })));

        cut.Find("[data-testid='chat-edit-m1']").Click();

        states.Should().ContainSingle().Which.Should().Be(
            new TmChatDraftState(HasComposerDraft: false, HasThreadReplyDraft: false, EditingMessageId: "m1", HasUnsavedContent: false),
            "opening an edit without changing anything is not an unsaved draft");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public void Edit_Typing_FiresUnsavedContent_AndReverting_ClearsIt()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat([OwnMessage("m1", "Hello")], states: states, extra: p => p.Add(c => c.OnEditMessage,
            EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { })));

        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.Find("[data-testid='chat-edit-input']").Input("Changed");

        states[^1].Should().Be(new TmChatDraftState(false, false, "m1", true));

        // Reverting to the original text clears the flag again — the comparison is stateless.
        cut.Find("[data-testid='chat-edit-input']").Input("Hello");
        states[^1].Should().Be(new TmChatDraftState(false, false, "m1", false));
    }

    [Fact]
    public async Task Edit_Commit_ClosesTheEdit_AndClearsTheDraft()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat([OwnMessage("m1", "Hello")], states: states, extra: p => p.Add(c => c.OnEditMessage,
            EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { })));

        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.Find("[data-testid='chat-edit-input']").Input("Hello v2");
        cut.Find("[data-testid='chat-edit-save']").Click();
        await cut.InvokeAsync(() => { });

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmChatDraftState(false, false, null, false));
    }

    [Fact]
    public void Edit_Cancel_ClearsTheDraft()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat([OwnMessage("m1", "Hello")], states: states, extra: p => p.Add(c => c.OnEditMessage,
            EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { })));

        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.Find("[data-testid='chat-edit-input']").Input("Changed");
        cut.Find("[data-testid='chat-edit-cancel']").Click();

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Last().Should().Be(new TmChatDraftState(false, false, null, false));
    }

    // ── DiscardDraftAsync ────────────────────────────────────────────

    [Fact]
    public async Task DiscardDraftAsync_ClearsEveryDraftSlot_AndReportsOneClearedState()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat([OwnMessage("m1", "Hello")], states: states, extra: p => p
            .Add(c => c.OnEditMessage, EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { }))
            .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.Find(".tm-chat__input").Input("composer draft");
        cut.Find("[data-testid='chat-reply-m1']").Click();
        cut.Find("[data-testid='chat-thread-input']").Input("thread draft");
        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.Find("[data-testid='chat-edit-input']").Input("edit draft");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        (cut.Find(".tm-chat__input").GetAttribute("value") ?? string.Empty).Should().BeEmpty();
        cut.FindAll("[data-testid='chat-edit-input']").Should().BeEmpty("the inline edit is cancelled");
        states.Last().Should().Be(new TmChatDraftState(false, false, null, false));
    }

    [Fact]
    public async Task DiscardDraftAsync_WithNoDraft_IsANoOp_WithoutNotification()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(OneIncoming, states: states);

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        states.Should().BeEmpty("the dedup suppresses a notification when nothing changed");
    }
}
