using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>K6: threads (reply panel), edit/delete, reactions, per-user read receipts.</summary>
public class TmChatK6Tests : LocalizationTestBase
{
    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private IRenderedComponent<TmChat> Render(IReadOnlyList<ChatMessage> messages, Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me).Add(c => c.Messages, messages);
            extra?.Invoke(p);
        });

    // ── Threads ──────────────────────────────────────────────────

    [Fact]
    public void Root_WithReplies_ShowsThreadBadge_AndHidesRepliesFromMainList()
    {
        var messages = new[]
        {
            new ChatMessage("m1", "Question?", Bob, ChatMessageType.Incoming),
            new ChatMessage("r1", "Answer", Me, ChatMessageType.Outgoing) { ThreadRootId = "m1", ReplyToId = "m1" },
        };

        var cut = Render(messages);

        // Reply is not shown in the main list (only the root). Descendant selector: since the
        // custom-template phase every message is wrapped in a .tm-chat__item carrying the
        // data-message-id anchor, so .tm-chat__message is no longer a DIRECT child.
        cut.FindAll(".tm-chat__messages .tm-chat__message").Should().HaveCount(1);
        var badge = cut.Find("[data-testid='chat-thread-open-m1']");
        badge.TextContent.Should().Contain("1");
    }

    [Fact]
    public void OpenThread_ShowsPanelWithRootAndReplies_AndSendFiresOnSendWithThreadContext()
    {
        ChatSendRequest? sent = null;
        var messages = new[]
        {
            new ChatMessage("m1", "Question?", Bob, ChatMessageType.Incoming),
            new ChatMessage("r1", "Answer", Me, ChatMessageType.Outgoing) { ThreadRootId = "m1" },
        };

        var cut = Render(messages, p => p.Add(c => c.OnSend,
            EventCallback.Factory.Create<ChatSendRequest>(this, r => sent = r)));

        cut.Find("[data-testid='chat-thread-open-m1']").Click();

        var panel = cut.Find("[data-testid='chat-thread-panel']");
        panel.TextContent.Should().Contain("Question?");
        panel.TextContent.Should().Contain("Answer");

        cut.Find("[data-testid='chat-thread-input']").Input("Follow-up");
        cut.Find("[data-testid='chat-thread-send']").Click();

        sent.Should().NotBeNull();
        sent!.Text.Should().Be("Follow-up");
        sent.ThreadRootId.Should().Be("m1");
        sent.ReplyToId.Should().Be("m1");
    }

    // ── Edit / delete ────────────────────────────────────────────

    [Fact]
    public void EditOwnMessage_CommitsViaOnEditMessage()
    {
        ChatMessageEdit? edit = null;
        var messages = new[] { new ChatMessage("m1", "Helo", Me, ChatMessageType.Outgoing) };

        var cut = Render(messages, p => p.Add(c => c.OnEditMessage,
            EventCallback.Factory.Create<ChatMessageEdit>(this, e => edit = e)));

        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.Find("[data-testid='chat-edit-input']").Input("Hello");
        cut.Find("[data-testid='chat-edit-save']").Click();

        edit.Should().NotBeNull();
        edit!.MessageId.Should().Be("m1");
        edit.NewText.Should().Be("Hello");
    }

    [Fact]
    public async Task EditCommit_ThrowsInHost_EditStaysOpenWithText()
    {
        var messages = new[] { new ChatMessage("m1", "Helo", Me, ChatMessageType.Outgoing) };
        var cut = Render(messages, p => p.Add(c => c.OnEditMessage,
            EventCallback.Factory.Create<ChatMessageEdit>(this, _ => throw new InvalidOperationException("host handler failed"))));

        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.Find("[data-testid='chat-edit-input']").Input("Hello");

        await Assert.ThrowsAsync<InvalidOperationException>(() => cut.InvokeAsync(
            () => cut.Find("[data-testid='chat-edit-save']").Click()));

        cut.Find("[data-testid='chat-edit-input']").GetAttribute("value").Should().Be("Hello",
            "the editing id is cleared only after the handler completes — a failed edit keeps the text");
        cut.FindAll("[data-testid='chat-edit-save']").Should().ContainSingle(
            "the edit stays open so the failure can be retried");
    }

    [Fact]
    public void IncomingMessage_HasNoEditOrDeleteActions()
    {
        var cut = Render(new[] { new ChatMessage("m1", "Hi", Bob, ChatMessageType.Incoming) });

        cut.FindAll("[data-testid='chat-edit-m1']").Should().BeEmpty();
        cut.FindAll("[data-testid='chat-delete-m1']").Should().BeEmpty();
    }

    [Fact]
    public void DeleteOwnMessage_FiresOnDeleteMessage()
    {
        string? deleted = null;
        var cut = Render(new[] { new ChatMessage("m1", "Oops", Me, ChatMessageType.Outgoing) },
            p => p.Add(c => c.OnDeleteMessage, EventCallback.Factory.Create<string>(this, id => deleted = id)));

        cut.Find("[data-testid='chat-delete-m1']").Click();

        deleted.Should().Be("m1");
    }

    [Fact]
    public void DeletedMessage_RendersTombstone_AndNoActions()
    {
        var cut = Render(new[] { new ChatMessage("m1", "gone", Me, ChatMessageType.Outgoing) { IsDeleted = true } });

        cut.Find("[data-testid='chat-deleted-m1']").TextContent.Should().Contain("deleted");
        cut.FindAll("[data-testid='chat-delete-m1']").Should().BeEmpty();
    }

    [Fact]
    public void EditedMessage_ShowsEditedMarker()
    {
        var cut = Render(new[]
        {
            new ChatMessage("m1", "Hello", Me, ChatMessageType.Outgoing) { EditedAt = DateTimeOffset.UtcNow }
        });

        cut.Find("[data-testid='chat-edited-m1']").TextContent.Should().Contain("edited");
    }

    // ── Reactions ────────────────────────────────────────────────

    [Fact]
    public void ExistingReactionChip_RendersAndTogglesOnClick()
    {
        ChatReactionToggle? toggled = null;
        var messages = new[]
        {
            new ChatMessage("m1", "Nice", Bob, ChatMessageType.Incoming)
            {
                Reactions = [new ChatReaction("👍", [Bob])]
            }
        };

        var cut = Render(messages, p => p.Add(c => c.OnToggleReaction,
            EventCallback.Factory.Create<ChatReactionToggle>(this, t => toggled = t)));

        var chip = cut.Find("[data-testid='chat-reaction-m1']");
        chip.TextContent.Should().Contain("1");
        chip.Click();

        toggled.Should().NotBeNull();
        toggled!.Emoji.Should().Be("👍");
        toggled.MessageId.Should().Be("m1");
    }

    [Fact]
    public void ReactButton_OpensPicker_AndEmojiFiresToggle()
    {
        ChatReactionToggle? toggled = null;
        var cut = Render(new[] { new ChatMessage("m1", "Hey", Bob, ChatMessageType.Incoming) },
            p => p.Add(c => c.OnToggleReaction,
                EventCallback.Factory.Create<ChatReactionToggle>(this, t => toggled = t)));

        cut.Find("[data-testid='chat-react-m1']").Click();
        cut.Find("[data-testid='chat-emoji-picker']").Should().NotBeNull();
        cut.Find("[data-testid='chat-emoji-m1-0']").Click();

        toggled.Should().NotBeNull();
        toggled!.MessageId.Should().Be("m1");
        toggled.Emoji.Should().Be("👍");
    }

    // ── Read receipts ────────────────────────────────────────────

    [Fact]
    public void OwnMessage_WithReadByOther_ShowsReceipts()
    {
        var cut = Render(new[]
        {
            new ChatMessage("m1", "Seen?", Me, ChatMessageType.Outgoing)
            {
                ReadBy = [new ChatReadReceipt(Bob)]
            }
        });

        var receipts = cut.Find("[data-testid='chat-receipts-m1']");
        receipts.TextContent.Should().Contain("Bob");
    }

    [Fact]
    public void IncomingUnreadMessage_FiresOnMessageRead()
    {
        var read = new List<string>();
        Render(new[] { new ChatMessage("m1", "Hi", Bob, ChatMessageType.Incoming) },
            p => p.Add(c => c.OnMessageRead, EventCallback.Factory.Create<string>(this, id => read.Add(id))));

        read.Should().Contain("m1");
    }

    [Fact]
    public void OnMessageRead_HandlerMutatingMessageList_DoesNotThrow()
    {
        // Regression: a host whose read handler mutates the SAME list the component was
        // handed (a shared conversation store) must not trip "collection modified" while
        // TmChat enumerates messages for read receipts during OnAfterRenderAsync.
        var messages = new List<ChatMessage>
        {
            new("m1", "Hi", Bob, ChatMessageType.Incoming),
            new("m2", "Yo", Bob, ChatMessageType.Incoming),
        };
        var addedOnce = false;

        var act = () => Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.Messages, messages)
            .Add(c => c.OnMessageRead, EventCallback.Factory.Create<string>(this, _ =>
            {
                if (addedOnce) return;
                addedOnce = true;
                messages.Add(new ChatMessage("m3", "auto", Bob, ChatMessageType.Incoming));
            })));

        act.Should().NotThrow();
    }

    [Fact]
    public void AlreadyReadIncomingMessage_DoesNotFireOnMessageRead()
    {
        var read = new List<string>();
        Render(new[]
        {
            new ChatMessage("m1", "Hi", Bob, ChatMessageType.Incoming) { ReadBy = [new ChatReadReceipt(Me)] }
        }, p => p.Add(c => c.OnMessageRead, EventCallback.Factory.Create<string>(this, id => read.Add(id))));

        read.Should().BeEmpty();
    }
}
