using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the shared <c>tm-panel-resize</c> ES module: the work-area resize
/// separator of <c>TmAppShell</c> (pointer drag, region measurement) and its optional layout
/// persistence. Lazily imports the module on first use (safe to construct during prerender); the
/// import/guard boilerplate is delegated to <see cref="LazyJsModule"/>.
/// <para>
/// The same module backs <c>TmEditorShell</c> (through <see cref="EditorShellInterop"/>, which pins
/// its own CSS width variable, drag class, region selector and storage prefix). Every interop is
/// guarded: where JS or storage is unavailable the shell simply keeps its parameter values and
/// never throws into the render loop.
/// </para>
/// </summary>
internal sealed class PanelResizeInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-panel-resize.js";

    private readonly HashSet<string> _attached = new();
    private readonly LazyJsModule _lazy;
    private bool _disposed;

    public PanelResizeInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>Reads the stored layout JSON for the prefix + key, or null when nothing is stored.</summary>
    /// <param name="prefix">The component's storage namespace.</param>
    /// <param name="key">The host-chosen persistence key.</param>
    public async Task<string?> LoadAsync(string prefix, string key)
    {
        try
        {
            var (ok, json) = await _lazy.TryInvokeAsync<string?>("loadStored", prefix, key);
            return ok ? json : null;
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return null;
    }

    /// <summary>Stores the layout JSON under the prefix + key; a null payload removes the entry. Best-effort.</summary>
    /// <param name="prefix">The component's storage namespace.</param>
    /// <param name="key">The host-chosen persistence key.</param>
    /// <param name="json">The layout JSON, or null to remove the entry.</param>
    public async Task SaveAsync(string prefix, string key, string? json)
    {
        try
        {
            await _lazy.TryInvokeVoidAsync("saveStored", prefix, key, json);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>The measured client width of the region element in CSS pixels, or NaN when it cannot be measured.</summary>
    /// <param name="region">The element the resize measures (the content region).</param>
    public async Task<double> MeasureRegionAsync(ElementReference region)
    {
        try
        {
            var (ok, width) = await _lazy.TryInvokeAsync<double>("measureRegion", region, null);
            if (!ok || width < 0) return double.NaN;
            return width;
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return double.NaN;
    }

    /// <summary>The rendered width of an element in CSS pixels, or NaN when it cannot be measured.</summary>
    /// <param name="element">The element to measure.</param>
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

    /// <summary>
    /// Attaches the pointer-drag resize to a separator; a second attach with the same id replaces the
    /// first. The clamped width is reported through <paramref name="method"/> with <c>(side, width)</c>.
    /// </summary>
    /// <param name="id">Registration key (one per separator, unique per component instance).</param>
    /// <param name="handle">The separator element.</param>
    /// <param name="panel">The panel whose width is resized.</param>
    /// <param name="region">The element measured for the "keep the content at its minimum" rule.</param>
    /// <param name="dotnet">The component reference that receives the commit callback.</param>
    /// <param name="widthVar">The CSS custom property the live width is written to.</param>
    /// <param name="side"><c>left</c> or <c>right</c>: which edge the separator sits on.</param>
    /// <param name="min">Smallest allowed width in CSS pixels.</param>
    /// <param name="max">Largest allowed width in CSS pixels.</param>
    /// <param name="minContent">The content width a resize never takes away.</param>
    /// <param name="method">The <c>[JSInvokable]</c> method name that receives the committed width.</param>
    public async Task AttachResizeAsync<T>(string id, ElementReference handle, ElementReference panel, ElementReference region,
        DotNetObjectReference<T> dotnet, string widthVar, string side, int min, int max, int minContent, string method)
        where T : class
    {
        try
        {
            if (await _lazy.GetAsync() is null) return;
            _attached.Add(id);
            await _lazy.TryInvokeVoidAsync("attachResize", handle, panel, region, dotnet, id,
                new { side, min, max, minRegion = minContent, widthVar, method });
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>Detaches the pointer-drag resize registered under the id.</summary>
    /// <param name="id">The registration key used by <see cref="AttachResizeAsync{T}"/>.</param>
    public async Task DetachResizeAsync(string id)
    {
        if (!_attached.Remove(id)) return;
        try
        {
            if (_lazy.IsImported) await _lazy.TryInvokeVoidAsync("detachResize", id);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>Tells whether this wrapper still holds a registration for the id.</summary>
    /// <param name="id">The registration key.</param>
    public bool IsAttached(string id) => _attached.Contains(id);

    private static bool IsBenign(Exception ex)
        => ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException or InvalidOperationException;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var ids = _attached.ToArray();
        foreach (var id in ids) await DetachResizeAsync(id);
        await _lazy.DisposeAsync();
    }
}

/// <summary>
/// The (de)serialization of <c>TmAppShell</c>'s persisted layout: the work area width the USER
/// resized (together with the host width it was resized from) and the sidebar collapsed state.
/// Reads validate every field and ignore anything else, so a stale, hand-edited or previous-format
/// value never reaches the style attribute.
/// </summary>
internal static class AppShellLayoutState
{
    /// <summary>The storage namespace of the application shell's entries.</summary>
    public const string StoragePrefix = "tempo.tm-app-shell.";

    /// <summary>Serializes the persisted layout; null when there is nothing to store.</summary>
    /// <param name="workArea">The user-resized work-area width and its host base, or null.</param>
    /// <param name="collapsed">The sidebar collapsed state, or null when the host drives it.</param>
    public static string? Serialize(StoredWidth? workArea, bool? collapsed)
    {
        if (workArea is null && collapsed is null) return null;
        var map = new Dictionary<string, object>();
        if (workArea is not null) map["workArea"] = new Dictionary<string, object> { ["w"] = workArea.W, ["base"] = workArea.Base };
        if (collapsed is not null) map["collapsed"] = collapsed.Value;
        return JsonSerializer.Serialize(map);
    }

    /// <summary>Parses stored JSON. A null, malformed or unrecognised payload yields (null, null).</summary>
    /// <param name="json">The stored payload.</param>
    public static (StoredWidth? WorkArea, bool? Collapsed) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            return (WorkArea(root), Collapsed(root));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static StoredWidth? WorkArea(JsonElement root)
    {
        if (!root.TryGetProperty("workArea", out var entry) || entry.ValueKind != JsonValueKind.Object) return null;
        if (!entry.TryGetProperty("w", out var w) || w.ValueKind != JsonValueKind.Number || !w.TryGetInt32(out var width)) return null;
        if (!entry.TryGetProperty("base", out var baseValue) || baseValue.ValueKind != JsonValueKind.String) return null;
        var text = baseValue.GetString();
        return string.IsNullOrEmpty(text) ? null : new StoredWidth(width, text);
    }

    private static bool? Collapsed(JsonElement root)
    {
        if (!root.TryGetProperty("collapsed", out var entry)) return null;
        return entry.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
