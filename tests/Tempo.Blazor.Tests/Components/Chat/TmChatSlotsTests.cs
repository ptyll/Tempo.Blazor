using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Slot contract of <see cref="TmChat"/> (FZE-2): the host may replace the composer
/// (<c>ComposerTemplate</c>), enrich the built-in composer (<c>ComposerAddon</c> above the input,
/// <c>ComposerFooter</c> below it) or drop it entirely (<c>ShowComposer=false</c>), put a
/// connection banner above the messages (<c>BannerContent</c>), take over the empty state
/// (<c>EmptyContent</c>) and the top of the component (<c>HeaderContent</c>), and hide the
/// built-in avatars (<c>ShowAvatars=false</c>). The pin tests freeze the feature switches
/// (<c>EnableReactions</c>, <c>EnableThreads</c>, <c>AllowEditDelete</c>,
/// <c>ShowReadReceipts</c>, <c>AutoReadReceipts</c>): switching one off must leave NOTHING of
/// that feature in the DOM, even with all handlers wired.
/// </summary>
public class TmChatSlotsTests : LocalizationTestBase
{
    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private static readonly DateTimeOffset Ts = new(2026, 5, 2, 10, 30, 0, TimeSpan.Zero);

    private static ChatMessage Incoming(string id, string text, string? kind = null) =>
        new(id, text, Bob, ChatMessageType.Incoming, Ts) { Kind = kind };

    private static ChatMessage Own(string id, string text, string? kind = null) =>
        new(id, text, Me, ChatMessageType.Outgoing, Ts) { Kind = kind };

    private IRenderedComponent<TmChat> RenderChat(
        IReadOnlyList<ChatMessage> messages,
        Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me).Add(c => c.Messages, messages);
            extra?.Invoke(p);
        });

    /// <summary>A composer template rendering a host-owned marker element and capturing its context.</summary>
    private static RenderFragment<ChatComposerContext> MarkerComposer(
        string marker, Action<ChatComposerContext>? onRender = null) => context =>
    {
        onRender?.Invoke(context);
        return builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", marker);
            builder.AddContent(2, $"{marker}-body");
            builder.CloseElement();
        };
    };

    private static RenderFragment MarkerFragment(string marker) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", marker);
        builder.AddContent(2, $"{marker}-body");
        builder.CloseElement();
    };

    // ── ComposerTemplate replaces the built-in composer ──────────────────────

    [Fact]
    public void ComposerTemplate_ReplacesBuiltInComposer()
    {
        ChatComposerContext? received = null;

        var cut = RenderChat([], p => p.Add(
            c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx)));

        cut.FindAll(".tm-chat__input").Should().BeEmpty("the built-in composer is fully replaced");
        cut.FindAll(".tm-chat__send-btn").Should().BeEmpty();
        cut.Find(".my-composer").Should().NotBeNull("the host's composer slot markup renders");
        received.Should().NotBeNull("the template receives a live ChatComposerContext");
    }

    [Fact]
    public void ComposerTemplate_SuppressesComposerAddonAndFooter()
    {
        var cut = RenderChat([], p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.ComposerAddon, MarkerFragment("composer-addon-note"))
            .Add(c => c.ComposerFooter, MarkerFragment("composer-footer-hint")));

        cut.Find(".my-composer").Should().NotBeNull();
        cut.FindAll(".composer-addon-note").Should().BeEmpty(
            "the template owns the whole composer area — the addon does not render");
        cut.FindAll(".composer-footer-hint").Should().BeEmpty(
            "the template owns the whole composer area — the footer does not render");
    }

    [Fact]
    public void ComposerTemplate_MarksTheInputAreaAsCustom()
    {
        var cut = RenderChat([], p => p.Add(c => c.ComposerTemplate, MarkerComposer("my-composer")));

        var area = cut.Find(".tm-chat__input-area");
        area.ClassList.Should().Contain("tm-chat__input-area--custom",
            "the wrapper (padding, border) stays but stacks the host's markup vertically");
        area.ClassList.Should().NotContain("tm-chat__input-area--stacked",
            "the stacked variant is for the built-in composer with addon/footer only");
    }

    [Fact]
    public void ComposerContext_InputId_MatchesBuiltInTextareaId()
    {
        var cut = RenderChat([]);

        var textareaId = cut.Find(".tm-chat__input").GetAttribute("id");
        textareaId.Should().NotBeNullOrEmpty("the built-in textarea carries the id in the DOM");
        cut.Instance.ComposerContext.InputId.Should().Be(textareaId,
            "the context exposes exactly the id the built-in textarea renders");
    }

    [Fact]
    public async Task ComposerTemplate_Context_SendAsync_FiresOnSendAndOnSendMessage()
    {
        ChatComposerContext? received = null;
        string? sentText = null;
        ChatSendRequest? request = null;

        var cut = RenderChat([], p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.OnSendMessage, EventCallback.Factory.Create<string>(this, t => sentText = t))
            .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, r => request = r)));

        await received!.SetTextAsync("  Hello custom composer  ");
        await received.SendAsync();

        sentText.Should().Be("Hello custom composer", "the text is trimmed on the same path as the built-in composer");
        request.Should().NotBeNull();
        request!.Text.Should().Be("Hello custom composer");
        received.Text.Should().BeEmpty("the composer text clears after send");
        received.IsSubmitting.Should().BeFalse("the send completed synchronously");
    }

    [Fact]
    public async Task ComposerTemplate_Context_CanSend_RespectsDisabledAndSubmitting()
    {
        ChatComposerContext? received = null;

        var cut = RenderChat([], p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.Disabled, true));

        received!.CanSend.Should().BeFalse("Disabled=true blocks sending");
        received.Disabled.Should().BeTrue();
        received.Text.Should().BeEmpty("empty text cannot send");

        cut.Render(p => p.Add(c => c.Disabled, false));
        await received.SetTextAsync("Hello");
        received.CanSend.Should().BeTrue();
        received.Text.Should().Be("Hello");

        cut.Render(p => p.Add(c => c.Disabled, true));
        received.CanSend.Should().BeFalse("the live context mirrors a later Disabled change");
    }

    [Fact]
    public async Task ComposerTemplate_Context_CanSend_FalseWhileSubmitting()
    {
        ChatComposerContext? received = null;
        var gate = new TaskCompletionSource();

        var cut = RenderChat([], p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => gate.Task)));

        await received!.SetTextAsync("In flight");
        var sendTask = received.SendAsync();

        received.IsSubmitting.Should().BeTrue("the send is awaiting the host handler");
        received.CanSend.Should().BeFalse("IsSubmitting blocks CanSend");

        gate.SetResult();
        await sendTask;

        received.IsSubmitting.Should().BeFalse("the send completed");
        received.Text.Should().BeEmpty("the composer text cleared on send");
        await received.SetTextAsync("Again");
        received.CanSend.Should().BeTrue("a new send is allowed once text arrives after the completed send");
    }

    [Fact]
    public async Task ComposerTemplate_Context_CanSend_RecoversAfterHostHandlerThrows()
    {
        ChatComposerContext? received = null;

        var cut = RenderChat([], p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.OnSendMessage, EventCallback.Factory.Create<string>(this, _ => { }))
            .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(
                this, _ => throw new InvalidOperationException("host handler failed"))));

        await received!.SetTextAsync("Hello");
        await Assert.ThrowsAsync<InvalidOperationException>(() => received.SendAsync());

        received.IsSubmitting.Should().BeFalse(
            "the finally resets the submit flag even when the host handler throws");
        // FZE-5: the composer text is cleared only AFTER a successful send — a failed send
        // keeps the draft (pinned by TmChatComposerTests.Composer_SendFailsInHost_TextIsNotLost).
        received.CanSend.Should().BeTrue("the text is preserved when the host handler throws");
        received.Text.Should().Be("Hello", "a failed send must not swallow the user's text");

        await received.SetTextAsync("Again");
        received.CanSend.Should().BeTrue("CanSend recovers once text is present again");
    }

    // ── Composer context: FocusAsync ─────────────────────────────────────────

    [Fact]
    public async Task ComposerContext_FocusAsync_FocusesBuiltInTextarea()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/chat.js");
        var cut = RenderChat([]);

        await cut.Instance.ComposerContext.FocusAsync();

        module.Invocations.Should().Contain(i => i.Identifier == "focusIfNeeded"
            && i.Arguments.Count >= 1
            && i.Arguments[0] is ElementReference,
            "the built-in textarea is rendered, so the context focuses it through the module's focusIfNeeded");
    }

    [Fact]
    public async Task FocusAsync_NoOp_AfterShowComposerToggledOff()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/chat.js");
        var cut = RenderChat([]);
        cut.Render(p => p.Add(c => c.ShowComposer, false));

        var act = async () => await cut.Instance.ComposerContext.FocusAsync();

        await act.Should().NotThrowAsync("the guard is a silent no-op without the built-in textarea");
        module.Invocations.Should().NotContain(i => i.Identifier == "focusIfNeeded");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "Blazor._internal.domWrapper.focus");
    }

    // ── ShowComposer / ComposerAddon / ComposerFooter ────────────────────────

    [Fact]
    public void ShowComposer_False_RendersNoComposer()
    {
        var cut = RenderChat([Incoming("m1", "Hello")], p => p.Add(c => c.ShowComposer, false));

        cut.FindAll(".tm-chat__input-area").Should().BeEmpty();
        cut.FindAll(".tm-chat__input").Should().BeEmpty();
        cut.FindAll(".tm-chat__send-btn").Should().BeEmpty();
        cut.FindAll(".tm-chat__messages").Should().HaveCount(1, "only the composer is dropped");
    }

    [Fact]
    public void ShowComposer_False_IgnoresComposerTemplate()
    {
        var cut = RenderChat([], p => p
            .Add(c => c.ShowComposer, false)
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer")));

        cut.FindAll(".my-composer").Should().BeEmpty("ShowComposer=false wins over the template");
        cut.FindAll(".tm-chat__input-area").Should().BeEmpty();
    }

    [Fact]
    public void ComposerAddon_RendersAboveBuiltInComposer()
    {
        var cut = RenderChat([], p => p.Add(c => c.ComposerAddon, MarkerFragment("composer-addon-note")));

        cut.Find(".composer-addon-note").Should().NotBeNull("the owner note renders");
        var inputArea = cut.Find(".tm-chat__input-area");
        inputArea.InnerHtml.IndexOf("composer-addon-note", StringComparison.Ordinal).Should()
            .BeLessThan(inputArea.InnerHtml.IndexOf("tm-chat__input", StringComparison.Ordinal),
                "the addon renders above the built-in input");
        inputArea.InnerHtml.IndexOf("composer-addon-note", StringComparison.Ordinal).Should()
            .BeLessThan(inputArea.InnerHtml.IndexOf("tm-chat__send-btn", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposerFooter_RendersBelowBuiltInComposer()
    {
        var cut = RenderChat([], p => p.Add(c => c.ComposerFooter, MarkerFragment("composer-footer-hint")));

        cut.Find(".composer-footer-hint").Should().NotBeNull();
        var inputArea = cut.Find(".tm-chat__input-area");
        inputArea.InnerHtml.IndexOf("tm-chat__input", StringComparison.Ordinal).Should()
            .BeLessThan(inputArea.InnerHtml.IndexOf("composer-footer-hint", StringComparison.Ordinal),
                "the footer renders below the built-in input");
    }

    [Fact]
    public void ComposerAddon_StacksTheComposerArea_InputRowBelowTheNote()
    {
        var cut = RenderChat([], p => p.Add(c => c.ComposerAddon, MarkerFragment("composer-addon-note")));

        var area = cut.Find(".tm-chat__input-area");
        area.ClassList.Should().Contain("tm-chat__input-area--stacked",
            "the addon stacks the composer area vertically, not into the input's flex row");
        cut.Find(".tm-chat__input-row").Should().NotBeNull("the input row keeps the horizontal textarea + send layout");
        area.InnerHtml.IndexOf("composer-addon-note", StringComparison.Ordinal).Should()
            .BeLessThan(area.InnerHtml.IndexOf("tm-chat__input-row", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposerFooter_StacksTheComposerArea_InputRowAboveTheHint()
    {
        var cut = RenderChat([], p => p.Add(c => c.ComposerFooter, MarkerFragment("composer-footer-hint")));

        var area = cut.Find(".tm-chat__input-area");
        area.ClassList.Should().Contain("tm-chat__input-area--stacked");
        area.InnerHtml.IndexOf("tm-chat__input-row", StringComparison.Ordinal).Should()
            .BeLessThan(area.InnerHtml.IndexOf("composer-footer-hint", StringComparison.Ordinal));
    }

    [Fact]
    public void Composer_Default_KeepsFlatInputArea()
    {
        var cut = RenderChat([]);

        var area = cut.Find(".tm-chat__input-area");
        area.ClassList.Should().NotContain("tm-chat__input-area--stacked", "the default composer keeps today's layout");
        cut.FindAll(".tm-chat__input-row").Should().BeEmpty("no row wrapper is added without slot content");
    }

    [Fact]
    public void ComposerDescribedBy_RendersAriaDescribedByOnBuiltInTextarea()
    {
        var cut = RenderChat([], p => p.Add(c => c.ComposerDescribedBy, "gate-hint gate-error"));

        cut.Find(".tm-chat__input").GetAttribute("aria-describedby").Should()
            .Be($"gate-hint gate-error {cut.Instance.ComposerInputId}-hint",
                "the component's own submit hint id is merged after the host's ids");

        cut.Render(p => p.Add(c => c.ComposerDescribedBy, (string?)null));
        cut.Find(".tm-chat__input").GetAttribute("aria-describedby").Should()
            .Be($"{cut.Instance.ComposerInputId}-hint",
                "without host ids the hint id is the only aria-describedby target");
    }

    // ── Banner / Header / Empty slots ────────────────────────────────────────

    [Fact]
    public void BannerContent_RendersAboveMessages()
    {
        var cut = RenderChat(
            [Incoming("m1", "Hello")],
            p => p.Add(c => c.BannerContent, MarkerFragment("connection-banner")));

        cut.Find(".connection-banner").Should().NotBeNull();
        var root = cut.Find(".tm-chat");
        root.InnerHtml.IndexOf("connection-banner", StringComparison.Ordinal).Should()
            .BeLessThan(root.InnerHtml.IndexOf("tm-chat__messages", StringComparison.Ordinal),
                "the banner renders above the message list");
    }

    [Fact]
    public void BannerContent_IsNotALiveRegion()
    {
        var cut = RenderChat([], p => p.Add(c => c.BannerContent, MarkerFragment("connection-banner")));

        cut.Find(".connection-banner").GetAttribute("aria-live").Should().BeNull(
            "the banner wrapper must not become a second live region (rule: exactly one aria-live in TmChat)");
        cut.Find(".connection-banner").GetAttribute("role").Should().BeNull(
            "role=\"status\" belongs to the host's banner content, not to the wrapper");
    }

    [Fact]
    public void EmptyContent_ReplacesBuiltInEmptyState()
    {
        var cut = RenderChat([], p => p.Add(c => c.EmptyContent, MarkerFragment("my-empty-state")));

        cut.FindAll(".tm-chat__empty").Should().BeEmpty("the built-in empty state is replaced");
        cut.Find(".my-empty-state").Should().NotBeNull("the host's empty state renders");
    }

    [Fact]
    public void EmptyContent_NotUsed_KeepsBuiltInEmptyState()
    {
        var cut = RenderChat([Incoming("m1", "Hello")], p => p.Add(c => c.EmptyContent, MarkerFragment("my-empty-state")));

        cut.FindAll(".my-empty-state").Should().BeEmpty("the slot only applies to an empty conversation");
        cut.FindAll(".tm-chat__message").Should().HaveCount(1);
    }

    [Fact]
    public void HeaderContent_RendersAtTop()
    {
        var cut = RenderChat(
            [Incoming("m1", "Hello")],
            p => p.Add(c => c.HeaderContent, MarkerFragment("my-chat-header")));

        cut.Find(".my-chat-header").Should().NotBeNull();
        var root = cut.Find(".tm-chat");
        root.InnerHtml.IndexOf("my-chat-header", StringComparison.Ordinal).Should()
            .BeLessThan(root.InnerHtml.IndexOf("tm-chat__messages", StringComparison.Ordinal),
                "the header renders at the very top");
    }

    // ── Composer text flows through the context into the draft guard ─────────

    [Fact]
    public async Task ComposerTemplate_TextViaContext_ReportsComposerDraft()
    {
        ChatComposerContext? received = null;
        TmChatDraftState? lastState = null;

        var cut = RenderChat([], p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmChatDraftState>(this, s => lastState = s)));

        await received!.SetTextAsync("WIP reply");

        lastState.Should().NotBeNull("SetTextAsync is a draft transition");
        lastState!.HasComposerDraft.Should().BeTrue("custom-composer text flows through the context into the draft guard");
        lastState.HasUnsavedContent.Should().BeTrue();
        cut.Instance.HasUnsavedDraft.Should().BeTrue("the draft guard keeps working for a replaced composer");

        await received.SendAsync();

        lastState.HasComposerDraft.Should().BeFalse("sending clears the composer draft");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    // ── ShowAvatars ──────────────────────────────────────────────────────────

    [Fact]
    public void ShowAvatars_DefaultTrue_RendersAvatars()
    {
        var cut = RenderChat([Incoming("m1", "Hello"), Own("m2", "Hi")]);

        cut.FindAll(".tm-chat__avatar").Should().HaveCount(2, "the default keeps today's behavior");
    }

    [Fact]
    public void ShowAvatars_False_HidesAvatarsInBubbles()
    {
        var cut = RenderChat(
            [Incoming("m1", "Hello"), Own("m2", "Hi")],
            p => p.Add(c => c.ShowAvatars, false));

        cut.FindAll(".tm-chat__avatar").Should().BeEmpty();
        cut.FindAll(".tm-chat__bubble").Should().HaveCount(2, "the messages themselves still render");
    }

    // ── Feature switches: pin tests (nothing of the feature stays in the DOM) ─

    [Fact]
    public void EnableReactions_False_NoReactionBarNoPickerNoReactButton()
    {
        var bobReactions = new[] { new ChatReaction("👍", [Bob]) };
        var cut = RenderChat(
            [Incoming("m1", "Hello") with { Reactions = bobReactions }],
            p => p
                .Add(c => c.EnableReactions, false)
                .Add(c => c.OnToggleReaction, EventCallback.Factory.Create<ChatReactionToggle>(this, _ => { })));

        cut.FindAll(".tm-chat__reaction").Should().BeEmpty("no reaction chips");
        cut.FindAll(".tm-chat__emoji-picker").Should().BeEmpty("no emoji picker");
        cut.FindAll("[data-testid^='chat-react-']").Should().BeEmpty("no react button");
    }

    [Fact]
    public void EnableThreads_False_NoThreadBadgeNoReplyButtonNoPanel_RepliesInline()
    {
        var cut = RenderChat(
            new[]
            {
                Incoming("m1", "Root"),
                new ChatMessage("m2", "A reply", Me, ChatMessageType.Outgoing, Ts) { ThreadRootId = "m1" },
            },
            p => p
                .Add(c => c.EnableThreads, false)
                .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.FindAll(".tm-chat__thread-badge").Should().BeEmpty("no thread badge");
        cut.FindAll("[data-testid^='chat-reply-']").Should().BeEmpty("no reply button");
        cut.FindAll(".tm-chat__thread-panel").Should().BeEmpty("no thread panel");
        cut.FindAll(".tm-chat__item").Should().HaveCount(2, "replies render inline in the root list");
        cut.Find(".tm-chat__messages").TextContent.Should().Contain("A reply");
    }

    [Fact]
    public void AllowEditDelete_False_NoEditNoDeleteButtons()
    {
        var cut = RenderChat(
            [Own("m1", "Mine")],
            p => p
                .Add(c => c.AllowEditDelete, false)
                .Add(c => c.OnEditMessage, EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { }))
                .Add(c => c.OnDeleteMessage, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.FindAll("[data-testid^='chat-edit-']").Should().BeEmpty("no edit button");
        cut.FindAll("[data-testid^='chat-delete-']").Should().BeEmpty("no delete button");
    }

    [Fact]
    public void ShowReadReceipts_False_NoReceipts()
    {
        var cut = RenderChat(
            [Own("m1", "Mine") with { ReadBy = [new ChatReadReceipt(Bob, Ts)] }],
            p => p.Add(c => c.ShowReadReceipts, false));

        cut.FindAll(".tm-chat__receipts").Should().BeEmpty();
        cut.FindAll("[data-testid^='chat-receipts-']").Should().BeEmpty();
    }

    [Fact]
    public void AutoReadReceipts_False_NeverFiresOnMessageRead()
    {
        var fired = new List<string>();

        RenderChat(
            [Incoming("m1", "Hello")],
            p => p
                .Add(c => c.AutoReadReceipts, false)
                .Add(c => c.OnMessageRead, EventCallback.Factory.Create<string>(this, id => fired.Add(id))));

        fired.Should().BeEmpty("AutoReadReceipts=false never reports reads");
    }

    [Fact]
    public void AllBuiltInsOff_RendersNoActionsContainer()
    {
        var bobReactions = new[] { new ChatReaction("👍", [Bob]) };
        var cut = RenderChat(
            new[]
            {
                Incoming("m1", "Hello") with { Reactions = bobReactions },
                Own("m2", "Mine") with { ReadBy = [new ChatReadReceipt(Bob, Ts)] },
                new ChatMessage("m3", "A reply", Me, ChatMessageType.Outgoing, Ts) { ThreadRootId = "m1" },
            },
            p => p
                .Add(c => c.EnableReactions, false)
                .Add(c => c.EnableThreads, false)
                .Add(c => c.AllowEditDelete, false)
                .Add(c => c.ShowReadReceipts, false)
                .Add(c => c.AutoReadReceipts, false)
                .Add(c => c.OnToggleReaction, EventCallback.Factory.Create<ChatReactionToggle>(this, _ => { }))
                .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { }))
                .Add(c => c.OnEditMessage, EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { }))
                .Add(c => c.OnDeleteMessage, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.FindAll(".tm-chat__actions").Should().BeEmpty(
            "with every built-in interaction switched off the actions container is not in the DOM at all");
        cut.FindAll(".tm-chat__reaction").Should().BeEmpty();
        cut.FindAll(".tm-chat__emoji-picker").Should().BeEmpty();
        cut.FindAll(".tm-chat__thread-badge").Should().BeEmpty();
        cut.FindAll(".tm-chat__thread-panel").Should().BeEmpty();
        cut.FindAll(".tm-chat__receipts").Should().BeEmpty();
        cut.FindAll("[data-testid^='chat-edit-']").Should().BeEmpty();
        cut.FindAll("[data-testid^='chat-delete-']").Should().BeEmpty();
        cut.FindAll(".tm-chat__message").Should().HaveCount(3, "the messages still render");
    }
}
