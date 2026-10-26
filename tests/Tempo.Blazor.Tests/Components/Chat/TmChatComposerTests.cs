using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Composer behavior of <see cref="TmChat"/> (FZE-5): the <c>chat.js</c> module owns
/// Enter/Shift+Enter and the IME guard once attached (the C# <c>@onkeydown</c> handler is only
/// the pre-attach fallback, so Enter activates exactly once), the composer text is cleared only
/// after a SUCCESSFUL send (a host handler that throws keeps the draft), the composer is
/// refocused after a successful send (<c>RefocusAfterSend</c>), <c>EnterToSend=false</c> turns
/// Enter into a native new line, and a custom composer can opt into the same behavior through
/// <see cref="ChatComposerContext.AttachAsync"/>.
/// </summary>
public class TmChatComposerTests : LocalizationTestBase
{
    private const string ComposerModulePath = "./_content/Tempo.Blazor/js/chat.js";

    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Bob = new("u2", "Bob", avatar: "B");

    private static ChatMessage Incoming(string id, string text) =>
        new(id, text, Bob, ChatMessageType.Incoming);

    private IRenderedComponent<TmChat> RenderChat(
        IReadOnlyList<ChatMessage> messages,
        Action<ComponentParameterCollectionBuilder<TmChat>>? extra = null)
        => Render<TmChat>(p =>
        {
            p.Add(c => c.CurrentUser, Me).Add(c => c.Messages, messages);
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

    private static DotNetObjectReference<TmChat> AttachDotNetRef(BunitJSModuleInterop module)
        => (DotNetObjectReference<TmChat>)module.Invocations
            .Last(i => i.Identifier == "attachComposer")
            .Arguments[1]!;

    // ── Clear + refocus after a successful send ──────────────────────────────

    [Fact]
    public void Composer_AfterSend_ClearsAndRefocuses()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var cut = RenderChat([], p => p.Add(c => c.OnSendMessage,
            EventCallback.Factory.Create<string>(this, _ => { })));

        cut.Find(".tm-chat__input").Input("Hello world");
        cut.Find(".tm-chat__send-btn").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-chat__input").GetAttribute("value")
            .Should().BeEmpty("the composer is cleared after the send completed"));
        module.Invocations.Should().Contain(i => i.Identifier == "focusIfNeeded",
            "RefocusAfterSend defaults to true — the loaded module refocuses the composer via focusIfNeeded");
    }

    // ── Enter via the JS module sends exactly once ───────────────────────────

    [Fact]
    public async Task Composer_EnterViaJs_SendsOnce()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var sent = new List<string>();
        var cut = RenderChat([], p => p.Add(c => c.OnSendMessage,
            EventCallback.Factory.Create<string>(this, t => sent.Add(t))));

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "attachComposer"));
        var dotNetRef = AttachDotNetRef(module);

        cut.Find(".tm-chat__input").Input("Hello");
        cut.Render();

        // With the module attached, the Blazor @onkeydown fallback must stay quiet.
        cut.Find(".tm-chat__input").KeyDown("Enter");
        sent.Should().BeEmpty("the JS module owns Enter; the C# fallback must not double-send");

        // The module calls back through the JSInvokable entry point — exactly one send.
        await cut.InvokeAsync(() => dotNetRef.Value.OnComposerSubmit());
        sent.Should().ContainSingle("the JS-invoked submit sends exactly once");
        sent[0].Should().Be("Hello");
    }

    [Fact]
    public void Composer_ShiftEnter_DoesNotSend()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var sent = new List<string>();
        var cut = RenderChat([], p => p.Add(c => c.OnSendMessage,
            EventCallback.Factory.Create<string>(this, t => sent.Add(t))));

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "attachComposer"));

        cut.Find(".tm-chat__input").Input("line one");
        cut.Render();
        cut.Find(".tm-chat__input").TriggerEvent("onkeydown", new KeyboardEventArgs
        {
            Key = "Enter",
            ShiftKey = true,
        });

        sent.Should().BeEmpty("Shift+Enter inserts a new line and never submits");
        cut.Find(".tm-chat__input").GetAttribute("value").Should().Be("line one");
    }

    // ── A failed send keeps the text (the clear happens after OnSend completes) ──

    [Fact]
    public async Task Composer_SendFailsInHost_TextIsNotLost()
    {
        var cut = RenderChat([], p => p
            .Add(c => c.OnSendMessage, EventCallback.Factory.Create<string>(this, _ => { }))
            .Add(c => c.OnSend, EventCallback.Factory.Create<ChatSendRequest>(
                this, _ => throw new InvalidOperationException("host handler failed"))));

        cut.Find(".tm-chat__input").Input("Keep me");
        cut.Render();

        var act = async () => await cut.Instance.ComposerContext.SendAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        cut.Find(".tm-chat__input").GetAttribute("value").Should().Be("Keep me",
            "the text is cleared only after OnSend completes — a failed send keeps the draft");
        cut.Instance.ComposerContext.CanSend.Should().BeTrue("the text is still present, so a retry is possible");
        cut.Instance.ComposerContext.IsSubmitting.Should().BeFalse(
            "the finally resets the submit flag even when the host handler throws");
    }

    // ── Text typed while a send is in flight is kept ────────────────────────

    [Fact]
    public async Task Composer_TypingDuringInFlightSend_IsKept()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new List<string>();
        var cut = RenderChat([], p => p.Add(c => c.OnSendMessage,
            EventCallback.Factory.Create<string>(this, async t =>
            {
                sent.Add(t);
                started.SetResult();
                await gate.Task;
            })));

        cut.Find(".tm-chat__input").Input("First");
        cut.Render();

        var send = cut.Instance.ComposerContext.SendAsync();
        await started.Task; // the send is in flight (the host handler is parked on the gate)

        // The user keeps typing while the send is in flight.
        cut.Find(".tm-chat__input").Input("First and more");

        gate.SetResult();
        await send;

        sent.Should().ContainSingle("the send dispatched the text as it was when it started");
        sent[0].Should().Be("First");
        cut.Find(".tm-chat__input").GetAttribute("value").Should().Be("and more",
            "text typed during the send is kept — only the sent prefix is removed");
        cut.Instance.ComposerContext.IsSubmitting.Should().BeFalse("the finally resets the flag");
        cut.Instance.ComposerContext.CanSend.Should().BeTrue("the kept text is still sendable");
    }

    [Fact]
    public async Task ThreadReply_DoubleSubmitWhileInFlight_SendsOnce()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new List<ChatSendRequest>();
        var messages = new[]
        {
            Incoming("m1", "Question?"),
            new ChatMessage("r1", "Answer", Me, ChatMessageType.Outgoing) { ThreadRootId = "m1" },
        };
        var cut = RenderChat(messages, p => p.Add(c => c.OnSend,
            EventCallback.Factory.Create<ChatSendRequest>(this, async r =>
            {
                sent.Add(r);
                await gate.Task;
            })));

        cut.Find("[data-testid='chat-thread-open-m1']").Click();
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "attachComposer"));

        cut.Find("[data-testid='chat-thread-input']").Input("Follow-up");
        cut.Render();

        var dotNetRef = AttachDotNetRef(module);
        var first = cut.InvokeAsync(() => dotNetRef.Value.OnThreadComposerSubmit());
        var second = cut.InvokeAsync(() => dotNetRef.Value.OnThreadComposerSubmit()); // while in flight

        gate.SetResult();
        await Task.WhenAll(first, second);

        sent.Should().ContainSingle("the in-flight flag makes a duplicate dispatch a no-op");
        sent[0].Text.Should().Be("Follow-up");
        cut.Find("[data-testid='chat-thread-input']").GetAttribute("value").Should().BeEmpty(
            "the thread reply is cleared after the send completed");
    }

    // ── Public focus API ─────────────────────────────────────────────────────

    [Fact]
    public async Task Composer_FocusComposerAsync_PublicApi()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var cut = RenderChat([]);

        await cut.Instance.FocusComposerAsync();

        module.Invocations.Should().Contain(i => i.Identifier == "focusIfNeeded"
            && i.Arguments.Count >= 1
            && i.Arguments[0] is ElementReference,
            "FocusComposerAsync focuses the built-in composer textarea through the module's focusIfNeeded");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "Blazor._internal.domWrapper.focus",
            "with the module loaded the raw FocusAsync fallback is not used");
    }

    [Fact]
    public async Task CustomComposer_FocusComposerAsync_FocusesAttachedElement()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        ChatComposerContext? received = null;
        var cut = RenderChat([], p => p.Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx)));

        await cut.InvokeAsync(() => received!.AttachAsync(new ElementReference("custom-input")));

        await cut.Instance.FocusComposerAsync();

        module.Invocations.Should().Contain(i => i.Identifier == "focusIfNeeded"
            && i.Arguments.Count >= 1
            && i.Arguments[0] is ElementReference
            && ((ElementReference)i.Arguments[0]).Id == "custom-input",
            "FocusComposerAsync targets the attached custom element (same logic as the refocus)");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "Blazor._internal.domWrapper.focus",
            "the built-in textarea is not rendered, so it is not the focus target");
    }

    // ── Discoverability: enterkeyhint + the visually-hidden submit hint ──────

    [Fact]
    public void Composer_HintSpan_MergesIntoDescribedBy_AndFollowsEnterToSend()
    {
        var cut = RenderChat([], p => p.Add(c => c.ComposerDescribedBy, "host-hint"));

        var input = cut.Find(".tm-chat__input");
        input.GetAttribute("enterkeyhint").Should().Be("send", "EnterToSend defaults to true");
        input.GetAttribute("aria-describedby").Should().Be($"host-hint {cut.Instance.ComposerInputId}-hint",
            "the hint id is merged after the host's ComposerDescribedBy ids");
        cut.Find($"#{cut.Instance.ComposerInputId}-hint").TextContent.Should().NotBeEmpty(
            "the hint span is rendered for assistive technology");

        cut.Render(p => p.Add(c => c.EnterToSend, false));

        cut.Find(".tm-chat__input").GetAttribute("enterkeyhint").Should().Be("enter");
        cut.Find($"#{cut.Instance.ComposerInputId}-hint").TextContent.Should().Contain("Ctrl+Enter",
            "the hint documents the Ctrl/⌘+Enter submit path of EnterToSend=false");
    }

    // ── EnterToSend=false: Enter is a new line, only the button sends ────────

    /// <summary>
    /// A module-level <c>attachComposer</c> handler that fails with a real JSException: the
    /// module loaded, the attach did not. bUnit's <c>Setup&lt;IJSObjectReference&gt;</c> guard
    /// rejects result-typed setups on the root interop, so module functions returning an
    /// object reference register through <c>AddInvocationHandler</c> instead.
    /// </summary>
    private sealed class FailingAttachHandler : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        public FailingAttachHandler()
            : base(invocation => invocation.Identifier == "attachComposer", false) { }

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
            => Task.FromException<IJSObjectReference>(new JSException("attach failed in this host"));
    }

    /// <summary>A module-level <c>attachComposer</c> handler that succeeds with a fixed handle.</summary>
    private sealed class AttachHandler : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        private readonly IJSObjectReference _handle;

        public AttachHandler(IJSObjectReference handle)
            : base(invocation => invocation.Identifier == "attachComposer", false)
            => _handle = handle;

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
            => Task.FromResult(_handle);
    }

    [Fact]
    public async Task EnterToSend_False_CSharpFallback_DoesNotSubmit()
    {
        // The module loads but the attach fails (a real JSException), so the C# fallback stays
        // active and must honour EnterToSend.
        var module = JSInterop.SetupModule(ComposerModulePath);
        module.AddInvocationHandler(new FailingAttachHandler());
        var sent = new List<string>();
        var cut = RenderChat([], p => p
            .Add(c => c.EnterToSend, false)
            .Add(c => c.OnSendMessage, EventCallback.Factory.Create<string>(this, t => sent.Add(t))));

        cut.Find(".tm-chat__input").Input("Hello");
        cut.Render();
        cut.Find(".tm-chat__input").KeyDown("Enter");

        sent.Should().BeEmpty("EnterToSend=false makes Enter a native new line — only the button sends");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Fallback_Enter_WhenAttachFails_SendsOnce()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        module.AddInvocationHandler(new FailingAttachHandler());
        var sent = new List<string>();
        var cut = RenderChat([], p => p.Add(c => c.OnSendMessage,
            EventCallback.Factory.Create<string>(this, t => sent.Add(t))));

        cut.Find(".tm-chat__input").Input("Hello");
        cut.Render();
        cut.Find(".tm-chat__input").KeyDown("Enter");

        sent.Should().ContainSingle("with the attach failed, the C# fallback owns Enter and sends once");

        // The failed attach rolled back, so the fallback keeps working on the next input.
        cut.Find(".tm-chat__input").Input("Hello again");
        cut.Render();
        cut.Find(".tm-chat__input").KeyDown("Enter");

        sent.Should().HaveCount(2, "the fallback keeps submitting after a failed attach");
        sent[1].Should().Be("Hello again");
    }

    [Fact]
    public async Task EnterToSend_False_WithModuleAttached_DoesNotSubmitViaFallback()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var sent = new List<string>();
        var cut = RenderChat([], p => p
            .Add(c => c.EnterToSend, false)
            .Add(c => c.OnSendMessage, EventCallback.Factory.Create<string>(this, t => sent.Add(t))));

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "attachComposer"));

        cut.Find(".tm-chat__input").Input("Hello");
        cut.Render();
        cut.Find(".tm-chat__input").KeyDown("Enter");

        sent.Should().BeEmpty("neither the module (enterToSend:false) nor the fallback submits");
        cut.Find(".tm-chat__input").GetAttribute("value").Should().Be("Hello");
    }

    // ── Custom composer: AttachAsync gives the slot the same behavior ────────

    [Fact]
    public async Task ChatComposerContext_AttachAsync_AttachesTheHostElement()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        ChatComposerContext? received = null;
        var cut = RenderChat([], p => p.Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx)));

        await cut.InvokeAsync(() => received!.AttachAsync(new ElementReference("custom-input")));

        module.Invocations.Should().Contain(i =>
            i.Identifier == "attachComposer"
            && i.Arguments.Count >= 1
            && i.Arguments[0] is ElementReference
            && ((ElementReference)i.Arguments[0]).Id == "custom-input",
            "the host's own input receives the same Enter/IME behavior as the built-in composer");
    }

    [Fact]
    public async Task ChatComposerContext_AttachAsync_CustomComposerRefocusesAfterSend()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        ChatComposerContext? received = null;
        var cut = RenderChat([], p => p.Add(c => c.ComposerTemplate, MarkerComposer("my-composer", ctx => received = ctx)));

        await cut.InvokeAsync(() => received!.AttachAsync(new ElementReference("custom-input")));
        await received!.SetTextAsync("Hello");
        await received.SendAsync();

        module.Invocations.Should().Contain(i => i.Identifier == "focusIfNeeded"
            && i.Arguments.Count >= 1
            && i.Arguments[0] is ElementReference
            && ((ElementReference)i.Arguments[0]).Id == "custom-input",
            "RefocusAfterSend focuses the registered custom-composer input after a successful send");
        received.Text.Should().BeEmpty("the custom composer clears after the send");
    }

    // ── Thread reply and edit submit through their own JSInvokable methods ───

    [Fact]
    public async Task ThreadReply_ViaJsInvokable_SendsWithThreadContext()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var messages = new[]
        {
            Incoming("m1", "Question?"),
            new ChatMessage("r1", "Answer", Me, ChatMessageType.Outgoing) { ThreadRootId = "m1" },
        };
        ChatSendRequest? sent = null;
        var cut = RenderChat(messages, p => p.Add(c => c.OnSend,
            EventCallback.Factory.Create<ChatSendRequest>(this, r => sent = r)));

        cut.Find("[data-testid='chat-thread-open-m1']").Click();
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "attachComposer"));

        cut.Find("[data-testid='chat-thread-input']").Input("Follow-up");
        cut.Render();

        var dotNetRef = AttachDotNetRef(module);
        await cut.InvokeAsync(() => dotNetRef.Value.OnThreadComposerSubmit());

        sent.Should().NotBeNull("the thread composer routes to OnThreadComposerSubmit");
        sent!.Text.Should().Be("Follow-up");
        sent.ThreadRootId.Should().Be("m1");
        cut.Find("[data-testid='chat-thread-input']").GetAttribute("value").Should().BeEmpty(
            "the thread reply is cleared after the send completed");
    }

    [Fact]
    public async Task Edit_Commit_ViaJsInvokable_CommitsTheEdit()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var messages = new[] { new ChatMessage("m1", "Helo", Me, ChatMessageType.Outgoing) };
        ChatMessageEdit? edit = null;
        var cut = RenderChat(messages, p => p.Add(c => c.OnEditMessage,
            EventCallback.Factory.Create<ChatMessageEdit>(this, e => edit = e)));

        cut.Find("[data-testid='chat-edit-m1']").Click();
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "attachComposer"));

        cut.Find("[data-testid='chat-edit-input']").Input("Hello");
        cut.Render();

        var dotNetRef = AttachDotNetRef(module);
        await cut.InvokeAsync(() => dotNetRef.Value.OnEditComposerSubmit());

        edit.Should().NotBeNull("the edit input routes to OnEditComposerSubmit");
        edit!.MessageId.Should().Be("m1");
        edit.NewText.Should().Be("Hello");
    }

    [Fact]
    public async Task ThreadReply_RefocusAfterSend_FocusesThreadInput()
    {
        var module = JSInterop.SetupModule(ComposerModulePath);
        var messages = new[]
        {
            Incoming("m1", "Question?"),
            new ChatMessage("r1", "Answer", Me, ChatMessageType.Outgoing) { ThreadRootId = "m1" },
        };
        var cut = RenderChat(messages, p => p.Add(c => c.OnSend,
            EventCallback.Factory.Create<ChatSendRequest>(this, _ => { })));

        cut.Find("[data-testid='chat-thread-open-m1']").Click();
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "attachComposer"));

        cut.Find("[data-testid='chat-thread-input']").Input("Follow-up");
        cut.Render();
        cut.Find("[data-testid='chat-thread-send']").Click();

        cut.WaitForAssertion(() => JSInterop.Invocations
            .Count(i => i.Identifier == "Blazor._internal.domWrapper.focus").Should().Be(1,
                "RefocusAfterSend focuses the thread input after a successful reply"));
        cut.Find("[data-testid='chat-thread-input']").GetAttribute("value").Should().BeEmpty();
    }

    // ── Overlapping renders while the module import is pending ───────────────

    /// <summary>
    /// N198 pattern: a JSInterop handler for the <c>import</c> call whose result completes only
    /// when the test releases it — the window in which a first sync is parked on the module
    /// import while a second render starts.
    /// </summary>
    private sealed class GatedImport : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        // bUnit's import handler answers with an internal BunitJSObjectReference bound to the
        // module — the gated equivalent must return the same type so the module calls work.
        private static readonly Type ObjectReferenceType = typeof(BunitJSInterop).Assembly
            .GetType("Bunit.BunitJSObjectReference")!;

        private readonly TaskCompletionSource<IJSObjectReference> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly BunitJSModuleInterop _module;

        public GatedImport(BunitJSModuleInterop module)
            : base(
                invocation => invocation.Identifier == "import"
                    && invocation.Arguments is [string path] && path == ComposerModulePath,
                false)
            => _module = module;

        /// <summary>How many import invocations have been dispatched to this handler.</summary>
        public int DispatchCount { get; private set; }

        public void Release() => _gate.TrySetResult(
            (IJSObjectReference)Activator.CreateInstance(ObjectReferenceType, _module)!);

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
        {
            DispatchCount++;
            return _gate.Task;
        }
    }

    [Fact]
    public async Task Sync_OverlappingRendersWhileImportPending_AttachOncePerElement()
    {
        var module = new BunitJSModuleInterop(JSInterop);
        var handle = (IJSObjectReference)Activator.CreateInstance(
            typeof(BunitJSInterop).Assembly.GetType("Bunit.BunitJSObjectReference")!, module)!;
        module.AddInvocationHandler(new AttachHandler(handle));
        var import = new GatedImport(module);
        JSInterop.AddInvocationHandler(import);

        // Render: the first OnAfterRenderAsync parks inside the gated import.
        var cut = RenderChat([]);
        cut.WaitForAssertion(() => import.DispatchCount.Should().Be(1));

        // A second render overlaps while the first sync is still parked on the import.
        cut.Render();

        import.Release();

        // The parked run finishes and the sync loop re-runs once — the slot is attached by
        // then, so exactly ONE attachComposer lands for the element and NO second import runs.
        cut.WaitForAssertion(() => module.Invocations["attachComposer"].Should().HaveCount(1));
        import.DispatchCount.Should().Be(1, "the cached import task is shared between the overlapping renders");
        module.Invocations["attachComposer"].Should().HaveCount(1, "exactly one attach per element per state");
    }
}
