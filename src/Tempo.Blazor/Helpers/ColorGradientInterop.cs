using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-color-gradient</c> ES module: the pointer-drag gestures of
/// a <c>TmColorGradient</c> (saturation/value area, hue track, alpha track) and the scroll-key
/// suppression for its keyboard sliders. Lazily imports the module on first use (safe to construct
/// during prerender).
/// </summary>
/// <remarks>Every interop is guarded: where JS is unavailable the gradient keeps its pointer
/// handlers' absence harmless (the RGBA number inputs remain the non-drag alternative) and never
/// throws into the render loop.</remarks>
internal sealed class ColorGradientInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-color-gradient.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _attached;
    private bool _disposed;

    public ColorGradientInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Attaches the drag gestures to a gradient root; a second attach with the same id replaces
    /// the first (used when <c>ShowAlpha</c> toggles and the alpha track appears or disappears).
    /// </summary>
    public async Task AttachAsync(ElementReference root, DotNetObjectReference<TmColorGradient> dotnet, string id)
    {
        try
        {
            var module = await ModuleAsync();
            if (module is null) return;
            _attached = true;
            await module.InvokeVoidAsync("attachGradient", root, dotnet, id);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>Detaches the gradient registered under the id.</summary>
    public async Task DetachAsync(string id)
    {
        if (!_attached) return;
        _attached = false;
        try
        {
            if (_module is not null) await _module.InvokeVoidAsync("detachGradient", id);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    private async ValueTask<IJSObjectReference?> ModuleAsync()
    {
        if (_disposed) return null;
        var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        return _disposed ? null : module;
    }

    private static bool IsBenign(Exception ex)
        => ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException or InvalidOperationException;

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
