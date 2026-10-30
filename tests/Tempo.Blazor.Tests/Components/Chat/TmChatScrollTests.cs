using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Threading;
using System.Threading.Tasks;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Scroll behavior of <see cref="TmChat"/> (FZE-6): the first render lands at the bottom, an
/// appended message scrolls only while the viewport WAS at the bottom (the <c>chat.js</c>
/// scroller decides through its own snapshot and reports <c>unread</c> for the away case), the
/// user's own messages always scroll, a PREPEND preserves the visual position instead of
/// scrolling, <c>AutoScroll.Never</c> restores the old no-scroll behavior, and the jump-to-new
/// button counts the messages that arrived while the user was reading above the bottom. The
/// bUnit fake handle EMULATES the documented <c>chat.js</c> contract (<c>WasAtBottom</c> is the
/// browser state before the update: append at the bottom → scroll to the bottom and return
/// <c>scrolled</c>; away → stay put and return <c>unread</c>), so the tests verify the C#→JS
/// orchestration end to end; the JS side itself is covered by the <c>chat.test.mjs</c> Node
/// lane. Any PRE-RENDER interop call (e.g. a reintroduced <c>beforeUpdate</c>) gets an
/// UNFINISHED ValueTask from the fake — the render must not wait for it.
/// </summary>
public class TmChatScrollTests : LocalizationTestBase
{
    private const string ChatModulePath = "./_content/Tempo.Blazor/js/chat.js";

    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private static ChatMessage Incoming(string id, string text) =>
        new(id, text, Bob, ChatMessageType.Incoming);

    private static ChatMessage Own(string id, string text) =>
        new(id, text, Me, ChatMessageType.Outgoing);

    /// <summary>
    /// The scroller handle double: records every invocation and emulates the documented
    /// afterUpdate contract (<see cref="WasAtBottom"/> is the browser state before the update).
    /// </summary>
    private sealed class FakeScrollerHandle : IJSObjectReference
    {
        /// <summary>The identifiers the component may call AFTER a render (or from a helper).</summary>
        private static readonly HashSet<string> PostRenderIdentifiers =
            new() { "afterUpdate", "scrollToBottom", "scrollToMessage", "focusMessage", "dispose" };

        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        /// <summary>Whether the viewport was at the bottom when the update started (the JS state).</summary>
        public bool WasAtBottom { get; set; } = true;

        /// <summary>
        /// How many afterUpdate round trips to PARK (answer later) — emulates a slow JS answer
        /// racing the next parameter set: the component has consumed the classification but not
        /// yet applied its result when the next DetectScrollChange runs.
        /// </summary>
        private int _afterUpdatesToHold;

        /// <summary>Parked (mode, completion) pairs released by <see cref="ReleaseAfterUpdates"/>.</summary>
        private readonly List<(string? Mode, Action Complete)> _heldAfterUpdates = [];

        /// <summary>Parks the NEXT afterUpdate round trip until <see cref="ReleaseAfterUpdates"/>.</summary>
        internal void HoldNextAfterUpdate() => _afterUpdatesToHold++;

        /// <summary>Answers every parked afterUpdate round trip with its emulated status.</summary>
        internal void ReleaseAfterUpdates()
        {
            var held = _heldAfterUpdates.ToArray();
            _heldAfterUpdates.Clear();
            foreach (var (_, complete) in held) complete();
        }

        private string? EmulateAfterUpdate(string? mode) => mode switch
        {
            "append" => WasAtBottom ? "scrolled" : "unread",
            "prepend" => "preserved",
            _ => "none",
        };

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args = null)
        {
            Calls.Add((identifier, args ?? Array.Empty<object?>()));

            if (!PostRenderIdentifiers.Contains(identifier))
            {
                // A PRE-RENDER call (e.g. the removed beforeUpdate round trip) never completes:
                // ComponentBase renders as soon as OnParametersSetAsync yields an unfinished
                // task, so a hanging call here proves the render path has no such dependency —
                // the test below asserts afterUpdate still runs exactly once.
                return new(new TaskCompletionSource<TValue>().Task);
            }

            switch (identifier)
            {
                case "afterUpdate":
                    var mode = args is [string m] ? m : null;
                    var status = EmulateAfterUpdate(mode);
                    if (_afterUpdatesToHold > 0)
                    {
                        _afterUpdatesToHold--;
                        var completion = new TaskCompletionSource<TValue>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        _heldAfterUpdates.Add((mode, () =>
                        {
                            // The synthetic scrollToBottom bookkeeping rides on the answer.
                            if (status == "scrolled") Calls.Add(("scrollToBottom", Array.Empty<object?>()));
                            completion.TrySetResult((TValue)(object)status!);
                        }));
                        return new(completion.Task);
                    }
                    if (status == "scrolled") Calls.Add(("scrollToBottom", Array.Empty<object?>()));
                    return new((TValue)(object)status!);
                case "scrollToMessage":
                    // The JS scroller reports whether the anchor row was found.
                    return new((TValue)(object)true);
                default:
                    // scrollToBottom / focusMessage / dispose: void-ish, complete immediately.
                    return default;
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Serves the fake handle for the attachScroller call of the chat.js module.</summary>
    private sealed class AttachScrollerHandler : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        private readonly IJSObjectReference _handle;

        public AttachScrollerHandler(IJSObjectReference handle)
            : base(invocation => invocation.Identifier == "attachScroller", false)
            => _handle = handle;

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
            => Task.FromResult(_handle);
    }

    /// <summary>
    /// A stateful template row for <see cref="Prepend_ReusesExistingRowInstances"/>: registers
    /// itself when initialized, so the test can tell RE-USED instances from new ones.
    /// </summary>
    private sealed class StatefulRow : ComponentBase
    {
        public static readonly List<StatefulRow> Created = [];

        [Parameter] public string Text { get; set; } = string.Empty;

        public int Inits { get; private set; }

        protected override void OnInitialized()
        {
            Inits++;
            Created.Add(this);
        }
    }

    private (BunitJSModuleInterop Module, FakeScrollerHandle Handle) SetupScroller()
    {
        var module = JSInterop.SetupModule(ChatModulePath);
        var handle = new FakeScrollerHandle();
        module.AddInvocationHandler(new AttachScrollerHandler(handle));
        return (module, handle);
    }

    private IRenderedComponent<TmChat> RenderChat(
        IReadOnlyList<ChatMessage> messages,
        Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me).Add(c => c.Messages, messages);
            extra?.Invoke(p);
        });

    private static void WaitForAttach(BunitJSModuleInterop module) =>
        module.Invocations["attachScroller"].Should().NotBeNull();

    private static bool Called(FakeScrollerHandle handle, string identifier) =>
        handle.Calls.Any(c => c.Identifier == identifier);

    private static void CalledOnce(FakeScrollerHandle handle, string identifier, string because) =>
        handle.Calls.Count(c => c.Identifier == identifier).Should().Be(1, because);

    // ── First render ─────────────────────────────────────────────────────────

    [Fact]
    public void FirstRender_ScrollsToBottom()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello"), Incoming("m2", "hi")]);

        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        cut.WaitForAssertion(() => CalledOnce(handle, "scrollToBottom",
            "the first render lands at the bottom (the documented default AutoScroll=WhenAtBottom)"));
        cut.Instance.IsAtBottom.Should().BeTrue("no scroll report has said otherwise yet");
    }

    // ── Appends ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task NewMessage_WhenAtBottom_ScrollsToBottom()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        handle.Calls.Clear();

        // The user is at the bottom (the scroller keeps reporting it) and a message arrives.
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(true));
        handle.WasAtBottom = true;
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }));

        cut.WaitForAssertion(() => CalledOnce(handle, "afterUpdate",
            "the append goes through the afterUpdate bookkeeping"));
        handle.Calls.Last(c => c.Identifier == "afterUpdate")
            .Args.Should().Equal("append");
        handle.Calls.Should().NotContain(c => c.Identifier == "beforeUpdate",
            "the scroller keeps its own pre-update snapshot — no beforeUpdate round trip");
        CalledOnce(handle, "scrollToBottom",
            "at the bottom the JS scroller scrolls to the new bottom (emulated by the fake)");
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("no unread when the user keeps up with the conversation");
    }

    [Fact]
    public async Task NewMessage_WhenScrolledUp_ShowsJumpButtonWithCount()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello"), Incoming("m2", "hi")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        handle.Calls.Clear();

        // The user scrolled up to read; the scroller reported the state change.
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;

        var appended = new List<ChatMessage> { Incoming("m1", "hello"), Incoming("m2", "hi") };
        for (var i = 3; i <= 5; i++) appended.Add(Incoming($"m{i}", $"message {i}"));
        cut.Render(p => p.Add(c => c.Messages, appended));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));
        handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "append" }), "the append bookkeeping ran");
        Called(handle, "scrollToBottom").Should().BeFalse(
            "away from the bottom an append must not scroll (the reading position stays)");
        var button = cut.Find("[data-testid='chat-jump-to-new']");
        button.TextContent.Should().Contain("3 new messages",
            "the visible count is part of the button (TmChat_NewMessages plural)");
        button.GetAttribute("aria-label").Should().BeNull(
            "with built-in content there is no aria-label: the count + the sr-only span IS the name");
        button.TextContent.Should().Contain("Jump to new messages",
            "the sr-only action name completes the accessible name (TmChat_JumpToNew)");
    }

    [Fact]
    public async Task JumpButton_Click_ScrollsAndHides()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello"), Incoming("m2", "hi")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi"), Incoming("m3", "third") }));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));

        cut.Find("[data-testid='chat-jump-to-new']").Click();

        cut.WaitForAssertion(() =>
        {
            CalledOnce(handle, "scrollToBottom",
                "the click scrolls to the bottom (and focus moves off the unmounting button — never the body)");
            cut.FindAll("[data-testid='chat-jump-to-new']")
                .Should().BeEmpty("the count resets and the button hides");
        });
    }

    [Fact]
    public async Task OwnMessage_AlwaysScrolls()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Own("m2", "my own reply") }));

        cut.WaitForAssertion(() => CalledOnce(handle, "scrollToBottom",
            "the user's own message always scrolls to the bottom, regardless of the reading position"));
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("own messages never count as unread");
    }

    [Fact]
    public async Task OwnMessage_WhileJumpButtonVisible_HidesIt()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        // An incoming message arrives: the button appears...
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));

        // ... and the user's OWN message that follows must hide it IMMEDIATELY (the core scroll
        // re-renders when the button was visible — the unread count never incremented, but the
        // visible button of the previous render must go away with THIS one).
        cut.Render(p => p.Add(c => c.Messages,
            new[]
            {
                Incoming("m1", "hello"), Incoming("m2", "hi"), Own("m3", "my own reply"),
            }));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("the own message scrolled: the button hides with the same interaction"));
        CalledOnce(handle, "scrollToBottom", "the own message lands at the bottom");
    }

    [Fact]
    public async Task ScrollToBottomAsync_HidesVisibleJumpButton()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello"), Incoming("m2", "hi")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi"), Incoming("m3", "third") }));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));

        await cut.Instance.ScrollToBottomAsync();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("the host-driven scroll resets the count and hides the visible button"));
        CalledOnce(handle, "scrollToBottom", "the public helper scrolled the list");
    }

    [Fact]
    public async Task ScrollToMessageAsync_ReportsWhetherTheAnchorWasFound()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        var found = await cut.Instance.ScrollToMessageAsync("m1");

        found.Should().BeTrue("the JS scroller found the anchor row (emulated by the fake)");
        handle.Calls.Should().Contain(c => c.Identifier == "scrollToMessage"
            && c.Args.SequenceEqual(new object?[] { "m1" }),
            "the public helper forwards the message id to the JS anchor scroll");
    }

    // ── None classification (the fix: classify from the RENDERED ROOT messages) ──

    [Fact]
    public async Task TypingOnlyRerender_WhileScrolledUp_DoesNotScroll()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        // A typing indicator appears: a re-render with the SAME root message sequence.
        cut.Render(p => p.Add(c => c.TypingUsers, new[] { Bob }));

        cut.WaitForAssertion(() => handle.Calls.Should().NotContain(
            c => c.Identifier == "scrollToBottom",
            "a typing-only re-render is neither an append nor a prepend — nothing may scroll"));
        handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "none" }),
            "the no-change render still refreshes the scroller's snapshot (and re-pins a grown viewport)");
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("a typing indicator is not an unread message");
    }

    [Fact]
    public async Task ParentRerender_SameMessages_DoesNotScroll()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        // An unrelated parameter changes: the messages are the same instances with the same Ids.
        cut.Render(p => p.Add(c => c.Placeholder, "type here"));

        cut.WaitForAssertion(() => handle.Calls.Should().NotContain(
            c => c.Identifier == "scrollToBottom",
            "a parameter-only re-render with unchanged boundary Ids must not move the list"));
        handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "none" }));
        cut.FindAll("[data-testid='chat-jump-to-new']").Should().BeEmpty();
    }

    [Fact]
    public async Task SlidingWindowAppend_WhileScrolledUp_CountsUnread()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat(new[]
        {
            Incoming("m1", "one"), Incoming("m2", "two"), Incoming("m3", "three"), Incoming("m4", "four"),
        });
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        // A sliding-window feed: the head (m1) is trimmed and m5/m6 arrive — the OLD last
        // message (m4) now sits INTERIOR to the list, which is still an append.
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m2", "two"), Incoming("m3", "three"), Incoming("m4", "four"),
            Incoming("m5", "five"), Incoming("m6", "six"),
        }));

        cut.WaitForAssertion(() => handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "append" }),
            "the old last Id is interior: new messages arrived at the end"));
        Called(handle, "scrollToBottom").Should().BeFalse("the user is away — the position stays");
        cut.Find("[data-testid='chat-jump-to-new']").TextContent.Should().Contain("2 new messages",
            "exactly the appended ROOT messages count (the trimmed m1 does not)");
    }

    [Fact]
    public async Task ThreadReply_DoesNotCountUnread()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        // A thread REPLY arrives: it renders in the thread panel, so the rendered ROOT sequence
        // is unchanged — the root first/last Ids survive and nothing counts as unread.
        var reply = new ChatMessage("r1", "a reply", Bob, ChatMessageType.Incoming) { ThreadRootId = "m1" };
        cut.Render(p => p.Add(c => c.Messages, new[] { Incoming("m1", "hello"), reply }));

        cut.WaitForAssertion(() => handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "none" }),
            "the reply is not part of the message list"));
        handle.Calls.Should().NotContain(c => c.Identifier == "scrollToBottom");
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("a thread reply is not an unread list message");
    }

    // ── Prepend ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Prepend_CallsPreservePosition_NotScrollToBottom()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat(new[] { Incoming("old-1", "old 1"), Incoming("old-2", "old 2") });
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        var prepended = new[]
        {
            Incoming("old-0", "older"), Incoming("old-1", "old 1"), Incoming("old-2", "old 2"),
        };
        cut.Render(p => p.Add(c => c.Messages, prepended));

        cut.WaitForAssertion(() => handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "prepend" })));
        handle.Calls.Should().NotContain(c => c.Identifier == "beforeUpdate",
            "the scroller keeps its own pre-update snapshot — no beforeUpdate round trip");
        Called(handle, "scrollToBottom").Should().BeFalse(
            "a prepend preserves the visual position instead of scrolling");
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("older history is not 'new messages'");
    }

    [Fact]
    public void Prepend_ReusesExistingRowInstances()
    {
        var (module, handle) = SetupScroller();
        StatefulRow.Created.Clear();

        static RenderFragment<ChatMessageContext> Template() => context => builder =>
        {
            builder.OpenComponent<StatefulRow>(0);
            builder.AddAttribute(1, nameof(StatefulRow.Text), context.Message.Text);
            builder.CloseComponent();
        };

        var cut = RenderChat(new[] { Incoming("old-1", "old 1"), Incoming("old-2", "old 2") },
            p => p.Add(c => c.MessageTemplate, Template()));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        var before = StatefulRow.Created.ToList();
        before.Should().HaveCount(2, "one template component per rendered row");
        before.Should().OnlyContain(row => row.Inits.Equals(1));
        var firstRowBefore = before[0];

        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("old-0", "older"), Incoming("old-1", "old 1"), Incoming("old-2", "old 2"),
        }));

        var after = StatefulRow.Created;
        after.Should().HaveCount(3, "only the NEW row instantiates a template component");
        after[^1].Text.Should().Be("older", "the new instance is the prepended row's");
        after.Should().Contain(firstRowBefore,
            "@key preserves the row's component instance across the prepend");
        firstRowBefore.Text.Should().Be("old 1",
            "the preserved instance kept ITS parameters (positional diffing would have overwritten them)");
        firstRowBefore.Inits.Should().Be(1, "the preserved row is never re-initialized");
    }

    // ── AutoScroll.Never — the old behavior ──────────────────────────────────

    [Fact]
    public async Task AutoScroll_Never_NoScrollCalls()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")],
            p => p.Add(c => c.AutoScroll, ChatAutoScroll.Never));
        await cut.WaitForAssertionAsync(() => handle.Calls.Should().NotContain(
            c => c.Identifier == "scrollToBottom" || c.Identifier == "afterUpdate",
            "AutoScroll.Never is the pre-2.10 opt-out: the component itself makes no scroll calls"));

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }));

        await cut.WaitForAssertionAsync(() => handle.Calls.Should().BeEmpty(
            "no append bookkeeping either — the host owns every scroll with AutoScroll.Never"));
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("the jump button belongs to the auto-scroll feature");
    }

    [Fact]
    public async Task AutoScroll_Always_ScrollsOnEveryChange_AndCountsNoUnread()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")],
            p => p.Add(c => c.AutoScroll, ChatAutoScroll.Always));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false; // the user is reading above the bottom — Always ignores that
        handle.Calls.Clear();

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }));

        cut.WaitForAssertion(() => CalledOnce(handle, "scrollToBottom",
            "Always pins the list to the bottom on every message change, regardless of position"));
        handle.Calls.Should().NotContain(c => c.Identifier == "afterUpdate",
            "Always needs no append/prepend bookkeeping (the scroll decides, nothing counts as unread)");
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("nothing counts as unread when the list is pinned");
    }

    // ── Render timing: no pre-render interop dependency ──────────────────────

    [Fact]
    public async Task PreRenderInteropCalls_Hang_TheRenderAppliesTheAppendOnce()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        // The fake returns an UNFINISHED ValueTask for any pre-render call — the render must
        // not wait for it (ComponentBase renders as soon as OnParametersSetAsync yields).
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }));

        cut.WaitForAssertion(() => handle.Calls.Count(c => c.Identifier == "afterUpdate"
                && c.Args.SequenceEqual(new object?[] { "append" })).Should().Be(1,
            "the append bookkeeping ran exactly once, from the after-render half"));
        handle.Calls.Should().NotContain(c => c.Identifier == "beforeUpdate",
            "there is no pre-render round trip at all");
        handle.Calls.Should().NotContain(c => c.Identifier == "scrollToBottom",
            "the fake stayed away from the bottom: the append counts as unread instead of scrolling");
        cut.Find("[data-testid='chat-jump-to-new']").Should().NotBeNull(
            "the render completes and the unread message is counted behind the button");
    }

    [Fact]
    public async Task AppendThenNone_BeforeOneAfterRender_CountsTheUnread()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        handle.Calls.Clear();

        var appended = new[]
        {
            Incoming("m1", "hello"), Incoming("m2", "a"), Incoming("m3", "b"), Incoming("m4", "c"),
        };

        // The append's afterUpdate round trip is SLOW (the fake parks it): the classification
        // is consumed, but the unread count is not applied until the JS answer arrives.
        handle.HoldNextAfterUpdate();
        cut.Render(p => p.Add(c => c.Messages, appended));
        cut.WaitForAssertion(() => handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "append" }),
            "the append's afterUpdate round trip started"));

        // A NONE parameter set lands while that round trip is still in flight: it must neither
        // clobber the pending appended count (3) nor downgrade the pending mode.
        cut.Render(p => p.Add(c => c.Messages, appended));
        cut.FindAll("[data-testid='chat-jump-to-new']")
            .Should().BeEmpty("the count is not applied while the round trip is in flight");

        handle.ReleaseAfterUpdates();
        cut.WaitForAssertion(() => cut.Find("[data-testid='chat-jump-to-new']").TextContent.Should()
            .Contain("3 new messages", "the append classified before the none still counts its 3 messages"));
        handle.Calls.Count(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "append" })).Should().Be(1,
            "the parked append is answered (and counted) exactly once");
    }

    // ── Load older ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadOlder_FiresWhenHasMoreBefore_NotWhileLoading()
    {
        var (module, handle) = SetupScroller();
        var loaded = new List<List<ChatMessage>>();
        var cut = RenderChat([Incoming("m1", "hello")], p => p
            .Add(c => c.HasMoreBefore, true)
            .Add(c => c.IsLoadingOlder, false)
            .Add(c => c.OnLoadOlder, EventCallback.Factory.Create(this,
                () => loaded.Add([Incoming("old", "older")]))));

        cut.WaitForAssertion(() => cut.Find("[data-testid='chat-load-older']")
            .Should().NotBeNull("HasMoreBefore + OnLoadOlder render the built-in load-older affordance"));
        loaded.Should().BeEmpty();

        cut.Find("[data-testid='chat-load-older']").Click();
        cut.WaitForAssertion(() => loaded.Should().HaveCount(1, "the click fires OnLoadOlder once"));

        // While the host is loading, the affordance is aria-disabled (NOT removed) and fires nothing.
        cut.Render(p => p.Add(c => c.IsLoadingOlder, true));
        cut.Find("[data-testid='chat-load-older']").GetAttribute("aria-disabled")
            .Should().Be("true", "the button is aria-disabled while IsLoadingOlder");
        cut.Find(".tm-chat__messages").GetAttribute("aria-busy")
            .Should().Be("true", "the message list is marked busy while the page loads");
        cut.Find("[data-testid='chat-load-older']").Click();
        loaded.Should().HaveCount(1, "IsLoadingOlder guards the callback");

        // The scroll-edge trigger honours the same guard.
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        loaded.Should().HaveCount(1, "no re-fire while IsLoadingOlder");

        cut.Render(p => p.Add(c => c.IsLoadingOlder, false));
        // One page per VISIT to the top edge: the finished load alone does not re-fire — the
        // user must leave the top band and reach it again (a real prepend's position shift
        // reports near-top=false, which is exactly that "leave").
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        loaded.Should().HaveCount(1, "staying near the top must not re-fire the trigger");
        cut.Find("[data-testid='chat-load-older']").GetAttribute("aria-disabled").Should().BeNull();

        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(false));
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        cut.WaitForAssertion(() => loaded.Should().HaveCount(2,
            "a fresh visit to the top edge loads the next page"));
    }

    [Fact]
    public async Task LoadOlder_ConcurrentClickAndNearTop_FiresOnce()
    {
        var (module, handle) = SetupScroller();
        var gate = new TaskCompletionSource();
        var fired = 0;
        var cut = RenderChat([Incoming("m1", "hello")], p => p
            .Add(c => c.HasMoreBefore, true)
            .Add(c => c.OnLoadOlder, EventCallback.Factory.Create(this, async () =>
            {
                fired++;
                await gate.Task; // the load is in flight until the test releases the gate
            })));

        cut.WaitForAssertion(() => cut.Find("[data-testid='chat-load-older']").Should().NotBeNull());

        // The click starts the load — the handler parks on the gate.
        cut.Find("[data-testid='chat-load-older']").Click();
        cut.WaitForAssertion(() => fired.Should().Be(1, "the click's handler started (in flight)"));

        // The top-edge report arrives WHILE the click's load is still in flight — the
        // component-side in-flight guard blocks it (the host's IsLoadingOlder has not
        // flipped yet, so only this guard can).
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        fired.Should().Be(1, "a click racing the near-top report fires the load once");

        gate.SetResult();
        await cut.WaitForAssertionAsync(() => cut.Find("[data-testid='chat-load-older']").Should().NotBeNull());
        fired.Should().Be(1, "the released load still fired exactly once");
    }

    [Fact]
    public async Task LoadOlder_ClickPrepend_WhenButtonHadFocus_FocusesThePreviouslyFirstRow()
    {
        var (module, handle) = SetupScroller();
        var clicked = false;
        var cut = RenderChat(new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }, p => p
            .Add(c => c.HasMoreBefore, true)
            .Add(c => c.OnLoadOlder, EventCallback.Factory.Create(this, () => clicked = true)));

        cut.WaitForAssertion(() => cut.Find("[data-testid='chat-load-older']").Should().NotBeNull());

        // The keyboard/tab focus is ON the button when it is clicked.
        cut.Find("[data-testid='chat-load-older']").TriggerEvent("onfocusin", new FocusEventArgs());
        cut.Find("[data-testid='chat-load-older']").Click();
        clicked.Should().BeTrue("the click fired the host's load handler");

        // The host prepends; the render applies it and moves focus onto the previously-first
        // row (m1) — the button is aria-disabled and must not keep the focus.
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("old-0", "older"), Incoming("m1", "hello"), Incoming("m2", "hi"),
        }));

        cut.WaitForAssertion(() => handle.Calls.Should().Contain(c => c.Identifier == "focusMessage"
            && c.Args.SequenceEqual(new object?[] { "m1" }),
            "the click-initiated prepend focuses the previously-first row"));
        handle.Calls.Count(c => c.Identifier == "focusMessage").Should().Be(1,
            "the scroller focuses the anchor exactly once");

        // A NEAR-TOP-initiated prepend (the button never had focus) must not move focus.
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(false));
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        cut.WaitForAssertion(() => handle.Calls.Count(c => c.Identifier == "focusMessage")
            .Should().Be(1, "only the click-initiated path moves focus"));
    }

    [Fact]
    public async Task LoadOlder_StaleFocusAnchor_AutoPrependDoesNotStealFocus()
    {
        var (module, handle) = SetupScroller();
        var clicked = false;
        var cut = RenderChat(new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }, p => p
            .Add(c => c.HasMoreBefore, true)
            .Add(c => c.OnLoadOlder, EventCallback.Factory.Create(this, () => clicked = true)));

        cut.WaitForAssertion(() => cut.Find("[data-testid='chat-load-older']").Should().NotBeNull());

        // The keyboard focus is ON the button when it is clicked: the previously-first row is
        // captured as the focus anchor — but the host's load produces NO prepend (nothing
        // older arrived), so the anchor is now stale.
        cut.Find("[data-testid='chat-load-older']").TriggerEvent("onfocusin", new FocusEventArgs());
        cut.Find("[data-testid='chat-load-older']").Click();
        clicked.Should().BeTrue("the click fired the host's load handler");

        // A parameter set that is NOT a prepend (the load finished without older history)
        // must drop the stale anchor...
        cut.Render(p => p.Add(c => c.IsLoadingOlder, false));

        // ... so a LATER AUTO prepend (a fresh near-top visit, not the abandoned click) cannot
        // move focus onto a row the long-gone click knew about.
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(false));
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("old-0", "older"), Incoming("m1", "hello"), Incoming("m2", "hi"),
        }));

        cut.WaitForAssertion(() => handle.Calls.Should().NotContain(c => c.Identifier == "focusMessage",
            "a stale anchor from an abandoned click must not steal focus on a later auto prepend"));
    }

    // ── Jump button: focus contract ──────────────────────────────────────────

    [Fact]
    public async Task JumpButton_KeyboardActivation_FocusesTheComposer()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));
        handle.Calls.Clear();

        // Detail == 0: a KEYBOARD activation (Enter/Space) — the composer takes focus.
        cut.Find("[data-testid='chat-jump-to-new']").TriggerEvent(
            "onclick", new MouseEventArgs { Detail = 0 });

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(
            invocation => invocation.Identifier == "focusIfNeeded",
            "keyboard activation moves focus to the composer input — never to the body"));
        handle.Calls.Should().NotContain(c => c.Identifier == "focusMessage",
            "the composer exists, so the row fallback is not used");
    }

    [Fact]
    public async Task JumpButton_PointerActivation_FocusesTheLastRow()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello"), Incoming("m2", "hi")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi"), Incoming("m3", "third") }));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));
        handle.Calls.Clear();

        // detail > 0: a POINTER activation — the user is pointing, not typing: the last row
        // takes focus (preventScroll), NOT the composer.
        cut.Find("[data-testid='chat-jump-to-new']").Click();

        cut.WaitForAssertion(() => handle.Calls.Should().Contain(c => c.Identifier == "focusMessage"
            && c.Args.SequenceEqual(new object?[] { "m3" }),
            "the pointer path focuses the last message row through the scroller"));
        module.Invocations.Should().NotContain(
            invocation => invocation.Identifier == "focusIfNeeded",
            "the pointer path must not steal focus for the composer");
    }

    [Fact]
    public async Task JumpButton_KeyboardActivationWithoutComposer_FocusesTheLastRow()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")], p => p.Add(c => c.ShowComposer, false));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi") }));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));
        handle.Calls.Clear();

        // Detail == 0 with ShowComposer=false: the fallback chain keeps focus off the body —
        // the last message row takes it.
        cut.Find("[data-testid='chat-jump-to-new']").TriggerEvent(
            "onclick", new MouseEventArgs { Detail = 0 });

        cut.WaitForAssertion(() => handle.Calls.Should().Contain(c => c.Identifier == "focusMessage"
            && c.Args.SequenceEqual(new object?[] { "m2" }),
            "without a composer the keyboard path falls back to the last row"));
    }

    [Fact]
    public async Task JumpButton_CustomTemplate_ComposesTheAriaLabel()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")], p => p
            .Add(c => c.JumpToNewTemplate, (RenderFragment<int>)(count => builder =>
                builder.AddContent(0, $"custom {count}"))));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false;

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi"), Incoming("m3", "third") }));

        cut.WaitForAssertion(() =>
        {
            var button = cut.Find("[data-testid='chat-jump-to-new']");
            button.TextContent.Should().Contain("custom 2",
                "the template replaces the button content and receives the count");
            button.GetAttribute("aria-label").Should().Be("2 new messages, Jump to new messages",
                "the host-owned content is opaque, so the label composes the count + the action name");
        });
    }

    // ── Public API ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IsAtBottom_FollowsTheScrollReports()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "hello")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        cut.Instance.IsAtBottom.Should().BeTrue("the initial state assumes the bottom");

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        cut.Instance.IsAtBottom.Should().BeFalse("the JS scroller reported the user scrolled up");

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(true));
        cut.Instance.IsAtBottom.Should().BeTrue("the user is back at the bottom");
    }
}
