using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the shared <c>tm-sheet</c> module. Lazily imports the module on
/// first attach (safe to construct during prerender — no interop happens until
/// <see cref="AttachAsync{T}"/> is called from <c>OnAfterRenderAsync</c>), tracks the visible
/// viewport so the sheet lifts above an on-screen keyboard, and reports the snap a gesture
/// settled on. The module never writes the sheet's height; the component owns the snap index.
/// Top-layer promotion lives in <see cref="TopLayerInterop"/> — the shared helper every modal
/// viewport-anchored overlay root uses. The import/guard boilerplate is delegated to
/// <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
/// <remarks>
/// Used by <c>TmDrawer</c>, <c>TmModal</c> and <c>TmDialog</c> so a sheet has one implementation.
/// Every interop is guarded: when JS is unavailable the sheet renders from the stylesheet defaults
/// and never throws into the render loop.
/// </remarks>
internal sealed class SheetInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-sheet.js";

    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly LazyJsModule _lazy;
    private bool _attached;
    private bool _disposed;

    public SheetInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>
    /// Attaches the gesture and the viewport tracking. <paramref name="handle"/> is what the finger
    /// grabs; <paramref name="panel"/> is what moves. A null handle tracks the viewport only.
    /// <paramref name="basis"/> is the explicit inline host element an inline (non-tracking) sheet
    /// measures its snaps against — without it the module falls back to the legacy
    /// <c>.tm-drawer</c> closest lookup (F6 carry-forward).
    /// </summary>
    public async Task AttachAsync<T>(
        ElementReference? handle,
        ElementReference panel,
        double[] snaps,
        bool swipeToDismiss,
        DotNetObjectReference<T>? host,
        bool trackViewport = true,
        ElementReference? basis = null) where T : class
    {
        if (_disposed) return;
        // Null when JS is unavailable — or when the owner was disposed while the import awaited
        // (the helper released the module in that case).
        var module = await _lazy.GetAsync();
        if (module is null) return;

        var options = basis.HasValue
            ? new { track = trackViewport, basis = basis.Value }
            : (object)trackViewport;
        // A failed invoke never attached: a later DetachAsync stays silent instead of sending a
        // pointless detach for a gesture that does not exist.
        if (!await _lazy.TryInvokeVoidAsync("attachGesture", handle, panel, snaps, swipeToDismiss, host, _id, options)) return;
        if (_disposed) return;

        _attached = true;
    }

    /// <summary>Removes the gesture listeners. Safe to call twice.</summary>
    public async Task DetachAsync()
    {
        if (!_attached) return;
        _attached = false;
        if (!_lazy.IsImported) return;
        await _lazy.TryInvokeVoidAsync("detach", _id);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _attached = false;
        // The detach never triggers a fresh import and the JS detach is idempotent: a module that
        // was imported is always asked to clean up, even when the attach was still in flight or
        // never reported success — that attach may have landed anyway.
        if (_lazy.IsImported) await _lazy.TryInvokeVoidAsync("detach", _id);
        await _lazy.DisposeAsync();
    }
}
