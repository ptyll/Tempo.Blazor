using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Contract of <see cref="TmChatBubble"/> (FZE-3): the presentation bubble a host renders inside
/// a <c>TmChat.MessageTemplate</c> (or standalone). Frozen behaviour:
/// every <see cref="ChatBubbleVariant"/> emits its <c>tm-chat-bubble--{variant}</c> modifier
/// class and a <c>data-variant</c> hook; the <c>Badge</c>, <c>Meta</c>, <c>Note</c> and
/// <c>Actions</c> slots wrap host content;
/// <see cref="ChatBubbleVariant.Discarded"/> strikes its text through with a SEMANTIC
/// <c>&lt;del&gt;</c> element (never CSS-only); <see cref="ChatBubbleVariant.Locked"/> never
/// renders <c>ChildContent</c> — locked content must not leak into the DOM, while the price
/// badge, note and unlock actions still render; the meta row renders a machine-readable
/// <c>&lt;time datetime="…"&gt;</c> when <c>Timestamp</c> is given and separates
/// <c>ActorLabel</c> from the time with an <c>aria-hidden</c> middle dot. A set
/// <c>Status</c>-style <see cref="TmChatBubble.StatusLabel"/> renders FIRST as a visually-hidden
/// span, and <see cref="TmChatBubble.ShowVariantIcon"/> decorates Queued/Locked with a
/// redundant icon in the badge area. The component carries NO domain copy —
/// every label (draft, scheduled, discard reason, price) comes from the host.
/// </summary>
public class TmChatBubbleTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Ts = new(2026, 5, 2, 10, 30, 0, TimeSpan.Zero);

    private IRenderedComponent<TmChatBubble> RenderBubble(
        Action<ComponentParameterCollectionBuilder<TmChatBubble>>? extra = null)
        => Render<TmChatBubble>(p => extra?.Invoke(p));

    // ── Variant modifier classes ─────────────────────────────────────────────

    [Theory]
    [InlineData(ChatBubbleVariant.Incoming, "tm-chat-bubble--incoming")]
    [InlineData(ChatBubbleVariant.Outgoing, "tm-chat-bubble--outgoing")]
    [InlineData(ChatBubbleVariant.System, "tm-chat-bubble--system")]
    [InlineData(ChatBubbleVariant.Pill, "tm-chat-bubble--pill")]
    [InlineData(ChatBubbleVariant.Pending, "tm-chat-bubble--pending")]
    [InlineData(ChatBubbleVariant.Queued, "tm-chat-bubble--queued")]
    [InlineData(ChatBubbleVariant.Discarded, "tm-chat-bubble--discarded")]
    [InlineData(ChatBubbleVariant.Locked, "tm-chat-bubble--locked")]
    public void EachVariant_RendersItsModifierClass(ChatBubbleVariant variant, string expectedClass)
    {
        var cut = RenderBubble(p => p.Add(x => x.Variant, variant));

        cut.Find(".tm-chat-bubble").Should().NotBeNull("the root carries the block class");
        cut.Find($".{expectedClass}").Should().NotBeNull("the variant modifier class is emitted");
    }

    // ── Slots ────────────────────────────────────────────────────────────────

    [Fact]
    public void ChildContent_RendersInsideBody()
    {
        var cut = RenderBubble(p => p.Add(x => x.ChildContent, "Hello from the host"));

        cut.Find(".tm-chat-bubble__body").TextContent.Should().Contain("Hello from the host");
    }

    [Fact]
    public void BadgeSlot_RendersHostContent()
    {
        var cut = RenderBubble(p => p.Add(x => x.Badge, "<b>AI draft</b>"));

        cut.Find(".tm-chat-bubble__badge").InnerHtml.Should().Contain("AI draft");
    }

    [Fact]
    public void MetaSlot_RendersInsideMetaRow()
    {
        var cut = RenderBubble(p => p.Add(x => x.Meta, "<span class='my-meta'>edited</span>"));

        cut.Find(".tm-chat-bubble__meta .my-meta").Should().NotBeNull();
    }

    [Fact]
    public void NoteSlot_Renders()
    {
        var cut = RenderBubble(p => p.Add(x => x.Note, "<span>Discarded by Alice</span>"));

        cut.Find(".tm-chat-bubble__note").TextContent.Should().Contain("Discarded by Alice");
    }

    [Fact]
    public void ActionsSlot_Renders()
    {
        var cut = RenderBubble(p => p.Add(x => x.Actions, "<button>Approve</button>"));

        cut.Find(".tm-chat-bubble__actions button").TextContent.Should().Be("Approve");
    }

    // ── Discarded: semantic strikethrough ────────────────────────────────────

    [Fact]
    public void Discarded_StrikesContentThroughWithSemanticElement()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.Variant, ChatBubbleVariant.Discarded)
            .Add(x => x.ChildContent, "an old message"));

        cut.Find("del.tm-chat-bubble__body").TextContent.Should().Be(
            "an old message",
            "discarded text is rendered as a semantic <del>, not styled with CSS only");
        cut.Markup.Should().NotContain("<s>", "the strikethrough is <del>, never a bare <s>");
    }

    // ── Locked: content must not leak ────────────────────────────────────────

    [Fact]
    public void Locked_DoesNotRenderChildContent_EvenWhenPassed()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.Variant, ChatBubbleVariant.Locked)
            .Add(x => x.ChildContent, "secret-locked-content"));

        cut.FindAll(".tm-chat-bubble__body").Should().BeEmpty();
        cut.Markup.Should().NotContain("secret-locked-content",
            "locked content must not leak into the DOM");
    }

    [Fact]
    public void Locked_StillRendersBadgeNoteAndActions()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.Variant, ChatBubbleVariant.Locked)
            .Add(x => x.Badge, "<span class='price'>19 Kč</span>")
            .Add(x => x.Note, "<span>Unlock to view</span>")
            .Add(x => x.Actions, "<button>Unlock</button>"));

        cut.Find(".price").TextContent.Should().Be("19 Kč");
        cut.Find(".tm-chat-bubble__note").TextContent.Should().Contain("Unlock to view");
        cut.Find(".tm-chat-bubble__actions button").TextContent.Should().Be("Unlock");
    }

    // ── Meta row: actor, timestamp, separator ───────────────────────────────

    [Fact]
    public void Timestamp_RendersTimeElementWithDatetimeAttribute()
    {
        var cut = RenderBubble(p => p.Add(x => x.Timestamp, Ts));

        var time = cut.Find(".tm-chat-bubble__meta time");
        time.GetAttribute("datetime").Should().StartWith("2026-05-02T10:30:00");
        time.TextContent.Should().NotBeEmpty("the time element carries a human-readable label");
    }

    [Fact]
    public void NoTimestamp_NoTimeElement()
    {
        var cut = RenderBubble();

        cut.FindAll(".tm-chat-bubble__meta time").Should().BeEmpty();
    }

    [Fact]
    public void ActorLabelAndTime_AreSeparatedByMiddleDot()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.ActorLabel, "Alice")
            .Add(x => x.Timestamp, Ts));

        var meta = cut.Find(".tm-chat-bubble__meta");
        meta.TextContent.Should().Contain("Alice").And.Contain("·");
        var html = meta.InnerHtml;
        html.IndexOf("Alice", StringComparison.Ordinal).Should().BeLessThan(
            html.IndexOf("<time", StringComparison.Ordinal),
            "the actor label comes before the time element");
    }

    [Fact]
    public void ActorLabelWithoutTimestamp_RendersActorOnly()
    {
        var cut = RenderBubble(p => p.Add(x => x.ActorLabel, "Alice"));

        cut.Find(".tm-chat-bubble__actor").TextContent.Should().Be("Alice");
        cut.FindAll(".tm-chat-bubble__meta time").Should().BeEmpty();
    }

    [Fact]
    public void NoActorNoTimestampNoMeta_MetaRowIsNotRendered()
    {
        var cut = RenderBubble(p => p.Add(x => x.ChildContent, "just content"));

        cut.FindAll(".tm-chat-bubble__meta").Should().BeEmpty();
    }

    [Fact]
    public void MetaSeparator_IsLiteralMiddleDotAndHiddenFromAssistiveTech()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.ActorLabel, "Alice")
            .Add(x => x.Timestamp, Ts));

        var separator = cut.Find(".tm-chat-bubble__meta-separator");
        separator.TextContent.Should().Be(" · ", "the separator is a literal middle dot between actor and time");
        separator.GetAttribute("aria-hidden").Should().Be("true",
            "the separator is decoration — the reading order alone carries it");
    }

    [Fact]
    public void MetaSeparator_AbsentWhenOnlyTimestampIsSet()
    {
        var cut = RenderBubble(p => p.Add(x => x.Timestamp, Ts));

        cut.FindAll(".tm-chat-bubble__meta-separator").Should().BeEmpty(
            "a time with no actor label has nothing to separate");
    }

    // ── Accessibility: StatusLabel, data-variant, variant icon ──────────────

    [Fact]
    public void StatusLabel_RendersFirstAsVisuallyHiddenSpan()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.Variant, ChatBubbleVariant.Queued)
            .Add(x => x.StatusLabel, "Queued — sends tomorrow at 08:00")
            .Add(x => x.ChildContent, "the message")
            .Add(x => x.Badge, "<span>AI draft</span>"));

        var status = cut.Find(".tm-chat-bubble > .tm-sr-only");
        status.TextContent.Should().Be("Queued — sends tomorrow at 08:00");
        var inner = cut.Find(".tm-chat-bubble").InnerHtml;
        inner.IndexOf("tm-sr-only", StringComparison.Ordinal).Should().BeLessThan(
            inner.IndexOf("tm-chat-bubble__badge", StringComparison.Ordinal),
            "the status leads the reading order, before badge, body and meta");
    }

    [Fact]
    public void StatusLabel_Null_RendersNoStatusSpan()
    {
        var cut = RenderBubble(p => p.Add(x => x.ChildContent, "plain"));

        cut.FindAll(".tm-sr-only").Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChatBubbleVariant.Incoming, "incoming")]
    [InlineData(ChatBubbleVariant.Outgoing, "outgoing")]
    [InlineData(ChatBubbleVariant.System, "system")]
    [InlineData(ChatBubbleVariant.Pill, "pill")]
    [InlineData(ChatBubbleVariant.Pending, "pending")]
    [InlineData(ChatBubbleVariant.Queued, "queued")]
    [InlineData(ChatBubbleVariant.Discarded, "discarded")]
    [InlineData(ChatBubbleVariant.Locked, "locked")]
    public void Root_CarriesMachineReadableDataVariant(ChatBubbleVariant variant, string expected)
    {
        var cut = RenderBubble(p => p.Add(x => x.Variant, variant));

        cut.Find(".tm-chat-bubble").GetAttribute("data-variant").Should().Be(expected);
    }

    [Fact]
    public void Locked_RendersDecorativeLockIconInBadgeArea()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.Variant, ChatBubbleVariant.Locked)
            .Add(x => x.ChildContent, "secret"));

        var badge = cut.Find(".tm-chat-bubble__badge");
        badge.QuerySelector("svg").Should().NotBeNull("the lock icon decorates the badge area");
        badge.QuerySelector("svg")!.GetAttribute("aria-hidden").Should().Be("true",
            "the icon is decorative — the state must come from a Badge or the StatusLabel");
        cut.Markup.Should().NotContain("secret");
    }

    [Fact]
    public void Queued_RendersDecorativeClockIconInBadgeArea()
    {
        var cut = RenderBubble(p => p.Add(x => x.Variant, ChatBubbleVariant.Queued));

        cut.Find(".tm-chat-bubble__badge svg").Should().NotBeNull("the clock icon decorates the badge area");
    }

    [Fact]
    public void ShowVariantIcon_RendersAlongsideHostBadge()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.Variant, ChatBubbleVariant.Locked)
            .Add(x => x.Badge, "<span class='price'>19 Kč</span>"));

        var badge = cut.Find(".tm-chat-bubble__badge");
        badge.TextContent.Should().Contain("19 Kč");
        badge.QuerySelector("svg").Should().NotBeNull("the icon and the host badge share the badge area");
    }

    [Fact]
    public void ShowVariantIcon_False_RendersNoIcon()
    {
        var cut = RenderBubble(p => p
            .Add(x => x.Variant, ChatBubbleVariant.Locked)
            .Add(x => x.ShowVariantIcon, false));

        cut.FindAll(".tm-chat-bubble__badge").Should().BeEmpty();
        cut.FindAll(".tm-chat-bubble__badge svg").Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChatBubbleVariant.Incoming)]
    [InlineData(ChatBubbleVariant.Outgoing)]
    [InlineData(ChatBubbleVariant.System)]
    [InlineData(ChatBubbleVariant.Pill)]
    [InlineData(ChatBubbleVariant.Pending)]
    [InlineData(ChatBubbleVariant.Discarded)]
    public void VariantsWithoutReservedIcon_RenderNoVariantIcon(ChatBubbleVariant variant)
    {
        var cut = RenderBubble(p => p.Add(x => x.Variant, variant));

        cut.FindAll(".tm-chat-bubble__badge").Should().BeEmpty(
            "only Locked (lock) and Queued (clock) decorate the badge area");
    }

    // ── Base plumbing: Class, DataTestId, AdditionalAttributes ──────────────

    [Fact]
    public void Class_IsAppendedToRootClass()
    {
        var cut = RenderBubble(p => p.Add(x => x.Class, "my-bubble"));

        cut.Find(".tm-chat-bubble.my-bubble").Should().NotBeNull();
    }

    [Fact]
    public void DataTestId_OverridesRootTestId()
    {
        var cut = RenderBubble(p => p.Add(x => x.DataTestId, "inbox-pending-1"));

        cut.Find("[data-testid='inbox-pending-1']").Should().NotBeNull();
    }

    [Fact]
    public void DefaultTestId_IsChatBubble()
    {
        var cut = RenderBubble();

        cut.Find("[data-testid='chat-bubble']").Should().NotBeNull();
    }

    [Fact]
    public void AdditionalAttributes_AreSplattedOnRoot()
    {
        var cut = RenderBubble(p => p
            .AddUnmatched("aria-label", "Queued message")
            .AddUnmatched("data-host-hook", "queued-1"));

        cut.Find(".tm-chat-bubble[aria-label='Queued message'][data-host-hook='queued-1']")
            .Should().NotBeNull();
    }

    // ── Inside TmChat: MessageTemplate renders the bubble ────────────────────

    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    [Fact]
    public void InsideTmChat_MessageTemplate_RendersIncomingAndOutgoingBubbles()
    {
        var messages = new[]
        {
            new ChatMessage("m1", "Hello from Bob", Bob, ChatMessageType.Incoming, Ts),
            new ChatMessage("m2", "Hi from me", Me, ChatMessageType.Outgoing, Ts),
        };

        var cut = Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me);
            p.Add(c => c.Messages, messages);
            p.Add(c => c.MessageTemplate, (ChatMessageContext context) => (RenderFragment)(builder =>
            {
                builder.OpenComponent<TmChatBubble>(0);
                builder.AddAttribute(1, nameof(TmChatBubble.Variant),
                    context.IsOwn ? ChatBubbleVariant.Outgoing : ChatBubbleVariant.Incoming);
                builder.AddAttribute(2, nameof(TmChatBubble.Timestamp), context.Message.Timestamp);
                builder.AddAttribute(3, nameof(TmChatBubble.ChildContent),
                    (RenderFragment)(b => b.AddContent(0, context.Message.Text)));
                builder.CloseComponent();
            }));
        });

        cut.Find(".tm-chat__item .tm-chat-bubble--incoming").TextContent.Should()
            .Contain("Hello from Bob", "a received message renders the Incoming bubble");
        cut.Find(".tm-chat__item .tm-chat-bubble--outgoing").TextContent.Should()
            .Contain("Hi from me", "an own message renders the Outgoing bubble");
        cut.FindAll(".tm-chat__item .tm-chat-bubble").Count.Should().Be(2);
    }
}
