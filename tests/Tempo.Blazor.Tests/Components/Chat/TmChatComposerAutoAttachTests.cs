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
/// FZE-12 — the OPT-IN auto-attach of a <c>ComposerTemplate</c> input by id
/// (<c>AutoAttachComposerInput</c>): the host input carries <c>id="@composer.InputId"</c>, and
/// TmChat attaches it through the <c>chat.js</c> <c>attachComposerById</c> (scoped to the chat
/// root) without the host's <c>OnAfterRenderAsync</c> + <c>AttachAsync</c> dance. The contract:
/// default OFF (no id-based call at all), the FIRST sync attaches once, further renders only
/// <c>sync()</c> the handle, an EXPLICIT <c>AttachAsync</c> always wins (the auto handle is
/// disposed) and <c>AttachAsync(default)</c> hands control back, <c>ShowComposer=false</c> /
/// turning the parameter off / template removal dispose the handle, an <c>EnterToSend</c> change
/// re-attaches, the post-send refocus targets the auto-attached input, disposal swallows a
/// dropped circuit and the parameter is ignored without a <c>ComposerTemplate</c>.
/// </summary>
public class TmChatComposerAutoAttachTests : LocalizationTestBase
{
    private const string ComposerModulePath = "./_content/Tempo.Blazor/js/chat.js";

    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");

    private IRenderedComponent<TmChat> RenderChat(
        Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me);
            extra?.Invoke(p);
        });

    private static RenderFragment<ChatComposerContext> MarkerComposer(
        string marker, Action<ChatComposerContext>? onRender = null) => context =>
    {
        onRender?.Invoke(context);
        return builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", marker);
            builder.CloseElement();
        };
    };

    /// <summary>Gives the async render pipeline (OnAfterRenderAsync → sync) time to settle.</summary>
    private static Task SettleAsync(IRenderedComponent<TmChat> cut)
        => cut.InvokeAsync(async () => await Task.Delay(20));

    /// <summary>A chat.js auto-attach handle double: records every invocation.</summary>
    private sealed class FakeAutoAttachHandle : IJSObjectReference
    {
        /// <summary>
        /// The handle contract of <c>attachComposerById</c> — anything else reaching this double
        /// is a bug in the component (e.g. a slot-style <c>dispose</c>-only handle or a
        /// module-level call misrouted to the handle) and fails the test loudly.
        /// </summary>
        private static readonly HashSet<string> HandleIdentifiers = new(StringComparer.Ordinal)
        { "sync", "focusIfNeeded", "resize", "dispose" };

        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        /// <summary>Identifier → failure: the call is recorded and then throws (dropped circuit).</summary>
        public Dictionary<string, Exception> ThrowOn { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args = null)
        {
            // bUnit's InvokeVoidAsync extension routes through InvokeAsync<object?> — the
            // void-ish handle calls (sync / focusIfNeeded / resize / dispose) land HERE.
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
                    $"FakeAutoAttachHandle: unexpected identifier '{identifier}' — the auto-attach "
                    + "handle contract is sync/focusIfNeeded/resize/dispose.");
            }

            Calls.Add((identifier, args));
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Serves a FRESH fake handle per <c>attachComposerById</c> dispatch.</summary>
    private sealed class AutoAttachHandler : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        public int DispatchCount { get; private set; }

        public List<JSRuntimeInvocation> Requests { get; } = [];

        public FakeAutoAttachHandle LastHandle { get; private set; } = new();

        public AutoAttachHandler()
            : base(invocation => invocation.Identifier == "attachComposerById", false) { }

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
        {
            DispatchCount++;
            Requests.Add(invocation);
            LastHandle = new FakeAutoAttachHandle();
            return Task.FromResult<IJSObjectReference>(LastHandle);
        }
    }

    // ── default OFF: no id-based attach call at all ──────────────────────────

    [Fact]
    public async Task AutoAttach_Default_NoAttachByIdCall()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p.Add(c => c.ComposerTemplate, MarkerComposer("my-composer")));

        // The module loads (the scroller hangs off it), the pipeline settles — and still no
        // attachComposerById: the default keeps today's behavior.
        cut.WaitForAssertion(() => module.Invocations.Should()
            .Contain(i => i.Identifier == "attachScroller", "the module loaded"));
        await SettleAsync(cut);

        handler.DispatchCount.Should().Be(0,
            "AutoAttachComposerInput defaults to false — no id-based attach without the opt-in");
    }

    // ── opt-in: attachComposerById(root, InputId) on the first sync ──────────

    [Fact]
    public async Task AutoAttach_On_CallsAttachComposerById_WithInputId()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1,
            "the opt-in auto-attach resolves the host input by id exactly once"));

        var request = handler.Requests[0];
        request.Arguments.Should().HaveCountGreaterThanOrEqualTo(3);
        ((ElementReference)request.Arguments[0]!).Id.Should().Be(cut.Instance.ChatRoot.Id,
            "the lookup is scoped to the CHAT ROOT, not the document");
        ((string)request.Arguments[1]!).Should().Be(cut.Instance.ComposerInputId,
            "the id is ChatComposerContext.InputId — the one the host puts on its textarea");
        request.Arguments[2].Should().BeOfType<DotNetObjectReference<TmChat>>();
    }

    // ── further renders sync the handle instead of re-attaching ─────────────

    [Fact]
    public async Task AutoAttach_SubsequentRender_CallsSyncNotReattach()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var handle = handler.LastHandle;

        cut.Render(); // a parameter-less re-render — the template input may have been swapped

        await SettleAsync(cut);
        handler.DispatchCount.Should().Be(1, "no second attach for an unchanged state");
        handle.Calls.Should().Contain(c => c.Identifier == "sync",
            "further renders re-query through handle.sync() (same node = no-op)");
    }

    // ── an explicit AttachAsync always wins ──────────────────────────────────

    [Fact]
    public async Task ExplicitAttachAsync_WinsOverAutoAttach()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        ChatComposerContext? received = null;
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var autoHandle = handler.LastHandle;

        await cut.InvokeAsync(() => received!.AttachAsync(new ElementReference("custom-input")));

        module.Invocations.Should().Contain(i =>
            i.Identifier == "attachComposer"
            && i.Arguments.Count >= 1
            && i.Arguments[0] is ElementReference
            && ((ElementReference)i.Arguments[0]!).Id == "custom-input",
            "the explicit AttachAsync registers the host element through attachComposer");
        autoHandle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "the auto-attached handle is dropped — the explicit registration wins");

        await SettleAsync(cut);
        handler.DispatchCount.Should().Be(1,
            "no further id-based attaches while the explicit registration holds");
    }

    // ── AttachAsync(default) hands control back to the auto-attach ──────────

    [Fact]
    public async Task AttachAsyncDefault_ReturnsToAutoAttach()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        ChatComposerContext? received = null;
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));

        await cut.InvokeAsync(() => received!.AttachAsync(new ElementReference("custom-input")));
        await cut.InvokeAsync(() => received!.AttachAsync(default));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(2,
            "AttachAsync(default) returns the control to the auto-attach — a fresh id-based attach"));
    }

    // ── ShowComposer=false drops the auto handle ─────────────────────────────

    [Fact]
    public async Task ShowComposerFalse_DisposesHandle()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var autoHandle = handler.LastHandle;

        cut.Render(p => p.Add(c => c.ShowComposer, false));

        await SettleAsync(cut);
        autoHandle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "no composer rendered — the handle must not survive with its listeners");
        handler.DispatchCount.Should().Be(1, "and nothing re-attaches");
    }

    // ── turning the parameter off disposes the handle ─────────────────────────

    [Fact]
    public async Task AutoAttachOff_DisposesHandle()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var autoHandle = handler.LastHandle;

        cut.Render(p => p.Add(c => c.AutoAttachComposerInput, false));

        await SettleAsync(cut);
        autoHandle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "the opt-out drops the handle — no id-based attach may survive it");
        handler.DispatchCount.Should().Be(1, "and nothing re-attaches while the option is off");

        cut.Render(); // a later render must not resurrect the handle

        await SettleAsync(cut);
        handler.DispatchCount.Should().Be(1, "the opt-out sticks — no id-based attach returns");
        autoHandle.Calls.Should().NotContain(c => c.Identifier == "sync",
            "no sync runs on a disposed handle");
    }

    // ── removing the template disposes the handle ─────────────────────────────

    [Fact]
    public async Task TemplateRemoved_DisposesHandle()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var autoHandle = handler.LastHandle;

        cut.Render(p => p.Add(c => c.ComposerTemplate, (RenderFragment<ChatComposerContext>?)null!));

        await SettleAsync(cut);
        autoHandle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "no template — the handle must not survive with its listeners");
        handler.DispatchCount.Should().Be(1, "no second id-based attach");
        module.Invocations.Should().Contain(i => i.Identifier == "attachComposer",
            "the built-in composer (rendered again) is attached by element as before");
    }

    // ── an EnterToSend change re-attaches (the option is baked into the attach) ──

    [Fact]
    public async Task EnterToSendChange_Reattaches()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var firstHandle = handler.LastHandle;

        cut.Render(p => p.Add(c => c.EnterToSend, false));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(2,
            "EnterToSend is an attach option — a change disposes and re-attaches"));
        firstHandle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "the old handle (with the old enterToSend) is gone");
    }

    // ── the post-send refocus targets the auto-attached input ────────────────

    [Fact]
    public async Task RefocusAfterSend_UsesAutoHandle()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        ChatComposerContext? received = null;
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx))
            .Add(c => c.AutoAttachComposerInput, true)
            .Add(c => c.OnSendMessage, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var autoHandle = handler.LastHandle;

        await received!.SetTextAsync("Hello");
        await received.SendAsync();

        autoHandle.Calls.Should().Contain(c => c.Identifier == "focusIfNeeded",
            "RefocusAfterSend targets the AUTO-ATTACHED input through handle.focusIfNeeded");
        autoHandle.Calls.Should().Contain(c => c.Identifier == "resize",
            "the auto-attached input is re-measured after the programmatic clear");
        module.Invocations.Should().NotContain(i => i.Identifier == "focusIfNeeded",
            "the module-level element-based focusIfNeeded is not used in auto mode");
    }

    // ── disposal: the handle is disposed and a dropped circuit is swallowed ──

    [Fact]
    public async Task Dispose_DisposesHandle_SwallowsJSDisconnected()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p
            .Add(c => c.ComposerTemplate, MarkerComposer("my-composer"))
            .Add(c => c.AutoAttachComposerInput, true));

        cut.WaitForAssertion(() => handler.DispatchCount.Should().Be(1));
        var autoHandle = handler.LastHandle;
        autoHandle.ThrowOn["dispose"] = new JSDisconnectedException("circuit gone");

        var act = async () => await cut.Instance.DisposeAsync();

        await act.Should().NotThrowAsync(
            "DisposeAsyncCore must swallow the JSDisconnectedException of the handle dispose");
        autoHandle.Calls.Should().Contain(c => c.Identifier == "dispose",
            "the dispose was attempted (recorded before the failure)");
    }

    // ── without a ComposerTemplate the parameter is ignored ──────────────────

    [Fact]
    public async Task NoComposerTemplate_IgnoresParameter()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var handler = new AutoAttachHandler();
        module.AddInvocationHandler(handler);
        var cut = RenderChat(p => p.Add(c => c.AutoAttachComposerInput, true)); // built-in composer

        cut.WaitForAssertion(() => module.Invocations.Should()
            .Contain(i => i.Identifier == "attachComposer",
                "the built-in textarea is attached by element, exactly as before"));
        await SettleAsync(cut);

        handler.DispatchCount.Should().Be(0,
            "the parameter applies ONLY with a ComposerTemplate — the built-in composer path is unchanged");
    }
}
