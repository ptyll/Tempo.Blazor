using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-sortable</c> ES module: the pointer-drag reorder of a
/// <c>TmSortableList</c>, which rides the shared <c>pointer-drag.js</c> and
/// <c>pointer-gesture.js</c> helpers. Lazily imports the module on first use (safe to construct
/// during prerender); every interop is guarded, so a host without JS keeps the keyboard path and the
/// move menu and never sees a render-loop exception.
/// </summary>
internal sealed class SortableListInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-sortable.js";

    private readonly HashSet<string> _attached = new();
    private readonly LazyJsModule _lazy;
    private bool _disposed;

    public SortableListInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>
    /// Attaches the pointer drag to the list root; a second attach with the same id replaces the
    /// first, so a changed handle mode never leaves two sessions behind.
    /// </summary>
    /// <param name="id">Registration key (one per component instance).</param>
    /// <param name="root">The <c>role="list"</c> element the module delegates from.</param>
    /// <param name="dotnet">Receives <c>OnSortableReorderCommitted</c> once per committed drag.</param>
    /// <param name="handleMode">"handle" or "whole-item".</param>
    /// <param name="group">The cross-list drag group, or an empty string for a standalone list.</param>
    /// <param name="listId">How the host recognises this list inside its group.</param>
    /// <param name="trackDragOver">Whether the module reports the list a live drag would land in.</param>
    public async Task AttachAsync<T>(string id, ElementReference root, DotNetObjectReference<T> dotnet,
        string handleMode, string group = "", string? listId = null, bool trackDragOver = false) where T : class
    {
        try
        {
            if (await _lazy.GetAsync() is null)
            {
                return;
            }

            _attached.Add(id);
            await _lazy.TryInvokeVoidAsync("attachSortable", root, dotnet, id, handleMode,
                new { group, listId = listId ?? id, dragOver = trackDragOver });
        }
        catch (Exception ex) when (IsBenign(ex))
        {
        }
    }

    /// <summary>Detaches the drag registered under the id. Safe for an unknown or already-detached id.</summary>
    public async Task DetachAsync(string id)
    {
        if (!_attached.Remove(id))
        {
            return;
        }

        try
        {
            if (_lazy.IsImported)
            {
                await _lazy.TryInvokeVoidAsync("detachSortable", id);
            }
        }
        catch (Exception ex) when (IsBenign(ex))
        {
        }
    }

    private static bool IsBenign(Exception ex)
        => ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException
            or InvalidOperationException;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        var ids = _attached.ToArray();
        foreach (var id in ids)
        {
            await DetachAsync(id);
        }

        _disposed = true;
        await _lazy.DisposeAsync();
    }
}
