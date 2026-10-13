using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-splitter</c> ES module: the pointer-drag resize of a
/// <c>TmSplitterPane</c> separator and the pane measurement the keyboard path steps from. Lazily
/// imports the module on first use (safe to construct during prerender).
/// </summary>
/// <remarks>Every interop is guarded: where JS is unavailable the splitter keeps its parameter size
/// and never throws into the render loop.</remarks>
internal sealed class SplitterInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-splitter.js";

    private readonly IJSRuntime _js;
    private readonly HashSet<string> _attached = new();
    private IJSObjectReference? _module;
    private bool _disposed;

    public SplitterInterop(IJSRuntime js) => _js = js;

    /// <summary>Attaches the pointer-drag resize to a separator; a second attach with the same id replaces the first.</summary>
    public async Task AttachResizeAsync<T>(string id, ElementReference handle, ElementReference pane,
        DotNetObjectReference<T> dotnet, string orientation, int min, int max) where T : class
    {
        try
        {
            var module = await ModuleAsync();
            if (module is null) return;
            _attached.Add(id);
            await module.InvokeVoidAsync("attachResize", handle, pane, dotnet, id, new { orientation, min, max });
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>The rendered size of the pane in whole pixels, or -1 when it cannot be measured.</summary>
    public async Task<int> MeasureSizeAsync(ElementReference pane, string orientation)
    {
        try
        {
            var module = await ModuleAsync();
            if (module is null) return -1;
            return await module.InvokeAsync<int>("measureSize", pane, orientation);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return -1;
    }

    /// <summary>The element's computed writing direction ("ltr" wherever it cannot be determined).</summary>
    public async Task<string> GetDirectionAsync(ElementReference element)
    {
        try
        {
            var module = await ModuleAsync();
            if (module is null) return "ltr";
            return await module.InvokeAsync<string>("directionOf", element) ?? "ltr";
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return "ltr";
    }

    /// <summary>Detaches the pointer-drag resize registered under the id.</summary>
    public async Task DetachResizeAsync(string id)
    {
        if (!_attached.Remove(id)) return;
        try
        {
            if (_module is not null) await _module.InvokeVoidAsync("detachResize", id);
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
        var ids = _attached.ToArray();
        foreach (var id in ids) await DetachResizeAsync(id);
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

/// <summary>
/// The pure resize rules shared by the keyboard path (C#) and the pointer path
/// (<c>tm-splitter.js clampSize</c>): whole pixels, never negative, within [min, max].
/// </summary>
internal static class SplitterResize
{
    /// <summary>The clamped pane size in whole pixels (identical to <c>clampSize</c> in JS).</summary>
    /// <param name="requested">The size the pointer or key asked for.</param>
    /// <param name="min">Smallest allowed pane size in px.</param>
    /// <param name="max">Largest allowed pane size in px.</param>
    public static int Clamp(double requested, int min, int max)
    {
        // Math.Floor(x + 0.5) is JS Math.round: half rounds up, so both sides agree on .5.
        var size = (int)Math.Min(max, Math.Max(min, Math.Floor(requested + 0.5)));
        return Math.Max(0, size);
    }
}
