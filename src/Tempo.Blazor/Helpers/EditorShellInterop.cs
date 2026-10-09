using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-editor-shell</c> ES module: the panel resize separator
/// (pointer drag, canvas measurement) and the optional width persistence of <c>TmEditorShell</c>.
/// Lazily imports the module on first use (safe to construct during prerender).
/// </summary>
/// <remarks>Every interop is guarded: where JS or storage is unavailable the shell simply keeps its
/// parameter widths and never throws into the render loop.</remarks>
internal sealed class EditorShellInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-editor-shell.js";

    private readonly IJSRuntime _js;
    private readonly HashSet<string> _attached = new();
    private IJSObjectReference? _module;
    private bool _disposed;

    public EditorShellInterop(IJSRuntime js) => _js = js;

    /// <summary>Reads the stored widths JSON for the key, or null when nothing is stored.</summary>
    public async Task<string?> LoadWidthsAsync(string key)
    {
        try
        {
            var module = await ModuleAsync();
            return module is null ? null : await module.InvokeAsync<string?>("loadPanelWidths", key);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return null;
    }

    /// <summary>Stores the widths JSON for the key; a null payload removes the entry. Best-effort.</summary>
    public async Task SaveWidthsAsync(string key, string? json)
    {
        try
        {
            var module = await ModuleAsync();
            if (module is not null) await module.InvokeVoidAsync("savePanelWidths", key, json);
        }
        catch (Exception ex) when (IsBenign(ex)) { }
    }

    /// <summary>The canvas width in CSS pixels, or NaN when it cannot be measured.</summary>
    public async Task<double> MeasureCanvasAsync(ElementReference main)
    {
        try
        {
            var module = await ModuleAsync();
            if (module is null) return double.NaN;
            var width = await module.InvokeAsync<double>("measureCanvas", main);
            return width < 0 ? double.NaN : width;
        }
        catch (Exception ex) when (IsBenign(ex)) { }
        return double.NaN;
    }

    /// <summary>Attaches the pointer-drag resize to a separator; a second attach with the same id replaces the first.</summary>
    public async Task AttachResizeAsync<T>(string id, ElementReference handle, ElementReference panel, ElementReference main,
        DotNetObjectReference<T> dotnet, string side, int min, int max, int minCanvas) where T : class
    {
        try
        {
            var module = await ModuleAsync();
            if (module is null) return;
            _attached.Add(id);
            await module.InvokeVoidAsync("attachResize", handle, panel, main, dotnet, id,
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

/// <summary>
/// The pure resize rules shared by the keyboard path (C#) and the pointer path
/// (<c>tm-editor-shell.js clampWidth</c>): clamp to the min/max width and keep the canvas at least
/// its minimum, and parse a plain pixel length without ever trusting a raw string.
/// </summary>
internal static partial class EditorShellResize
{
    /// <summary>The clamped panel width in whole pixels (identical to <c>clampWidth</c> in JS).</summary>
    /// <param name="requested">The width the pointer or key asked for.</param>
    /// <param name="current">The width the gesture started from.</param>
    /// <param name="min">Smallest allowed panel width.</param>
    /// <param name="max">Largest allowed panel width.</param>
    /// <param name="canvas">The canvas width at the start of the gesture; NaN when unknown.</param>
    /// <param name="minCanvas">The canvas width a resize never takes away.</param>
    public static double Clamp(double requested, double current, int min, int max, double canvas, int minCanvas)
    {
        // Math.Floor(x + 0.5) is JS Math.round: half rounds up, so both sides agree on .5.
        var width = Math.Min(max, Math.Max(min, Math.Floor(requested + 0.5)));
        if (double.IsFinite(canvas) && width > current)
        {
            width = Math.Min(width, Math.Max(current, current + (canvas - minCanvas)));
        }
        return width;
    }

    /// <summary>Parses a plain "NNNpx" length; anything else (rem, %, calc, trailing junk, negatives) is rejected.</summary>
    public static bool TryParsePx(string? value, out int px)
    {
        px = 0;
        if (value is null) return false;
        var match = PixelLength().Match(value);
        if (!match.Success) return false;
        var number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (number > int.MaxValue) return false;
        px = (int)Math.Floor(number + 0.5);
        return true;
    }

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)px\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PixelLength();
}