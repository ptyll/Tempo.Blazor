using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-trace-waterfall</c> ES module, which owns the one thing a
/// <c>TmTraceWaterfall</c> cannot express in Blazor: cancelling the browser's scroll default for the
/// navigation keys its rows handle (↑/↓/←/→, Home/End, Space). The import/guard boilerplate is
/// delegated to <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
/// <remarks>
/// Every interop is guarded: where JavaScript is unavailable (bUnit, prerender, a disconnected
/// circuit) the component keeps its keyboard behaviour and only the scroll default stays — never a
/// throw into the render loop.
/// </remarks>
internal sealed class TraceWaterfallInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-trace-waterfall.js";

    private readonly LazyJsModule _lazy;
    private ElementReference? _grid;
    private bool _disposed;

    public TraceWaterfallInterop(IJSRuntime js)
    {
        // An import that outlived the owner is detached, then released — a racing dispose must not
        // leave the guard on a grid whose component is gone.
        _lazy = new LazyJsModule(js, ModulePath, async module =>
        {
            if (_grid is { } grid) await module.InvokeVoidAsync("detachScrollKeyGuard", grid);
        });
    }

    /// <summary>Attaches the scroll-key guard to the treegrid. Idempotent per element.</summary>
    /// <param name="grid">The element carrying <c>role="treegrid"</c>.</param>
    public async Task AttachAsync(ElementReference grid)
    {
        if (_disposed) return;
        _grid = grid;
        await _lazy.TryInvokeVoidAsync("attachScrollKeyGuard", grid);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_lazy.IsImported && _grid is { } grid)
            {
                await _lazy.TryInvokeVoidAsync("detachScrollKeyGuard", grid);
            }
        }
        finally
        {
            await _lazy.DisposeAsync();
        }
    }
}
