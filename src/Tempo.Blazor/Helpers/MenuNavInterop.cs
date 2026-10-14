using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around <c>tm-menu-nav.js</c> <c>focusFirst</c>: a menu opened from the keyboard hands
/// focus to its first enabled item, or the roving keys (which listen on the menu) are unreachable. One owner for
/// <c>TmDropdown</c>, <c>TmSplitButton</c> and <c>TmContextMenu</c>. Best-effort - without JS (prerender, a torn
/// down circuit) the menu keeps the native Tab order and nothing throws. The import/guard boilerplate is
/// delegated to <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
internal sealed class MenuNavInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-menu-nav.js";

    private readonly LazyJsModule _lazy;
    private bool _disposed;

    public MenuNavInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>Focuses the first enabled item of the open <c>role=menu</c> inside <paramref name="host"/> (its popover or sheet stays in the host's DOM).</summary>
    public async Task FocusFirstAsync(ElementReference host)
    {
        if (_disposed) return;
        await _lazy.TryInvokeVoidAsync("focusFirst", host);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _lazy.DisposeAsync();
    }
}
