using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>syncScrollRegion</c> export of the shared <c>tm-focus-trap</c>
/// ES module. Keeps an overlay's scroller a keyboard-reachable region only while it actually
/// overflows, so a short dialog grows no extra tab stop and the region never takes initial focus.
/// Lazily imports the module on first attach (safe to construct during prerender — no interop
/// happens until <see cref="AttachAsync"/> is called from <c>OnAfterRenderAsync</c>). The
/// import/guard boilerplate is delegated to <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
/// <remarks>
/// Used by <c>TmDialog</c>, <c>TmModal</c> and <c>TmDrawer</c>. Every interop is guarded: when JS is
/// unavailable (bUnit / prerender / disconnected circuit) the scroller simply keeps its stylesheet
/// overflow and never throws into the render loop.
/// </remarks>
internal sealed class ScrollRegionInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly LazyJsModule _lazy;
    private bool _attached;
    // The region is wanted from AttachAsync until a detach/dispose. A stale attach's cleanup
    // decision needs this: a newer attach REUSES the same region id, so a blind stop would tear
    // the newer region down.
    private bool _wanted;
    // The latest requested accessible name. Stored unconditionally (also while an attach is still
    // in flight) so a label update that lands before the region exists is reconciled right after
    // the attach's sync completes instead of being dropped.
    private string? _labelledBy;
    private string? _label;
    private bool _disposed;
    // cluster-B 8.3: a monotonic attach generation. A detach that lands while an attach is still
    // in flight must win over the attach's completion — the old code dropped the detach via an
    // `!_attached` early return and the attach then left its ResizeObserver on the removed node.
    private int _generation;

    public ScrollRegionInterop(IJSRuntime js)
    {
        // An import that outlived the owner is stopped, then released — an attach that lost the
        // race against a dispose must still tear its region down.
        _lazy = new LazyJsModule(js, ModulePath, async module => await module.InvokeVoidAsync("stopScrollRegion", _id));
    }

    /// <summary>
    /// Starts watching <paramref name="element"/>. <paramref name="labelledBy"/> names the region
    /// when the scroller has a title; <paramref name="label"/> names it when there is no title id (an
    /// aria-label-only surface); both null leaves it nameless. A second attach replaces the first.
    /// </summary>
    public async Task AttachAsync(ElementReference element, string? labelledBy, string? label = null)
    {
        if (_disposed) return;
        _wanted = true;
        _labelledBy = labelledBy;
        _label = label;
        var generation = ++_generation;
        // Null when JS is unavailable — or when the owner was disposed while the import awaited
        // (the helper released the module in that case).
        var module = await _lazy.GetAsync();
        if (_disposed || generation != _generation)
        {
            // A newer attach that still wants the region owns the shared region id — stopping
            // here would kill ITS region. Only an unwanted or disposed wrapper cleans up.
            if (_disposed || !_wanted) await StopOrDropAsync();
            return;
        }
        if (module is null) return;

        var synced = await _lazy.TryInvokeVoidAsync("syncScrollRegion", element, _id, labelledBy, label);
        if (_disposed || generation != _generation)
        {
            if (_disposed || !_wanted) await StopOrDropAsync();
            return;
        }
        // A failed sync never attached: stay unattached so a later UpdateLabelAsync stays silent
        // instead of re-pointing a region that does not exist.
        if (!synced) return;
        _attached = true;
        // A label update that landed while this sync was in flight could only be stored (there
        // was no region to re-point yet) — reconcile it once if it already moved on.
        if (_labelledBy != labelledBy || _label != label)
        {
            await _lazy.TryInvokeVoidAsync("updateScrollRegionLabel", _id, _labelledBy, _label);
        }
    }

    /// <summary>
    /// Re-points the region's accessible name after the title rendered or changed (cluster-B 8.3).
    /// While no region is attached the values are only stored — an update that lands while an
    /// attach is still in flight is reconciled right after that attach's sync completes.
    /// </summary>
    public async Task UpdateLabelAsync(string? labelledBy, string? label)
    {
        if (_disposed) return;
        _labelledBy = labelledBy;
        _label = label;
        if (!_attached) return;
        if (!_lazy.IsImported) return;
        await _lazy.TryInvokeVoidAsync("updateScrollRegionLabel", _id, labelledBy, label);
    }

    /// <summary>
    /// Removes the observer and the region attributes. Safe to call twice; also cancels an attach
    /// that is still in flight (the stale attach stops itself when it resumes).
    /// </summary>
    public async Task DetachAsync()
    {
        _generation++;
        _attached = false;
        _wanted = false;
        await StopOrDropAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _wanted = false;
        await DetachAsync();
        await _lazy.DisposeAsync();
    }

    // Never triggers the import: a stale attach that lost the race detaches through the module the
    // helper already imported, and a module that was never imported has no region to stop.
    private async Task StopOrDropAsync()
    {
        if (!_lazy.IsImported) return;
        await _lazy.TryInvokeVoidAsync("stopScrollRegion", _id);
    }
}
