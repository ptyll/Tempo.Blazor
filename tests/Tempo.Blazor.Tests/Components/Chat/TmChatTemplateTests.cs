using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Custom-template contract of <see cref="TmChat"/>: a host-supplied
/// <c>MessageTemplate</c> (with the optional <c>UseTemplateFor</c> filter) may replace the built-in
/// message row for any message while TmChat keeps doing everything "around" the message — the
/// <c>data-message-id</c> wrapper (scroll anchor + read receipts), the thread filter, the
/// draft guard and the built-in rendering for messages the host does not take over.
/// </summary>
public class TmChatTemplateTests : LocalizationTestBase
{
    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private static readonly DateTimeOffset Ts = new(2026, 5, 2, 10, 30, 0, TimeSpan.Zero);

    private static ChatMessage Incoming(string id, string text, string? kind = null) =>
        new(id, text, Bob, ChatMessageType.Incoming, Ts) { Kind = kind };

    private static ChatMessage Own(string id, string text, string? kind = null) =>
        new(id, text, Me, ChatMessageType.Outgoing, Ts) { Kind = kind };

    /// <summary>A template that records every context it receives and renders a host bubble span.</summary>
    private static RenderFragment<ChatMessageContext> CapturingTemplate(List<ChatMessageContext> captured) =>
        context => builder =>
        {
            captured.Add(context);
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "my-template-bubble");
            builder.AddContent(2, context.Message.Text);
            builder.CloseElement();
        };

    private IRenderedComponent<TmChat> RenderChat(
        IReadOnlyList<ChatMessage> messages,
        Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me).Add(c => c.Messages, messages);
            extra?.Invoke(p);
        });

    // ── Template replaces the built-in bubble ────────────────────

    [Fact]
    public void MessageTemplate_WhenSet_RendersTemplateInsteadOfBuiltInBubble()
    {
        var messages = new[]
        {
            Incoming("m1", "Hello"),
            Own("m2", "Hi"),
        };

        var cut = RenderChat(messages, p => p.Add(
            c => c.MessageTemplate, CapturingTemplate([])));

        cut.FindAll(".my-template-bubble").Count.Should().Be(2);
        cut.FindAll(".tm-chat__bubble").Should().BeEmpty("the built-in row is fully replaced");
    }

    [Fact]
    public void MessageTemplate_ReceivesContext_IsOwnInThreadIndex()
    {
        var captured = new List<ChatMessageContext>();
        var messages = new[]
        {
            Incoming("m1", "Hello"),
            Own("m2", "Hi"),
        };

        RenderChat(messages, p => p.Add(c => c.MessageTemplate, CapturingTemplate(captured)));

        captured.Should().HaveCount(2);
        captured[0].Message.Id.Should().Be("m1");
        captured[0].Index.Should().Be(0);
        captured[0].IsOwn.Should().BeFalse("Bob's message is not the current user's");
        captured[0].InThread.Should().BeFalse();
        captured[1].Message.Id.Should().Be("m2");
        captured[1].Index.Should().Be(1);
        captured[1].IsOwn.Should().BeTrue();
        captured[1].InThread.Should().BeFalse();
    }

    [Fact]
    public void MessageTemplate_DefaultContent_RendersBuiltInRow()
    {
        var messages = new[]
        {
            Incoming("m1", "Hello"),
            Own("m2", "Hi"),
        };

        var plain = RenderChat(messages);
        var templated = RenderChat(messages, p => p.Add(
            c => c.MessageTemplate, (ChatMessageContext ctx) => ctx.DefaultContent));

        templated.Find(".tm-chat__messages").InnerHtml.Should().Be(
            plain.Find(".tm-chat__messages").InnerHtml,
            "a template returning DefaultContent renders exactly the built-in row");
    }

    [Fact]
    public void MessageTemplate_PayloadAs_ReturnsTypedHostDto()
    {
        var dto = new HostMessageDto("d-1", "Draft reply", "policy-violation");
        ChatMessageContext? received = null;
        RenderFragment<ChatMessageContext> template = ctx => builder =>
        {
            received = ctx;
            builder.AddContent(0, ctx.PayloadAs<HostMessageDto>()!.Text);
        };

        var messages = new[] { Incoming("m1", "typed") with { Payload = dto } };
        RenderChat(messages, p => p.Add(c => c.MessageTemplate, template));

        received.Should().NotBeNull();
        received!.PayloadAs<HostMessageDto>().Should().Be(dto);
        received.PayloadAs<OtherHostDto>().Should().BeNull("a foreign DTO type does not cast");
    }

    [Fact]
    public void MessageTemplate_GetMetadata_TypedValueAndFallback()
    {
        ChatMessageContext? received = null;
        RenderFragment<ChatMessageContext> template = ctx => builder =>
        {
            received = ctx;
            builder.AddContent(0, ctx.Message.Text);
        };

        var messages = new[]
        {
            Incoming("m1", "meta") with
            {
                Metadata = new Dictionary<string, object?> { ["reason"] = "policy", ["price"] = 9.5 },
            }
        };
        RenderChat(messages, p => p.Add(c => c.MessageTemplate, template));

        received!.GetMetadata<string>("reason").Should().Be("policy");
        received.GetMetadata<string>("missing", "fallback").Should().Be("fallback");
        received.GetMetadata<string>("price").Should().BeNull("no implicit conversion across types");
        received.GetMetadata<double>("price").Should().Be(9.5);
    }

    [Fact]
    public void MessageTemplate_KeepsDataMessageIdWrapper()
    {
        var messages = new[]
        {
            Incoming("m1", "Hello"),
            Own("m2", "Hi"),
        };

        var cut = RenderChat(messages, p => p.Add(
            c => c.MessageTemplate, CapturingTemplate([])));

        cut.FindAll("[data-message-id]").Should().HaveCount(2,
            "every message keeps its wrapper anchor for scroll and read receipts");
        cut.Find(".tm-chat__item[data-message-id='m1']").Should().NotBeNull();
        cut.Find(".tm-chat__item[data-message-id='m2']").Should().NotBeNull();
    }

    [Fact]
    public void MessageTemplate_WrapperCarriesDataKind()
    {
        var messages = new[] { Incoming("m1", "draft", kind: "ai-draft") };

        var cut = RenderChat(messages, p => p.Add(
            c => c.MessageTemplate, CapturingTemplate([])));

        cut.Find(".tm-chat__item[data-message-id='m1']")
            .GetAttribute("data-kind").Should().Be("ai-draft");
    }

    [Fact]
    public void MessageTemplate_NotSet_RendersExactlyAsBefore()
    {
        // Non-breaking guarantee pinned on the system-message scenario from TmChatTests:
        // the built-in markup keeps its classes and content, wrapped in a neutral item div.
        var messages = new[]
        {
            new ChatMessage("m1", "Bob joined the conversation", type: ChatMessageType.System, timestamp: Ts),
        };

        var cut = RenderChat(messages);

        // role="region" + an accessible name + tabindex="0" (FZE-7): a scrollable region must be
        // keyboard-focusable, and role=log is forbidden (it would imply a second live region).
        cut.Find(".tm-chat__messages").MarkupMatches(
            @"<div class=""tm-chat__messages"" role=""region"" aria-label=""Messages"" tabindex=""0"">
                <div class=""tm-chat__item"" data-message-id=""m1"">
                    <div class=""tm-chat__message tm-chat__message--system"">
                        <span class=""tm-chat__system-text"">Bob joined the conversation</span>
                    </div>
                </div>
            </div>");
    }

    // ── UseTemplateFor: override only special kinds ──────────────

    [Fact]
    public void UseTemplateFor_TrueOnlyForMatchingMessages()
    {
        var messages = new[]
        {
            Incoming("m1", "Draft", kind: "ai-draft"),
            Incoming("m2", "Plain"),
        };

        var cut = RenderChat(messages, p => p
            .Add(c => c.MessageTemplate, CapturingTemplate([]))
            .Add(c => c.UseTemplateFor, (Func<ChatMessage, bool>)(m => m.Kind == "ai-draft")));

        cut.FindAll(".my-template-bubble").Should().ContainSingle("only the ai-draft message is templated");
        cut.FindAll(".tm-chat__bubble").Should().ContainSingle("the plain message keeps the built-in row");
    }

    // ── Everything TmChat does "around" the message keeps working ──

    [Fact]
    public void MessageTemplate_WithAutoReadReceipts_StillFiresOnMessageRead()
    {
        var read = new List<string>();
        RenderChat(
            new[] { Incoming("m1", "Hello") },
            p => p.Add(c => c.MessageTemplate, CapturingTemplate([]))
                  .Add(c => c.OnMessageRead, EventCallback.Factory.Create<string>(this, read.Add)));

        read.Should().Contain("m1");
    }

    [Fact]
    public void MessageTemplate_Threads_RepliesStayFilteredFromRootSequence()
    {
        var captured = new List<ChatMessageContext>();
        var messages = new[]
        {
            Incoming("m1", "Question?"),
            Own("r1", "Answer") with { ThreadRootId = "m1", ReplyToId = "m1" },
        };

        var cut = RenderChat(messages, p => p
            .Add(c => c.MessageTemplate, CapturingTemplate(captured))
            .Add(c => c.ActiveThreadRootId, "m1"));

        captured.Where(c => !c.InThread).Should().ContainSingle("the reply stays out of the root sequence")
            .Which.Message.Id.Should().Be("m1");
        var threadContexts = captured.Where(c => c.InThread).ToList();
        threadContexts.Should().HaveCount(2, "the thread panel renders the root and the reply");
        threadContexts[0].Message.Id.Should().Be("m1");
        threadContexts[0].Index.Should().Be(0);
        threadContexts[0].IsFirstInGroup.Should().BeTrue();
        threadContexts[1].Message.Id.Should().Be("r1");
        threadContexts[1].Index.Should().Be(1);
    }

    [Fact]
    public void MessageTemplate_DraftGuard_ComposerTypingStillReportsDraft()
    {
        var states = new List<TmChatDraftState>();
        var cut = RenderChat(
            new[] { Incoming("m1", "Hello") },
            p => p.Add(c => c.MessageTemplate, CapturingTemplate([]))
                  .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmChatDraftState>(
                      this, states.Add)));

        cut.Find(".tm-chat__input").Input("Hello");

        states.Should().ContainSingle().Which.HasComposerDraft.Should().BeTrue();
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    private sealed record HostMessageDto(string Id, string Text, string PolicyReason);

    private sealed record OtherHostDto(string Id);
}
