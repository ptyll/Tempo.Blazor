using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around <c>tm-menu-nav.js</c> <c>focusFirst</c>: a menu opened from the keyboard hands
/// focus to its first enabled item, or the roving keys (which listen on the menu) are unreachable. One owner for
/// <c>TmDropdown</c>, <c>TmSplitButton</c> and <c>TmContextMenu</c>. Best-effort - without JS (prerender, a torn
/// down circuit) the menu keeps the native Tab order and nothing throws.
/// </summary>
internal sealed class MenuNavInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-menu-nav.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _disposed;

    public MenuNavInterop(IJSRuntime js) => _js = js;

    /// <summary>Focuses the first enabled item of the open <c>role=menu</c> inside <paramref name="host"/> (its popover or sheet stays in the host's DOM).</summary>
    public async Task FocusFirstAsync(ElementReference host)
    {
        if (_disposed) return;
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || _module is null) return;
            await _module.InvokeAsync<bool>("focusFirst", host);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException
            or ObjectDisposedException or InvalidOperationException)
        {
            // Best-effort: a torn-down circuit or a missing module must never fail the open.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        var module = _module;
        _module = null;
        if (module is null) return;
        try
        {
            await module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
        }
    }
}