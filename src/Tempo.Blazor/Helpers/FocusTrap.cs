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
    private bool _noted;
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

    /// <summary>
    /// Notes the currently focused element as this trap's return target, BEFORE the render that
    /// presents the owning surface is applied. <see cref="ActivateAsync{T}"/> can only capture the
    /// opener after the lazy module import completed, so an app that focuses a field inside the
    /// overlay during the open would otherwise poison the capture — the close would then focus a
    /// dying element and the focus would fall to <c>&lt;body&gt;</c> when the surface left the
    /// DOM. Gated owners (TmModal/TmDialog promote first) note in the scope's FIRST parameter-set
    /// phase — the mount render, before any content can autofocus — through the scope's
    /// <c>NoteOpenerOnMount</c> wiring; it rides ahead of the render batch (its own
    /// interop message on Blazor Server). A note sent but never activated is forgotten on close
    /// and dispose, so a cancelled open never leaks into the next one.
    /// </summary>
    public async Task NoteReturnTargetAsync()
    {
        if (_disposed) return;
        _noted = true;
        await _lazy.TryInvokeVoidAsync("noteReturnTarget", _id);
    }

    /// <summary>
    /// Imports the module and installs its document-level opener records (the pointerdown record,
    /// the focus history) without activating anything. Owners call this from their first
    /// <c>OnAfterRenderAsync</c> while still closed, so the FIRST open of a browser session opens
    /// with the same evidence every later one has — the cold start used to load the module with
    /// the first activation and lose the opener record with it.
    /// </summary>
    public static async Task EnsureLoadedAsync(IJSRuntime js)
    {
        // A throwaway LazyJsModule — deliberately NOT a FocusTrap, whose dispose deactivates by
        // contract and would send a phantom "deactivate" for an id that never activated anything.
        // Releasing the module reference is all the loader needs: the ES module itself stays
        // cached in the browser and the listeners it installed are module-level state.
        var loader = new LazyJsModule(js, ModulePath);
        try
        {
            await loader.TryInvokeVoidAsync("ensureLoaded");
        }
        finally
        {
            await loader.DisposeAsync();
        }
    }

    /// <summary>Deactivates the trap and restores focus to the previously-focused element.</summary>
    public async Task DeactivateAsync()
    {
        // Dispose always deactivates, even when activation never reported success: the module's
        // deactivate is idempotent, and a trap that attached before the flag flipped would leak.
        // A note sent without an activation (a close that beat the promote gate) deactivates too,
        // so the module drops the opener even though no trap was ever registered under the id.
        if (!_active && !_noted && !_disposed) return;
        _active = false;
        _noted = false;
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
