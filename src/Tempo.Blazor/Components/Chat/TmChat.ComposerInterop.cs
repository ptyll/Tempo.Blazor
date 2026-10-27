using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Threading;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Composer JS-interop half of <see cref="TmChat"/> (FZE-5): the <c>chat.js</c> ES module owns
/// the composer keydown behavior — Enter submits (with <c>preventDefault</c>), Shift+Enter and
/// Alt/Ctrl/Meta+Enter stay native, and an IME composition session (<c>event.isComposing</c>,
/// the legacy <c>keyCode === 229</c>) never submits. The IME guard MUST live in JS: a net8
/// <see cref="Web.KeyboardEventArgs"/> has no <c>IsComposing</c> (it exists only on net9+,
/// where the C# fallback guards it under <c>#if NET9_0_OR_GREATER</c>). While the module is not
/// yet attached (prerender / before interactivity, a failed import) the C# <c>@onkeydown</c>
/// handlers remain the fallback, so Enter keeps working with exactly the same submit semantics.
/// <para>
/// The module is imported once per component (unconditionally on the first render) and attached
/// to up to three inputs — the built-in composer textarea (or the element a custom composer
/// registered through <see cref="ChatComposerContext.AttachAsync"/>, or — FZE-12 — the template
/// input resolved BY ID when <c>AutoAttachComposerInput</c> is on, see
/// <c>TmChat.ComposerAutoAttach.cs</c>), the open thread panel's reply input and the open
/// message edit — re-attached whenever an input remounts or <see cref="TmChat.EnterToSend"/>
/// changes, and disposed in <see cref="DisposeAsyncCore"/>.
/// </para>
/// </summary>
public partial class TmChat
{
    private const string ComposerModulePath = "./_content/Tempo.Blazor/js/chat.js";

    /// <summary>
    /// Rows the composer textareas may grow to. 5 rows ≈ 125 px of text area — the CSS
    /// <c>max-height: 120px</c> of <c>.tm-chat__input</c> remains the HARD cap (the JS cap is
    /// <c>min(rowCap, computed max-height)</c>), so 5 rows and the CSS cap agree for a 21 px line.
    /// </summary>
    private const int ComposerMaxRows = 5;

    private IJSObjectReference? _module;

    /// <summary>
    /// The in-flight import, cached so overlapping renders share one import instead of racing.
    /// Reset to <see langword="null"/> on failure so a later render retries.
    /// </summary>
    private Task<IJSObjectReference>? _moduleTask;

    private DotNetObjectReference<TmChat>? _selfRef;

    /// <summary>Element a custom composer registered through <see cref="ChatComposerContext.AttachAsync"/>.</summary>
    private ElementReference? _customComposerElement;

    private readonly ComposerSlot _mainSlot = new();
    private readonly ComposerSlot _threadSlot = new();
    private readonly ComposerSlot _editSlot = new();

    /// <summary>
    /// Serialization gates of <see cref="SyncJsComposersAsync"/>: a sync invoked while another
    /// one is still awaiting (an overlapping render, a host <c>AttachAsync</c> during a pending
    /// attach) sets <see cref="_syncAgain"/> instead of issuing a second parallel attach, and
    /// the running loop re-runs once it finishes.
    /// </summary>
    private bool _syncing;
    private bool _syncAgain;

    /// <summary>Per-input attach state of the chat.js composer module.</summary>
    private sealed class ComposerSlot
    {
        /// <summary>The <c>{ dispose, resize }</c> handle returned by <c>attachComposer</c>.</summary>
        public IJSObjectReference? Handle;

        /// <summary>Id of the element the handle is attached to (empty when not attached).</summary>
        public string ElementId = string.Empty;

        /// <summary>The <c>enterToSend</c> option the handle was attached with (re-attach when it changes).</summary>
        public bool EnterToSend = true;

        /// <summary>
        /// Whether the attach call completed with a handle — once true the JS module owns Enter
        /// for this input and the C# <c>@onkeydown</c> fallback must stay quiet (exactly one
        /// activation). Set ONLY after a non-null handle came back, never before the call.
        /// </summary>
        public bool JsOwnsEnter;
    }

    /// <summary>
    /// Called by the <c>chat.js</c> composer module when Enter submits the MAIN composer
    /// (the IME guard already ran in JS). Runs through <see cref="InvokeAsync(Func{Task})"/> so
    /// the submit is serialized on the renderer's synchronization context exactly like a button
    /// click; <see cref="SubmitComposerAsync"/> itself dedups a duplicate dispatch (submitting
    /// flag / empty text), so a late duplicate is a no-op rather than a second send. A host
    /// handler that throws is dispatched back to the renderer
    /// (<see cref="ComponentBase.DispatchExceptionAsync"/>) so the Enter path surfaces errors
    /// the same way a button click does — the JS side keeps its catch for disconnects only.
    /// </summary>
    [JSInvokable]
    public Task OnComposerSubmit() => InvokeAsync(async () =>
    {
        try
        {
            await SubmitComposerAsync();
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    });

    /// <summary>
    /// Called by the <c>chat.js</c> composer module when Enter submits the open thread panel's
    /// reply input. Ends with an explicit re-render: a JS-invoked entry point has no post-event
    /// automatic render to apply the cleared input. Failures are dispatched
    /// (<see cref="ComponentBase.DispatchExceptionAsync"/>) like in <see cref="OnComposerSubmit"/>.
    /// </summary>
    [JSInvokable]
    public Task OnThreadComposerSubmit() => InvokeAsync(async () =>
    {
        try
        {
            await SendThreadReplyAsync();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    });

    /// <summary>
    /// Called by the <c>chat.js</c> composer module when Enter commits an open message edit —
    /// with the same explicit re-render and failure dispatch as
    /// <see cref="OnThreadComposerSubmit"/>.
    /// </summary>
    [JSInvokable]
    public Task OnEditComposerSubmit() => InvokeAsync(async () =>
    {
        try
        {
            await CommitEditAsync();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    });

    /// <summary>
    /// Registers a CUSTOM composer's input element (the host's own textarea inside a
    /// <c>ComposerTemplate</c>) so it receives the same Enter/Shift+Enter + IME behavior as the
    /// built-in composer, is auto-resized and is the refocus target after a successful send.
    /// Called from the host's <c>OnAfterRenderAsync</c> once its input has mounted; call again
    /// when the element is replaced, and with <c>default</c> to detach. Safe from any thread —
    /// routed through the same serialized sync as the render-driven path.
    /// </summary>
    internal Task AttachCustomComposerAsync(ElementReference element)
        => InvokeAsync(async () =>
        {
            if (_disposed) return;
            _customComposerElement = element;
            await SyncJsComposersAsync();
        });

    /// <summary>Submit-method routing of the three composer inputs (the <c>chat.js</c> option).</summary>
    private static class ComposerSubmitMethods
    {
        public const string Main = "OnComposerSubmit";
        public const string Thread = "OnThreadComposerSubmit";
        public const string Edit = "OnEditComposerSubmit";
    }

    /// <summary>
    /// (Re)attaches the module to every currently rendered composer input and detaches it from
    /// the ones that are gone. Called after every render and from
    /// <see cref="AttachCustomComposerAsync"/>; the per-element id comparison makes the
    /// re-attach a no-op for unchanged inputs. Serialized through <see cref="_syncing"/> /
    /// <see cref="_syncAgain"/> so overlapping invocations never issue a parallel
    /// <c>attachComposer</c> for the same element — exactly one attach per element per state.
    /// </summary>
    private async Task SyncJsComposersAsync()
    {
        if (_syncing)
        {
            _syncAgain = true;
            return;
        }

        _syncing = true;
        try
        {
            do
            {
                _syncAgain = false;
                await SyncJsComposersCoreAsync();
            }
            while (_syncAgain && !_disposed);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>The single sync pass behind <see cref="SyncJsComposersAsync"/>.</summary>
    private async Task SyncJsComposersCoreAsync()
    {
        if (_disposed) return;
        _selfRef ??= DotNetObjectReference.Create(this);

        if (ShowComposer && ComposerTemplate is not null && _customComposerElement is { } custom && !string.IsNullOrEmpty(custom.Id))
        {
            // An EXPLICIT AttachAsync ALWAYS wins — the auto-attached handle (FZE-12) is
            // dropped for as long as the host registration exists (AttachAsync(default)
            // hands the control back to the auto-attach).
            await DetachAutoAttachHandleAsync();
            await EnsureModuleAsync();
            await SyncComposerSlotAsync(_mainSlot, custom, ComposerSubmitMethods.Main);
        }
        else if (ShowComposer && ComposerTemplate is not null && AutoAttachComposerInput)
        {
            // FZE-12: the template input carries id="@composer.InputId" — resolve and attach it
            // by id inside the chat root; further renders only sync() the handle.
            await DetachComposerSlotAsync(_mainSlot);
            await EnsureModuleAsync();
            await SyncAutoAttachHandleAsync();
        }
        else if (ShowComposer && ComposerTemplate is null)
        {
            await DetachAutoAttachHandleAsync();
            await EnsureModuleAsync();
            await SyncComposerSlotAsync(_mainSlot, _composerInput, ComposerSubmitMethods.Main);
        }
        else
        {
            // No composer rendered (ShowComposer=false, a template with nothing attached and
            // no auto-attach, …) — drop the registration so a stale element cannot be
            // re-attached later.
            await DetachAutoAttachHandleAsync();
            await DetachComposerSlotAsync(_mainSlot);
            _customComposerElement = null;
        }

        if (EnableThreads && ActiveThreadRoot is not null)
        {
            await EnsureModuleAsync();
            await SyncComposerSlotAsync(_threadSlot, _threadInputElement, ComposerSubmitMethods.Thread);
        }
        else
        {
            await DetachComposerSlotAsync(_threadSlot);
        }

        if (_editingId is not null)
        {
            await EnsureModuleAsync();
            await SyncComposerSlotAsync(_editSlot, _editInputElement, ComposerSubmitMethods.Edit);
        }
        else
        {
            await DetachComposerSlotAsync(_editSlot);
        }
    }

    private async Task SyncComposerSlotAsync(ComposerSlot slot, ElementReference element, string submitMethod)
    {
        var elementId = element.Id;
        if (string.IsNullOrEmpty(elementId) || _module is null || _selfRef is null)
        {
            await DetachComposerSlotAsync(slot);
            return;
        }

        if (slot.JsOwnsEnter && slot.ElementId == elementId && slot.EnterToSend == EnterToSend)
        {
            return;
        }

        await DetachComposerSlotAsync(slot);
        try
        {
            var handle = await _module.InvokeAsync<IJSObjectReference>(
                "attachComposer",
                element,
                _selfRef,
                new { enterToSend = EnterToSend, maxRows = ComposerMaxRows, submitMethod });
            if (_disposed)
            {
                // The component went away while the attach was in flight — the handle would
                // leak (and its listeners would outlive the element).
                await DisposeJsHandleAsync(handle);
                return;
            }

            slot.Handle = handle;
            if (handle is not null)
            {
                // In the window between the JS attach and this line both the JS listener and
                // the C# fallback may see the same Enter; the send paths dedup it
                // (IsSendDisabled / _isThreadSubmitting / _editingId), so it still sends once.
                slot.ElementId = elementId;
                slot.EnterToSend = EnterToSend;
                slot.JsOwnsEnter = true;
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // The attach failed — roll back so the C# fallback keeps owning Enter and the next
            // render retries (self-healing after a dropped circuit or a transient failure).
            slot.JsOwnsEnter = false;
        }
    }

    private async Task DetachComposerSlotAsync(ComposerSlot slot)
    {
        slot.ElementId = string.Empty;
        slot.JsOwnsEnter = false;
        var handle = slot.Handle;
        slot.Handle = null;
        await DisposeJsHandleAsync(handle);
    }

    /// <summary>
    /// Disposes ANY chat.js handle (a composer slot's or the scroller's): invokes its
    /// <c>dispose</c> (removing the JS-side listeners) and releases the reference. All failure
    /// modes are swallowed — an unreachable circuit has no listeners to detach.
    /// </summary>
    private async ValueTask DisposeJsHandleAsync(IJSObjectReference? handle)
    {
        if (handle is null) return;
        try
        {
            await handle.InvokeVoidAsync("dispose");
            await handle.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // Already unreachable (unmounted element / dropped circuit) — nothing to detach.
        }
    }

    private async ValueTask DisposeComposerInteropAsync()
    {
        await DetachAutoAttachHandleAsync();

        foreach (var slot in new[] { _mainSlot, _threadSlot, _editSlot })
        {
            var handle = slot.Handle;
            slot.Handle = null;
            slot.ElementId = string.Empty;
            slot.JsOwnsEnter = false;
            await DisposeJsHandleAsync(handle);
        }

        _customComposerElement = null;

        _selfRef?.Dispose();
        _selfRef = null;

        if (_module is not null)
        {
            var module = _module;
            _module = null;
            _moduleTask = null;
            try
            {
                await module.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
            {
                // Circuit already gone (the TmScrollSpyNav pattern) — the module is unreachable.
            }
        }
        else
        {
            _moduleTask = null;
        }
    }

    /// <summary>
    /// Imports the composer module once per component, sharing the in-flight import between
    /// overlapping renders (<see cref="_moduleTask"/>). A failure (no interop, dropped circuit,
    /// missing asset) is swallowed, the cached task is reset and the next render retries — the
    /// C# fallback keeps Enter working in the meantime.
    /// </summary>
    private async Task EnsureModuleAsync()
    {
        if (_module is not null) return;
        _moduleTask ??= JS.InvokeAsync<IJSObjectReference>("import", ComposerModulePath).AsTask();
        try
        {
            var module = await _moduleTask;
            if (_disposed)
            {
                // The component was disposed while the import was in flight — dispose the
                // freshly imported module instead of keeping it.
                await DisposeUnreachableAsync(module);
                return;
            }

            _module = module;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
            // No interop here (before interactivity / dropped connection) — the C#
            // @onkeydown handlers remain the fallback; a later render retries the import.
            _moduleTask = null;
        }
    }

    /// <summary>
    /// Disposes a JS object reference that arrived after the component went away. A no-op for
    /// <see langword="null"/>; all disposal failures are swallowed — the reference is
    /// unreachable anyway.
    /// </summary>
    private async ValueTask DisposeUnreachableAsync(IJSObjectReference? reference)
    {
        if (reference is null) return;
        try
        {
            await reference.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // Unreachable — the dispose failure has no owner to report to.
        }
    }

    /// <summary>
    /// Re-measures a composer textarea after a programmatic clear (a send): unlike typing, a
    /// value written by the render raises no <c>input</c> event, so the height would stay grown.
    /// The module schedules the actual measurement on the next animation frame, after the
    /// pending render batch has been applied.
    /// </summary>
    private async Task ResizeComposerAfterClearAsync(ComposerSlot slot)
    {
        var handle = slot.Handle;
        if (handle is null) return;
        try
        {
            await handle.InvokeVoidAsync("resize");
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // A dropped circuit loses the resize, not the text.
        }
    }

    /// <summary>
    /// Refocuses the composer after a successful send — the same logic as
    /// <see cref="FocusComposerInputAsync"/> (the custom attached element if there is one,
    /// otherwise the built-in textarea), exposed for <see cref="TmChat.RefocusAfterSend"/>.
    /// </summary>
    private Task RefocusComposerAfterSendAsync() => FocusComposerInputAsync().AsTask();
}
