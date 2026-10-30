using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Threading;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// The generic <see cref="TmTypedChat{TItem}"/> (FZE-15): a SEPARATE component composed over the
/// non-generic <see cref="TmChat"/>. The selector maps Items → ChatMessage in OnParametersSet
/// (payload defaults to the item, cached per item reference), an ItemTemplate receives the typed
/// <see cref="ChatItemContext{TItem}"/>, every other TmChat parameter is forwarded through the
/// attribute splat, the public helpers delegate to the inner chat and no-op before the first
/// render. The existing Chat tests staying untouched is the proof that a plain
/// <c>&lt;TmChat&gt;</c> (no generic argument) still compiles.
/// </summary>
public class TmTypedChatTests : LocalizationTestBase
{
    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private static readonly DateTimeOffset Ts = new(2026, 5, 2, 10, 30, 0, TimeSpan.Zero);

    private sealed record Ticket(int Id, string Subject, string Author = "Support");

    /// <summary>A selector that records the items it received — the cache assertions observe it.</summary>
    private static Func<Ticket, ChatMessage> CountingSelector(
        List<Ticket> calls, string? ownPayload = null)
        => ticket =>
        {
            calls.Add(ticket);
            var message = new ChatMessage($"ticket-{ticket.Id}", ticket.Subject, Bob, ChatMessageType.Incoming, Ts);
            return ownPayload is null ? message : message with { Payload = ownPayload };
        };

    private static ChatMessage DefaultSelector(Ticket ticket)
        => new($"ticket-{ticket.Id}", ticket.Subject, Bob, ChatMessageType.Incoming, Ts);

    private IRenderedComponent<TmTypedChat<Ticket>> RenderTyped(
        IReadOnlyList<Ticket> items,
        Func<Ticket, ChatMessage>? selector = null,
        Action<ComponentParameterCollectionBuilder<TmTypedChat<Ticket>>>? extra = null)
        => Render<TmTypedChat<Ticket>>(p =>
        {
            p.Add(c => c.Items, items)
             .Add(c => c.MessageSelector, selector ?? DefaultSelector)
             .AddUnmatched("CurrentUser", Me);
            extra?.Invoke(p);
        });

    /// <summary>
    /// Re-renders the wrapper with new parameter values (bUnit 1.38 has no
    /// <c>SetParametersAndRender</c> for this shape — the same InvokeAsync pattern the
    /// TmChatViewportTests use).
    /// </summary>
    private static Task ReRenderAsync<TItem>(
        IRenderedComponent<TmTypedChat<TItem>> cut, Dictionary<string, object?> parameters)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(parameters)));

    private static RenderFragment<ChatItemContext<Ticket>> CapturingTemplate(
        List<ChatItemContext<Ticket>> captured)
        => ctx =>
        {
            captured.Add(ctx);
            return builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "my-typed-bubble");
                builder.AddContent(2, ctx.Item!.Subject);
                builder.CloseElement();
            };
        };

    // ── Rendering through the selector ───────────────────────────────────────

    [Fact]
    public void Renders_ItemsThroughSelector()
    {
        var items = new[] { new Ticket(1, "Printer offline"), new Ticket(2, "VPN drops") };

        var cut = RenderTyped(items);

        cut.FindAll(".tm-chat__item").Count.Should().Be(2);
        cut.Instance.Chat.Should().NotBeNull("the inner chat is part of the render output");
        cut.Instance.Chat!.Messages.Should().HaveCount(2)
            .And.Contain(m => m.Text == "Printer offline")
            .And.Contain(m => m.Text == "VPN drops");
        cut.Instance.Chat.Messages.Select(m => m.Id)
            .Should().Equal("ticket-1", "ticket-2");
    }

    [Fact]
    public void Payload_DefaultsToItem_SelectorPayloadWins()
    {
        var plain = new[] { new Ticket(1, "Plain ticket") };
        var owned = new[] { new Ticket(2, "Ticket with own payload") };

        var cut = RenderTyped(plain);
        cut.Instance.Chat!.Messages.Single().Payload.Should().Be(plain[0],
            "a selector message without a payload carries the ITEM, so plain TmChat templates can PayloadAs<TItem>()");

        var cut2 = RenderTyped(owned, s => new ChatMessage(
            $"ticket-{s.Id}", s.Subject, Bob, ChatMessageType.Incoming, Ts) { Payload = "host-dto" });
        cut2.Instance.Chat!.Messages.Single().Payload.Should().Be("host-dto",
            "a payload the selector sets itself wins and is never overwritten");
    }

    [Fact]
    public void ItemTemplate_ReceivesTypedItem()
    {
        var items = new[] { new Ticket(1, "Printer offline") };
        var captured = new List<ChatItemContext<Ticket>>();

        var cut = RenderTyped(items, extra: p => p.Add(
            c => c.ItemTemplate, CapturingTemplate(captured)));

        cut.FindAll(".my-typed-bubble").Count.Should().Be(1);
        cut.FindAll(".tm-chat__bubble").Should().BeEmpty("the typed template replaces the built-in row");
        captured.Should().HaveCount(1);
        captured[0].Item.Should().Be(items[0], "the template receives the exact item reference");
        captured[0].Message.Message.Id.Should().Be("ticket-1");
        captured[0].IsOwn.Should().BeFalse();
        captured[0].Index.Should().Be(0);
        captured[0].DefaultContent.Should().NotBeNull("the built-in row stays available as a fallback");
    }

    [Fact]
    public void UseTemplateFor_TypedPredicate_Surgical()
    {
        var items = new[]
        {
            new Ticket(1, "Plain one"),
            new Ticket(2, "Special one"),
            new Ticket(3, "Another plain"),
        };
        var captured = new List<ChatItemContext<Ticket>>();

        var cut = RenderTyped(items, extra: p => p
            .Add(c => c.ItemTemplate, CapturingTemplate(captured))
            .Add(c => c.UseTemplateFor, (Ticket t) => t.Id == 2));

        cut.FindAll(".my-typed-bubble").Count.Should().Be(1, "the typed predicate accepts a single item");
        cut.FindAll(".tm-chat__bubble").Count.Should().Be(2, "the other items keep the built-in row");
        captured.Single().Item!.Id.Should().Be(2);
    }

    [Fact]
    public void ItemTemplate_WithoutPredicate_SelectorOwnedPayload_RendersBuiltIn()
    {
        var captured = new List<ChatItemContext<Ticket>>();
        var items = new[] { new Ticket(1, "Typed row"), new Ticket(2, "Own payload row") };

        var cut = RenderTyped(
            items,
            t => new ChatMessage($"ticket-{t.Id}", t.Subject, Bob, ChatMessageType.Incoming, Ts)
            {
                // The selector keeps its OWN payload for ticket 2 — something that is not a Ticket.
                Payload = t.Id == 2 ? "host-dto" : null,
            },
            p => p.Add(c => c.ItemTemplate, CapturingTemplate(captured)));

        cut.FindAll(".my-typed-bubble").Count.Should().Be(1, "only the item payload reaches the typed template");
        cut.FindAll(".tm-chat__bubble").Count.Should().Be(1,
            "a selector-owned payload renders the BUILT-IN row — it must not reach the typed template as default(TItem)");
        captured.Single().Item!.Id.Should().Be(1);
    }

    // ── Splat forwarding ─────────────────────────────────────────────────────

    [Fact]
    public void Splat_ForwardsTmChatParameters()
    {
        var sent = new List<ChatSendRequest>();
        var cut = RenderTyped(
            [new Ticket(1, "Printer offline")],
            extra: p => p
                .AddUnmatched("Placeholder", "Forwarded placeholder")
                .AddUnmatched("FillParent", true)
                .AddUnmatched("OnSend", EventCallback.Factory.Create<ChatSendRequest>(
                    this, req => sent.Add(req)))
                .AddUnmatched("MessagesAriaLabel", "Typed chat messages"));

        cut.Find("textarea").GetAttribute("placeholder").Should().Be("Forwarded placeholder",
            "the splat forwarded Placeholder onto the inner chat");
        cut.Instance.Chat!.FillParent.Should().BeTrue("the splat forwarded FillParent onto the inner chat");
        cut.Find(".tm-chat").GetAttribute("class").Should().Contain("tm-chat--fill");

        cut.Find(".tm-chat__input").Input("Hello from the typed wrapper");
        cut.Find(".tm-chat__send-btn").Click();

        cut.WaitForAssertion(() => sent.Should().ContainSingle(
            "the splatted OnSend reached the inner chat and fired through the send pipeline"));
    }

    [Fact]
    public void Splat_ConflictingMessages_Throws()
    {
        var act = () => RenderTyped(
            [new Ticket(1, "Printer offline")],
            extra: p => p.AddUnmatched("Messages", Array.Empty<ChatMessage>()));

        act.Should().Throw<ArgumentException>().WithMessage("*Messages*owned by TmTypedChat*",
            "Messages is derived from Items + MessageSelector — a splatted conflict would "
            + "silently fight the mapping");
    }

    [Fact]
    public void Splat_MethodGroupAsString_ThrowsHelpfulError()
    {
        // A bare method group (OnSend="HandleSend") compiles to the handler's NAME as a plain
        // string — the validation must stop the render with the factory form to write instead,
        // not let the inner chat die on an InvalidCastException.
        var act = () => RenderTyped(
            [new Ticket(1, "Printer offline")],
            extra: p => p.AddUnmatched("OnSend", "HandleSend"));

        act.Should().Throw<ArgumentException>().WithMessage(
            "*OnSend*STRING*EventCallback.Factory.Create<ChatSendRequest>(this, HandleSend)*",
            "the message names the attribute, explains the method-group pitfall and shows the fix");
    }

    [Fact]
    public void Splat_TypeMismatch_ThrowsNamingExpectedType()
    {
        var act = () => RenderTyped(
            [new Ticket(1, "Printer offline")],
            extra: p => p.AddUnmatched("FillParent", "yes"));

        act.Should().Throw<ArgumentException>().WithMessage("*FillParent*String*Boolean*",
            "the message names the forwarded key and the expected parameter type");
    }

    // ── Selector cache ───────────────────────────────────────────────────────

    [Fact]
    public async Task SelectorCache_UnchangedReference_NotRemapped()
    {
        var calls = new List<Ticket>();
        var items = new[] { new Ticket(1, "Printer offline"), new Ticket(2, "VPN drops") };

        var cut = RenderTyped(items, CountingSelector(calls));

        var firstMapping = cut.Instance.Chat!.Messages.ToArray();
        calls.Should().HaveCount(2, "each item is mapped once");

        // An unrelated parameter pass re-runs OnParametersSet but must NOT re-run the selector.
        await ReRenderAsync(cut, new Dictionary<string, object?> { ["CurrentUser"] = new ChatUser("u9", "Zed") });

        calls.Should().HaveCount(2, "an unchanged item reference reuses the mapped ChatMessage instance");
        cut.Instance.Chat!.Messages.Should().Equal(firstMapping);
    }

    [Fact]
    public async Task SelectorCache_RemovedItems_Evicted()
    {
        var calls = new List<Ticket>();
        var a = new Ticket(1, "Printer offline");
        var b = new Ticket(2, "VPN drops");

        var cut = RenderTyped([a, b], CountingSelector(calls));
        calls.Should().HaveCount(2);

        // b disappears — the cache must drop it, so a later re-arrival re-maps it.
        await ReRenderAsync(cut, new Dictionary<string, object?> { ["Items"] = new[] { a } });
        calls.Should().HaveCount(2, "the surviving item is still cached");

        await ReRenderAsync(cut, new Dictionary<string, object?> { ["Items"] = new[] { a, b } });
        calls.Should().HaveCount(3, "the evicted item was re-mapped when it came back");
        cut.Instance.Chat!.Messages.Select(m => m.Id).Should().Equal("ticket-1", "ticket-2");
    }

    private sealed class SelectorHub
    {
        public List<Ticket> Calls { get; } = [];

        public ChatMessage Map(Ticket ticket)
        {
            Calls.Add(ticket);
            return new ChatMessage($"ticket-{ticket.Id}", ticket.Subject, Bob, ChatMessageType.Incoming, Ts);
        }
    }

    [Fact]
    public async Task SelectorCache_FreshDelegateOfSameInstanceMethod_NotRemapped()
    {
        // The Razor compiler builds a FRESH delegate for a method-group selector on every render;
        // Delegate.Equals must treat the fresh binding of the same instance method as the SAME
        // selector, or every render would clear the cache and re-map the whole conversation.
        var hub = new SelectorHub();
        var items = new[] { new Ticket(1, "Printer offline"), new Ticket(2, "VPN drops") };

        var cut = RenderTyped(items, hub.Map);
        var firstMapping = cut.Instance.Chat!.Messages.ToArray();
        hub.Calls.Should().HaveCount(2);

        await ReRenderAsync(cut, new Dictionary<string, object?>
        {
            ["MessageSelector"] = hub.Map, // a NEW delegate instance of the SAME instance method
            ["CurrentUser"] = new ChatUser("u9", "Zed"),
        });

        hub.Calls.Should().HaveCount(2, "a fresh delegate of the same instance method does not invalidate the cache");
        cut.Instance.Chat!.Messages.Should().Equal(firstMapping);
    }

    [Fact]
    public async Task SelectorCache_ReplacedSelector_Remaps()
    {
        // The cache saves mapping work only: a genuinely different selector must force the re-map,
        // so the rows reflect the mapping the host asked for.
        var first = new SelectorHub();
        var second = new SelectorHub();
        var items = new[] { new Ticket(1, "Printer offline") };

        var cut = RenderTyped(items, first.Map);
        first.Calls.Should().HaveCount(1);

        await ReRenderAsync(cut, new Dictionary<string, object?> { ["MessageSelector"] = second.Map });

        second.Calls.Should().HaveCount(1, "the replaced selector re-mapped the item");
        first.Calls.Should().HaveCount(1, "the retired selector is no longer called");
    }

    [Fact]
    public async Task NullItem_MappedFreshOnEveryPass()
    {
        var nullCalls = 0;
        var ticket = new Ticket(1, "Printer offline");

        var cut = Render<TmTypedChat<Ticket?>>(p => p
            .Add(c => c.Items, new Ticket?[] { ticket, null })
            .Add(c => c.MessageSelector, (Ticket? t) =>
            {
                if (t is null) nullCalls++;
                return new ChatMessage(
                    t is null ? "row-removed" : $"ticket-{t.Id}",
                    t?.Subject ?? "(removed)",
                    Bob,
                    ChatMessageType.Incoming,
                    Ts);
            })
            .AddUnmatched("CurrentUser", Me));

        cut.FindAll(".tm-chat__item").Count.Should().Be(2, "a null item still renders a row");
        nullCalls.Should().Be(1);
        cut.Instance.Chat!.Messages.Single(m => m.Id == "row-removed").Text.Should().Be("(removed)");

        // A null item has no identity to cache by — every parameter pass maps it again.
        await ReRenderAsync(cut, new Dictionary<string, object?> { ["CurrentUser"] = new ChatUser("u9", "Zed") });

        nullCalls.Should().Be(2, "the null item is re-mapped on every pass, never cached");
    }

    [Fact]
    public async Task ValueTypeItems_Work()
    {
        var calls = 0;
        var cut = Render<TmTypedChat<int>>(p => p
            .Add(c => c.Items, new[] { 7, 11 })
            .Add(c => c.MessageSelector, i =>
            {
                calls++;
                return new ChatMessage($"msg-{i}", $"Value #{i}", Bob, ChatMessageType.Incoming, Ts);
            })
            .AddUnmatched("CurrentUser", Me));

        cut.FindAll(".tm-chat__item").Count.Should().Be(2);
        cut.Instance.Chat!.Messages.Single(m => m.Id == "msg-7").Payload.Should().Be(7,
            "a value-type item is boxed into the payload");

        // Value types have no identity — every parameter pass re-maps them.
        await ReRenderAsync(cut, new Dictionary<string, object?> { ["CurrentUser"] = new ChatUser("u9", "Zed") });
        calls.Should().Be(4, "value-type items are mapped on every pass (2 items × 2 passes)");
    }

    // ── Delegation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ScrollToItemAsync_DelegatesWithSelectorId()
    {
        var (module, handle) = SetupScroller();
        var items = new[] { new Ticket(1, "Printer offline"), new Ticket(2, "VPN drops") };
        var cut = RenderTyped(items, DefaultSelector,
            p => p.AddUnmatched("AutoScroll", ChatAutoScroll.Never));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        var found = await cut.Instance.ScrollToItemAsync(items[1]);

        found.Should().BeTrue("the fake scroller reports the anchor as found");
        handle.Calls.Should().Contain(c => c.Identifier == "scrollToMessage"
            && c.Args.SequenceEqual(new object?[] { "ticket-2" }),
            "the wrapper resolved the item through the selector's message id");
    }

    [Fact]
    public async Task ItemNavigation_ResolvedFromSelectorCache()
    {
        var (module, handle) = SetupScroller();
        var calls = new List<Ticket>();
        var items = new[] { new Ticket(1, "Printer offline"), new Ticket(2, "VPN drops") };
        var cut = RenderTyped(items, CountingSelector(calls),
            p => p.AddUnmatched("AutoScroll", ChatAutoScroll.Never));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        calls.Should().HaveCount(2);

        (await cut.Instance.ScrollToItemAsync(items[1])).Should().BeTrue();
        (await cut.Instance.FocusItemAsync(items[0])).Should().BeTrue();

        calls.Should().HaveCount(2, "the id comes from the selector cache — the host's selector does not re-run");
        handle.Calls.Should().Contain(c => c.Identifier == "scrollToMessage"
            && c.Args.SequenceEqual(new object?[] { "ticket-2" }));
        handle.Calls.Should().Contain(c => c.Identifier == "focusMessageVisible"
            && c.Args.SequenceEqual(new object?[] { "ticket-1" }));
    }

    [Fact]
    public async Task MethodsBeforeFirstRender_NoThrow()
    {
        var selectorCalled = false;
        var wrapper = new TmTypedChat<Ticket>
        {
            MessageSelector = t => { selectorCalled = true; return DefaultSelector(t); },
        };

        Func<Task> act = async () =>
        {
            await wrapper.ScrollToBottomAsync();
            (await wrapper.ScrollToItemAsync(new Ticket(1, "x"))).Should().BeFalse();
            (await wrapper.ScrollToMessageAsync("ticket-1")).Should().BeFalse();
            (await wrapper.FocusItemAsync(new Ticket(1, "x"))).Should().BeFalse();
            await wrapper.AnnounceAsync("Koncept schválen");
            await wrapper.FocusComposerAsync();
            await wrapper.DiscardDraftAsync();
        };

        await act.Should().NotThrowAsync("every helper no-ops before the inner chat exists");
        wrapper.HasUnsavedDraft.Should().BeFalse();
        wrapper.Chat.Should().BeNull();
        selectorCalled.Should().BeFalse("the id resolution is skipped together with the delegation");
    }

    // ── chat.js scroller double (the same contract TmChatScrollTests emulates) ──

    private const string ChatModulePath = "./_content/Tempo.Blazor/js/chat.js";

    private (BunitJSModuleInterop Module, FakeScrollerHandle Handle) SetupScroller()
    {
        var module = JSInterop.SetupModule(ChatModulePath);
        var handle = new FakeScrollerHandle();
        module.AddInvocationHandler(new AttachScrollerHandler(handle));
        return (module, handle);
    }

    private sealed class FakeScrollerHandle : IJSObjectReference
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args = null)
        {
            Calls.Add((identifier, args ?? Array.Empty<object?>()));
            return identifier is "scrollToMessage" or "focusMessageVisible"
                ? new ValueTask<TValue>((TValue)(object)true)
                : default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => default;
    }

    private sealed class AttachScrollerHandler : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        private readonly IJSObjectReference _handle;

        public AttachScrollerHandler(IJSObjectReference handle)
            : base(invocation => invocation.Identifier == "attachScroller", false)
            => _handle = handle;

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
            => Task.FromResult(_handle);
    }
}
