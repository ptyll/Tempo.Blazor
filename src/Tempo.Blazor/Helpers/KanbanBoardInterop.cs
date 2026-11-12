using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-kanban</c> ES module: the touch drag of a
/// <c>TmKanbanBoard</c>, which rides the shared <c>pointer-drag.js</c> helper. Lazily imports the
/// module on first use (safe during prerender); every interop is guarded, so a host without JS
/// keeps the HTML5 drag path, the keyboard path and the move menu and never sees a render-loop
/// exception.
/// </summary>
internal sealed class KanbanBoardInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-kanban.js";

    private bool _attached;
    private string? _attachedId;
    private readonly LazyJsModule _lazy;
    private bool _disposed;

    public KanbanBoardInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>
    /// Attaches the touch drag to the board root. A second attach replaces the first registration,
    /// so the module's registry never holds a stale session for this instance.
    /// </summary>
    /// <param name="id">Registration key (one per component instance).</param>
    /// <param name="root">The board root element the module delegates from.</param>
    /// <param name="dotnet">Receives <c>OnKanbanTouchDrop</c> / <c>OnKanbanTouchDragCancelled</c>.</param>
    public async Task AttachAsync<T>(string id, ElementReference root, DotNetObjectReference<T> dotnet)
        where T : class
    {
        try
        {
            if (await _lazy.GetAsync() is null)
            {
                return;
            }

            // The await above yields — the wrapper may have been disposed while the module import
            // was in flight (a board removed in the same batch). Attaching after disposal would
            // resurrect a session the DisposeAsync cleanup already ran.
            if (_disposed)
            {
                return;
            }

            _attached = true;
            _attachedId = id;
            await _lazy.TryInvokeVoidAsync("attachKanbanBoard", root, dotnet, id);
        }
        catch (Exception ex) when (IsBenign(ex))
        {
        }
    }

    /// <summary>Detaches the drag registered for this wrapper. Safe to call twice.</summary>
    public async Task DetachAsync(string id)
    {
        if (!_attached || _attachedId != id)
        {
            return;
        }

        _attached = false;
        _attachedId = null;

        try
        {
            if (_lazy.IsImported)
            {
                await _lazy.TryInvokeVoidAsync("detachKanbanBoard", id);
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

        if (_attached && _attachedId is { } id)
        {
            await DetachAsync(id);
        }

        _disposed = true;
        await _lazy.DisposeAsync();
    }
}
