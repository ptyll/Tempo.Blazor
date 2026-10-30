using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Threading.Tasks;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// The opt-in render window of <see cref="TmChat"/> (FZE-14, decision
/// <c>TMCHAT-RENDER-WINDOW</c>): a very long conversation renders a contiguous
/// <c>[Start, count)</c> slice of the root sequence that grows upward by
/// <c>RenderWindowStep</c> rows, while every data-driven contract — announcements, the
/// jump-to-new count, read receipts, the draft guard, <c>Index</c>/<c>IsFirstInGroup</c>
/// grouping — keeps working over the FULL <c>Messages</c> sequence, and the scroll contract
/// keeps holding because a window expansion is a render PREPEND for the <c>chat.js</c>
/// scroller. With <c>RenderWindowSize=null</c> the component renders every row (today's
/// behavior). The pure window rules are covered by <see cref="ChatRenderWindowStateTests"/>; the
/// bUnit fake handle REJECTS any identifier outside the documented <c>chat.js</c> contract.
/// </summary>
public class TmChatRenderWindowTests : LocalizationTestBase
{
    private const string ChatModulePath = "./_content/Tempo.Blazor/js/chat.js";

    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private static readonly DateTimeOffset Ts = new(2026, 5, 2, 10, 30, 0, TimeSpan.Zero);

    private static ChatMessage Incoming(string id, string text) =>
        new(id, text, Bob, ChatMessageType.Incoming);

    private static List<ChatMessage> Messages(int count, string prefix = "m")
        => Enumerable.Range(1, count).Select(i => Incoming($"{prefix}{i}", $"Message {prefix}{i}")).ToList();

    /// <summary>
    /// The scroller handle double: records every invocation, emulates the documented afterUpdate
    /// contract and REJECTS any identifier outside the chat.js scroller contract — a call the
    /// component must not make fails loudly instead of hanging silently.
    /// </summary>
    private sealed class FakeScrollerHandle : IJSObjectReference
    {
        /// <summary>The identifiers of the chat.js scroller handle the component may call.</summary>
        private static readonly HashSet<string> ContractIdentifiers =
            new() { "afterUpdate", "scrollToBottom", "scrollToMessage", "focusMessage", "focusMessageVisible", "isNearTop", "dispose" };

        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        /// <summary>Whether the viewport was at the bottom when the update started (the JS state).</summary>
        public bool WasAtBottom { get; set; } = true;

        /// <summary>Ids the emulated scroller can find (scrollToMessage/focusMessageVisible answer false otherwise).</summary>
        public HashSet<string> FindableIds { get; } = new();

        /// <summary>The live near-top geometry the emulated scroller answers for isNearTop.</summary>
        public bool IsNearTop { get; set; } = true;

        /// <summary>Parks the NEXT afterUpdate round trip until <see cref="ReleaseAfterUpdates"/>.</summary>
        private int _afterUpdatesToHold;

        /// <summary>Parked (mode, completion) pairs released by <see cref="ReleaseAfterUpdates"/>.</summary>
        private readonly List<(string? Mode, Action Complete)> _heldAfterUpdates = [];

        internal void HoldNextAfterUpdate() => _afterUpdatesToHold++;

        internal void ReleaseAfterUpdates()
        {
            var held = _heldAfterUpdates.ToArray();
            _heldAfterUpdates.Clear();
            foreach (var (_, complete) in held) complete();
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args = null)
        {
            if (!ContractIdentifiers.Contains(identifier))
            {
                throw new InvalidOperationException(
                    $"FakeScrollerHandle: '{identifier}' is not part of the chat.js scroller contract.");
            }

            Calls.Add((identifier, args ?? Array.Empty<object?>()));

            if (identifier == "afterUpdate")
            {
                var mode = args is [string m] ? m : null;
                var status = EmulateAfterUpdate(mode);
                if (_afterUpdatesToHold > 0)
                {
                    _afterUpdatesToHold--;
                    var completion = new TaskCompletionSource<TValue>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _heldAfterUpdates.Add((mode, () => completion.TrySetResult((TValue)(object)status!)));
                    return new(completion.Task);
                }

                return new((TValue)(object)status!);
            }

            return identifier switch
            {
                "scrollToMessage" => new((TValue)(object)(args is [string id] && FindableIds.Contains(id))),
                "focusMessageVisible" => new((TValue)(object)(args is [string id] && FindableIds.Contains(id))),
                "isNearTop" => new((TValue)(object)IsNearTop),
                _ => default, // scrollToBottom / focusMessage: void-ish
            };
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => default;

        private string EmulateAfterUpdate(string? mode) => mode switch
        {
            "append" => WasAtBottom ? "scrolled" : "unread",
            "prepend" => "preserved",
            _ => "none",
        };
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

    private static int RenderedRowCount(IRenderedComponent<TmChat> cut)
        => cut.FindAll(".tm-chat__item").Count;

    private static IReadOnlyList<string> RenderedIds(IRenderedComponent<TmChat> cut)
        => cut.FindAll(".tm-chat__item")
            .Select(el => el.GetAttribute("data-message-id") ?? string.Empty)
            .ToList();

    // ── The default stays byte-identical ─────────────────────────────────────

    [Fact]
    public void Default_RendersAllRows()
    {
        SetupScroller();
        var cut = RenderChat(Messages(30));

        RenderedRowCount(cut).Should().Be(30,
            "without RenderWindowSize every root row renders (today's behavior)");
        RenderedIds(cut).First().Should().Be("m1");
        RenderedIds(cut).Last().Should().Be("m30");
    }

    [Fact]
    public void RenderWindowSize_Null_MarkupIdenticalToUnset()
    {
        SetupScroller();
        var cut = RenderChat(Messages(12));
        var before = NormalizeElementReferences(cut.Markup);

        // The SAME instance re-rendered with an explicit null: an identical code path must
        // produce byte-identical markup (the per-render element-reference ids are normalized —
        // they are the component's own interop handles, not chat markup).
        cut.Render(p => p.Add(c => c.RenderWindowSize, (int?)null));

        NormalizeElementReferences(cut.Markup).Should().Be(before,
            "an explicit RenderWindowSize=null is the same code path as the default: byte-identical markup");
    }

    // bUnit render-tree noise: the per-render event-handler ids shift with the parameter
    // sequence, the element-reference ids and the CSS-isolation scope attribute are generated —
    // the CONTRACT is the element structure, so those are normalized away.
    private static readonly System.Text.RegularExpressions.Regex ElementReferenceRegex
        = new("blazor:elementReference=\"[^\"]*\"",
            System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex ScopeAttributeRegex
        = new("\\sb-[a-z0-9]+=\"\"", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex HandlerIdRegex
        = new(@"blazor:([a-z]+)=""\d+""", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string NormalizeElementReferences(string markup)
        => HandlerIdRegex.Replace(
            ScopeAttributeRegex.Replace(
                ElementReferenceRegex.Replace(markup, "blazor:elementReference=\"\""),
                string.Empty),
            m => m.Groups[1].Value + "=\"#\"");

    // The bubble time renders through ToLocalTime (HH:mm) — a machine-timezone detail, not chat
    // markup: the golden comparison collapses it.
    private static readonly System.Text.RegularExpressions.Regex TimeTextRegex
        = new(@">\d{2}:\d{2}<", System.Text.RegularExpressions.RegexOptions.Compiled);

    // ── The window itself ────────────────────────────────────────────────────

    [Fact]
    public void Window_RendersTailOnly()
    {
        SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        var ids = RenderedIds(cut);
        RenderedRowCount(cut).Should().Be(100, "the window renders the last `size` root rows");
        ids.First().Should().Be("m101", "the window is the TAIL of the sequence");
        ids.Last().Should().Be("m200");
    }

    [Fact]
    public void Window_SizeAndStep_Clamped()
    {
        SetupScroller();
        var cut = RenderChat(Messages(200), p => p
            .Add(c => c.RenderWindowSize, 5)
            .Add(c => c.RenderWindowStep, 3));

        RenderedRowCount(cut).Should().Be(20, "RenderWindowSize clamps to the minimum of 20");
        cut.Instance.RenderWindowStart.Should().Be(180);
    }

    // ── Rule (d): the top edge expands the window as a render prepend ───────

    [Fact]
    public async Task NearTop_ExpandsWindow_InvokesPrepend_NotOnLoadOlder()
    {
        var (_, handle) = SetupScroller();
        var loadOlderCalls = 0;
        var cut = RenderChat(Messages(200), p => p
            .Add(c => c.RenderWindowSize, 100)
            .Add(c => c.HasMoreBefore, true)
            .Add(c => c.OnLoadOlder, EventCallback.Factory.Create(this, () => loadOlderCalls++)));

        RenderedRowCount(cut).Should().Be(100);
        handle.Calls.Clear();

        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));

        cut.WaitForAssertion(() => RenderedRowCount(cut).Should().Be(150,
            "one step (50) is added above the window when the top edge is reached"));
        RenderedIds(cut).First().Should().Be("m51");
        cut.WaitForAssertion(() => handle.Calls.Should().Contain(
            c => c.Identifier == "afterUpdate" && c.Args.SequenceEqual(new object?[] { "prepend" }),
            "the expansion is applied as a render PREPEND (the scroller restores the reading position)"));
        loadOlderCalls.Should().Be(0,
            "the window expansion never calls OnLoadOlder — the host page load fires at Start == 0");
    }

    [Fact]
    public async Task NearTop_TransientReport_VetoedByLiveGeometry_DoesNotExpand()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        // A transient scroll clamp mid-churn (a deep-link restore, a huge window render) fires a
        // momentary near-top report while the live geometry is nowhere near the top band: the
        // expansion must be vetoed, or a single transient mounts thousands of rows.
        handle.IsNearTop = false;
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));

        cut.WaitForAssertion(() => RenderedRowCount(cut).Should().Be(100,
            "a near-top report vetoed by the live geometry does not expand the window"));
        handle.Calls.Should().NotContain(c => c.Identifier == "afterUpdate"
                && c.Args.SequenceEqual(new object?[] { "prepend" }),
            "no expansion render happened for the vetoed report");
    }

    [Fact]
    public async Task NearTop_AtStartWithHasMoreBefore_InvokesOnLoadOlder()
    {
        var (_, handle) = SetupScroller();
        var loadOlderCalls = 0;
        var cut = RenderChat(Messages(30), p => p
            .Add(c => c.RenderWindowSize, 20)
            .Add(c => c.HasMoreBefore, true)
            .Add(c => c.OnLoadOlder, EventCallback.Factory.Create(this, () => loadOlderCalls++)));

        RenderedRowCount(cut).Should().Be(20);

        // First top-edge visit: the window expands to cover the whole (short) sequence.
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        cut.WaitForAssertion(() => RenderedRowCount(cut).Should().Be(30));
        loadOlderCalls.Should().Be(0);

        // The user leaves the top band and comes back — the window is already at Start == 0,
        // so the host page load fires.
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(false));
        handle.Calls.Clear();
        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));

        cut.WaitForAssertion(() => loadOlderCalls.Should().Be(1,
            "at Start == 0 && HasMoreBefore the top edge loads the host's older page"));
        handle.Calls.Should().NotContain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "prepend" }),
            "the host page load keeps its own prepend classification (no expansion happened — the start is already 0)");
    }

    [Fact]
    public async Task TopEdgeExpansion_PersistsAcrossParameterRenders()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));
        cut.WaitForAssertion(() => RenderedRowCount(cut).Should().Be(150));

        // A later parameter-only set re-runs SyncRenderWindow — the tracked keep-id must point
        // at the EXPANDED start row, or the Relocate snaps the window back to the tail.
        cut.Render(p => p.Add(c => c.HasMoreBefore, true));

        cut.WaitForAssertion(() =>
        {
            RenderedRowCount(cut).Should().Be(150, "the top-edge expansion survives a parameter-only re-render");
            RenderedIds(cut).First().Should().Be("m51");
        });
    }

    // ── Rule (e): a host prepend keeps the first rendered row ────────────────

    [Fact]
    public async Task HostPrepend_KeepsFirstRenderedId_AndExpands()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        RenderedIds(cut).First().Should().Be("m101");

        // The host prepends a page of 20 older messages (m1's id moves DOWN → a Prepend).
        var prepended = Messages(20, "old");
        prepended.AddRange(Messages(200));
        cut.Render(p => p.Add(c => c.Messages, prepended));

        cut.WaitForAssertion(() =>
        {
            var ids = RenderedIds(cut);
            RenderedRowCount(cut).Should().Be(150,
                "the previously rendered rows all survive and one step (50) is added above");
            ids.Should().Contain("m101",
                "the window keeps the previously-first rendered row rendered");
            ids.Should().NotContain("old20",
                "the one-step expansion reaches exactly 50 rows above the tracked row (the 20-row page sits above it, rendered on the NEXT expansion)");
            ids.First().Should().Be("m51",
                "the window expanded exactly one step above the previously-first rendered row");
        });
        handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "prepend" }),
            "the host prepend keeps the anchor restore of the scroll contract");
    }

    // ── Rules (b)/(c): the append trims only when pinned / on return ────────

    [Fact]
    public async Task AppendPinned_TrimsTop()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.Calls.Clear();

        // The viewport is pinned to the bottom (the initial state; no report needed).
        var appended = Messages(200);
        for (var i = 201; i <= 205; i++) appended.Add(Incoming($"m{i}", $"Message m{i}"));
        cut.Render(p => p.Add(c => c.Messages, appended));

        cut.WaitForAssertion(() =>
        {
            var ids = RenderedIds(cut);
            RenderedRowCount(cut).Should().Be(100,
                "a pinned append slides the window with the tail: the size is kept");
            ids.First().Should().Be("m106", "the top is trimmed to count - size");
            ids.Last().Should().Be("m205");
        });
    }

    [Fact]
    public async Task AppendUnpinned_DoesNotTrim_TrimsOnReturnToBottom()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        // The user reads above the bottom; the scroller reported the state.
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.Calls.Clear();

        var appended = Messages(200);
        for (var i = 201; i <= 205; i++) appended.Add(Incoming($"m{i}", $"Message m{i}"));
        cut.Render(p => p.Add(c => c.Messages, appended));

        cut.WaitForAssertion(() =>
        {
            RenderedRowCount(cut).Should().Be(105,
                "an append while the user reads above the bottom trims NOTHING (the DOM above the viewport must not change)");
            RenderedIds(cut).First().Should().Be("m101", "the window start does not move");
        });

        // The return to the bottom (the scroller reports it) is when the trim happens.
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(true));
        cut.WaitForAssertion(() =>
        {
            RenderedRowCount(cut).Should().Be(100, "the return to the bottom trims back to the size");
            RenderedIds(cut).First().Should().Be("m106");
        });
    }

    [Fact]
    public async Task ReturnToBottomTrim_PersistsAcrossParameterRenders()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        // The user reads above the bottom; an append arrives and leaves reclaimable excess.
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        var appended = Messages(200);
        appended.Add(Incoming("new-1", "Arrived 1"));
        cut.Render(p => p.Add(c => c.Messages, appended));
        cut.WaitForAssertion(() => RenderedRowCount(cut).Should().Be(101));

        // The return to the bottom trims the excess — and must REFRESH the tracked keep-id, or
        // the next parameter set's Relocate snaps the window back to the untrimmed start.
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(true));
        cut.WaitForAssertion(() =>
        {
            RenderedRowCount(cut).Should().Be(100, "the return to the bottom trims back to the size");
            RenderedIds(cut).First().Should().Be("m102");
        });

        cut.Render(p => p.Add(c => c.HasMoreBefore, true));

        cut.WaitForAssertion(() =>
        {
            RenderedRowCount(cut).Should().Be(100, "the trim survives a parameter-only re-render");
            RenderedIds(cut).First().Should().Be("m102", "the keep-id followed the trimmed start");
        });
    }

    [Fact]
    public async Task WindowHeadRemoved_KeepsStart()
    {
        SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        RenderedIds(cut).First().Should().Be("m101");

        // The tracked window-head row (m101) is REMOVED from the list: the first/last boundary
        // Ids survive (m1/m200), so this is a middle removal — the Relocate path with a MISSING
        // tracked row. The window must KEEP its start (the reader's rows must not change);
        // falling back to the tail would render rows the reader already scrolled past.
        var removed = Messages(200)
            .Where(m => m.Id is not ("m101" or "m102" or "m103" or "m104" or "m105"))
            .ToList();
        cut.Render(p => p.Add(c => c.Messages, removed));

        cut.WaitForAssertion(() =>
        {
            var ids = RenderedIds(cut);
            ids.First().Should().Be("m106", "a missing tracked row keeps the window start");
            ids.Should().NotContain("m96", "the tail fallback would jump the reader's rows");
            RenderedRowCount(cut).Should().Be(95, "195 roots remain and the start is unchanged");
        });
    }

    // ── Deep links outside the window ────────────────────────────────────────

    [Fact]
    public async Task ScrollToMessage_OutsideWindow_ExpandsThenScrolls()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.Calls.Clear();
        handle.FindableIds.Add("m51");

        var found = await cut.Instance.ScrollToMessageAsync("m51");

        found.Should().BeTrue();
        cut.WaitForAssertion(() =>
        {
            RenderedIds(cut).Should().Contain("m51",
                "the window expanded so the anchor row is mounted before the JS navigation");
            RenderedRowCount(cut).Should().Be(150);
        });
        var prependAt = handle.Calls.FindIndex(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "prepend" }));
        var scrollAt = handle.Calls.FindIndex(c => c.Identifier == "scrollToMessage");
        (prependAt >= 0 && scrollAt > prependAt).Should().BeTrue(
            "the JS scrollToMessage runs only after the expansion render applied (afterUpdate prepend first)");
    }

    [Fact]
    public async Task FocusMessage_OutsideWindow_ExpandsThenFocuses()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.Calls.Clear();
        handle.FindableIds.Add("m51");

        var found = await cut.Instance.FocusMessageAsync("m51");

        found.Should().BeTrue();
        cut.WaitForAssertion(() => RenderedIds(cut).Should().Contain("m51"));
        var prependAt = handle.Calls.FindIndex(c => c.Identifier == "afterUpdate"
            && c.Args.SequenceEqual(new object?[] { "prepend" }));
        var focusAt = handle.Calls.FindIndex(c => c.Identifier == "focusMessageVisible");
        (prependAt >= 0 && focusAt > prependAt).Should().BeTrue(
            "the JS focusMessageVisible runs only after the expansion render applied");
    }

    [Fact]
    public async Task UnknownId_ReturnsFalse()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.Calls.Clear();

        var found = await cut.Instance.ScrollToMessageAsync("nope");

        found.Should().BeFalse("an unknown id is reported as not found, exactly as without a window");
        RenderedRowCount(cut).Should().Be(100, "no expansion happens for an unknown id");
    }

    [Fact]
    public async Task DeepLinkExpansion_SurvivesReturnToBottomReport()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.Calls.Clear();
        handle.FindableIds.Add("m51");

        var found = await cut.Instance.ScrollToMessageAsync("m51");
        found.Should().BeTrue();
        cut.WaitForAssertion(() => RenderedIds(cut).Should().Contain("m51"));

        // The prepend restore lands the viewport back at the bottom and its at-bottom report
        // arrives — the rule-(c) trim reclaims only AWAY-APPEND excess, so the rows the deep
        // link deliberately mounted must survive it (the anchor must stay for the navigation).
        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(true));

        cut.WaitForAssertion(() =>
        {
            RenderedIds(cut).Should().Contain("m51",
                "the deep-link expansion is not reclaimed by the restore's own at-bottom report");
            RenderedRowCount(cut).Should().Be(150);
        });
    }

    [Fact]
    public async Task DeepLinkExpansion_PersistsAcrossParameterRenders()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.FindableIds.Add("m51");

        var found = await cut.Instance.ScrollToMessageAsync("m51");
        found.Should().BeTrue();
        cut.WaitForAssertion(() => RenderedRowCount(cut).Should().Be(150));

        // A later parameter-only set re-runs SyncRenderWindow — the tracked keep-id must point
        // at the EXPANDED start row, or the Relocate snaps the window back to the tail and
        // unmounts the anchor the user just navigated to.
        cut.Render(p => p.Add(c => c.HasMoreBefore, true));

        cut.WaitForAssertion(() =>
        {
            RenderedRowCount(cut).Should().Be(150, "the deep-link expansion survives a parameter-only re-render");
            RenderedIds(cut).First().Should().Be("m51");
        });
    }

    [Fact]
    public async Task ConcurrentDeepLinks_AllComplete()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.FindableIds.Add("m20");
        handle.FindableIds.Add("m30");

        // Two deep links racing while the FIRST expansion's after-render bookkeeping is still
        // parked: the second expansion must SHARE the first wait — parking a second TCS over it
        // strands the first waiter forever.
        handle.HoldNextAfterUpdate();
        var first = cut.Instance.ScrollToMessageAsync("m30");
        var second = cut.Instance.ScrollToMessageAsync("m20");

        handle.ReleaseAfterUpdates();
        var both = Task.WhenAll(first.AsTask(), second.AsTask());
        var winner = await Task.WhenAny(both, Task.Delay(5_000));
        winner.Should().Be(both, "both concurrent deep links complete — they share one wait");

        first.Result.Should().BeTrue("the first deep link finds its anchor");
        second.Result.Should().BeTrue("the second deep link shares the first's wait and finds its anchor");
        RenderedIds(cut).Should().Contain("m30").And.Contain("m20");
        RenderedRowCount(cut).Should().Be(181, "the window start ended at the deepest anchor (m20 → index 19)");
    }

    [Fact]
    public async Task WrongRender_EmptiedList_DoesNotReleaseDeepLinkWait_DisposeDoes()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        handle.FindableIds.Add("m51");

        // Park the expansion render's after-render bookkeeping so the wait is still pending.
        handle.HoldNextAfterUpdate();
        var pending = cut.Instance.ScrollToMessageAsync("m51");

        // A render that never runs the row loop (the list emptied) must NOT release the wait —
        // only a render that actually mounted rows (or dispose) may complete the navigation.
        cut.Render(p => p.Add(c => c.Messages, new List<ChatMessage>()));
        await Task.Delay(100);
        pending.IsCompleted.Should().BeFalse("a loop-less render does not release the deep-link wait");

        await cut.Instance.DisposeAsync();

        // The waiter's continuation runs off the dispatcher — await it: dispose completes the
        // shared wait unconditionally, so the navigation finishes (finding nothing, the row is
        // gone) instead of hanging.
        var completed = await Task.WhenAny(pending.AsTask(), Task.Delay(5_000));
        completed.Should().Be(pending.AsTask(), "dispose completes the shared wait unconditionally");
    }

    // ── AutoScroll=Never: the expansion's anchor restore still runs ──────────

    [Fact]
    public async Task Never_WindowExpansion_StillRestoresAnchor()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p
            .Add(c => c.RenderWindowSize, 100)
            .Add(c => c.AutoScroll, ChatAutoScroll.Never));

        await cut.InvokeAsync(() => cut.Instance.OnScrollNearTop(true));

        cut.WaitForAssertion(() => RenderedRowCount(cut).Should().Be(150,
            "the top-edge expansion applies under AutoScroll=Never too"));
        handle.Calls.Should().Contain(c => c.Identifier == "afterUpdate"
                && c.Args.SequenceEqual(new object?[] { "prepend" }),
            "the expansion is a DOM prepend the component made itself — its anchor restore runs even under Never");
    }

    // ── Golden markup: RenderWindowSize unset renders the full list ──────────

    [Fact]
    public void RenderWindowUnset_GoldenMarkup_ThreadReactionsLoadOlder()
    {
        SetupScroller();
        var loadOlderCalls = 0;
        var messages = new List<ChatMessage>
        {
            new("m1", "Morning! The release notes are ready.", Bob, ChatMessageType.Incoming, Ts),
            new("m2", "Thanks — reviewing now.", Me, ChatMessageType.Outgoing, Ts),
            new("m3", "What about the billing thread?", Bob, ChatMessageType.Incoming, Ts)
            {
                ReplyCount = 2,
                Reactions = [new ChatReaction { Emoji = "👍", Users = [Bob] }],
                ReadBy = [new ChatReadReceipt(Me, Ts.AddMinutes(1))],
            },
            new("r1", "Invoice #42 shipped yesterday.", Me, ChatMessageType.Outgoing, Ts)
            {
                ThreadRootId = "m3",
                ReplyToId = "m3",
            },
        };

        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.Messages, messages)
            .Add(c => c.HasMoreBefore, true)
            .Add(c => c.OnLoadOlder, EventCallback.Factory.Create(this, () => loadOlderCalls++)));

        // Exactly ONE load-older affordance (the HasMoreBefore behaviour — no render-window
        // extra) and exactly the root rows: the thread reply renders in the panel, not here.
        cut.FindAll(".tm-chat__load-older").Should().HaveCount(1);
        RenderedRowCount(cut).Should().Be(3, "RenderWindowSize is unset — every root row renders");
        RenderedIds(cut).Should().BeEquivalentTo(["m1", "m2", "m3"]);

        // The committed golden markup: the load-older button, the three keyed rows with the
        // thread/reaction affordances — and NOTHING else (no render-window extras). The bubble
        // time is a machine-timezone detail (ToLocalTime) and the interop/scope attributes are
        // bUnit noise — both are normalized away on both sides.
        TimeTextRegex.Replace(
                NormalizeElementReferences(cut.Find(".tm-chat__messages").InnerHtml), ">TIME<")
            .Should().Be(
                """
                <button type="button" class="tm-chat__load-older" data-testid="chat-load-older" onclick="#" onfocusin="#" onfocusout="#">Load older messages</button><div class="tm-chat__item" data-message-id="m1"><div class="tm-chat__message tm-chat__message--incoming"><div class="tm-chat__avatar tm-chat__avatar--incoming"><span>B</span></div><div class="tm-chat__bubble"><div class="tm-chat__meta"><span class="tm-chat__author">Bob</span>
                                    <span class="tm-chat__message-time">TIME</span></div><div class="tm-chat__text"><span>Morning! The release notes are ready.</span></div></div></div></div><div class="tm-chat__item" data-message-id="m2"><div class="tm-chat__message tm-chat__message--outgoing"><div class="tm-chat__avatar tm-chat__avatar--outgoing"><span>A</span></div><div class="tm-chat__bubble"><div class="tm-chat__meta"><span class="tm-chat__author">Alice</span>
                                    <span class="tm-chat__message-time">TIME</span></div><div class="tm-chat__text"><span>Thanks — reviewing now.</span></div></div></div></div><div class="tm-chat__item" data-message-id="m3"><div class="tm-chat__message tm-chat__message--incoming"><div class="tm-chat__avatar tm-chat__avatar--incoming"><span>B</span></div><div class="tm-chat__bubble"><div class="tm-chat__meta"><span class="tm-chat__author">Bob</span>
                                    <span class="tm-chat__message-time">TIME</span></div><div class="tm-chat__text"><span>What about the billing thread?</span></div><div class="tm-chat__reactions"><button type="button" class="tm-chat__reaction" data-testid="chat-reaction-m3" data-emoji="👍" onclick="#"><span class="tm-chat__reaction-emoji">👍</span>
                                                <span class="tm-chat__reaction-count">1</span></button></div><button type="button" class="tm-chat__thread-badge" data-testid="chat-thread-open-m3" onclick="#" blazor:elementReference=""><svg class="tm-icon tm-icon-sm tm-icon-current" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"></path></svg>
                                        <span>1 reply</span></button></div></div></div>
                """);
    }

    // ── Rule (g): index and grouping over the FULL sequence ─────────────────

    [Fact]
    public void IndexAndGrouping_FromFullSequence()
    {
        SetupScroller();
        var captured = new List<ChatMessageContext>();
        RenderFragment<ChatMessageContext> template = context => builder =>
        {
            captured.Add(context);
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "my-template-bubble");
            builder.AddContent(2, $"{context.Index}:{(context.IsFirstInGroup ? "G" : "-")}");
            builder.CloseElement();
        };

        // All messages share the author, so ONLY m1 is first-in-group — the window edge must not
        // change that for the first rendered row. With size 100 over 200 rows the window start
        // sits at index 100.
        var cut = RenderChat(Messages(200), p => p
            .Add(c => c.RenderWindowSize, 100)
            .Add(c => c.MessageTemplate, template));

        cut.FindAll(".my-template-bubble").Should().HaveCount(100);
        cut.FindAll(".my-template-bubble").First().TextContent.Should().Be("100:-",
            "Index is the FULL-sequence index and the first rendered row is NOT first-in-group (its predecessor m100 has the same author)");
        cut.FindAll(".my-template-bubble").Last().TextContent.Should().Be("199:-");
    }

    // ── Rule (f): the editing row is never trimmed out ───────────────────────

    [Fact]
    public async Task EditingRow_NeverTrimmed()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p
            .Add(c => c.RenderWindowSize, 100)
            .Add(c => c.OnEditMessage, EventCallback.Factory.Create<ChatMessageEdit>(this, _ => Task.CompletedTask)));

        // The edit affordance is offered to the CURRENT USER only — m106 is Alice's row sitting
        // ABOVE the future trim target (a pinned append of 10 would trim the start to
        // 210 - 100 = 110, past index 105).
        var withOwnRow = Messages(200);
        withOwnRow[105] = new("m106", "Message m106", Me, ChatMessageType.Outgoing);
        cut.Render(p => p.Add(c => c.Messages, withOwnRow));

        cut.Find("[data-testid='chat-edit-m106']").Click();
        cut.Find("[data-testid='chat-edit-input']").Should().NotBeNull();

        var appended = new List<ChatMessage>(withOwnRow);
        for (var i = 201; i <= 210; i++) appended.Add(Incoming($"m{i}", $"Message m{i}"));
        cut.Render(p => p.Add(c => c.Messages, appended));

        cut.WaitForAssertion(() =>
        {
            RenderedIds(cut).Should().Contain("m106",
                "the row under edit is never trimmed out of the window (rule f)");
            RenderedRowCount(cut).Should().Be(105,
                "the trim stops AT the editing row (start = 105 < the 110 the size alone would ask for)");
        });
        cut.Find("[data-testid='chat-edit-input']").Should().NotBeNull("the edit stays open");
    }

    // ── The data-driven invariants are untouched by the window ──────────────

    [Fact]
    public async Task Announcements_UnaffectedByWindow()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p
            .Add(c => c.RenderWindowSize, 100)
            .Add(c => c.MessageAnnouncement, (Func<ChatMessage, string?>)(m => $"New: {m.Id}")));

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false; // the emulated scroller answers "unread" for the away append

        var appended = Messages(200);
        appended.Add(Incoming("new-1", "Arrived 1"));
        appended.Add(Incoming("new-2", "Arrived 2"));
        appended.Add(Incoming("new-3", "Arrived 3"));
        cut.Render(p => p.Add(c => c.Messages, appended));

        cut.WaitForAssertion(() =>
        {
            var region = cut.Find("[data-testid='chat-announcements']");
            region.TextContent.Should().Contain("new-3",
                "announcements are driven by the DATA (the appended messages), never by the DOM window");
            cut.FindAll("[aria-live]").Should().HaveCount(1);
        });
        cut.Find("[data-testid='chat-jump-to-new']").TextContent.Should().Contain("3 new messages",
            "the jump-to-new count comes from the appended messages, not from rendered rows");
    }

    [Fact]
    public void LoadOlderButton_VisibleWhenStartAboveZero()
    {
        SetupScroller();
        // A window with start > 0 and NO OnLoadOlder wired: the affordance still renders — its
        // click expands the window. Without a window and without HasMoreBefore it does not
        // (today's DOM).
        var windowed = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));
        windowed.Find("[data-testid='chat-load-older']").Should().NotBeNull(
            "the load-older affordance is visible while the window start sits above zero");

        var plain = RenderChat(Messages(200));
        plain.FindAll("[data-testid='chat-load-older']").Should().BeEmpty(
            "no window and no pagination → no affordance (today's DOM)");
    }

    [Fact]
    public async Task Dispose_DuringPendingExpand_DoesNotThrow()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        // Park the after-render bookkeeping so the deep-link wait is still pending when the
        // component goes away.
        handle.HoldNextAfterUpdate();
        var pending = cut.Instance.ScrollToMessageAsync("m51");

        await cut.Instance.DisposeAsync();

        // Awaiting the parked ValueTask must complete (dispose signals the waiter) — no
        // deadlock, no exception.
        var act = async () => await pending;
        await act.Should().NotThrowAsync("dispose completes a pending deep-link wait");
    }

    // ── The scroll contract keeps working through the window ────────────────

    [Fact]
    public async Task JumpToNew_CountsAppended_AcrossWindowTrims()
    {
        var (_, handle) = SetupScroller();
        var cut = RenderChat(Messages(200), p => p.Add(c => c.RenderWindowSize, 100));

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateChanged(false));
        handle.WasAtBottom = false; // the emulated scroller answers "unread" for the away append
        handle.Calls.Clear();

        var appended = Messages(200);
        appended.Add(Incoming("new-1", "Arrived 1"));
        cut.Render(p => p.Add(c => c.Messages, appended));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='chat-jump-to-new']").Should().HaveCount(1));
        cut.Find("[data-testid='chat-jump-to-new']").Click();

        // The click IS a return to the bottom: the trim runs IMMEDIATELY on the click path (the
        // scroller's at-bottom report stays the backstop for a manual scroll down) and refreshes
        // the tracked keep-id, so a later parameter set cannot snap the window back.
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='chat-jump-to-new']").Should().BeEmpty("the count resets on the click");
            RenderedRowCount(cut).Should().Be(100,
                "the jump-to-new click trims the away-append excess without waiting for the scroller's report");
            RenderedIds(cut).First().Should().Be("m102", "trimmed to count - size (201 roots)");
            RenderedIds(cut).Last().Should().Be("new-1", "the tail row is the target of the scroll");
        });

        // A later parameter-only render must not undo the click's trim either.
        cut.Render(p => p.Add(c => c.HasMoreBefore, true));
        cut.WaitForAssertion(() =>
        {
            RenderedRowCount(cut).Should().Be(100);
            RenderedIds(cut).First().Should().Be("m102", "the keep-id followed the trimmed start");
        });
    }
}

/// <summary>
/// The pure window rules of <see cref="ChatRenderWindow"/> — index/step arithmetic with no
/// Blazor and no message types (decision TMCHAT-RENDER-WINDOW).
/// </summary>
public class ChatRenderWindowStateTests
{
    private static ChatRenderWindow Window(int size = 100, int step = 50) => new(size, step);

    [Fact]
    public void Reset_LastSizeRows()
    {
        var window = Window();
        window.Reset(200);
        window.Start.Should().Be(100, "the first sight of a list windows the last `size` rows");

        window.Reset(50);
        window.Start.Should().Be(0, "a shorter list renders whole");
        window.Reset(0);
        window.Start.Should().Be(0, "an empty list is safe");
    }

    [Fact]
    public void Size_And_Step_Are_Clamped()
    {
        var window = new ChatRenderWindow(5, 3);
        window.Size.Should().Be(ChatRenderWindow.MinSize);
        window.Step.Should().Be(ChatRenderWindow.MinStep);

        var generous = new ChatRenderWindow(100, 50);
        generous.Size.Should().Be(100);
        generous.Step.Should().Be(50);
    }

    [Fact]
    public void Append_Pinned_TrimsToTail()
    {
        var window = Window();
        window.Reset(200);
        window.OnAppend(210, keepIndex: 100, pinned: true, protectedIndex: int.MaxValue);
        window.Start.Should().Be(110, "a pinned append slides the start to count - size");
    }

    [Fact]
    public void Append_Away_KeepsTheTrackedRow()
    {
        var window = Window();
        window.Reset(200);
        window.OnAppend(205, keepIndex: 100, pinned: false, protectedIndex: int.MaxValue);
        window.Start.Should().Be(100, "an append away from the bottom trims NOTHING");
    }

    [Fact]
    public void Append_TrackedRowGone_KeepsTheStart()
    {
        var window = Window();
        window.Reset(200);
        window.OnAppend(210, keepIndex: -1, pinned: false, protectedIndex: int.MaxValue);
        window.Start.Should().Be(100,
            "a vanished tracked row KEEPS the window start (the reader's rows must not jump)");
    }

    [Fact]
    public void Append_Pinned_TrackedRowGone_FallsBackToTail()
    {
        var window = Window();
        window.Reset(200);
        window.OnAppend(210, keepIndex: -1, pinned: true, protectedIndex: int.MaxValue);
        window.Start.Should().Be(110, "the PINNED append keeps its tail fallback (bounded by the size)");
    }

    [Fact]
    public void Prepend_KeepsTrackedRow_AndExpandsOneStep()
    {
        var window = Window();
        window.Reset(200);
        window.OnPrepend(220, keepIndex: 120, protectedIndex: int.MaxValue);
        window.Start.Should().Be(70, "the tracked row stays and one step (50) is added above it");
    }

    [Fact]
    public void Prepend_TrackedRowGone_KeepsTheStart()
    {
        var window = Window();
        window.Reset(200);
        window.OnPrepend(220, keepIndex: -1, protectedIndex: int.MaxValue);
        window.Start.Should().Be(100, "a vanished tracked row keeps the start (no reader-visible jump)");
    }

    [Fact]
    public void ExpandUpOneStep_StopsAtZero()
    {
        var window = Window(size: 20, step: 50);
        window.Reset(30);
        window.Start.Should().Be(10);
        window.ExpandUpOneStep().Should().BeTrue();
        window.Start.Should().Be(0);
        window.ExpandUpOneStep().Should().BeFalse("the window already covers the whole sequence");
    }

    [Fact]
    public void ExpandToInclude_MovesStartToIndex()
    {
        var window = Window();
        window.Reset(200);
        window.ExpandToInclude(150).Should().BeFalse("the row is already inside the window");
        window.ExpandToInclude(40).Should().BeTrue();
        window.Start.Should().Be(40);
        window.ExpandToInclude(60).Should().BeFalse("the window only ever grows at the top");
    }

    [Fact]
    public void TrimToTail_RespectsProtectedIndex_AndReportsChange()
    {
        var window = Window(size: 20, step: 50);
        window.Reset(30);
        window.Start.Should().Be(10);

        // No away-append yet: the return to the bottom reclaims nothing.
        window.TrimToTail(200, protectedIndex: int.MaxValue).Should().BeFalse(
            "a trim without a pending away-append is a no-op (a deep-link/top-edge expansion must survive it)");

        // The away-append leaves reclaimable excess; the return to the bottom claims it.
        window.OnAppend(200, keepIndex: 10, pinned: false, protectedIndex: int.MaxValue);
        window.TrimToTail(200, protectedIndex: int.MaxValue).Should().BeTrue();
        window.Start.Should().Be(180);

        window.TrimToTail(200, protectedIndex: int.MaxValue).Should().BeFalse(
            "trimming an already-trimmed window changes nothing");

        // The editing row at index 100 blocks the trim of the NEXT away-append: the window is a
        // contiguous slice, so it can neither trim past the protected row nor skip over it.
        window.OnAppend(210, keepIndex: 180, pinned: false, protectedIndex: int.MaxValue);
        window.TrimToTail(210, protectedIndex: 100).Should().BeFalse(
            "the trim never moves the start past a protected row");
        window.Start.Should().Be(180);
    }

    [Fact]
    public void Relocate_KeepsTrackedRow()
    {
        var window = Window();
        window.Reset(200);
        window.Relocate(200, keepIndex: 100, protectedIndex: int.MaxValue);
        window.Start.Should().Be(100);

        window.Relocate(210, keepIndex: 110, protectedIndex: int.MaxValue);
        window.Start.Should().Be(110, "the relocated tracked row is the new start");
    }

    [Fact]
    public void Relocate_TrackedRowGone_KeepsTheStart()
    {
        var window = Window();
        window.Reset(200);
        window.Relocate(210, keepIndex: -1, protectedIndex: int.MaxValue);
        window.Start.Should().Be(100, "a vanished tracked row keeps the start (clamped into the new list)");
    }

    [Fact]
    public void Expansions_ClearPendingTrim()
    {
        var window = Window(size: 20, step: 50);
        window.Reset(100);

        // An away-append leaves reclaimable excess …
        window.OnAppend(200, keepIndex: 80, pinned: false, protectedIndex: int.MaxValue);

        // … but a TOP-EDGE expansion mounts its rows deliberately: the pending claim is dropped,
        // so the restore's own at-bottom report cannot reclaim the expansion.
        window.ExpandUpOneStep().Should().BeTrue();
        window.TrimToTail(200, protectedIndex: int.MaxValue).Should().BeFalse(
            "the expansion clears PendingTrim — its rows survive the return to the bottom");
        window.Start.Should().Be(30);

        window.OnAppend(210, keepIndex: 30, pinned: false, protectedIndex: int.MaxValue);

        // … a DEEP-LINK expansion drops the claim too.
        window.ExpandToInclude(10).Should().BeTrue();
        window.TrimToTail(210, protectedIndex: int.MaxValue).Should().BeFalse(
            "the deep-link expansion clears PendingTrim as well");
        window.Start.Should().Be(10);

        window.OnAppend(220, keepIndex: 10, pinned: false, protectedIndex: int.MaxValue);

        // … and a HOST PREPEND drops it too.
        window.OnPrepend(230, keepIndex: 20, protectedIndex: int.MaxValue);
        window.TrimToTail(230, protectedIndex: int.MaxValue).Should().BeFalse(
            "the host prepend clears PendingTrim");
        window.Start.Should().Be(0);
    }

    [Fact]
    public void Append_Pinned_NeverTrimsPastProtectedIndex()
    {
        var window = Window(size: 20, step: 50);
        window.Reset(100);
        window.OnAppend(210, keepIndex: 80, pinned: true, protectedIndex: 100);
        window.Start.Should().Be(100, "the trimming stops at the protected row (start ≤ its index)");
    }
}

/// <summary>
/// The pure pending-mode merge of the scroll half (TmChat.MergeScrollChangeModes): a PREPEND and
/// an APPEND arriving before ONE render merge by READING POSITION — above the bottom the
/// prepend's anchor restore wins and the appended rows travel as the accumulated
/// <c>_pendingRootDelta</c> (counted behind the jump button by the after-render prepend
/// branch); at the bottom the append wins. Reset wins over anything, a None keeps what was
/// pending, any other real change replaces a None.
/// </summary>
public class TmChatScrollMergeTests
{
    [Fact]
    public void PrependAndAppend_AwayFromBottom_KeepPrepend()
    {
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.Prepend, TmChat.ScrollChangeMode.Append, atBottom: false)
            .Should().Be(TmChat.ScrollChangeMode.Prepend,
                "the position restore must win — an append classification would drop it for the older history");
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.Append, TmChat.ScrollChangeMode.Prepend, atBottom: false)
            .Should().Be(TmChat.ScrollChangeMode.Prepend,
                "the appended rows are counted behind the jump button by the prepend branch instead");
    }

    [Fact]
    public void PrependAndAppend_AtBottom_KeepAppend()
    {
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.Prepend, TmChat.ScrollChangeMode.Append, atBottom: true)
            .Should().Be(TmChat.ScrollChangeMode.Append, "at the bottom the list pins to the tail");
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.Append, TmChat.ScrollChangeMode.Prepend, atBottom: true)
            .Should().Be(TmChat.ScrollChangeMode.Prepend,
                "the last real change wins at the bottom (the plain replace rule)");
    }

    [Fact]
    public void ResetWinsOverAnything_AndNoneKeepsPending()
    {
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.Prepend, TmChat.ScrollChangeMode.Reset, atBottom: false)
            .Should().Be(TmChat.ScrollChangeMode.Reset);
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.Reset, TmChat.ScrollChangeMode.Append, atBottom: false)
            .Should().Be(TmChat.ScrollChangeMode.Reset);
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.Append, TmChat.ScrollChangeMode.None, atBottom: false)
            .Should().Be(TmChat.ScrollChangeMode.Append, "Append-then-None still scrolls (and counts) on the single render");
        TmChat.MergeScrollChangeModes(TmChat.ScrollChangeMode.None, TmChat.ScrollChangeMode.Prepend, atBottom: false)
            .Should().Be(TmChat.ScrollChangeMode.Prepend, "a real change replaces a None");
    }
}
