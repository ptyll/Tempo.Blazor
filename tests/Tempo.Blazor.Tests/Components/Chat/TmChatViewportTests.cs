using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// FZE-13 — the OPT-IN virtual-keyboard tracker (<c>TrackVirtualKeyboard</c>): the
/// <c>chat.js</c> <c>attachViewportTracker(root, options)</c> keeps the narrow sticky composer
/// above the iOS on-screen keyboard by writing the runtime <c>--tm-chat-keyboard-inset</c> knob
/// (and the <c>data-tm-keyboard-open</c> marker) into the chat root's inline style. The
/// contract: default OFF (no <c>visualViewport</c> listeners at all), the first render attaches
/// ONCE and further renders with unchanged state re-use the handle, turning the parameter off
/// disposes it, and component disposal attempts the handle dispose even against a dropped
/// circuit (swallowed). The CSS half of the contract (the narrow-chat <c>bottom</c> offsets read
/// the knob; the knob is never declared) lives in <c>TmChatLayoutTests</c>.
/// </summary>
public class TmChatViewportTests : LocalizationTestBase
{
    private const string ChatModulePath = "./_content/Tempo.Blazor/js/chat.js";

    private static readonly ChatUser Me = new("u1", "Alice");

    private IRenderedComponent<TmChat> RenderChat(
        Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me);
            extra?.Invoke(p);
        });

    /// <summary>Gives the async render pipeline (OnAfterRenderAsync → sync) time to settle.</summary>
    private static Task SettleAsync(IRenderedComponent<TmChat> cut)
        => cut.InvokeAsync(async () => await Task.Delay(20));

    /// <summary>
    /// Re-renders the component with new parameter values — a parent re-render, the way a host
    /// would flip <see cref="TmChat.TrackVirtualKeyboard"/> (bUnit has no
    /// <c>SetParametersAndRenderAsync</c> in this version).
    /// </summary>
    private static Task ReRenderAsync(IRenderedComponent<TmChat> cut, Dictionary<string, object?> parameters)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(parameters)));

    /// <summary>A chat.js viewport-tracker handle double: records every invocation.</summary>
    private sealed class FakeViewportTrackerHandle : IJSObjectReference
    {
        /// <summary>
        /// The handle contract of <c>attachViewportTracker</c> — <c>dispose</c> is the WHOLE
        /// contract (the tracker drives itself from the visual viewport events); anything else
        /// reaching this double is a bug in the component and fails the test loudly.
        /// </summary>
        private static readonly HashSet<string> HandleIdentifiers = new(StringComparer.Ordinal)
        { "dispose" };

        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        /// <summary>Identifier → failure: the call is recorded and then throws (dropped circuit).</summary>
        public Dictionary<string, Exception> ThrowOn { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args = null)
        {
            Record(identifier, args ?? Array.Empty<object?>());
            if (ThrowOn.TryGetValue(identifier, out var failure)) return ValueTask.FromException<TValue>(failure);
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask InvokeVoidAsync(string identifier, CancellationToken cancellationToken = default)
            => InvokeVoidAsync(identifier, Array.Empty<object?>(), cancellationToken);

        public ValueTask InvokeVoidAsync(string identifier, object?[] args, CancellationToken cancellationToken = default)
        {
            Record(identifier, args ?? Array.Empty<object?>());
            if (ThrowOn.TryGetValue(identifier, out var failure)) return ValueTask.FromException(failure);
            return ValueTask.CompletedTask;
        }

        private void Record(string identifier, object?[] args)
        {
            if (!HandleIdentifiers.Contains(identifier))
            {
                throw new JSException(
                    $"FakeViewportTrackerHandle: unexpected identifier '{identifier}' — the "
                    + "viewport-tracker handle contract is dispose.");
            }

            Calls.Add((identifier, args));
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Serves a FRESH fake handle per <c>attachViewportTracker</c> dispatch.</summary>
    private sealed class ViewportTrackerHandler : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        public int DispatchCount { get; private set; }

        public List<JSRuntimeInvocation> Requests { get; } = [];

        public FakeViewportTrackerHandle LastHandle { get; private set; } = new();

        public ViewportTrackerHandler()
            : base(invocation => invocation.Identifier == "attachViewportTracker", false) { }

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
        {
            DispatchCount++;
            Requests.Add(invocation);
            LastHandle = new FakeViewportTrackerHandle();
            return Task.FromResult<IJSObjectReference>(LastHandle);
        }
    }

    // ── default OFF: no visualViewport tracking at all ────────────────────────

    [Fact]
    public async Task TrackVirtualKeyboard_Default_NoTrackerCall()
    {
        var module = JSInterop.SetupModule(ChatModulePath);
        var handler = new ViewportTrackerHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat();

        // The module loads (the scroller hangs off it) and the pipeline settles — and still no
        // attachViewportTracker: the default keeps today's behavior.
        cut.WaitForAssertion(() => module.Invocations.Should()
            .Contain(i => i.Identifier == "attachScroller", "the module loaded"));
        await SettleAsync(cut);

        handler.DispatchCount.Should().Be(0,
            "TrackVirtualKeyboard defaults to false — no visualViewport listeners without the opt-in");
    }

    // ── opt-in: one attach per state ──────────────────────────────────────────

    [Fact]
    public async Task TrackVirtualKeyboard_On_AttachesTrackerOnce()
    {
        var module = JSInterop.SetupModule(ChatModulePath);
        var handler = new ViewportTrackerHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p.Add(c => c.TrackVirtualKeyboard, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1,
            "the first interactive render attaches the tracker exactly once"));
        handler.Requests[0].Arguments.Should().HaveCount(2,
            "the call carries the chat root element and the options bag");

        // A further render with unchanged state must not re-attach.
        await ReRenderAsync(cut, new() { [nameof(TmChat.Placeholder)] = "changed" });
        await SettleAsync(cut);
        handler.DispatchCount.Should().Be(1,
            "the handle is re-used while the parameter and the scroller state are unchanged");
    }

    [Fact]
    public async Task Toggle_Off_DisposesTracker()
    {
        var module = JSInterop.SetupModule(ChatModulePath);
        var handler = new ViewportTrackerHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p.Add(c => c.TrackVirtualKeyboard, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var handle = handler.LastHandle;

        await ReRenderAsync(cut, new() { [nameof(TmChat.TrackVirtualKeyboard)] = false });
        await SettleAsync(cut);

        handle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "turning the parameter off detaches the tracker (and the inline knob falls back to 0px)");
    }

    // ── disposal: swallow the dropped circuit ─────────────────────────────────

    [Fact]
    public async Task Dispose_DisposesTracker_SwallowsJSDisconnected()
    {
        var module = JSInterop.SetupModule(ChatModulePath);
        var handler = new ViewportTrackerHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p.Add(c => c.TrackVirtualKeyboard, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var handle = handler.LastHandle;
        handle.ThrowOn["dispose"] = new JSDisconnectedException("circuit gone");

        var act = async () => await cut.Instance.DisposeAsync();

        await act.Should().NotThrowAsync(
            "DisposeAsyncCore must swallow the JSDisconnectedException of the tracker dispose");
        handle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "the dispose was attempted (recorded before the failure)");
    }
}
