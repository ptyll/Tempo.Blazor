using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// FZE-12 — the OPT-IN auto-attach of a <c>ComposerTemplate</c> input by id. A host whose
/// template input carries <c>id="@composer.InputId"</c> sets
/// <see cref="AutoAttachComposerInput"/> and TmChat resolves the element itself: the
/// <c>chat.js</c> <c>attachComposerById(root, inputId, dotNetRef, options)</c> looks the input
/// up INSIDE the chat root (never page-wide, because the id is per-TmChat-instance) and owns
/// the same Enter/Shift+Enter + IME + auto-resize behavior as the built-in composer. The
/// explicit <see cref="ChatComposerContext.AttachAsync"/> ALWAYS wins — as long as the host has
/// a registration, the auto handle is detached; an <c>AttachAsync(default)</c> hands the
/// control back to the auto-attach. Split out of <c>TmChat.razor.cs</c> to keep that file under
/// the 1200-line source ceiling (N270/F13), like the other partials.
/// </summary>
public partial class TmChat
{
    /// <summary>
    /// Attaches the <see cref="ComposerTemplate"/> input by id automatically — without the
    /// host's <c>OnAfterRenderAsync</c> + <see cref="ChatComposerContext.AttachAsync"/> dance.
    /// Default <see langword="false"/> (today's behavior: the host registers its input
    /// explicitly, or gets no composer interop at all).
    /// <para>
    /// Applies ONLY together with <see cref="ComposerTemplate"/> (the built-in composer is
    /// attached by element as before, and the parameter is ignored without a template). The
    /// template input must carry <c>id="@composer.InputId"</c> — the lookup is
    /// <c>root.querySelector('#' + id)</c> scoped to this chat instance's root, so the same id
    /// elsewhere on the page never wins. The input inside a nested host component that
    /// re-renders independently of TmChat is re-captured on the next TmChat render (the handle
    /// <c>sync()</c> re-queries); a host that swaps the element WITHOUT TmChat re-rendering
    /// still needs the explicit <see cref="ChatComposerContext.AttachAsync"/>.
    /// </para>
    /// <para>
    /// Cost: with the option on, EVERY render makes one interop round-trip — the first render
    /// issues the <c>attachComposerById</c> attach, every further render one synchronous
    /// <c>sync()</c> on the handle (a no-op in JS while the input node is unchanged). A host
    /// that re-renders <see cref="TmChat"/> at high frequency should weigh this against the
    /// explicit <see cref="ChatComposerContext.AttachAsync"/> handshake (one call per replaced
    /// element).
    /// </para>
    /// <para>
    /// Precedence: an explicit <see cref="ChatComposerContext.AttachAsync"/> ALWAYS wins — the
    /// auto-attached handle is disposed for as long as the host registration exists, and an
    /// <c>AttachAsync(default)</c> returns the control to the auto-attach. Turning the
    /// parameter off (or <see cref="ShowComposer"/>=<see langword="false"/>, or removing the
    /// template) disposes the handle. An <see cref="EnterToSend"/> change re-attaches (the
    /// option is baked into the attach call). The refocus after a successful send
    /// (<c>RefocusAfterSend</c>) and <see cref="FocusComposerAsync"/> /
    /// <see cref="ChatComposerContext.FocusAsync"/> target the auto-attached input.
    /// </para>
    /// <para>
    /// Like every composer path the attach is a JS-module feature: BEFORE interactivity
    /// (prerender, a failed import) nothing happens — the C# <c>@onkeydown</c> fallback cannot
    /// help a template input it did not render, so the composer is inert until the JS module
    /// attaches (documented in the composer contract).
    /// </para>
    /// </summary>
    [Parameter] public bool AutoAttachComposerInput { get; set; }

    /// <summary>The chat root element (the <c>.tm-chat</c> div) — the scope of the id lookup.</summary>
    private ElementReference _chatRoot;

    /// <summary>
    /// The <c>{ sync, focusIfNeeded, dispose }</c> handle of the auto-attached composer input.
    /// <see langword="null"/> while the parameter is off, the template is gone, the explicit
    /// <see cref="ChatComposerContext.AttachAsync"/> holds the registration, or the attach has
    /// not happened yet.
    /// </summary>
    private IJSObjectReference? _autoAttachHandle;

    /// <summary>The <c>enterToSend</c> option the auto handle was attached with (re-attach on change).</summary>
    private bool _autoAttachEnterToSend = true;

    /// <summary>Test-visible: the captured root element reference (the <c>attachComposerById</c> scope).</summary>
    internal ElementReference ChatRoot => _chatRoot;

    /// <summary>
    /// The auto-attach half of <see cref="SyncJsComposersCoreAsync"/> (run while a
    /// <see cref="ComposerTemplate"/> renders, <see cref="ShowComposer"/> is
    /// <see langword="true"/>, the parameter is on and NO explicit registration exists):
    /// the first pass issues ONE <c>attachComposerById</c> for the chat root and the composer
    /// input id; every further render only calls the handle's <c>sync()</c> (same node = no-op,
    /// replaced node = re-attach, missing node = detach). An <see cref="EnterToSend"/> change
    /// disposes the handle and re-attaches — the option is baked into the attach call. The
    /// serialized sync of <see cref="SyncJsComposersAsync"/> makes overlapping renders safe.
    /// </summary>
    private async Task SyncAutoAttachHandleAsync()
    {
        if (_disposed || _module is null || _selfRef is null)
        {
            await DetachAutoAttachHandleAsync();
            return;
        }

        if (_autoAttachHandle is not null)
        {
            if (_autoAttachEnterToSend == EnterToSend)
            {
                try
                {
                    await _autoAttachHandle.InvokeVoidAsync("sync");
                    return;
                }
                catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
                {
                    // The handle is unreachable (dropped circuit / disposed module) — drop it
                    // and fall through to a fresh attach; the C# fallback cannot help a
                    // template input, so the re-attach is the self-healing path.
                    await DetachAutoAttachHandleAsync();
                }
            }
            else
            {
                // The option changed — the running handle submits with the old mode.
                await DetachAutoAttachHandleAsync();
            }
        }

        try
        {
            var handle = await _module.InvokeAsync<IJSObjectReference>(
                "attachComposerById",
                _chatRoot,
                _composerInputId,
                _selfRef,
                new { enterToSend = EnterToSend, maxRows = ComposerMaxRows, submitMethod = ComposerSubmitMethods.Main });
            if (_disposed)
            {
                // The component went away while the attach was in flight — the handle would
                // leak (and its listeners would outlive the element).
                await DisposeJsHandleAsync(handle);
                return;
            }

            _autoAttachHandle = handle;
            _autoAttachEnterToSend = EnterToSend;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // The attach failed — nothing is attached (the template input has no C# fallback
            // of ours) and the NEXT render retries (self-healing after a transient failure).
            await DetachAutoAttachHandleAsync();
        }
    }

    /// <summary>
    /// Drops the auto-attach handle (idempotent — a <see langword="null"/> handle is a no-op).
    /// Called when the explicit <see cref="ChatComposerContext.AttachAsync"/> wins, when the
    /// composer disappears (<see cref="ShowComposer"/>=<see langword="false"/> / template
    /// removed / parameter off) and from <see cref="DisposeAsyncCore"/> via
    /// <see cref="DisposeComposerInteropAsync"/> — a dropped circuit is swallowed there.
    /// </summary>
    private async Task DetachAutoAttachHandleAsync()
    {
        var handle = _autoAttachHandle;
        _autoAttachHandle = null;
        await DisposeJsHandleAsync(handle);
    }

    /// <summary>
    /// Re-measures the AUTO-attached composer textarea after a programmatic clear (a send):
    /// the same contract as <see cref="ResizeComposerAfterClearAsync"/> for the slot handles —
    /// a value written by the render raises no <c>input</c> event, so the height would stay
    /// grown. A no-op while nothing is auto-attached; failures are swallowed (a dropped circuit
    /// loses the resize, not the text).
    /// </summary>
    private async ValueTask ResizeAutoComposerAfterClearAsync()
    {
        var handle = _autoAttachHandle;
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
}
