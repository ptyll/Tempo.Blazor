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
        DotNetObjectReference<T>? host) where T : class
    {
        if (_disposed) return;
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed)
            {
                await ReleaseAsync();
                return;
            }

            if (_module is null) return;

            await _module.InvokeVoidAsync("attachGesture", handle, panel, snaps, swipeToDismiss, host, _id);
            if (_disposed)
            {
                await _module.InvokeVoidAsync("detach", _id);
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DetachAsync();
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        if (_module is null) return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        _module = null;
    }
}
