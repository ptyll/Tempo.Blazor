using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-color-gradient</c> ES module: the pointer-drag gestures of
/// a <c>TmColorGradient</c> (saturation/value area, hue track, alpha track) and the scroll-key
/// suppression for its keyboard sliders. Lazily imports the module on first use (safe to construct
/// during prerender). The import/guard boilerplate is delegated to <see cref="LazyJsModule"/>
/// (cluster-B 8.8).
/// </summary>
/// <remarks>Every interop is guarded: where JS is unavailable the gradient keeps its pointer
/// handlers' absence harmless (the RGBA number inputs remain the non-drag alternative) and never
/// throws into the render loop.</remarks>
internal sealed class ColorGradientInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-color-gradient.js";

    private readonly LazyJsModule _lazy;
    private bool _attached;
    private bool _disposed;

    public ColorGradientInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>
    /// Attaches the drag gestures to a gradient root; a second attach with the same id replaces
    /// the first (used when <c>ShowAlpha</c> toggles and the alpha track appears or disappears).
    /// </summary>
    public async Task AttachAsync(ElementReference root, DotNetObjectReference<TmColorGradient> dotnet, string id)
    {
        try
        {
            if (await _lazy.GetAsync() is null) return;
            _attached = true;
            await _lazy.TryInvokeVoidAsync("attachGradient", root, dotnet, id);
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
            if (_lazy.IsImported) await _lazy.TryInvokeVoidAsync("detachGradient", id);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    private static bool IsBenign(Exception ex)
        => ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException or InvalidOperationException;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lazy.DisposeAsync();
    }
}
