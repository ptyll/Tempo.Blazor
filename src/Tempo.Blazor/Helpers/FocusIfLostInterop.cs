using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>focusIfLost</c> export of the shared <c>tm-focus-trap</c> ES
/// module. A host surface (the editor shell) that re-renders its own trigger after one of its panels
/// closed moves focus there ONLY when focus was lost — on the body, on a detached element or inside
/// the closing surface — and never when the user or the host placed focus on another live element
/// (F6 review round 1, F6). Lazily imports the module on first use (safe to construct during
/// prerender — nothing is called before <see cref="FocusIfLostAsync"/>).
/// </summary>
/// <remarks>Every interop is guarded: where JS is unavailable the call is a no-op.</remarks>
internal sealed class FocusIfLostInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _disposed;

    public FocusIfLostInterop(IJSRuntime js) => _js = js;

    /// <summary>Focuses the element with the id when focus is lost or sits inside the container with <paramref name="containerId"/>. Returns whether focus moved.</summary>
    public async Task<bool> FocusIfLostAsync(string elementId, string? containerId = null)
    {
        if (_disposed) return false;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || module is null) return false;
            return await module.InvokeAsync<bool>("focusIfLost", elementId, containerId);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        return false;
    }

    /// <summary>
    /// Imports the module ahead of time. <see cref="FocusWithinAsync"/> must reach the browser BEFORE
    /// the render that removes the focused element; a first-use import would let that render win.
    /// </summary>
    public async Task WarmUpAsync()
    {
        if (_disposed) return;
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>Whether focus currently sits inside the element. False where JS is unavailable.</summary>
    public async Task<bool> FocusWithinAsync(ElementReference element)
    {
        if (_disposed) return false;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || module is null) return false;
            return await module.InvokeAsync<bool>("focusWithin", element);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        return false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var module = _module;
        _module = null;
        if (module is null) return;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }
}
