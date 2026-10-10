using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-toolbar</c> ES module (roving tabindex + the fit measurement of
/// <c>TmToolbar</c>). Lazily imports the module on first use (safe to construct during prerender).
/// </summary>
/// <remarks>Every interop is guarded: where JS is unavailable the toolbar keeps the natural tab order and
/// never collapses, and nothing throws into the render loop.</remarks>
internal sealed class ToolbarInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-toolbar.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private ElementReference? _attached;
    private bool _disposed;

    public ToolbarInterop(IJSRuntime js) => _js = js;

    /// <summary>Attaches (or re-attaches with new options) the toolbar behaviour to the root element.</summary>
    public async Task AttachAsync<T>(ElementReference root, DotNetObjectReference<T> dotNetRef, bool overflow) where T : class
    {
        if (_disposed) return;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || module is null) return;
            await module.InvokeVoidAsync("attach", root, dotNetRef, new { overflow });
            _attached = root;
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var module = _module;
        var attached = _attached;
        _module = null;
        _attached = null;
        if (module is null) return;
        try
        {
            if (attached is { } root) await module.InvokeVoidAsync("detach", root);
            await module.DisposeAsync();
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    private static bool IsBenign(Exception ex) => ex is JSException or JSDisconnectedException
        or TaskCanceledException or ObjectDisposedException or InvalidOperationException;
}
