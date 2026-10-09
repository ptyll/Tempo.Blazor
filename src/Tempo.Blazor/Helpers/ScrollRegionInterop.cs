using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>syncScrollRegion</c> export of the shared <c>tm-focus-trap</c>
/// ES module. Keeps an overlay's scroller a keyboard-reachable region only while it actually
/// overflows, so a short dialog grows no extra tab stop and the region never takes initial focus.
/// Lazily imports the module on first attach (safe to construct during prerender — no interop
/// happens until <see cref="AttachAsync"/> is called from <c>OnAfterRenderAsync</c>).
/// </summary>
/// <remarks>
/// Used by <c>TmDialog</c>. Every interop is guarded: when JS is unavailable (bUnit / prerender /
/// disconnected circuit) the scroller simply keeps its stylesheet overflow and never throws into
/// the render loop.
/// </remarks>
internal sealed class ScrollRegionInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    private readonly IJSRuntime _js;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private IJSObjectReference? _module;
    private bool _attached;
    private bool _disposed;

    public ScrollRegionInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Starts watching <paramref name="element"/>. <paramref name="labelledBy"/> names the region
    /// when the scroller has a title; <paramref name="label"/> names it when there is no title id (an
    /// aria-label-only surface); both null leaves it nameless. A second attach replaces the first.
    /// </summary>
    public async Task AttachAsync(ElementReference element, string? labelledBy, string? label = null)
    {
        if (_disposed) return;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed)
            {
                await StopOrDropAsync(module);
                await ReleaseAsync(module);
                return;
            }
            if (module is null) return;

            await module.InvokeVoidAsync("syncScrollRegion", element, _id, labelledBy, label);
            if (_disposed)
            {
                await StopOrDropAsync(module);
                await ReleaseAsync(module);
                return;
            }
            _attached = true;
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (NullReferenceException) { }
    }

    /// <summary>Removes the observer and the region attributes. Safe to call twice.</summary>
    public async Task DetachAsync()
    {
        if (!_attached) return;
        _attached = false;
        await StopOrDropAsync(_module);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DetachAsync();
        await ReleaseAsync(_module);
    }

    private async Task StopOrDropAsync(IJSObjectReference? module)
    {
        if (module is null) return;
        try { await module.InvokeVoidAsync("stopScrollRegion", _id); }
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
}
