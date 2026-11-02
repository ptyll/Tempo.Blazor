using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-editor-shell</c> ES module: the panel resize separator
/// (pointer drag, canvas measurement) and the optional width persistence of <c>TmEditorShell</c>.
/// Lazily imports the module on first use (safe to construct during prerender). The import/guard
/// boilerplate is delegated to <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
/// <remarks>Every interop is guarded: where JS or storage is unavailable the shell simply keeps its
/// parameter widths and never throws into the render loop.</remarks>
internal sealed class EditorShellInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-editor-shell.js";

    private readonly HashSet<string> _attached = new();
    private readonly LazyJsModule _lazy;
    private ElementReference? _observedCanvas;
    private bool _disposed;

    public EditorShellInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>Reads the stored widths JSON for the key, or null when nothing is stored.</summary>
    public async Task<string?> LoadWidthsAsync(string key)
    {
        try
        {
            var (ok, json) = await _lazy.TryInvokeAsync<string?>("loadPanelWidths", key);
            return ok ? json : null;
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return null;
    }

    /// <summary>Stores the widths JSON for the key; a null payload removes the entry. Best-effort.</summary>
    public async Task SaveWidthsAsync(string key, string? json)
    {
        try
        {
            await _lazy.TryInvokeVoidAsync("savePanelWidths", key, json);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>The canvas width in CSS pixels, or NaN when it cannot be measured.</summary>
    public async Task<double> MeasureCanvasAsync(ElementReference main)
    {
        try
        {
            var (ok, width) = await _lazy.TryInvokeAsync<double>("measureCanvas", main);
            if (!ok || width < 0) return double.NaN;
            return width;
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return double.NaN;
    }

    /// <summary>The rendered width of an element in CSS pixels, or NaN when it cannot be measured.</summary>
    public async Task<double> MeasureWidthAsync(ElementReference element)
    {
        try
        {
            var (ok, width) = await _lazy.TryInvokeAsync<double>("measureWidth", element);
            if (!ok || width < 0) return double.NaN;
            return width;
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return double.NaN;
    }

    /// <summary>Scrolls the mobile panels sheet back to its top (the sheet body is one scroller shared by every tab).</summary>
    public async Task ResetSheetScrollAsync(ElementReference stage)
    {
        try
        {
            await _lazy.TryInvokeVoidAsync("resetSheetScroll", stage);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>Attaches the pointer-drag resize to a separator; a second attach with the same id replaces the first.</summary>
    public async Task AttachResizeAsync<T>(string id, ElementReference handle, ElementReference panel, ElementReference main,
        DotNetObjectReference<T> dotnet, string side, int min, int max, int minCanvas) where T : class
    {
        try
        {
            if (await _lazy.GetAsync() is null) return;
            _attached.Add(id);
            await _lazy.TryInvokeVoidAsync("attachResize", handle, panel, main, dotnet, id,
                new { side, min, max, minCanvas });
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>Detaches the pointer-drag resize registered under the id.</summary>
    public async Task DetachResizeAsync(string id)
    {
        if (!_attached.Remove(id)) return;
        try
        {
            if (_lazy.IsImported) await _lazy.TryInvokeVoidAsync("detachResize", id);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>
    /// Watches the shell's canvas region with a ResizeObserver: a container resize that changes the
    /// canvas without a Blazor render makes the module call <c>OnCanvasResized</c>. A second call
    /// for the same element replaces the previous observer.
    /// </summary>
    public async Task ObserveCanvasAsync<T>(ElementReference main, DotNetObjectReference<T> dotnet) where T : class
    {
        try
        {
            if (await _lazy.GetAsync() is null) return;
            _observedCanvas = main;
            await _lazy.TryInvokeVoidAsync("observeCanvas", main, dotnet);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>Stops the canvas watch started by <see cref="ObserveCanvasAsync{T}"/>.</summary>
    public async Task DisconnectCanvasAsync()
    {
        if (_observedCanvas is not { } main) return;
        _observedCanvas = null;
        try
        {
            if (_lazy.IsImported) await _lazy.TryInvokeVoidAsync("disconnectCanvas", main);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    private static bool IsBenign(Exception ex)
        => ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException or InvalidOperationException;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectCanvasAsync();
        var ids = _attached.ToArray();
        foreach (var id in ids) await DetachResizeAsync(id);
        _disposed = true;
        await _lazy.DisposeAsync();
    }
}

/// <summary>A user-resized panel width and the host-supplied width it was resized FROM (the base).</summary>
/// <param name="W">The width in whole CSS pixels the user chose.</param>
/// <param name="Base">The width string the host supplied when the user resized; a stored value applies only while the host still supplies the same one.</param>
internal sealed record StoredWidth(int W, string Base);

/// <summary>
/// The (de)serialization of the persisted <c>TmEditorShell</c> panel widths. Only user-resized
/// widths are stored, each as <c>{"w":300,"base":"280px"}</c>; reads validate every field and ignore
/// anything else, so a stale, hand-edited or previous-format value never reaches the style attribute.
/// </summary>
internal static class EditorShellWidths
{
    /// <summary>Serializes the user-resized widths; null when there is nothing to store.</summary>
    public static string? Serialize(StoredWidth? left, StoredWidth? right)
    {
        if (left is null && right is null) return null;
        var map = new Dictionary<string, object>();
        if (left is not null) map["left"] = new Dictionary<string, object> { ["w"] = left.W, ["base"] = left.Base };
        if (right is not null) map["right"] = new Dictionary<string, object> { ["w"] = right.W, ["base"] = right.Base };
        return JsonSerializer.Serialize(map);
    }

    /// <summary>Parses stored JSON. A null, malformed or unrecognised payload yields (null, null).</summary>
    public static (StoredWidth? Left, StoredWidth? Right) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            return (Side(root, "left"), Side(root, "right"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static StoredWidth? Side(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var entry) || entry.ValueKind != JsonValueKind.Object) return null;
        if (!entry.TryGetProperty("w", out var w) || w.ValueKind != JsonValueKind.Number || !w.TryGetInt32(out var width)) return null;
        if (!entry.TryGetProperty("base", out var baseValue) || baseValue.ValueKind != JsonValueKind.String) return null;
        var text = baseValue.GetString();
        return string.IsNullOrEmpty(text) ? null : new StoredWidth(width, text);
    }
}
