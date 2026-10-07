using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the shared <c>tm-focus-trap</c> ES module. Lazily imports the
/// module on first activation (safe to construct during prerender — no interop happens until
/// <see cref="ActivateAsync{T}"/> is called from <c>OnAfterRenderAsync</c>), traps Tab focus
/// inside an overlay, restores focus to the trigger on close, and optionally routes a
/// document-level Escape key back to the owning component.
/// </summary>
/// <remarks>
/// Used by <c>TmFocusScope</c>, which is how TmModal, TmDialog, TmDrawer and the other
/// migrated overlays share one focus-trap implementation.
/// All interop is guarded: when JS is unavailable (bUnit / prerender / disconnected circuit)
/// activation degrades to a best-effort <see cref="ElementReference.FocusAsync()"/> and never
/// throws into the render loop.
/// </remarks>
internal sealed class FocusTrap : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    private readonly IJSRuntime _js;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private IJSObjectReference? _module;
    private bool _active;
    private bool _disposed;

    public FocusTrap(IJSRuntime js) => _js = js;

    /// <summary>
    /// Activates the trap on <paramref name="element"/>. When <paramref name="closeOnEscape"/> is
    /// true and <paramref name="escapeHandler"/> is supplied, a document-level Escape listener
    /// invokes the component's <c>[JSInvokable] HandleFocusTrapEscapeAsync</c>.
    /// </summary>
    public async Task ActivateAsync<T>(
        ElementReference element,
        DotNetObjectReference<T>? escapeHandler = null,
        bool closeOnEscape = false,
        ElementReference? restoreTarget = null,
        bool modal = true) where T : class
    {
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed)
            {
                // The import outlived the owner. Deactivate on the captured module, then drop it:
                // a deactivate against a field a racing dispose already nulled would leak the trap.
                await DeactivateOrDropAsync(module);
                await ReleaseAsync(module);
                return;
            }
            if (_module is null)
            {
                // JS unavailable (bUnit loose interop / prerender) — best-effort focus.
                await FallbackFocusAsync(element);
                return;
            }
            await _module.InvokeVoidAsync("activate", element, _id, escapeHandler, closeOnEscape, restoreTarget, modal);
            if (_disposed)
            {
                await DeactivateOrDropAsync();
                return;
            }
            _active = true;
        }
        catch (JSException) { await FallbackFocusAsync(element); }
        catch (InvalidOperationException) { await FallbackFocusAsync(element); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (NullReferenceException) { await FallbackFocusAsync(element); }
    }

    /// <summary>Deactivates the trap and restores focus to the previously-focused element.</summary>
    public async Task DeactivateAsync()
    {
        // Dispose always deactivates, even when activation never reported success: the module's
        // deactivate is idempotent, and a trap that attached before the flag flipped would leak.
        if (!_active && !_disposed) return;
        _active = false;
        var module = _module;
        if (module is null) return;
        try
        {
            await module.InvokeVoidAsync("deactivate", _id);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private static async Task FallbackFocusAsync(ElementReference element)
    {
        try { await element.FocusAsync(); } catch { /* JS unavailable — best effort */ }
    }

    private Task DeactivateOrDropAsync() => DeactivateOrDropAsync(_module);

    private async Task DeactivateOrDropAsync(IJSObjectReference? module)
    {
        if (module is null) return;
        try { await module.InvokeVoidAsync("deactivate", _id); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private async Task ReleaseAsync(IJSObjectReference? module)
    {
        if (module is null) return;
        if (ReferenceEquals(_module, module)) _module = null;
        try { await module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        // The scope that owns this trap is disposed both by its host and by the renderer.
        if (_disposed) return;
        _disposed = true;

        await DeactivateAsync();
        if (_module is null) return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }

}
