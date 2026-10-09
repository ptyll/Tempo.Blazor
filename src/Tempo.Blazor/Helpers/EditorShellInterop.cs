using System.Text.Json;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-editor-shell</c> ES module. Loads and saves the optional
/// panel-width persistence of <c>TmEditorShell</c> through the host's localStorage. Lazily imports
/// the module on first use (safe to construct during prerender).
/// </summary>
/// <remarks>Every interop is guarded: where JS or storage is unavailable the shell simply keeps
/// its parameter widths and never throws into the render loop.</remarks>
internal sealed class EditorShellInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-editor-shell.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _disposed;

    public EditorShellInterop(IJSRuntime js) => _js = js;

    /// <summary>Reads the stored widths JSON for the key, or null when nothing is stored.</summary>
    public async Task<string?> LoadWidthsAsync(string key)
    {
        if (_disposed) return null;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || module is null) return null;
            return await module.InvokeAsync<string?>("loadPanelWidths", key);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        return null;
    }

    /// <summary>Stores the widths JSON for the key. Best-effort.</summary>
    public async Task SaveWidthsAsync(string key, string json)
    {
        if (_disposed) return;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || module is null) return;
            await module.InvokeVoidAsync("savePanelWidths", key, json);
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
/// The (de)serialization of the persisted <c>TmEditorShell</c> panel widths — a small JSON object
/// with an optional <c>left</c> and <c>right</c> CSS width. Versionless on purpose: unknown
/// members are ignored, so a newer writer never breaks an older reader.
/// </summary>
internal static class EditorShellWidths
{
    public static string Serialize(string left, string right)
        => JsonSerializer.Serialize(new Dictionary<string, string> { ["left"] = left, ["right"] = right });

    /// <summary>Parses stored JSON. A null/malformed payload yields (null, null).</summary>
    public static (string? Left, string? Right) Parse(string json)
    {
        try
        {
            var widths = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (widths is null) return (null, null);
            return (StringOrNull(widths, "left"), StringOrNull(widths, "right"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? StringOrNull(Dictionary<string, JsonElement> widths, string key)
        => widths.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
