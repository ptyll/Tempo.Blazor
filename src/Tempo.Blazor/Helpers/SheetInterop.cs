using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the shared <c>tm-sheet</c> module. Lazily imports the module on
/// first attach (safe to construct during prerender — no interop happens until
/// <see cref="AttachAsync{T}"/> is called from <c>OnAfterRenderAsync</c>), tracks the visible
/// viewport so the sheet lifts above an on-screen keyboard, and reports the snap a gesture
/// settled on. The module never writes the sheet's height; the component owns the snap index.
/// </summary>
/// <remarks>
/// Used by <c>TmDrawer</c>, <c>TmModal</c> and <c>TmDialog</c> so a sheet has one implementation.
/// Every interop is guarded: when JS is unavailable the sheet renders from the stylesheet defaults
/// and never throws into the render loop.
/// </remarks>
internal sealed class SheetInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-sheet.js";

    private readonly IJSRuntime _js;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private IJSObjectReference? _module;
    private bool _attached;
    private bool _disposed;

    public SheetInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Attaches the gesture and the viewport tracking. <paramref name="handle"/> is what the finger
    /// grabs; <paramref name="panel"/> is what moves. A null handle tracks the viewport only.
    /// </summary>
    public async Task AttachAsync<T>(
        ElementReference? handle,
        ElementReference panel,
        double[] snaps,
        bool swipeToDismiss,
        DotNetObjectReference<T>? host,
        bool trackViewport = true) where T : class
    {
        if (_disposed) return;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed)
            {
                await ReleaseAsync(module);
                return;
            }

            if (module is null) return;

            await module.InvokeVoidAsync("attachGesture", handle, panel, snaps, swipeToDismiss, host, _id, trackViewport);
            if (_disposed)
            {
                await module.InvokeVoidAsync("detach", _id);
                return;
            }

            _attached = true;
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>Removes the gesture listeners. Safe to call twice.</summary>
    public async Task DetachAsync()
    {
        if (!_attached) return;
        _attached = false;
        if (_module is null) return;
        try
        {
            await _module.InvokeVoidAsync("detach", _id);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Promotes a modal sheet root to the browser top layer (Popover API). No-op where the API is
    /// missing or JS is unavailable — the sheet then renders exactly as before, fixed-positioned.
    /// </summary>
    public async Task PromoteAsync(ElementReference root)
    {
        if (_disposed) return;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || module is null) return;
            await module.InvokeVoidAsync("promote", root);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DetachAsync();
        await ReleaseAsync();
    }

    private Task ReleaseAsync() => ReleaseAsync(_module);

    private async Task ReleaseAsync(IJSObjectReference? module)
    {
        if (module is null) return;
        if (ReferenceEquals(_module, module)) _module = null;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }
}
