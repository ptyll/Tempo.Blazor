using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the focus-recovery exports (<c>focusIfLost</c>, <c>focusWithin</c>,
/// <c>restoreTrapFocusIfLost</c>) of the shared <c>tm-focus-trap</c> ES module. A host surface (the
/// editor shell) that re-renders its own trigger after one of its panels closed moves focus there
/// ONLY when focus was lost — on the body, on a detached element or inside the closing surface —
/// and never when the user or the host placed focus on another live element (F6 review round 1,
/// F6). Lazily imports the module on first use (safe to construct during prerender — nothing is
/// called before <see cref="FocusIfLostAsync"/>). The import/guard boilerplate is delegated to
/// <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
/// <remarks>Every interop is guarded: where JS is unavailable the call is a no-op.</remarks>
internal sealed class FocusIfLostInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    private readonly LazyJsModule _lazy;
    private bool _disposed;

    public FocusIfLostInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>Focuses the element with the id when focus is lost or sits inside the container with <paramref name="containerId"/>. Returns whether focus moved.</summary>
    public async Task<bool> FocusIfLostAsync(string elementId, string? containerId = null)
    {
        if (_disposed) return false;
        var (ok, moved) = await _lazy.TryInvokeAsync<bool>("focusIfLost", elementId, containerId);
        return ok && moved;
    }

    /// <summary>
    /// Imports the module ahead of time. <see cref="FocusWithinAsync"/> must reach the browser BEFORE
    /// the render that removes the focused element; a first-use import would let that render win.
    /// </summary>
    public async Task WarmUpAsync()
    {
        if (_disposed) return;
        await _lazy.GetAsync();
    }

    /// <summary>Whether focus currently sits inside the element. False where JS is unavailable.</summary>
    public async Task<bool> FocusWithinAsync(ElementReference element)
    {
        if (_disposed) return false;
        var (ok, inside) = await _lazy.TryInvokeAsync<bool>("focusWithin", element);
        return ok && inside;
    }

    /// <summary>
    /// Asks the shared focus trap to recover focus for the topmost MODAL trap after a surface
    /// outside it (a toast) removed the focused element. Acts only when an inert-exempt surface
    /// actually held focus and its element has since left the DOM — a toast expiring while nobody
    /// focused it never moves focus (WCAG 3.2.1/APG), and a focus the user placed elsewhere is
    /// never stolen. False where JS is unavailable, no modal trap is open, or focus was not lost.
    /// </summary>
    public async Task<bool> RestoreTrapFocusIfLostAsync()
    {
        if (_disposed) return false;
        var (ok, recovered) = await _lazy.TryInvokeAsync<bool>("restoreTrapFocusIfLost");
        return ok && recovered;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lazy.DisposeAsync();
    }
}
