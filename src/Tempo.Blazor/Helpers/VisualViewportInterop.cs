using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the shared <c>tm-visual-viewport.js</c> module: tracks how far an on-screen keyboard reaches into one
/// element and writes the gap as <c>--tm-visual-viewport-inset-block-end</c> on it (see the module header for the measuring rules).
/// </summary>
/// <remarks>
/// Advanced API for components that own a surface pinned to the layout viewport (<c>TmModal.TrackVirtualKeyboard</c> and the
/// DocumentEditor's bounded phone frame use it). The tracker lives in the browser under this instance's own id, so
/// <see cref="DetachAsync"/> and <see cref="DisposeAsync"/> release it even when Blazor already removed the element, an attach that
/// finishes after a detach or a dispose detaches itself, and the module is imported lazily on the first attach (safe to construct
/// during prerender). Every interop failure class is swallowed through <see cref="LazyJsModule"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class VisualViewportInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-visual-viewport.js";

    private readonly string _id = $"tm-vv-{Guid.NewGuid():N}";
    private readonly LazyJsModule _lazy;
    // Bumped by every attach / detach request: an attach whose generation is stale when its import or its invoke completes is a
    // request the owner already withdrew.
    private int _generation;
    private bool _attached;
    private bool _disposed;

    /// <summary>Creates the wrapper. No interop happens here.</summary>
    public VisualViewportInterop(IJSRuntime js)
    {
        // An import that outlived the owner still gets the detach by id (a no-op when nothing was attached) before it is released.
        _lazy = new LazyJsModule(js, ModulePath, module => module.InvokeVoidAsync("detach", _id).AsTask());
    }

    /// <summary>
    /// Starts tracking <paramref name="element"/>. Returns <c>false</c> when the tracker could not be attached (JS unavailable, the module
    /// failed to import, or the owner detached / disposed in the meantime) - a caller that wants to avoid retrying every render latches on it.
    /// </summary>
    /// <param name="element">The element that carries the property.</param>
    /// <param name="property">Another custom property name for a consumer with its own knob; <c>null</c> = <c>--tm-visual-viewport-inset-block-end</c>.</param>
    public async Task<bool> AttachAsync(ElementReference element, string? property = null)
    {
        if (_disposed) return false;
        var generation = ++_generation;

        var module = await _lazy.GetAsync();
        if (module is null || _disposed || generation != _generation) return false;

        var ok = await _lazy.TryInvokeVoidAsync("attach", element, new { id = _id, property });
        if (!ok) return false;

        if (_disposed || generation != _generation)
        {
            // A detach or a dispose asked for while the attach was in flight: the attach finishes by detaching itself.
            await _lazy.TryInvokeVoidAsync("detach", _id);
            return false;
        }

        _attached = true;
        return true;
    }

    /// <summary>Stops tracking and removes the property. Safe to call twice and before any attach.</summary>
    public async Task DetachAsync()
    {
        _generation++;
        if (!_attached || !_lazy.IsImported) return;
        _attached = false;
        await _lazy.TryInvokeVoidAsync("detach", _id);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _attached = false;
        // Detach by id is idempotent in the module: an imported module is always asked, even when an attach was still in flight.
        if (_lazy.IsImported) await _lazy.TryInvokeVoidAsync("detach", _id);
        await _lazy.DisposeAsync();
    }
}
