using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using System.Threading;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Accessibility contract of <see cref="TmChat"/> (FZE-7): EXACTLY ONE live region for the whole
/// component (the announcements region — the banner, the typing indicator and every template stay
/// silent), a trailing-throttled announcement pipeline whose TEXT the host supplies
/// (<c>MessageAnnouncement</c> / <c>BatchAnnouncement</c> / <c>AnnounceAsync</c>), the messages
/// container as a labelled keyboard-scrollable <c>role="region"</c> (never <c>role="log"</c>,
/// which would imply a second live region), an <c>aria-label</c> on the built-in composer textarea
/// and focus hooks: <c>FocusMessageAsync</c> through the container-scoped <c>chat.js</c> scroller
/// and focus restoration onto the button that opened a thread after the thread closes.
/// <para>
/// TDD note: the file ran RED against the pre-FZE-7 component — the announcement region, the new
/// parameters and every role/label did not exist. The stub run failed e.g. with
/// <c>System.Exception: Unable to find element matching '[data-testid=chat-announcements]'</c>
/// (NoAnnouncement_ByDefault_RegionEmpty), <c>Expected cut.FindAll("[aria-live]").Count to be 1,
/// but found 0</c> (ExactlyOneLiveRegion), <c>Expected scroller.GetAttribute("role") to be
/// "region", but found null</c> and <c>Expected cut.Find(".tm-chat__input").GetAttribute
/// ("aria-label") to be "Type a message…", but found null</c>.
/// </para>
/// </summary>
public class TmChatA11yTests : LocalizationTestBase
{
    private const string ChatModulePath = "./_content/Tempo.Blazor/js/chat.js";

    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");
    private static readonly ChatUser Jana = new("u3", "Jana", avatar: "J");

    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(1);

    private static ChatMessage Incoming(string id, string text) =>
        new(id, text, Bob, ChatMessageType.Incoming);

    private static ChatMessage Own(string id, string text) =>
        new(id, text, Me, ChatMessageType.Outgoing);

    private static ChatMessage ReplyTo(string rootId, string id, string text) =>
        new(id, text, Bob, ChatMessageType.Incoming) { ThreadRootId = rootId };

    private static IElement Region(IRenderedComponent<TmChat> cut) =>
        cut.Find("[data-testid='chat-announcements']");

    /// <summary>Registers a <see cref="FakeTimeProvider"/> as the component's clock.</summary>
    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    /// <summary>Drains renderer-dispatched work queued by timer callbacks.</summary>
    private static Task DrainDispatcherAsync(IRenderedComponent<TmChat> cut)
        => cut.InvokeAsync(() => { });

    private (BunitJSModuleInterop Module, RecordingScrollerHandle Handle) SetupScroller()
    {
        var module = JSInterop.SetupModule(ChatModulePath);
        var handle = new RecordingScrollerHandle();
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

    /// <summary>A thread root m1 with one reply m2 — the thread badge renders on m1.</summary>
    private static readonly ChatMessage[] ThreadedMessages =
    [
        Incoming("m1", "thread root"),
        ReplyTo("m1", "m2", "a reply"),
    ];

    // ── The single live region ───────────────────────────────────────────────

    [Fact]
    public void NoAnnouncement_ByDefault_RegionEmpty()
    {
        var cut = RenderChat([Incoming("m1", "hello")]);

        var region = Region(cut);
        region.GetAttribute("class").Should().Contain("tm-sr-only", "the region is visually hidden");
        region.GetAttribute("aria-live").Should().Be("polite");
        region.GetAttribute("aria-atomic").Should().Be("true");
        region.TextContent.Should().BeEmpty(
            "with no MessageAnnouncement and no AnnounceAsync call nothing is ever announced");
    }

    [Fact]
    public void ExactlyOneLiveRegion()
    {
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.Messages, new[] { Incoming("m1", "hello") })
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.BannerContent, Banner("Offline — reconnecting…"))
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));

        cut.FindAll("[aria-live]").Should().HaveCount(1,
            "the banner and the typing indicator must not turn into a second live region");
        var region = cut.Find("[aria-live]");
        region.GetAttribute("aria-live").Should().Be("polite");
        region.GetAttribute("aria-atomic").Should().Be("true");
        region.GetAttribute("data-testid").Should().Be("chat-announcements");

        // The SAME count over the live-region ROLES: role=status/alert/log/marquee/timer all
        // imply aria-live — a hidden second live region must not slip in through a role alone.
        LiveRegionSelectorMatches(cut).Should().Be(1,
            "exactly one element carries a live region (attribute or role) — the announcements region");
    }

    /// <summary>
    /// Counts the elements matching the live-region contract: <c>[aria-live]</c> plus every role
    /// that implies one (status, alert, log, marquee, timer). The same selector the E2E spec uses.
    /// </summary>
    private static int LiveRegionSelectorMatches(IRenderedComponent<TmChat> cut) =>
        cut.FindAll("[aria-live], [role='status'], [role='alert'], [role='log'], [role='marquee'], [role='timer']").Count;

    [Fact]
    public void TypingIsNeverAnnounced()
    {
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.Messages, new[] { Incoming("m1", "hello") })
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));

        cut.Find(".tm-chat__typing-indicator").Should().NotBeNull();
        Region(cut).TextContent.Should().BeEmpty("typing is visual only — never announced");

        // A typing-only re-render (a second user starts typing) announces nothing either.
        cut.Render(p => p.Add(c => c.TypingUsers, new[] { Jana, Bob }));
        Region(cut).TextContent.Should().BeEmpty();
    }

    // ── MessageAnnouncement ──────────────────────────────────────────────────

    [Fact]
    public async Task MessageAnnouncement_NewIncoming_AnnouncesHostText()
    {
        var cut = RenderChat([Incoming("m1", "hello")], p => p.Add(
            c => c.MessageAnnouncement,
            (Func<ChatMessage, string?>)(m => $"New message from {m.Author?.Name}: {m.Text}")));

        Region(cut).TextContent.Should().BeEmpty("the initial load is not announced");

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi there") }));

        cut.WaitForAssertion(() => Region(cut).TextContent.Should()
            .Be("New message from Bob: hi there", "the host's text is announced verbatim"));
    }

    [Fact]
    public void MessageAnnouncement_NullResult_IsNotAnnounced()
    {
        var cut = RenderChat([Incoming("m1", "hello")], p => p.Add(
            c => c.MessageAnnouncement,
            (Func<ChatMessage, string?>)(m => m.Kind == "announce" ? m.Text : null)));

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), new ChatMessage("m2", "quiet", Bob) { Kind = "quiet" } }));

        cut.WaitForAssertion(() => Region(cut).TextContent.Should().BeEmpty(
            "a null result from MessageAnnouncement means 'do not announce this message'"));
    }

    [Fact]
    public void InitialLoadAndPrepend_AreNotAnnounced()
    {
        var cut = RenderChat(
            [Incoming("m1", "one"), Incoming("m2", "two")],
            p => p.Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));

        Region(cut).TextContent.Should().BeEmpty("the initial load is not an announcement");

        // A prepend of older history preserves the reading position — and announces nothing.
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m0", "older"), Incoming("m1", "one"), Incoming("m2", "two"),
        }));

        cut.WaitForAssertion(() => Region(cut).TextContent.Should().BeEmpty("a prepend is not news"));

        // A fill from an EMPTY list is an initial load too (a loaded history is not news).
        var filled = RenderChat([], p => p.Add(
            c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));
        filled.Render(p => p.Add(c => c.Messages, new[] { Incoming("m1", "one") }));
        filled.WaitForAssertion(() => Region(filled).TextContent.Should().BeEmpty());
    }

    [Fact]
    public void OwnMessages_NotAnnounced_ByDefault()
    {
        var cut = RenderChat([Incoming("m1", "hello")], p => p.Add(
            c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Own("m2", "my own message") }));

        cut.WaitForAssertion(() => Region(cut).TextContent.Should().BeEmpty(
            "the user's own messages are not announced by default (they know what they typed)"));

        // Opting in announces them through the same region.
        var optedIn = RenderChat([Incoming("m1", "hello")], p => p
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text))
            .Add(c => c.AnnounceOwnMessages, true));
        optedIn.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Own("m2", "my own message") }));
        optedIn.WaitForAssertion(() => Region(optedIn).TextContent.Should().Be("my own message"));
    }

    // ── Trailing throttle ────────────────────────────────────────────────────

    [Fact]
    public async Task Announcements_TrailingThrottle_OnlyLastWithinWindow()
    {
        var time = UseFakeClock();
        var cut = RenderChat([Incoming("m1", "first")], p => p.Add(
            c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));

        // The window is free: the first arrival is announced immediately (the leading flush).
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "first"), Incoming("m2", "second") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("second"));

        // A burst WITHIN the window is held: nothing is announced while the throttle is pending.
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m1", "first"), Incoming("m2", "second"), Incoming("m3", "third"),
        }));
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m1", "first"), Incoming("m2", "second"), Incoming("m3", "third"),
            Incoming("m4", "fourth"),
        }));
        Region(cut).TextContent.Should().Be("second", "the burst is held until the window ends");

        time.Advance(Throttle);
        await DrainDispatcherAsync(cut);

        Region(cut).TextContent.Should().Be("fourth",
            "the trailing flush announces the LATEST text of the window");
    }

    [Fact]
    public async Task Announcements_TrailingThrottle_BatchAnnouncement_Summarizes()
    {
        var time = UseFakeClock();
        var cut = RenderChat([Incoming("m1", "first")], p => p
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text))
            .Add(c => c.BatchAnnouncement,
                (Func<IReadOnlyList<ChatMessage>, string?>)(batch => $"{batch.Count} new messages")));

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "first"), Incoming("m2", "second") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("second",
            "a single arrival is announced through its own text, not the summary"));

        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m1", "first"), Incoming("m2", "second"), Incoming("m3", "third"),
        }));
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m1", "first"), Incoming("m2", "second"), Incoming("m3", "third"),
            Incoming("m4", "fourth"),
        }));

        time.Advance(Throttle);
        await DrainDispatcherAsync(cut);

        Region(cut).TextContent.Should().Be("2 new messages",
            "the host's BatchAnnouncement summarizes the messages held by the window");
    }

    // ── AnnounceAsync (host actions) ─────────────────────────────────────────

    [Fact]
    public async Task AnnounceAsync_PublicApi_UsesSameRegionAndThrottle()
    {
        var time = UseFakeClock();
        var cut = RenderChat([Incoming("m1", "hello")]);

        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Draft approved"));

        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("Draft approved",
            "the host action goes through the SAME single live region"));
        cut.FindAll("[aria-live]").Should().HaveCount(1);

        // A second call WITHIN the window is deferred by the same throttle.
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Draft approved"));
        cut.Instance.AnnouncementTimerIsPending.Should().BeTrue(
            "AnnounceAsync shares the trailing throttle with the message pipeline");
        Region(cut).TextContent.Should().Be("Draft approved");

        time.Advance(Throttle);
        await DrainDispatcherAsync(cut);
        Region(cut).TextContent.Should().Be("Draft approved", "the deferred text is announced at window end");
    }

    [Fact]
    public async Task SameTextTwice_KeyedSpan_IsAnnouncedAgainAsANewNode()
    {
        var time = UseFakeClock();
        var host = Render<AnnouncementHost>(p => p.Add(
            x => x.Messages, new[] { Incoming("m1", "hello") }));
        var cut = host.FindComponent<TmChat>();

        // First append: the free window flushes at once — sequence 1.
        host.Render(p => p.Add(
            x => x.Messages, new[] { Incoming("m1", "hello"), Incoming("m2", "same text") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("same text"));
        cut.Instance.AnnouncementSequence.Should().Be(1);
        var firstSpan = cut.Find("[data-testid='chat-announcements'] > span");

        time.Advance(Throttle + TimeSpan.FromMilliseconds(100));
        await DrainDispatcherAsync(cut);

        // Second append of the SAME sentence: the keyed span is REPLACED (a new DOM node), so
        // the live region re-announces without a two-phase clear-then-set render.
        host.Render(p => p.Add(
            x => x.Messages, new[] { Incoming("m1", "hello"), Incoming("m2", "same text"), Incoming("m3", "same text") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("same text"));
        cut.Instance.AnnouncementSequence.Should().Be(2,
            "the same sentence announces again — the sequence (and the span) moved on");
        var secondSpan = cut.Find("[data-testid='chat-announcements'] > span");
        ReferenceEquals(firstSpan, secondSpan).Should().BeFalse(
            "the @key destroyed the old span and inserted a new one — an unchanged text node would stay silent");
    }

    [Fact]
    public void HostClickHandler_AnnounceAsync_ReachesTheSameRegion()
    {
        var host = Render<AnnouncementHost>(p => p.Add(
            x => x.Messages, new[] { Incoming("m1", "hello") }));
        var cut = host.FindComponent<TmChat>();

        // The host's own UI (a click handler rendering TmChat from its own markup) announces
        // through the component's single region.
        host.Find("#host-announce").Click();

        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("Draft approved"));
        cut.Instance.AnnouncementSequence.Should().Be(1);
        cut.FindAll("[aria-live]").Should().HaveCount(1);
    }

    [Fact]
    public async Task AnnounceAsync_ThenSingleMessage_UsesIndividualText_NotBatch()
    {
        var time = UseFakeClock();
        var cut = RenderChat([Incoming("m1", "hello")], p => p
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text))
            .Add(c => c.BatchAnnouncement,
                (Func<IReadOnlyList<ChatMessage>, string?>)(batch => $"{batch.Count} new messages")));

        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Draft approved"));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("Draft approved"));

        // A single message inside the same window: the flush announces its INDIVIDUAL text —
        // the batch summary is never consulted for a lone message (the host action already went
        // out in the free window; it is not re-read at the flush).
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "hi there") }));
        time.Advance(Throttle);
        await DrainDispatcherAsync(cut);

        Region(cut).TextContent.Should().Be("hi there",
            "the single message is announced through its own text, not the batch summary");
        cut.Instance.AnnouncementSequence.Should().Be(2, "exactly one more announcement was rendered");
    }

    [Fact]
    public async Task HostActionAndMessageInOneWindow_BothSurvive()
    {
        var time = UseFakeClock();
        var cut = RenderChat([Incoming("m1", "hello")], p => p
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text))
            .Add(c => c.BatchAnnouncement,
                (Func<IReadOnlyList<ChatMessage>, string?>)(batch => $"{batch.Count} new messages")));

        // A message in a free window is announced at once…
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "first") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("first"));

        // …then a burst of two messages AND a host action land INSIDE the same window.
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m1", "hello"), Incoming("m2", "first"), Incoming("m3", "second"), Incoming("m4", "third"),
        }));
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Draft approved"));

        time.Advance(Throttle);
        await DrainDispatcherAsync(cut);

        Region(cut).TextContent.Should().Be("Draft approved 2 new messages",
            "the host action and the messages of one window both survive — composed around the summary");
    }

    [Fact]
    public async Task TailReplacedReload_AnnouncesOnlyUnseenIds()
    {
        var time = UseFakeClock();
        var cut = RenderChat([Incoming("m1", "first")], p => p
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text))
            .Add(c => c.BatchAnnouncement,
                (Func<IReadOnlyList<ChatMessage>, string?>)(batch => $"{batch.Count} new messages")));

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "first"), Incoming("m2", "second") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("second"));

        time.Advance(Throttle + TimeSpan.FromMilliseconds(100));
        await DrainDispatcherAsync(cut);

        // An optimistic send is announced, then the host's reload replaces the tail: the
        // confirmed row arrives with a fresh id while the already-seen history is re-delivered.
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "first"), Incoming("m2", "second"), Incoming("m3-draft", "sending…") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("sending…"));

        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m1", "first"), Incoming("m2", "second"),
            Incoming("m3-confirmed", "delivered"), Incoming("m4", "fourth"),
        }));
        time.Advance(Throttle);
        await DrainDispatcherAsync(cut);

        Region(cut).TextContent.Should().Be("2 new messages",
            "only the never-seen ids (m3-confirmed, m4) are news — the re-delivered history (m2) is dropped");
    }

    [Fact]
    public async Task DuplicateIdArrival_IsNotReannounced()
    {
        var cut = RenderChat([Incoming("m1", "hello")], p => p.Add(
            c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "second") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("second"));

        // A page-overlap re-delivery duplicates an id the component has already announced —
        // the seen-id set swallows it and the region keeps its previous content.
        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "hello"), Incoming("m2", "second"), Incoming("m2", "second") }));

        cut.Instance.AnnouncementSequence.Should().Be(1, "the duplicate id was not queued again");
        Region(cut).TextContent.Should().Be("second");
    }

    [Fact]
    public void AnnounceAsync_WhitespaceText_IsNoOp()
    {
        var cut = RenderChat([Incoming("m1", "hello")]);

        cut.InvokeAsync(() => cut.Instance.AnnounceAsync("   ")).Wait();

        Region(cut).TextContent.Should().BeEmpty("an empty announcement must not clear the region");
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_CancelsPendingAnnouncement()
    {
        var time = UseFakeClock();
        var cut = RenderChat([Incoming("m1", "first")], p => p.Add(
            c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => m.Text)));

        cut.Render(p => p.Add(c => c.Messages,
            new[] { Incoming("m1", "first"), Incoming("m2", "second") }));
        cut.WaitForAssertion(() => Region(cut).TextContent.Should().Be("second"));

        // A burst within the window arms the trailing flush…
        cut.Render(p => p.Add(c => c.Messages, new[]
        {
            Incoming("m1", "first"), Incoming("m2", "second"), Incoming("m3", "third"),
        }));
        cut.Instance.AnnouncementTimerIsPending.Should().BeTrue("the trailing flush is armed");

        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        cut.Instance.AnnouncementTimerIsPending.Should().BeFalse(
            "DisposeAsync must cancel the pending announcement");

        var act = () => time.Advance(Throttle + TimeSpan.FromSeconds(1));
        act.Should().NotThrow("a cancelled timer must never fire into a disposed component");
    }

    // ── Roles and labels ─────────────────────────────────────────────────────

    [Fact]
    public void MessageRegion_IsARegionWithLabelAndKeyboardScrolling()
    {
        var cut = RenderChat([Incoming("m1", "hello")]);

        var scroller = cut.Find(".tm-chat__messages");
        scroller.GetAttribute("role").Should().Be("region",
            "role=log would imply a SECOND live region — a plain region is labelled instead");
        scroller.GetAttribute("aria-label").Should().Be("Messages",
            "the default label comes from the localized TmChat_MessagesLabel");
        scroller.GetAttribute("tabindex").Should().Be("0",
            "a scrollable region must be keyboard-focusable (WCAG 2.1 scrolling areas)");
    }

    [Fact]
    public void MessageRegion_AriaLabel_IsOverridable()
    {
        var cut = RenderChat([Incoming("m1", "hello")], p => p.Add(c => c.MessagesAriaLabel, "Team chat"));

        cut.Find(".tm-chat__messages").GetAttribute("aria-label").Should().Be("Team chat");
    }

    [Fact]
    public void ComposerTextarea_HasAnAriaLabel()
    {
        var cut = RenderChat([Incoming("m1", "hello")]);

        cut.Find("textarea.tm-chat__input").GetAttribute("aria-label").Should().Be("Type a message…",
            "the textarea previously had only a placeholder (no accessible name)");

        var renamed = RenderChat([Incoming("m1", "hello")], p => p.Add(c => c.Placeholder, "Write to Bob…"));
        renamed.Find("textarea.tm-chat__input").GetAttribute("aria-label").Should().Be("Write to Bob…",
            "the default aria-label follows the effective placeholder");

        var labelled = RenderChat([Incoming("m1", "hello")], p => p
            .Add(c => c.Placeholder, "Write to Bob…")
            .Add(c => c.ComposerAriaLabel, "Message Bob"));
        labelled.Find("textarea.tm-chat__input").GetAttribute("aria-label").Should().Be("Message Bob");
    }

    [Fact]
    public void ThreadReplyTextarea_HasAnAriaLabel()
    {
        var cut = RenderChat(ThreadedMessages, p => p.Add(
            c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.Find("[data-testid='chat-thread-open-m1']").Click();
        cut.Find(".tm-chat__thread-input").GetAttribute("aria-label").Should().NotBeNullOrEmpty(
            "the thread reply input must not rely on its placeholder alone");
    }

    [Fact]
    public void MessageEditTextarea_HasAnAriaLabel()
    {
        var cut = RenderChat([Own("m1", "my message")], p => p.Add(
            c => c.OnEditMessage, EventCallback.Factory.Create<ChatMessageEdit>(this, _ => { })));

        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.Find("textarea.tm-chat__edit-input").GetAttribute("aria-label").Should().NotBeNullOrEmpty(
            "the inline edit input had no accessible name at all");
    }

    // ── Focus hooks ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FocusMessageAsync_UsesTheContainerScopedScroller()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat([Incoming("m1", "one"), Incoming("m2", "two")]);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        var found = await cut.InvokeAsync(async () => await cut.Instance.FocusMessageAsync("m1"));

        found.Should().BeTrue("the scroller handle reports whether the anchor was found");
        handle.Calls.Should().Contain(c => c.Identifier == "focusMessageVisible"
            && c.Args.SequenceEqual(new object?[] { "m1" }),
            "FocusMessageAsync goes through the chat.js scroller scoped to the messages container");
        handle.Calls.Count(c => c.Identifier == "focusMessageVisible").Should().Be(1);

        // An empty id is a no-op BEFORE any interop; an UNKNOWN id is passed through — the
        // scroller owns the lookup and reports "not found" itself.
        await cut.InvokeAsync(() => cut.Instance.FocusMessageAsync(""));
        handle.Calls.Count(c => c.Identifier == "focusMessageVisible").Should().Be(1);
        await cut.InvokeAsync(() => cut.Instance.FocusMessageAsync("missing"));
        handle.Calls.Last(c => c.Identifier == "focusMessageVisible").Args
            .Should().Equal(new object?[] { "missing" });
        handle.Calls.Count(c => c.Identifier == "focusMessageVisible").Should().Be(2);
    }

    [Fact]
    public async Task FocusMessageAsync_ThreadPanelOpen_StillQueriesTheContainerOnly()
    {
        var (module, handle) = SetupScroller();
        var cut = RenderChat(ThreadedMessages);
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        // The thread panel renders the root message a SECOND time; the focus must still resolve
        // exactly once — through the container-scoped scroller, never a page-wide query.
        await cut.InvokeAsync(async () => await cut.Instance.FocusMessageAsync("m1"));

        handle.Calls.Count(c => c.Identifier == "focusMessageVisible").Should().Be(1);
    }

    [Fact]
    public async Task ThreadClose_ReturnsFocusToTheOpenerButton()
    {
        var (module, _) = SetupScroller();
        var cut = RenderChat(ThreadedMessages, p => p.Add(
            c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        cut.Find("[data-testid='chat-thread-open-m1']").Click();
        cut.Find("[data-testid='chat-thread-panel']").Should().NotBeNull();

        cut.Find("[data-testid='chat-thread-close']").Click();
        cut.FindAll("[data-testid='chat-thread-panel']").Should().BeEmpty("the thread closed");

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(
            invocation => invocation.Identifier == "restoreFocus",
            "focus returns to the button that opened the thread instead of falling to the body"));
        module.Invocations.Count(i => i.Identifier == "restoreFocus").Should().Be(1, "restored exactly once");
    }

    [Fact]
    public async Task ThreadOpen_FocusesTheThreadInput()
    {
        var (module, _) = SetupScroller();
        var cut = RenderChat(ThreadedMessages, p => p.Add(
            c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        // Opening a thread through the built-in UI moves the focus to the panel's reply input
        // after the render that mounted it (best effort). "Blazor._internal.domWrapper.focus"
        // is how bUnit surfaces ElementReference.FocusAsync.
        cut.Find("[data-testid='chat-thread-open-m1']").Click();

        cut.WaitForAssertion(() => JSInterop.Invocations
            .Count(i => i.Identifier == "Blazor._internal.domWrapper.focus").Should().Be(1,
                "the panel's reply input is focused once the render mounted it"));
    }

    [Fact]
    public async Task ThreadPanel_Escape_ClosesAndRestoresFocus()
    {
        var (module, _) = SetupScroller();
        var cut = RenderChat(ThreadedMessages, p => p.Add(
            c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        cut.Find("[data-testid='chat-thread-open-m1']").Click();
        cut.Find("[data-testid='chat-thread-panel']").Should().NotBeNull();

        // Escape wherever inside the panel the focus sits (dispatched ON the aside — the
        // container-level dismiss key) closes the thread…
        cut.Find("[data-testid='chat-thread-panel']").KeyDown("Escape");
        cut.FindAll("[data-testid='chat-thread-panel']").Should().BeEmpty("Escape dismissed the panel");

        // …and the opener button regains the focus through the same restore path.
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(
            invocation => invocation.Identifier == "restoreFocus",
            "the Escape close restores the focus the same way the button close does"));
    }

    [Fact]
    public async Task ThreadClose_ReplyActionOpener_RegainsFocusToo()
    {
        var (module, _) = SetupScroller();
        var cut = RenderChat(ThreadedMessages, p => p.Add(
            c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        cut.Find("[data-testid='chat-reply-m1']").Click();
        cut.Find("[data-testid='chat-thread-panel']").Should().NotBeNull();
        cut.Find("[data-testid='chat-thread-close']").Click();

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(
            invocation => invocation.Identifier == "restoreFocus",
            "the reply action is an opener too"));
    }

    [Fact]
    public async Task ThreadClose_ParameterDrivenUnmount_RegainsFocus()
    {
        var (module, _) = SetupScroller();
        var cut = RenderChat(ThreadedMessages, p => p.Add(
            c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));
        cut.WaitForAssertion(() => module.Invocations["attachScroller"].Should().NotBeEmpty());

        cut.Find("[data-testid='chat-thread-open-m1']").Click();
        cut.Find("[data-testid='chat-thread-panel']").Should().NotBeNull();

        // The thread's ROOT MESSAGE leaves Messages (deleted / reloaded host store) — the panel
        // unmounts parameter-driven, and focus still returns to the button that opened it.
        cut.Render(p => p.Add(c => c.Messages, new[] { ReplyTo("m1", "m2", "a reply") }));

        cut.FindAll("[data-testid='chat-thread-panel']").Should().BeEmpty("the thread closed");
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(
            invocation => invocation.Identifier == "restoreFocus",
            "a parameter-driven close restores focus the same way"));
    }

    // ── Keyboard: no hover-only affordance ───────────────────────────────────

    [Fact]
    public void MessageActions_AreRevealedOnItemFocusWithin_InBothStylesheets()
    {
        // The carry-forward of the slots phase: the per-message actions must not be hover-only —
        // keyboard users reach them through :focus-within on the message item, in the scoped
        // stylesheet AND in the global fallback.
        var scoped = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Chat", "TmChat.razor.css")));
        var fallback = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_chat.css")));

        foreach (var (css, origin) in new[] { (scoped, "TmChat.razor.css"), (fallback, "_chat.css") })
        {
            var body = RuleBody(css, ".tm-chat__item:focus-within .tm-chat__actions");
            body.Should().NotBeNull(
                "{0} must reveal .tm-chat__actions when the message item holds focus", origin);
            body.Should().Contain("opacity: 1").And.Contain("pointer-events: auto");
        }
    }

    [Fact]
    public void MessageActions_AreAlwaysRevealedOnTouch_InBothStylesheets()
    {
        // Touch has no hover: the hide-by-default bar must never hide behind a gesture a touch
        // device cannot perform — the coarse-pointer / hover-less media query reveals it.
        var scoped = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Chat", "TmChat.razor.css")));
        var fallback = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_chat.css")));

        foreach (var (css, origin) in new[] { (scoped, "TmChat.razor.css"), (fallback, "_chat.css") })
        {
            var body = MediaRuleBody(css, "(hover: none), (pointer: coarse)", ".tm-chat__actions");
            body.Should().NotBeNull(
                "{0} must reveal .tm-chat__actions on touch (media {1})", origin, "(hover: none), (pointer: coarse)");
            body.Should().Contain("opacity: 1").And.Contain("pointer-events: auto");
        }
    }

    [Fact]
    public void MessageRegion_HasAVisibleKeyboardFocusOutline_InBothStylesheets()
    {
        // The messages container is a Tab stop (tabindex=0): a keyboard user must see the focus.
        var scoped = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Chat", "TmChat.razor.css")));
        var fallback = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_chat.css")));

        foreach (var (css, origin) in new[] { (scoped, "TmChat.razor.css"), (fallback, "_chat.css") })
        {
            var body = RuleBody(css, ".tm-chat__messages:focus-visible");
            body.Should().NotBeNull("{0} must style .tm-chat__messages:focus-visible", origin);
            body.Should().Contain("outline: 2px solid var(--tm-border-color-focus)")
                .And.Contain("outline-offset: -2px", "the outline is INSET so the scroller's clipping keeps it visible");
        }
    }

    /// <summary>
    /// The declaration body of <paramref name="selector"/> inside the first
    /// <c>@media <paramref name="condition"/></c> block that carries it, or
    /// <see langword="null"/>.
    /// </summary>
    private static string? MediaRuleBody(string css, string condition, string selector)
    {
        var escaped = System.Text.RegularExpressions.Regex.Escape(condition);
        var media = new System.Text.RegularExpressions.Regex(
            $@"@media [^{{]*{escaped}\s*\{{(?<block>([^{{}}]|\{{[^{{}}]*\}})*)\}}");
        foreach (System.Text.RegularExpressions.Match mediaMatch in media.Matches(css))
        {
            var body = RuleBody(mediaMatch.Groups["block"].Value, selector);
            if (body is not null) return body;
        }

        return null;
    }

    /// <summary>
    /// The declaration body of the first rule that lists <paramref name="selector"/> as one of its
    /// comma-separated parts (exact match), or <see langword="null"/>.
    /// </summary>
    private static string? RuleBody(string css, string selector)
    {
        var rules = new System.Text.RegularExpressions.Regex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}");
        foreach (System.Text.RegularExpressions.Match rule in rules.Matches(css))
        {
            var parts = ThemeCss.Normalise(rule.Groups["selector"].Value)
                .Split(',').Select(part => part.Trim());
            if (parts.Contains(selector, StringComparer.Ordinal))
            {
                return rule.Groups["body"].Value;
            }
        }

        return null;
    }

    private static RenderFragment Banner(string text) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "banner");
        builder.AddContent(2, text);
        builder.CloseElement();
    };

    /// <summary>
    /// A host that renders <see cref="TmChat"/> from ITS OWN markup (plus an announcing button):
    /// the parameter set — and therefore the announcement flush — happens inside the PARENT's
    /// render batch, the render-tree shape a real host has. Used by the keyed-announcement tests.
    /// </summary>
    private sealed class AnnouncementHost : ComponentBase
    {
        [Parameter] public IReadOnlyList<ChatMessage> Messages { get; set; } = [];

        public TmChat? Chat { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmChat>(0);
            builder.AddAttribute(1, nameof(TmChat.CurrentUser), Me);
            builder.AddAttribute(2, nameof(TmChat.Messages), Messages);
            builder.AddAttribute(3, nameof(TmChat.MessageAnnouncement),
                (Func<ChatMessage, string?>)(m => m.Text));
            builder.AddComponentReferenceCapture(4, reference => Chat = (TmChat)reference);
            builder.CloseComponent();

            builder.OpenElement(5, "button");
            builder.AddAttribute(6, "type", "button");
            builder.AddAttribute(7, "id", "host-announce");
            builder.AddAttribute(8, "onclick", EventCallback.Factory.Create(this,
                () => Chat?.AnnounceAsync("Draft approved")));
            builder.AddContent(9, "Announce");
            builder.CloseElement();
        }
    }

    /// <summary>A recording scroller handle double (the focusMessageVisible calls are what this file pins).</summary>
    private sealed class RecordingScrollerHandle : IJSObjectReference
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args = null)
        {
            Calls.Add((identifier, args ?? Array.Empty<object?>()));
            // The boolean scroller calls (focusMessageVisible, scrollToMessage) report "found".
            return typeof(TValue) == typeof(bool)
                ? new ValueTask<TValue>((TValue)(object)true)
                : default;
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
}
