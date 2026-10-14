using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the shared <c>tm-focus-trap</c> ES module. Lazily imports the
/// module on first activation (safe to construct during prerender — no interop happens until
/// <see cref="ActivateAsync{T}"/> is called from <c>OnAfterRenderAsync</c>), traps Tab focus
/// inside an overlay, restores focus to the trigger on close, and optionally routes a
/// document-level Escape key back to the owning component. The import/guard boilerplate is
/// delegated to <see cref="LazyJsModule"/> (cluster-B 8.8).
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

    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly LazyJsModule _lazy;
    private bool _active;
    private bool _disposed;

    public FocusTrap(IJSRuntime js)
    {
        // An import that outlived the owner is deactivated, then released — a trap that attached
        // before a racing dispose flipped the flag must never leak.
        _lazy = new LazyJsModule(js, ModulePath, async module => await module.InvokeVoidAsync("deactivate", _id));
    }

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
        bool modal = true,
        ElementReference? initialTarget = null) where T : class
    {
        var activated = await _lazy.TryInvokeVoidAsync(
            "activate", element, _id, escapeHandler, closeOnEscape, restoreTarget, modal, initialTarget);
        if (activated)
        {
            if (!_lazy.IsDisposed) _active = true;
            return;
        }
        if (_lazy.IsDisposed) return;
        // JS unavailable (bUnit loose interop / prerender), the import failed or activate threw —
        // best-effort focus on the element.
        await FallbackFocusAsync(element);
    }

    /// <summary>Deactivates the trap and restores focus to the previously-focused element.</summary>
    public async Task DeactivateAsync()
    {
        // Dispose always deactivates, even when activation never reported success: the module's
        // deactivate is idempotent, and a trap that attached before the flag flipped would leak.
        if (!_active && !_disposed) return;
        _active = false;
        if (!_lazy.IsImported) return;
        await _lazy.TryInvokeVoidAsync("deactivate", _id);
    }

    private static async Task FallbackFocusAsync(ElementReference element)
    {
        try { await element.FocusAsync(); } catch { /* JS unavailable — best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        // The scope that owns this trap is disposed both by its host and by the renderer.
        if (_disposed) return;
        _disposed = true;

        await DeactivateAsync();
        await _lazy.DisposeAsync();
    }

}
