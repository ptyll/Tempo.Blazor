using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-toolbar</c> ES module (roving tabindex + the fit measurement of
/// <c>TmToolbar</c>). Lazily imports the module on first use (safe to construct during prerender).
/// The import/guard boilerplate is delegated to <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
/// <remarks>Every interop is guarded: where JS is unavailable the toolbar keeps the natural tab order and
/// never collapses, and nothing throws into the render loop.</remarks>
internal sealed class ToolbarInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-toolbar.js";

    private readonly LazyJsModule _lazy;
    private ElementReference? _attached;
    private bool _disposed;

    public ToolbarInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>Attaches (or re-attaches with new options) the toolbar behaviour to the root element.</summary>
    public async Task AttachAsync<T>(ElementReference root, DotNetObjectReference<T> dotNetRef, bool overflow) where T : class
    {
        if (_disposed) return;
        // Stored BEFORE the invoke: a dispose that races the attach still knows the root the
        // module may have attached to; the JS detach is idempotent.
        _attached = root;
        await _lazy.TryInvokeVoidAsync("attach", root, dotNetRef, new { overflow });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var attached = _attached;
        _attached = null;
        if (attached is { } root && _lazy.IsImported)
        {
            await _lazy.TryInvokeVoidAsync("detach", root);
        }
        await _lazy.DisposeAsync();
    }
}
